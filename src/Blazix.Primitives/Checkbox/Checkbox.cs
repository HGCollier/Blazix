using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

public sealed record CheckboxRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Checked, bool Indeterminate, bool Disabled, bool ReadOnly, bool Required)
    : RenderProps(Attributes);

/// <summary>State a <see cref="CheckboxRoot"/> shares with its indicator.</summary>
public sealed class CheckboxContext
{
    internal CheckboxContext(CheckboxRoot root) => Root = root;

    internal CheckboxRoot Root { get; }

    public bool Checked => Root.CurrentChecked;

    public bool Indeterminate => Root.CurrentIndeterminate;

    public bool Disabled => Root.CurrentDisabled;
}

/// <summary>
/// An easily stylable checkbox: a &lt;span role="checkbox"&gt;
/// with a hidden native checkbox beside it. Inside a <see cref="CheckboxGroupRoot"/>, its
/// <see cref="Value"/> joins the group's value; <see cref="Parent"/> makes it check them all.
/// </summary>
public class CheckboxRoot : CheckableRoot<CheckboxRenderProps>
{
    private readonly Controlled<bool> _checked = new(false);
    private readonly CheckboxContext _context;

    public CheckboxRoot() => _context = new CheckboxContext(this);

    [CascadingParameter] private CheckboxGroupContext? Group { get; set; }

    /// <summary>Whether it's checked. Use <c>@bind-Checked</c>, or set it once for an uncontrolled checkbox.</summary>
    [Parameter] public bool Checked { get; set; }

    [Parameter] public EventCallback<bool> CheckedChanged { get; set; }

    /// <summary>Runs before the checked state changes; call <c>Cancel()</c> to keep it.</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<bool>> OnCheckedChange { get; set; }

    /// <summary>Neither checked nor unchecked (aria-checked="mixed"). Defaults to false.</summary>
    [Parameter] public bool Indeterminate { get; set; }

    /// <summary>The value submitted with the form when checked, and the checkbox's value in a group. Defaults to "on".</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>The value submitted when unchecked. By default nothing is submitted.</summary>
    [Parameter] public string? UncheckedValue { get; set; }

    /// <summary>In a <see cref="CheckboxGroupRoot"/> with <c>AllValues</c>: checks or unchecks every checkbox in the group.</summary>
    [Parameter] public bool Parent { get; set; }

    internal bool CurrentChecked => IsChecked;

    internal bool CurrentIndeterminate => IsIndeterminate;

    internal bool CurrentDisabled => IsDisabled;

    protected override string Role => "checkbox";

    protected override string InputType => "checkbox";

    protected override bool RegistersWithField => Group is null;

    protected override bool IsChecked
    {
        get
        {
            if (Group is null)
            {
                return _checked.Value;
            }
            if (Parent)
            {
                return Group.AllChecked;
            }
            return Value is not null ? Group.Value.Contains(Value) : _checked.Value;
        }
    }

    private bool IsIndeterminate => Parent && Group is not null ? Group.SomeChecked : Indeterminate;

    protected override string AriaChecked => IsIndeterminate ? "mixed" : base.AriaChecked;

    protected override string? InputValue => Value;

    protected override bool IsDisabled => base.IsDisabled || (Group?.Disabled ?? false);

    protected override void OnParametersSet()
    {
        _checked.Sync(Checked);
        base.OnParametersSet();
    }

    protected override async Task ToggleAsync(bool? checkedByInput)
    {
        var next = checkedByInput ?? !(IsChecked && !IsIndeterminate);
        var args = new ValueChangeEventArgs<bool>(next, "none");
        await OnCheckedChange.InvokeAsync(args);
        if (args.Canceled)
        {
            StateHasChanged();
            return;
        }

        if (Group is not null && Parent)
        {
            await Group.SetAllAsync(next);
        }
        else if (Group is not null && Value is not null && !await Group.SetCheckedAsync(Value, next))
        {
            StateHasChanged();
            return;
        }

        _checked.Value = next;
        await CheckedChanged.InvokeAsync(next);
        await ReportValueAsync(next);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        Cascade(builder, _context, BuildPart);
        BuildHiddenInput(builder);
        if (UncheckedValue is not null && !IsChecked && EffectiveName is not null)
        {
            // Submits the unchecked value in place of the checkbox, which submits nothing unchecked.
            builder.OpenElement(40, "input");
            builder.AddAttribute(41, "type", "hidden");
            builder.AddAttribute(42, "name", EffectiveName);
            builder.AddAttribute(43, "value", UncheckedValue);
            builder.CloseElement();
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddCheckableAttributes(attributes);
        AddState(attributes, IsChecked, IsIndeterminate);
        attributes.Flag("data-parent", Parent);
    }

    internal static void AddState(AttributeBuilder attributes, bool isChecked, bool indeterminate) => attributes
        .Flag("data-checked", isChecked && !indeterminate)
        .Flag("data-unchecked", !isChecked && !indeterminate)
        .Flag("data-indeterminate", indeterminate);

    protected override CheckboxRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsChecked, IsIndeterminate, IsDisabled, ReadOnly, Required);
}

/// <summary>Indicates whether the checkbox is ticked. Renders a &lt;span&gt; while checked or indeterminate.</summary>
public class CheckboxIndicator : PrimitivePart<CheckboxRenderProps>
{
    [CascadingParameter] private CheckboxContext Checkbox { get; set; } = null!;

    [CascadingParameter] private FieldContext? Field { get; set; }

    /// <summary>Render while unchecked too (e.g. to animate it out). Defaults to false.</summary>
    [Parameter] public bool KeepMounted { get; set; }

    protected override string Tag => "span";

    protected override bool IsPresent => KeepMounted || Checkbox.Checked || Checkbox.Indeterminate;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes.FieldState(Field, Checkbox.Disabled);
        CheckboxRoot.AddState(attributes, Checkbox.Checked, Checkbox.Indeterminate);
        attributes
            .Flag("data-readonly", Checkbox.Root.ReadOnly)
            .Flag("data-required", Checkbox.Root.Required);
    }

    protected override CheckboxRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Checkbox.Checked, Checkbox.Indeterminate, Checkbox.Disabled, Checkbox.Root.ReadOnly, Checkbox.Root.Required);
}
