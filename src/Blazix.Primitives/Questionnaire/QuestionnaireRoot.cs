using JetBrains.Annotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>
/// A form that asks one question at a time. Only the
/// active <see cref="QuestionnaireItem"/> is shown; Previous, Skip, Next and Submit move through the
/// items, validating the active one first. Keyboard: Ctrl/⌘+Enter confirms the item, ↑/↓ move between
/// its answers, ←/→ between items, Enter on a filled answer confirms it, and <see cref="Shortcuts"/>
/// selects answers by letter or number.
/// </summary>
[PublicAPI]
public class QuestionnaireRoot : PrimitivePart<QuestionnaireRenderProps>, IAsyncDisposable
{
    private readonly Controlled<string?> _item = new(null);
    private readonly List<QuestionnaireItem> _registrations = [];
    private DotNetObjectReference<QuestionnaireInterop>? _interop;
    private JsController? _js;
    private EventCallback<EventArgs> _onSubmit;
    private EventCallback<EventArgs> _onReset;
    private (string Name, bool Invalid)? _pendingFocus;
    private bool _initialized;
    private bool _rendered;
    private bool _disposed;
    private string _id = "";

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    /// <summary>
    /// The questionnaire's items, in order. Optional: without it the order is the order the items render
    /// in. With it, progress, actions and shortcuts are right from the first render, and an item can be
    /// left out of the flow by marking its definition disabled.
    /// </summary>
    [Parameter] public IReadOnlyList<QuestionnaireItemDefinition>? Items { get; set; }

    /// <summary>The item shown first, and after a reset. Defaults to the first enabled item.</summary>
    [Parameter] public string? DefaultItem { get; set; }

    /// <summary>The active item's name. Use <c>@bind-Item</c> to store it outside the questionnaire.</summary>
    [Parameter] public string? Item { get; set; }

    [Parameter] public EventCallback<string> ItemChanged { get; set; }

    /// <summary>Select answers with A–Z or 1–9. Off by default.</summary>
    [Parameter] public QuestionnaireShortcuts? Shortcuts { get; set; }

    /// <summary>
    /// Whether to skip the browser's constraint validation (e.g. an email input's format). Defaults to
    /// true; set false to also check filled answers with the browser's validation.
    /// </summary>
    [Parameter] public bool NoValidate { get; set; } = true;

    /// <summary>Runs with the answers once every item is valid.</summary>
    [Parameter] public EventCallback<QuestionnaireAnswers> OnSubmit { get; set; }

    /// <summary>Runs when the form resets, before the answers return to their defaults.</summary>
    [Parameter] public EventCallback OnReset { get; set; }

    protected override string Tag => "form";

    internal QuestionnaireShortcuts? ShortcutMode => Shortcuts;

    internal bool NativeValidation => !NoValidate;

    internal bool HasDefinitions => Items is not null;

    /// <summary>The names of the items in the flow: the enabled definitions, else the enabled registered items.</summary>
    private List<string> LogicalNames() => Items is not null
        ? Items.Where(item => !item.Disabled).Select(item => item.Name).ToList()
        : _registrations.Where(item => !item.IsDisabled).Select(item => item.Name).ToList();

    /// <summary>The active item's name; the first item's when the current one isn't in the flow.</summary>
    internal string? ActiveName
    {
        get
        {
            var names = LogicalNames();
            return _item.Value is { } name && names.Contains(name) ? name : names.FirstOrDefault();
        }
    }

    private int ActiveIndex
    {
        get
        {
            var active = ActiveName;
            return active is null ? -1 : LogicalNames().IndexOf(active);
        }
    }

    internal int Total => LogicalNames().Count;

    internal int Current => ActiveIndex + 1;

    internal bool First => Total > 0 && ActiveIndex == 0;

    internal bool Last => Total > 0 && ActiveIndex == Total - 1;

    private QuestionnaireItem? ActiveRegistration
    {
        get
        {
            var active = ActiveName;
            return active is null ? null : _registrations.Find(item => !item.IsDisabled && item.Name == active);
        }
    }

    /// <summary>Whether the active item must be answered; null when there's none.</summary>
    internal bool? ActiveItemRequired
    {
        get
        {
            var active = ActiveName;
            if (active is null)
            {
                return null;
            }
            var definition = Definition(active);
            return definition?.Required ?? ActiveRegistration?.Required ?? false;
        }
    }

    internal QuestionnaireItemStatus? ActiveItemStatus => ActiveName is null ? null : ActiveRegistration?.Status ?? QuestionnaireItemStatus.Unanswered;

    /// <summary>The registered items in the flow's order.</summary>
    private List<QuestionnaireItem> OrderedRegistrations() => Items is not null
        ? Items.Where(item => !item.Disabled)
            .Select(item => _registrations.Find(registration => registration.Name == item.Name))
            .OfType<QuestionnaireItem>()
            .Where(item => !item.IsDisabled)
            .ToList()
        : _registrations.Where(item => !item.IsDisabled).ToList();

    internal Task InvokeOnRendererAsync(Func<Task> work) => InvokeAsync(work);

    internal QuestionnaireItemDefinition? Definition(string name) => Items?.FirstOrDefault(item => item.Name == name);

    protected override void OnInitialized()
    {
        _onSubmit = EventCallback.Factory.Create<EventArgs>(this, HandleSubmitAsync);
        _onReset = EventCallback.Factory.Create<EventArgs>(this, HandleResetAsync);
    }

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("questionnaire"));
        var replaced = _item.Sync(Item);
        if (!_initialized)
        {
            _initialized = true;
            _item.Value ??= InitialName();
        }
        else if (replaced && _rendered && _item.Value is { } name)
        {
            // Moved from outside: focus the new item, as a controlled change does upstream.
            _pendingFocus = (name, false);
        }
    }

    private string? InitialName()
    {
        if (Items is null)
        {
            return DefaultItem;
        }
        var definition = DefaultItem is null ? null : Definition(DefaultItem);
        return definition is { Disabled: false } ? definition.Name : Items.FirstOrDefault(item => !item.Disabled)?.Name;
    }

    internal void Register(QuestionnaireItem item)
    {
        _registrations.RemoveAll(registration => registration == item || registration.Name == item.Name);
        _registrations.Add(item);
        Changed();
    }

    internal void Unregister(QuestionnaireItem item)
    {
        if (_registrations.Remove(item))
        {
            Changed();
        }
    }

    /// <summary>Re-renders the questionnaire and every part (they all read its state).</summary>
    internal void Changed()
    {
        if (!_disposed)
        {
            StateHasChanged();
        }
    }

    private async Task SetItemAsync(string name, bool focusInvalid = false)
    {
        if (name == _item.Value)
        {
            return;
        }
        _pendingFocus = (name, focusInvalid);
        _item.Value = name;
        await ItemChanged.InvokeAsync(name);
        Changed();
    }

    internal async Task GoPreviousAsync()
    {
        var index = ActiveIndex;
        if (index > 0)
        {
            await SetItemAsync(LogicalNames()[index - 1]);
        }
    }

    internal async Task GoNextAsync()
    {
        var active = ActiveRegistration;
        var index = ActiveIndex;
        if (active is null || index >= Total - 1)
        {
            return;
        }
        if (!await active.ValidateAsync())
        {
            await FocusInvalidAsync(active);
            return;
        }
        await SetItemAsync(LogicalNames()[index + 1]);
    }

    /// <summary>Ctrl/⌘+Enter, or Enter on a filled answer: next item, or submit on the last.</summary>
    internal async Task ConfirmAsync()
    {
        var active = ActiveRegistration;
        if (active is null)
        {
            return;
        }
        if (!await active.ValidateAsync())
        {
            await FocusInvalidAsync(active);
            return;
        }
        if (Last)
        {
            await HandleSubmitAsync();
            return;
        }
        await SetItemAsync(LogicalNames()[ActiveIndex + 1]);
    }

    internal async Task SkipAsync()
    {
        var active = ActiveRegistration;
        if (active is null || active.Required)
        {
            return;
        }
        active.Skip();
        if (Last)
        {
            await HandleSubmitAsync();
            return;
        }
        await SetItemAsync(LogicalNames()[ActiveIndex + 1]);
    }

    private Task FocusInvalidAsync(QuestionnaireItem item) => _js?.InvokeVoidAsync("focusInvalid", item.ElementId) ?? Task.CompletedTask;

    internal async Task<bool> ReportValidityAsync(QuestionnaireItem item) =>
        _js is null || await _js.InvokeAsync<bool>("reportValidity", item.ElementId);

    private async Task HandleSubmitAsync()
    {
        QuestionnaireItem? invalid = null;
        foreach (var registration in OrderedRegistrations())
        {
            if (!await registration.ValidateAsync())
            {
                invalid = registration;
                break;
            }
        }

        if (invalid is not null)
        {
            if (invalid.Name == ActiveName)
            {
                _pendingFocus = null;
                await FocusInvalidAsync(invalid);
            }
            else
            {
                await SetItemAsync(invalid.Name, focusInvalid: true);
            }
            Changed();
            return;
        }

        var answers = new QuestionnaireAnswers();
        foreach (var registration in OrderedRegistrations())
        {
            registration.CollectAnswers(answers);
        }
        await OnSubmit.InvokeAsync(answers);
    }

    private async Task HandleResetAsync()
    {
        await OnReset.InvokeAsync();
        foreach (var registration in _registrations)
        {
            registration.Reset();
        }

        var name = Items is not null
            ? InitialName()
            : _registrations.Find(item => !item.IsDisabled && item.Name == DefaultItem)?.Name ?? _registrations.Find(item => !item.IsDisabled)?.Name;
        if (name is not null)
        {
            await SetItemAsync(name);
        }
        Changed();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _interop = DotNetObjectReference.Create(new QuestionnaireInterop(this));
            _js = new JsController(JsRuntime, "questionnaire.js", "createQuestionnaire");
            await _js.EnsureAsync(_id, _interop);
            _rendered = true;
        }

        // The active item left the flow (or was never in it): fall back to the first one.
        var names = LogicalNames();
        if (names.Count > 0 && (_item.Value is null || !names.Contains(_item.Value)))
        {
            if (_item.Value is null)
            {
                _item.Value = names[0];
                await ItemChanged.InvokeAsync(names[0]);
            }
            else
            {
                await SetItemAsync(names[0]);
            }
            return;
        }

        if (_pendingFocus is { } pending && pending.Name == ActiveName)
        {
            _pendingFocus = null;
            if (ActiveRegistration is { } active)
            {
                await _js!.InvokeVoidAsync(pending.Invalid ? "focusInvalid" : "focusItem", active.ElementId);
            }
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, this, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var total = Total;
        attributes
            .Set("id", _id)
            .Set("novalidate", NoValidate)
            .Set("onsubmit", _onSubmit)
            .Set("onreset", _onReset)
            .Set("data-shortcuts", QuestionnaireKeys.Name(Shortcuts))
            .Set("data-current", total > 0 ? Current.ToString(System.Globalization.CultureInfo.InvariantCulture) : "0")
            .Set("data-total", total.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Flag("data-first", First)
            .Flag("data-last", Last);
    }

    protected override QuestionnaireRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Current, Total, First, Last);

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        Dispose(true);
        if (_js is not null)
        {
            await _js.DisposeAsync();
        }
        _interop?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>What questionnaire.js calls for the keyboard.</summary>
internal sealed class QuestionnaireInterop(QuestionnaireRoot root)
{
    [JSInvokable]
    public Task ConfirmAsync() => root.InvokeOnRendererAsync(root.ConfirmAsync);

    [JSInvokable]
    public Task PreviousAsync() => root.InvokeOnRendererAsync(root.GoPreviousAsync);

    [JSInvokable]
    public Task NextAsync() => root.InvokeOnRendererAsync(root.GoNextAsync);
}
