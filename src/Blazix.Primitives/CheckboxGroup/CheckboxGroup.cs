using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

public sealed record CheckboxGroupRenderProps(IReadOnlyDictionary<string, object> Attributes, IReadOnlyList<string> Value, bool Disabled)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="CheckboxGroupRoot"/> to its checkboxes.</summary>
public sealed class CheckboxGroupContext
{
    private readonly CheckboxGroupRoot _root;

    internal CheckboxGroupContext(CheckboxGroupRoot root) => _root = root;

    public IReadOnlyList<string> Value => _root.CurrentValue;

    public bool Disabled => _root.IsDisabled;

    internal bool AllChecked => _root.AllValues is { Count: > 0 } all && all.All(Value.Contains);

    internal bool SomeChecked => !AllChecked && _root.AllValues is { Count: > 0 } all && all.Any(Value.Contains);

    internal Task<bool> SetCheckedAsync(string value, bool isChecked) => _root.SetCheckedAsync(value, isChecked);

    internal Task SetAllAsync(bool isChecked) => _root.SetAllAsync(isChecked);
}

/// <summary>
/// Provides a shared state to a series of checkboxes. Its value is
/// the list of checked checkboxes' values.
/// </summary>
public class CheckboxGroupRoot : PrimitivePart<CheckboxGroupRenderProps>
{
    private readonly Controlled<IReadOnlyList<string>> _value = new([], SequenceComparer<string>.Instance);
    private readonly CheckboxGroupContext _context;
    private string _id = "";

    public CheckboxGroupRoot() => _context = new CheckboxGroupContext(this);

    [CascadingParameter] private FieldContext? Field { get; set; }

    [CascadingParameter] private FieldsetContext? Fieldset { get; set; }

    /// <summary>The checked checkboxes' values. Use <c>@bind-Value</c>, or set it once for an uncontrolled group.</summary>
    [Parameter] public IReadOnlyList<string>? Value { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<string>?> ValueChanged { get; set; }

    [Parameter] public EventCallback<ValueChangeEventArgs<IReadOnlyList<string>>> OnValueChange { get; set; }

    /// <summary>Every checkbox's value, for a <see cref="CheckboxRoot.Parent"/> checkbox that checks them all.</summary>
    [Parameter] public IReadOnlyList<string>? AllValues { get; set; }

    [Parameter] public bool Disabled { get; set; }

    internal IReadOnlyList<string> CurrentValue => _value.Value;

    internal bool IsDisabled => Disabled || (Field?.Disabled ?? false) || (Fieldset?.Disabled ?? false);

    protected override void OnParametersSet()
    {
        if (Value is not null)
        {
            _value.Sync(Value);
        }
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("checkbox-group"));
        if (Field is { IsItem: false })
        {
            Field.RegisterControl(new FieldControlRegistration(_id, null, () => _value.Value));
        }
    }

    internal Task<bool> SetCheckedAsync(string value, bool isChecked)
    {
        IReadOnlyList<string> next = isChecked
            ? [.. _value.Value.Where(v => v != value), value]
            : [.. _value.Value.Where(v => v != value)];
        return SetValueAsync(next);
    }

    internal Task SetAllAsync(bool isChecked) => SetValueAsync(isChecked ? [.. AllValues ?? []] : []);

    private async Task<bool> SetValueAsync(IReadOnlyList<string> next)
    {
        var args = new ValueChangeEventArgs<IReadOnlyList<string>>(next, "none");
        await OnValueChange.InvokeAsync(args);
        if (args.Canceled)
        {
            return false;
        }
        _value.Value = next;
        await ValueChanged.InvokeAsync(next);
        if (Field is not null)
        {
            await Field.ValueChangedAsync(next);
        }
        StateHasChanged();
        return true;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Set("role", "group")
        .LabelledBy(Field is { HasLabel: true } ? Field.LabelId : null)
        .FieldState(Field, IsDisabled);

    protected override CheckboxGroupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, _value.Value, IsDisabled);
}
