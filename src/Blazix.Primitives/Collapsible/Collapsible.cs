using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record CollapsibleRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Open, bool Disabled)
    : RenderProps(Attributes);

public sealed record CollapsiblePanelRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Open)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="CollapsibleRoot"/>. Add <c>[CascadingParameter] CollapsibleContext</c> to build your own parts.</summary>
public sealed class CollapsibleContext
{
    internal CollapsibleContext(Disclosure disclosure, Func<bool> disabled)
    {
        Disclosure = disclosure;
        DisabledFunc = disabled;
    }

    internal Disclosure Disclosure { get; }

    private Func<bool> DisabledFunc { get; }

    public bool Open => Disclosure.IsOpen;

    public bool Disabled => DisabledFunc();

    public string TriggerId => Disclosure.TriggerId;

    public string PanelId => Disclosure.PanelId;

    internal Task ToggleAsync() => Disclosure.RequestAsync(!Disclosure.IsOpen, OpenChangeReason.TriggerPress);
}

/// <summary>A collapsible panel controlled by a button.</summary>
public class CollapsibleRoot : PrimitivePart<CollapsibleRenderProps>
{
    private Disclosure? _disclosure;
    private CollapsibleContext? _context;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    /// <summary>Whether the panel is open. Use <c>@bind-Open</c>, or set it once for an uncontrolled collapsible.</summary>
    [Parameter] public bool Open { get; set; }

    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    /// <summary>Runs before the open state changes; call <c>Cancel()</c> to keep it.</summary>
    [Parameter] public EventCallback<OpenChangeEventArgs> OnOpenChange { get; set; }

    [Parameter] public bool Disabled { get; set; }

    internal Disclosure Disclosure => _disclosure!;

    protected override void OnInitialized()
    {
        _disclosure = new Disclosure(JsRuntime, "collapsible", "collapsible-panel", RequestAsync);
        _context = new CollapsibleContext(_disclosure, () => Disabled);
    }

    public override async Task SetParametersAsync(ParameterView parameters)
    {
        // A parent closing it: pin the panel's size before the closed state renders, so it animates.
        // The parameters are applied first, since a ParameterView can't be read after an await.
        parameters.SetParameterProperties(this);
        if (_disclosure is not null && _disclosure.State.ParameterCloses(Open))
        {
            await _disclosure.PrepareCloseAsync();
        }
        await base.SetParametersAsync(ParameterView.Empty);
    }

    protected override void OnParametersSet()
    {
        if (_disclosure!.State.SyncParameter(Open) && Open)
        {
            _disclosure.Starting = _disclosure.Rendered;
        }
    }

    private async Task<bool> RequestAsync(bool open, OpenChangeReason reason)
    {
        if (open == _disclosure!.IsOpen || (Disabled && reason == OpenChangeReason.TriggerPress))
        {
            return false;
        }

        var args = new OpenChangeEventArgs(open, reason);
        await OnOpenChange.InvokeAsync(args);
        if (args.Canceled)
        {
            return false;
        }

        await _disclosure.ApplyAsync(open, reason);
        await OpenChanged.InvokeAsync(open);
        StateHasChanged();
        return true;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context!, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Flag("data-open", _disclosure!.IsOpen)
        .Flag("data-closed", !_disclosure.IsOpen)
        .Flag("data-disabled", Disabled);

    protected override CollapsibleRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, _disclosure!.IsOpen, Disabled);
}

/// <summary>A button that opens and closes the collapsible panel.</summary>
public class CollapsibleTrigger : ButtonPart<CollapsibleRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;

    [CascadingParameter] private CollapsibleContext Collapsible { get; set; } = null!;

    protected override string IdPrefix => "collapsible-trigger";

    protected override void OnInitialized() =>
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, () => Collapsible.ToggleAsync());

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        Collapsible.Disclosure.TriggerId = ButtonId;
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, Collapsible.Disabled);
        var disclosure = Collapsible.Disclosure;
        attributes
            .Set("aria-expanded", disclosure.IsOpen ? "true" : "false")
            .Set("aria-controls", disclosure is { HasPanel: true, State.IsMounted: true } ? disclosure.PanelId : null)
            .Flag("data-panel-open", disclosure.IsOpen)
            .Set("onclick", _onClick);
    }

    protected override CollapsibleRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Collapsible.Open, Collapsible.Disabled);
}

/// <summary>
/// The panel with the collapsible contents. Animate its height
/// with <c>var(--collapsible-panel-height)</c> and <c>[data-starting-style]</c>/<c>[data-ending-style]</c>.
/// </summary>
public class CollapsiblePanel : DisclosurePanelPart<CollapsiblePanelRenderProps>
{
    [CascadingParameter] private CollapsibleContext Collapsible { get; set; } = null!;

    internal override Disclosure Disclosure => Collapsible.Disclosure;

    protected override void BuildAttributes(AttributeBuilder attributes) => AddPanelAttributes(attributes);

    protected override CollapsiblePanelRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Collapsible.Open);
}
