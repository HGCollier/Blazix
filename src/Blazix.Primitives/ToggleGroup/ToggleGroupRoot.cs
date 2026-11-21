using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record ToggleGroupRenderProps(
    IReadOnlyDictionary<string, object> Attributes, IReadOnlyList<string> Value, bool Disabled, bool Multiple, Orientation Orientation)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="ToggleGroupRoot"/> to its toggles.</summary>
public sealed class ToggleGroupContext
{
    private readonly ToggleGroupRoot _root;

    internal ToggleGroupContext(ToggleGroupRoot root) => _root = root;

    public IReadOnlyList<string> Value => _root.CurrentValue;

    public bool Disabled => _root.IsDisabled;

    public bool Multiple => _root.Multiple;

    internal Task<bool> SetPressedAsync(string value, bool pressed) => _root.SetPressedAsync(value, pressed);
}

/// <summary>
/// Groups toggle buttons with one tab stop and arrow-key navigation.
/// Its value is the list of pressed toggles' values; with <see cref="Multiple"/> false at most one is pressed.
/// Inside a <see cref="ToolbarRoot"/>, its toggles join the toolbar's tab stop instead.
/// </summary>
public class ToggleGroupRoot : PrimitivePart<ToggleGroupRenderProps>
{
    private readonly Controlled<IReadOnlyList<string>> _value = new([], SequenceComparer<string>.Instance);
    private readonly ToggleGroupContext _context;
    private RovingGroup? _roving;

    public ToggleGroupRoot() => _context = new ToggleGroupContext(this);

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] private RovingGroup? ParentRoving { get; set; }

    /// <summary>The values of the pressed toggles. Use <c>@bind-Value</c>, or set it once for an uncontrolled group.</summary>
    [Parameter] public IReadOnlyList<string>? Value { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<string>?> ValueChanged { get; set; }

    /// <summary>Runs before the value changes; call <c>Cancel()</c> to keep it.</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<IReadOnlyList<string>>> OnValueChange { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Whether several toggles can be pressed at once. Defaults to false.</summary>
    [Parameter] public bool Multiple { get; set; }

    [Parameter] public Orientation Orientation { get; set; } = Orientation.Horizontal;

    /// <summary>Whether arrow keys wrap from the last toggle to the first. Defaults to true.</summary>
    [Parameter] public bool LoopFocus { get; set; } = true;

    internal IReadOnlyList<string> CurrentValue => _value.Value;

    internal bool IsDisabled => Disabled || (ParentRoving?.Disabled ?? false);

    protected override void OnParametersSet()
    {
        if (Value is not null)
        {
            _value.Sync(Value);
        }
        if (ParentRoving is null)
        {
            _roving ??= new RovingGroup(StateHasChanged, () => IsDisabled, remember: false);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && _roving is not null)
        {
            await RovingGroup.EnsureScriptAsync(JsRuntime);
        }
    }

    internal async Task<bool> SetPressedAsync(string value, bool pressed)
    {
        var current = _value.Value;
        IReadOnlyList<string> next;
        if (!pressed)
        {
            next = [.. current.Where(v => v != value)];
        }
        else
        {
            next = Multiple ? [.. current.Where(v => v != value), value] : [value];
        }

        var args = new ValueChangeEventArgs<IReadOnlyList<string>>(next, "none");
        await OnValueChange.InvokeAsync(args);
        if (args.Canceled)
        {
            return false;
        }

        _value.Value = next;
        await ValueChanged.InvokeAsync(next);
        StateHasChanged();
        return true;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (_roving is null)
        {
            Cascade(builder, _context, BuildPart);
            return;
        }
        Cascade(builder, _context, inner => Cascade(inner, _roving, BuildPart));
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("role", "group")
            .Set("data-orientation", Js.Orientation(Orientation))
            .Flag("data-disabled", IsDisabled)
            .Flag("data-multiple", Multiple);
        if (_roving is not null)
        {
            RovingGroup.AddGroupAttributes(attributes, Js.Orientation(Orientation), LoopFocus);
        }
    }

    protected override ToggleGroupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, _value.Value, IsDisabled, Multiple, Orientation);
}
