using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

public sealed record SwitchRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Checked, bool Disabled, bool ReadOnly, bool Required)
    : RenderProps(Attributes);

/// <summary>State a <see cref="SwitchRoot"/> shares with its thumb.</summary>
public sealed class SwitchContext
{
    internal SwitchContext(SwitchRoot root) => Root = root;

    internal SwitchRoot Root { get; }

    public bool Checked => Root.CurrentChecked;
}

/// <summary>
/// A control that indicates whether a setting is on or off: a
/// &lt;span role="switch"&gt; with a hidden native checkbox beside it.
/// </summary>
public class SwitchRoot : CheckableRoot<SwitchRenderProps>
{
    private readonly Controlled<bool> _checked = new(false);
    private readonly SwitchContext _context;

    public SwitchRoot() => _context = new SwitchContext(this);

    /// <summary>Whether it's on. Use <c>@bind-Checked</c>, or set it once for an uncontrolled switch.</summary>
    [Parameter] public bool Checked { get; set; }

    [Parameter] public EventCallback<bool> CheckedChanged { get; set; }

    [Parameter] public EventCallback<ValueChangeEventArgs<bool>> OnCheckedChange { get; set; }

    /// <summary>The value submitted with the form when on. Defaults to "on".</summary>
    [Parameter] public string? Value { get; set; }

    /// <summary>The value submitted when off. By default nothing is submitted.</summary>
    [Parameter] public string? UncheckedValue { get; set; }

    internal bool CurrentChecked => _checked.Value;

    internal bool CurrentDisabled => IsDisabled;

    protected override string Role => "switch";

    protected override string InputType => "checkbox";

    protected override bool IsChecked => _checked.Value;

    protected override string? InputValue => Value;

    protected override void OnParametersSet()
    {
        _checked.Sync(Checked);
        base.OnParametersSet();
    }

    protected override async Task ToggleAsync(bool? checkedByInput)
    {
        var next = checkedByInput ?? !_checked.Value;
        var args = new ValueChangeEventArgs<bool>(next, "none");
        await OnCheckedChange.InvokeAsync(args);
        if (args.Canceled)
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
        attributes.Flag("data-checked", IsChecked).Flag("data-unchecked", !IsChecked);
    }

    protected override SwitchRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsChecked, IsDisabled, ReadOnly, Required);
}

/// <summary>The movable part of the switch that indicates whether it's on.</summary>
public class SwitchThumb : PrimitivePart<SwitchRenderProps>
{
    [CascadingParameter] private SwitchContext Switch { get; set; } = null!;

    [CascadingParameter] private FieldContext? Field { get; set; }

    protected override string Tag => "span";

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .FieldState(Field, Switch.Root.CurrentDisabled)
        .Flag("data-checked", Switch.Checked)
        .Flag("data-unchecked", !Switch.Checked)
        .Flag("data-readonly", Switch.Root.ReadOnly)
        .Flag("data-required", Switch.Root.Required);

    protected override SwitchRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Switch.Checked, Switch.Root.CurrentDisabled, Switch.Root.ReadOnly, Switch.Root.Required);
}
