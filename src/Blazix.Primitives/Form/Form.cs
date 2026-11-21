using JetBrains.Annotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>Cascaded from <see cref="FormRoot"/> to its fields.</summary>
public sealed class FormContext
{
    private readonly FormRoot _root;

    internal FormContext(FormRoot root) => _root = root;

    public ValidationMode ValidationMode => _root.ValidationMode;

    /// <summary>Whether the form has been submitted at least once.</summary>
    public bool Submitted => _root.SubmitCount > 0;

    internal List<FieldRoot> Fields { get; } = [];

    internal void RegisterField(FieldRoot field) => Fields.Add(field);

    internal void UnregisterField(FieldRoot field) => Fields.Remove(field);

    internal IReadOnlyList<string>? ErrorsFor(string name) => _root.ErrorsFor(name);

    internal void ClearErrors(string name) => _root.ClearErrors(name);
}

/// <summary>
/// A native form with consolidated validation. On submit every field
/// validates; if any is invalid, the first one is focused and <see cref="OnFormSubmit"/> doesn't
/// run. <see cref="Errors"/> shows server errors on the fields with those names until they change.
/// </summary>
[PublicAPI]
public class FormRoot : PrimitivePart<PartRenderProps>
{
    private readonly FormContext _context;
    private EventCallback<EventArgs> _onSubmit;
    private Dictionary<string, IReadOnlyList<string>>? _errors;
    private IReadOnlyDictionary<string, IReadOnlyList<string>>? _lastErrors;

    public FormRoot() => _context = new FormContext(this);

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    /// <summary>When fields validate. A field's own ValidationMode wins. Defaults to OnSubmit.</summary>
    [Parameter] public ValidationMode ValidationMode { get; set; } = ValidationMode.OnSubmit;

    /// <summary>Errors from outside, typically a server after a submit, keyed by field name.</summary>
    [Parameter] public IReadOnlyDictionary<string, IReadOnlyList<string>>? Errors { get; set; }

    /// <summary>Runs with every named field's value once all fields are valid.</summary>
    [Parameter] public EventCallback<IReadOnlyDictionary<string, object?>> OnFormSubmit { get; set; }

    protected override string Tag => "form";

    internal int SubmitCount { get; private set; }

    protected override void OnInitialized() => _onSubmit = EventCallback.Factory.Create<EventArgs>(this, HandleSubmitAsync);

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_lastErrors, Errors))
        {
            _lastErrors = Errors;
            _errors = Errors?.ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }

    internal IReadOnlyList<string>? ErrorsFor(string name) => _errors is not null && _errors.TryGetValue(name, out var errors) ? errors : null;

    internal void ClearErrors(string name)
    {
        if (_errors?.Remove(name) == true)
        {
            StateHasChanged();
        }
    }

    /// <summary>Validates every field, or the one with this name.</summary>
    public async Task<bool> ValidateAsync(string? name = null)
    {
        var valid = true;
        foreach (var field in _context.Fields.Where(field => name is null || field.Name == name).ToList())
        {
            valid &= await field.ValidateAsync(force: true);
        }
        return valid;
    }

    private async Task HandleSubmitAsync()
    {
        SubmitCount++;
        await ValidateAsync();
        var invalid = _context.Fields.Find(field => field.IsInvalid);
        if (invalid is not null)
        {
            await JsModules.InvokeAsync<object>(JsRuntime, JsModules.Util, "focusElement", invalid.ControlElementId);
            StateHasChanged();
            return;
        }

        var values = new Dictionary<string, object?>();
        foreach (var field in _context.Fields.Where(field => field.Name is not null))
        {
            values[field.Name!] = field.CurrentValue;
        }
        await OnFormSubmit.InvokeAsync(values);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("novalidate", true)
        .Set("onsubmit", _onSubmit);

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}
