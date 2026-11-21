using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Blazix.Primitives;

public sealed record ToggleRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Pressed, bool Disabled)
    : RenderProps(Attributes);

/// <summary>
/// A two-state button that can be on or off. Renders a &lt;button&gt; with
/// <c>aria-pressed</c> and <c>data-pressed</c>. Inside a <see cref="ToggleGroupRoot"/> its
/// <see cref="Value"/> joins the group's value.
/// </summary>
public class ToggleRoot : ButtonPart<ToggleRenderProps>
{
    private readonly Controlled<bool> _pressed = new(false);
    private EventCallback<MouseEventArgs> _onClick;
    private EventCallback<FocusEventArgs> _onFocus;
    private RovingItem? _item;

    [CascadingParameter] private ToggleGroupContext? Group { get; set; }

    [CascadingParameter] private RovingGroup? Roving { get; set; }

    /// <summary>Whether the toggle is pressed. Use <c>@bind-Pressed</c>, or set it once for an uncontrolled toggle.</summary>
    [Parameter] public bool Pressed { get; set; }

    [Parameter] public EventCallback<bool> PressedChanged { get; set; }

    /// <summary>Runs before the pressed state changes; call <c>Cancel()</c> to keep it.</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<bool>> OnPressedChange { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Identifies the toggle in a <see cref="ToggleGroupRoot"/>'s value.</summary>
    [Parameter] public string? Value { get; set; }

    protected override string IdPrefix => "toggle";

    protected override bool IsComposite => Roving is not null;

    private bool IsPressed => Group is not null && Value is not null ? Group.Value.Contains(Value) : _pressed.Value;

    private bool IsDisabled => Disabled || (Group?.Disabled ?? false) || (Roving?.Disabled ?? false);

    protected override void OnInitialized()
    {
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, HandleClickAsync);
        _onFocus = EventCallback.Factory.Create<FocusEventArgs>(this, () => _item is not null && Roving is not null ? FocusedAsync() : Task.CompletedTask);
    }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        _pressed.Sync(Pressed);
        if (Roving is not null)
        {
            _item ??= new RovingItem(ButtonId, () => IsDisabled, () => IsPressed);
            _item.Id = ButtonId;
            Roving.Register(_item);
        }
    }

    private Task FocusedAsync()
    {
        Roving!.Focused(_item!);
        return Task.CompletedTask;
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, IsDisabled);
        attributes
            .Set("aria-pressed", IsPressed ? "true" : "false")
            .Flag("data-pressed", IsPressed)
            .Set("onclick", _onClick);
        if (Roving is not null && _item is not null)
        {
            Roving.AddItemAttributes(attributes, _item, _onFocus);
        }
    }

    protected override ToggleRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsPressed, IsDisabled);

    private async Task HandleClickAsync()
    {
        if (IsDisabled)
        {
            return;
        }

        var next = !IsPressed;
        var args = new ValueChangeEventArgs<bool>(next, "none");
        await OnPressedChange.InvokeAsync(args);
        if (args.Canceled)
        {
            return;
        }

        if (Group is not null && Value is not null && !await Group.SetPressedAsync(Value, next))
        {
            return;
        }

        _pressed.Value = next;
        await PressedChanged.InvokeAsync(next);
    }

    protected override void Dispose(bool disposing)
    {
        if (_item is not null)
        {
            Roving?.Unregister(_item);
        }
        base.Dispose(disposing);
    }
}
