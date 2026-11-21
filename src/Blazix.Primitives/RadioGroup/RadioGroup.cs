using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record RadioGroupRenderProps(IReadOnlyDictionary<string, object> Attributes, string? Value, bool Disabled, bool ReadOnly, bool Required)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="RadioGroupRoot"/> to its radios.</summary>
public sealed class RadioGroupContext
{
    private readonly RadioGroupRoot _root;

    internal RadioGroupContext(RadioGroupRoot root) => _root = root;

    public string? Value => _root.CurrentValue;

    public bool Disabled => _root.IsDisabled;

    public bool ReadOnly => _root.ReadOnly;

    public bool Required => _root.Required;

    internal string Name => _root.EffectiveName;

    internal RovingGroup Roving => _root.Roving;

    internal void Register(RadioRoot radio) => _root.Register(radio);

    internal void Unregister(RadioRoot radio) => _root.Unregister(radio);

    internal Task SelectAsync(string value) => _root.SelectAsync(value);

    internal Task FocusAsync() => _root.FieldFocusAsync(true);

    internal Task BlurAsync() => _root.FieldFocusAsync(false);
}

/// <summary>
/// Provides a shared state to a series of radio buttons. One tab stop:
/// arrow keys move to the next radio and check it.
/// </summary>
public class RadioGroupRoot : PrimitivePart<RadioGroupRenderProps>
{
    private readonly Controlled<string?> _value = new(null);
    private readonly RadioGroupContext _context;
    private readonly List<RadioRoot> _radios = [];
    private RovingGroup? _roving;
    private string _id = "";

    public RadioGroupRoot() => _context = new RadioGroupContext(this);

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] private FieldContext? Field { get; set; }

    [CascadingParameter] private FieldsetContext? Fieldset { get; set; }

    /// <summary>The checked radio's value. Use <c>@bind-Value</c>, or set it once for an uncontrolled group.</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    [Parameter] public EventCallback<ValueChangeEventArgs<string?>> OnValueChange { get; set; }

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public bool ReadOnly { get; set; }

    [Parameter] public bool Required { get; set; }

    /// <summary>The name submitted with the form. Defaults to the field's name.</summary>
    [Parameter] public string? Name { get; set; }

    internal string? CurrentValue => _value.Value;

    internal bool IsDisabled => Disabled || (Field?.Disabled ?? false) || (Fieldset?.Disabled ?? false);

    internal string EffectiveName => Name ?? Field?.Name ?? _id;

    internal RovingGroup Roving => _roving!;

    protected override void OnInitialized() => _roving = new RovingGroup(StateHasChanged, () => IsDisabled, remember: false);

    protected override void OnParametersSet()
    {
        _value.Sync(Value);
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("radio-group"));
        RegisterWithField();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RovingGroup.EnsureScriptAsync(JsRuntime);
        }
    }

    private void RegisterWithField()
    {
        if (Field is { IsItem: false })
        {
            var first = _radios.FirstOrDefault();
            var focus = _radios.Find(radio => radio.RadioValue == _value.Value) ?? first;
            Field.RegisterControl(new FieldControlRegistration(_id, first?.InputElementId, () => _value.Value, focus?.RootElementId));
        }
    }

    internal void Register(RadioRoot radio)
    {
        if (!_radios.Contains(radio))
        {
            _radios.Add(radio);
            RegisterWithField();
        }
    }

    internal void Unregister(RadioRoot radio) => _radios.Remove(radio);

    internal Task FieldFocusAsync(bool focused)
    {
        if (Field is null)
        {
            return Task.CompletedTask;
        }
        return focused ? Field.FocusAsync() : Field.BlurAsync();
    }

    internal async Task SelectAsync(string value)
    {
        if (IsDisabled || ReadOnly || _value.Value == value)
        {
            return;
        }
        var args = new ValueChangeEventArgs<string?>(value, "none");
        await OnValueChange.InvokeAsync(args);
        if (args.Canceled)
        {
            StateHasChanged();
            return;
        }
        _value.Value = value;
        await ValueChanged.InvokeAsync(value);
        if (Field is not null)
        {
            await Field.ValueChangedAsync(value);
        }
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        Cascade(builder, _context, inner => Cascade(inner, _roving!, BuildPart, isFixed: true));

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("id", _id)
            .Set("role", "radiogroup")
            .Set("aria-required", Required ? "true" : null)
            .Set("aria-disabled", IsDisabled ? "true" : null)
            .Set("aria-readonly", ReadOnly ? "true" : null)
            .LabelledBy(Field is { HasLabel: true } ? Field.LabelId : null)
            .FieldState(Field, IsDisabled)
            .FieldAria(Field, labelledByLabel: false);
        RovingGroup.AddGroupAttributes(attributes, "both", loop: true, activateOnFocus: true);
    }

    protected override RadioGroupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, _value.Value, IsDisabled, ReadOnly, Required);
}
