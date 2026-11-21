using JetBrains.Annotations;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>How urgently a toast is announced: high-priority toasts are alert dialogs.</summary>
public enum ToastPriority
{
    Low,
    High,
}

/// <summary>
/// Where an anchored toast appears: next to an element, like a
/// "Copied" bubble by a button. Render the toast in a <see cref="ToastPositioner"/>; its own
/// parameters, when set, win over these.
/// </summary>
public sealed class ToastPositionerOptions
{
    /// <summary>The id of the element to position the toast against.</summary>
    public string? Anchor { get; init; }

    /// <summary>Which side of the anchor it appears on (it flips when there isn't room). Defaults to top.</summary>
    public Side? Side { get; init; }

    /// <summary>How it lines up with the anchor along that side. Defaults to center.</summary>
    public Align? Align { get; init; }

    /// <summary>Gap between the anchor and the toast, in pixels.</summary>
    public int? SideOffset { get; init; }

    /// <summary>Shift along the anchor, in pixels.</summary>
    public int? AlignOffset { get; init; }

    /// <summary>Space to keep from the viewport edges, in pixels. Defaults to 5.</summary>
    public int? CollisionPadding { get; init; }

    /// <summary>Space between the arrow and the toast's corners, in pixels. Defaults to 5.</summary>
    public int? ArrowPadding { get; init; }

    /// <summary>Keep the toast in view when its anchor scrolls out of it.</summary>
    public bool? Sticky { get; init; }
}

/// <summary>What to show in a toast, for <see cref="ToastManager.Add"/> and <see cref="ToastManager.Update"/>.</summary>
public sealed class ToastOptions
{
    /// <summary>An id of your own, to update or close the toast later. Defaults to a generated one.</summary>
    public string? Id { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    /// <summary>A type of your own ("success", "error", "loading"...), rendered as <c>data-type</c>.</summary>
    public string? Type { get; init; }

    /// <summary>How long before it closes, in milliseconds. 0 keeps it open. Defaults to the provider's timeout.</summary>
    public int? Timeout { get; init; }

    public ToastPriority Priority { get; init; } = ToastPriority.Low;

    /// <summary>The label of the toast's action button, when it has one.</summary>
    public string? ActionLabel { get; init; }

    /// <summary>Runs when the action button is pressed. The toast closes afterwards.</summary>
    public Func<Task>? OnAction { get; init; }

    /// <summary>Runs when the toast starts closing.</summary>
    public Action<ToastObject>? OnClose { get; init; }

    /// <summary>Runs once the toast has left the page.</summary>
    public Action<ToastObject>? OnRemove { get; init; }

    /// <summary>Anything else your toast template needs.</summary>
    public object? Data { get; init; }

    /// <summary>Where an anchored toast appears, for a <see cref="ToastPositioner"/>.</summary>
    public ToastPositionerOptions? Positioner { get; init; }
}

/// <summary>A toast in a <see cref="ToastManager"/>: what its template renders.</summary>
[PublicAPI]
public sealed class ToastObject
{
    internal ToastObject(string id) => Id = id;

    public string Id { get; }

    public string? Title { get; private set; }

    public string? Description { get; private set; }

    public string? Type { get; private set; }

    public int Timeout { get; internal set; }

    public ToastPriority Priority { get; private set; }

    public string? ActionLabel { get; private set; }

    public object? Data { get; private set; }

    /// <summary>Where an anchored toast appears, for a <see cref="ToastPositioner"/>.</summary>
    public ToastPositionerOptions? Positioner { get; private set; }

    /// <summary>Whether the toast is playing its exit animation.</summary>
    public bool Closing { get; internal set; }

    internal Func<Task>? OnAction { get; private set; }

    internal Action<ToastObject>? OnClose { get; private set; }

    internal Action<ToastObject>? OnRemove { get; private set; }

    internal void Apply(ToastOptions options, int defaultTimeout)
    {
        Title = options.Title ?? Title;
        Description = options.Description ?? Description;
        Type = options.Type ?? Type;
        Timeout = options.Timeout ?? (Timeout > 0 ? Timeout : defaultTimeout);
        Priority = options.Priority;
        ActionLabel = options.ActionLabel ?? ActionLabel;
        Data = options.Data ?? Data;
        Positioner = options.Positioner ?? Positioner;
        OnAction = options.OnAction ?? OnAction;
        OnClose = options.OnClose ?? OnClose;
        OnRemove = options.OnRemove ?? OnRemove;
    }
}

/// <summary>
/// Creates, updates and closes toasts.
/// Get it from the cascading <see cref="ToastProvider"/>, or create one
/// (for example as a scoped service) and pass it to the provider, to show toasts from anywhere.
/// </summary>
public sealed class ToastManager
{
    private readonly List<ToastObject> _toasts = [];

    /// <summary>The toasts, newest first.</summary>
    public IReadOnlyList<ToastObject> Toasts => _toasts;

    /// <summary>Raised whenever toasts are added, updated, closed or removed.</summary>
    public event Action? Changed;

    internal int DefaultTimeout { get; set; } = 5000;

    /// <summary>Shows a toast and returns its id.</summary>
    public string Add(ToastOptions options)
    {
        var toast = new ToastObject(options.Id ?? Ids.Next("toast"));
        toast.Apply(options, DefaultTimeout);
        toast.Timeout = options.Timeout ?? DefaultTimeout;
        _toasts.RemoveAll(existing => existing.Id == toast.Id);
        _toasts.Insert(0, toast);
        Changed?.Invoke();
        return toast.Id;
    }

    /// <summary>Changes a toast that's showing.</summary>
    public void Update(string id, ToastOptions options)
    {
        var toast = _toasts.Find(t => t.Id == id);
        if (toast is null)
        {
            return;
        }
        toast.Apply(options, DefaultTimeout);
        if (options.Timeout is { } timeout)
        {
            toast.Timeout = timeout;
        }
        Changed?.Invoke();
    }

    /// <summary>Closes a toast (it animates out), or every toast when no id is given.</summary>
    public void Close(string? id = null)
    {
        foreach (var toast in _toasts.Where(t => (id is null || t.Id == id) && !t.Closing).ToList())
        {
            toast.Closing = true;
            toast.OnClose?.Invoke(toast);
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Shows a loading toast while <paramref name="task"/> runs, then turns it into a success or an
    /// error toast. Returns the task's result (or rethrows its exception).
    /// </summary>
    public async Task<T> Promise<T>(Task<T> task, ToastOptions loading, Func<T, ToastOptions> success, Func<Exception, ToastOptions> error)
    {
        ArgumentNullException.ThrowIfNull(task);
        var id = Add(new ToastOptions
        {
            Id = loading.Id,
            Title = loading.Title,
            Description = loading.Description,
            Type = loading.Type ?? "loading",
            Timeout = 0,
            Priority = loading.Priority,
            Data = loading.Data,
        });
        try
        {
            var result = await task;
            var options = success(result);
            Update(id, new ToastOptions
            {
                Title = options.Title,
                Description = options.Description,
                Type = options.Type ?? "success",
                Timeout = options.Timeout ?? DefaultTimeout,
                ActionLabel = options.ActionLabel,
                OnAction = options.OnAction,
                Data = options.Data,
            });
            return result;
        }
        catch (Exception e)
        {
            var options = error(e);
            Update(id, new ToastOptions
            {
                Title = options.Title,
                Description = options.Description,
                Type = options.Type ?? "error",
                Timeout = options.Timeout ?? DefaultTimeout,
                Data = options.Data,
            });
            throw;
        }
    }

    internal void Remove(string id)
    {
        var toast = _toasts.Find(t => t.Id == id);
        if (toast is null)
        {
            return;
        }
        _toasts.Remove(toast);
        toast.OnRemove?.Invoke(toast);
        Changed?.Invoke();
    }
}

/// <summary>
/// Provides a toast manager to the toasts inside it. Cascades the
/// <see cref="ToastManager"/>: add <c>[CascadingParameter] ToastManager Toasts</c> to show toasts.
/// </summary>
public sealed class ToastProvider : ComponentBase, IDisposable
{
    private ToastManager? _manager;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>A manager of your own (e.g. a scoped service, to show toasts from anywhere). Defaults to a new one.</summary>
    [Parameter] public ToastManager? ToastManager { get; set; }

    /// <summary>How long toasts stay open, in milliseconds. 0 keeps them open. Defaults to 5000.</summary>
    [Parameter] public int Timeout { get; set; } = 5000;

    /// <summary>How many toasts show at once; older ones get <c>data-limited</c>. Defaults to 3.</summary>
    [Parameter] public int Limit { get; set; } = 3;

    internal ToastManager Manager => _manager!;

    protected override void OnParametersSet()
    {
        var next = ToastManager ?? _manager ?? new ToastManager();
        if (!ReferenceEquals(next, _manager))
        {
            if (_manager is not null)
            {
                _manager.Changed -= OnChanged;
            }
            _manager = next;
            _manager.Changed += OnChanged;
        }
        _manager.DefaultTimeout = Timeout;
    }

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<ToastProvider>>(0);
        builder.AddComponentParameter(1, "Value", this);
        builder.AddComponentParameter(2, "ChildContent", (RenderFragment)(inner =>
        {
            inner.OpenComponent<CascadingValue<ToastManager>>(0);
            inner.AddComponentParameter(1, "Value", _manager);
            inner.AddComponentParameter(2, "ChildContent", ChildContent);
            inner.CloseComponent();
        }));
        builder.CloseComponent();
    }

    public void Dispose()
    {
        if (_manager is not null)
        {
            _manager.Changed -= OnChanged;
        }
    }
}

public sealed record ToastViewportRenderProps(IReadOnlyDictionary<string, object> Attributes, IReadOnlyList<ToastObject> Toasts)
    : RenderProps(Attributes);

public sealed record ToastRenderProps(IReadOnlyDictionary<string, object> Attributes, ToastObject Toast, int Index, bool Limited)
    : RenderProps(Attributes);

/// <summary>
/// A container for the toasts: a <c>role="region"</c> live region
/// in the top layer, which stays above dialogs. Its <see cref="Template"/> renders each toast.
/// Marked <c>data-expanded</c> while hovered or focused (timers pause); F6 focuses it.
/// </summary>
public class ToastViewport : PrimitivePart<ToastViewportRenderProps>, IAsyncDisposable
{
    private readonly string _hostId = Ids.Next("toast-host");
    private DotNetObjectReference<ToastInterop>? _interop;
    private JsController? _js;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] internal ToastProvider Provider { get; set; } = null!;

    /// <summary>Renders each toast (newest first).</summary>
    [Parameter] public RenderFragment<ToastObject>? Template { get; set; }

    /// <summary>The directions a toast can be swiped away in. Defaults to down and right.</summary>
    [Parameter] public IReadOnlyList<string> SwipeDirections { get; set; } = ["down", "right"];

    internal ToastManager Manager => Provider.Manager;

    internal int Limit => Provider.Limit;

    internal JsController? Js => _js;

    internal Task Dispatch(Func<Task> work) => InvokeAsync(work);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<ToastViewport>>(0);
        builder.AddComponentParameter(1, "Value", this);
        builder.AddComponentParameter(2, "IsFixed", true);
        builder.AddComponentParameter(3, "ChildContent", (RenderFragment)(inner =>
        {
            inner.OpenElement(0, "div");
            inner.AddAttribute(1, "popover", "manual");
            inner.AddAttribute(2, "id", _hostId);
            inner.AddAttribute(3, "data-blazix-popup-host", "");
            inner.AddContent(4, (RenderFragment)BuildPart);
            inner.CloseElement();
        }));
        builder.CloseComponent();
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("tabindex", "-1")
        .Set("role", "region")
        .Set("aria-live", "polite")
        .Set("aria-atomic", "false")
        .Set("aria-relevant", "additions text")
        .Default("aria-label", "Notifications")
        .Set("data-blazix-toast-viewport", "");

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        if (Template is not null)
        {
            foreach (var toast in Manager.Toasts)
            {
                KeyedContent.Add(builder, 5, toast.Id, Template(toast));
            }
        }
        builder.AddContent(6, ChildContent);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }
        _js = new JsController(JsRuntime, "toast.js", "createToasts");
        _interop = DotNetObjectReference.Create(new ToastInterop(this));
        await _js.EnsureAsync(_interop, _hostId, new Dictionary<string, object?> { ["swipeDirections"] = SwipeDirections });
    }

    protected override ToastViewportRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Manager.Toasts);

    public async ValueTask DisposeAsync()
    {
        Dispose(true);
        if (_js is not null)
        {
            await _js.DisposeAsync();
        }
        _interop?.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed class ToastInterop(ToastViewport viewport)
{
    [JSInvokable]
    public Task Close(string id) => viewport.Dispatch(() =>
    {
        viewport.Manager.Close(id);
        return Task.CompletedTask;
    });

    [JSInvokable]
    public Task Removed(string id) => viewport.Dispatch(() =>
    {
        viewport.Manager.Remove(id);
        return Task.CompletedTask;
    });
}

/// <summary>State a <see cref="ToastRoot"/> shares with its parts.</summary>
public sealed class ToastContext
{
    internal ToastContext(ToastRoot root) => Root = root;

    internal ToastRoot Root { get; }

    public ToastObject Toast => Root.Toast;

    internal string? TitleId { get; set; }

    internal string? DescriptionId { get; set; }
}

/// <summary>
/// An individual toast: a non-modal dialog (an alert dialog when high
/// priority). <c>--toast-index</c>, <c>--toast-offset-y</c> and <c>--toast-height</c> stack it;
/// swiping moves it by <c>--toast-swipe-movement-x/y</c> with <c>data-swiping</c>.
/// </summary>
public class ToastRoot : PrimitivePart<ToastRenderProps>
{
    private readonly ToastContext _context;
    private bool _closingReported;
    private bool _rendered;

    public ToastRoot() => _context = new ToastContext(this);

    [CascadingParameter] private ToastViewport Viewport { get; set; } = null!;

    /// <summary>The toast to render, from the viewport's template.</summary>
    [Parameter, EditorRequired] public ToastObject Toast { get; set; } = null!;

    private int Index => Viewport.Manager.Toasts.Where(t => !t.Closing).ToList().IndexOf(Toast);

    internal void Rerender() => StateHasChanged();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        _rendered = true;
        if (Toast.Closing && !_closingReported && Viewport.Js is not null)
        {
            _closingReported = true;
            await Viewport.Js.InvokeVoidAsync("closing", Toast.Id);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var index = Index;
        attributes
            .Set("role", Toast.Priority == ToastPriority.High ? "alertdialog" : "dialog")
            .Set("tabindex", "0")
            .Set("aria-modal", "false")
            .LabelledBy(_context.TitleId)
            .DescribedBy(_context.DescriptionId)
            .Set("data-blazix-toast", Toast.Id)
            .Set("data-timeout", Toast.Timeout.ToString(CultureInfo.InvariantCulture))
            .Set("data-type", Toast.Type)
            .Flag("data-limited", index >= Viewport.Limit)
            // In the DOM from the first render, so the toast's first style is where its transition
            // starts; toast.js removes it once the toast is laid out.
            .Flag("data-starting-style", !_rendered)
            .Flag("data-ending-style", Toast.Closing)
            .Set("style", $"--toast-index:{Math.Max(0, index).ToString(CultureInfo.InvariantCulture)}");
    }

    protected override ToastRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Toast, Index, Index >= Viewport.Limit);
}

/// <summary>A container for the toast's contents. Marked <c>data-behind</c> when it's not the frontmost toast.</summary>
public class ToastContent : PrimitivePart<PartRenderProps>
{
    [CascadingParameter] private ToastContext Toast { get; set; } = null!;

    [CascadingParameter] private ToastViewport Viewport { get; set; } = null!;

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("data-blazix-toast-content", "")
        .Flag("data-behind", Viewport.Manager.Toasts.Where(t => !t.Closing).ToList().IndexOf(Toast.Toast) > 0);

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>The toast's title. Renders the toast's title unless you give it content.</summary>
public class ToastTitle : PrimitivePart<PartRenderProps>
{
    private string _id = "";

    [CascadingParameter] private ToastContext Toast { get; set; } = null!;

    protected override string Tag => "h2";

    protected override bool IsPresent => ChildContent is not null || Toast.Toast.Title is not null;

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("toast-title"));
        if (Toast.TitleId != _id)
        {
            Toast.TitleId = _id;
            Toast.Root.Rerender();
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.Set("id", _id).Set("data-type", Toast.Toast.Type);

    protected override void BuildChildren(RenderTreeBuilder builder) => builder.AddContent(3, ChildContent ?? (b => b.AddContent(0, Toast.Toast.Title)));

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>The toast's description. Renders the toast's description unless you give it content.</summary>
public class ToastDescription : PrimitivePart<PartRenderProps>
{
    private string _id = "";

    [CascadingParameter] private ToastContext Toast { get; set; } = null!;

    protected override string Tag => "p";

    protected override bool IsPresent => ChildContent is not null || Toast.Toast.Description is not null;

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("toast-description"));
        if (Toast.DescriptionId != _id)
        {
            Toast.DescriptionId = _id;
            Toast.Root.Rerender();
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.Set("id", _id).Set("data-type", Toast.Toast.Type);

    protected override void BuildChildren(RenderTreeBuilder builder) => builder.AddContent(3, ChildContent ?? (b => b.AddContent(0, Toast.Toast.Description)));

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>Performs the toast's action. Renders only when the toast has one; the toast closes afterwards.</summary>
public class ToastAction : ButtonPart<PartRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;

    [CascadingParameter] private ToastContext Toast { get; set; } = null!;

    [CascadingParameter] private ToastViewport Viewport { get; set; } = null!;

    protected override string IdPrefix => "toast-action";

    protected override bool IsPresent => ChildContent is not null || Toast.Toast.ActionLabel is not null;

    protected override void OnInitialized() =>
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, async () =>
        {
            if (Toast.Toast.OnAction is { } action)
            {
                await action();
            }
            Viewport.Manager.Close(Toast.Toast.Id);
        });

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, false);
        attributes.Set("data-type", Toast.Toast.Type).Set("onclick", _onClick);
    }

    protected override void BuildChildren(RenderTreeBuilder builder) => builder.AddContent(3, ChildContent ?? (b => b.AddContent(0, Toast.Toast.ActionLabel)));

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>Closes the toast.</summary>
public class ToastClose : ButtonPart<PartRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;

    [CascadingParameter] private ToastContext Toast { get; set; } = null!;

    [CascadingParameter] private ToastViewport Viewport { get; set; } = null!;

    protected override string IdPrefix => "toast-close";

    protected override void OnInitialized() =>
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, () => Viewport.Manager.Close(Toast.Toast.Id));

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, false);
        attributes
            .Default("aria-label", "Close notification")
            .Set("data-type", Toast.Toast.Type)
            .Set("onclick", _onClick);
    }

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>
/// Positions a toast against an element, for anchored toasts:
/// put the <see cref="ToastRoot"/> inside it. Placement comes from the toast's
/// <see cref="ToastObject.Positioner"/> options unless set here. Like other positioners it gets
/// <c>data-side</c>, <c>data-align</c>, <c>--anchor-width</c>, <c>--available-height</c> and
/// <c>--transform-origin</c>; the toast inside gets <c>data-side</c> and <c>data-anchor-hidden</c>.
/// </summary>
public class ToastPositioner : PrimitivePart<PartRenderProps>
{
    /// <summary>The toast to position, from the viewport's template.</summary>
    [Parameter, EditorRequired] public ToastObject Toast { get; set; } = null!;

    /// <summary>The id of the element to position against, instead of the toast's.</summary>
    [Parameter] public string? Anchor { get; set; }

    /// <summary>Which side of the anchor to open on. It flips when there isn't room.</summary>
    [Parameter] public Side? Side { get; set; }

    /// <summary>How to line up with the anchor along that side.</summary>
    [Parameter] public Align? Align { get; set; }

    /// <summary>Gap between the anchor and the toast, in pixels.</summary>
    [Parameter] public int? SideOffset { get; set; }

    /// <summary>Shift along the anchor, in pixels.</summary>
    [Parameter] public int? AlignOffset { get; set; }

    /// <summary>Space to keep from the viewport edges, in pixels.</summary>
    [Parameter] public int? CollisionPadding { get; set; }

    /// <summary>Space between the arrow and the toast's corners, in pixels.</summary>
    [Parameter] public int? ArrowPadding { get; set; }

    /// <summary>Keep the toast in view when its anchor scrolls out of it.</summary>
    [Parameter] public bool? Sticky { get; set; }

    private string Placement()
    {
        var own = Toast.Positioner;
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var json = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("anchor", Anchor ?? own?.Anchor);
            json.WriteString("side", Js.Side(Side ?? own?.Side ?? Primitives.Side.Top));
            json.WriteString("align", Js.Align(Align ?? own?.Align ?? Primitives.Align.Center));
            json.WriteNumber("sideOffset", SideOffset ?? own?.SideOffset ?? 0);
            json.WriteNumber("alignOffset", AlignOffset ?? own?.AlignOffset ?? 0);
            json.WriteNumber("collisionPadding", CollisionPadding ?? own?.CollisionPadding ?? 5);
            json.WriteNumber("arrowPadding", ArrowPadding ?? own?.ArrowPadding ?? 5);
            json.WriteBoolean("sticky", Sticky ?? own?.Sticky ?? false);
            json.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("role", "presentation")
        .Set("data-blazix-toast-positioner", Toast.Id)
        .Set("data-blazix-placement", Placement());

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>
/// An arrow pointing at an anchored toast's anchor. Put it in the
/// <see cref="ToastRoot"/>; it's placed along the edge facing the anchor, with <c>data-side</c>.
/// </summary>
public class ToastArrow : SimplePart
{
    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("aria-hidden", "true")
        .Set("data-blazix-toast-arrow", "");
}

/// <summary>
/// A portal, kept for familiar markup: the viewport already renders in the browser's
/// top layer, so this renders its content where it is.
/// </summary>
public sealed class ToastPortal : ComponentBase
{
    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, ChildContent);
}
