using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record AccordionRenderProps(IReadOnlyDictionary<string, object> Attributes, IReadOnlyList<string> Value, bool Disabled, Orientation Orientation)
    : RenderProps(Attributes);

public sealed record AccordionItemRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Open, bool Disabled, int Index)
    : RenderProps(Attributes);

public sealed record AccordionPanelRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Open, int Index)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="AccordionRoot"/>.</summary>
public sealed class AccordionContext
{
    private readonly AccordionRoot _root;

    internal AccordionContext(AccordionRoot root) => _root = root;

    public IReadOnlyList<string> Value => _root.CurrentValue;

    public bool Disabled => _root.Disabled;

    public Orientation Orientation => _root.Orientation;

    internal bool KeepMounted => _root.KeepMounted;

    internal bool HiddenUntilFound => _root.HiddenUntilFound;

    internal List<AccordionItem> Items { get; } = [];

    internal Task<bool> SetItemOpenAsync(AccordionItem item, bool open) => _root.SetItemOpenAsync(item, open);
}

/// <summary>
/// A set of collapsible panels with headings. Its value is the list
/// of open items' values; with <see cref="Multiple"/> false at most one is open. Arrow keys move
/// between the triggers.
/// </summary>
public class AccordionRoot : PrimitivePart<AccordionRenderProps>
{
    private readonly Controlled<IReadOnlyList<string>> _value = new([], SequenceComparer<string>.Instance);
    private readonly AccordionContext _context;

    public AccordionRoot() => _context = new AccordionContext(this);

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    /// <summary>The values of the open items. Use <c>@bind-Value</c>, or set it once for an uncontrolled accordion.</summary>
    [Parameter] public IReadOnlyList<string>? Value { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<string>?> ValueChanged { get; set; }

    /// <summary>Runs before the value changes; call <c>Cancel()</c> to keep it.</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<IReadOnlyList<string>>> OnValueChange { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Whether several items can be open at once. Defaults to false.</summary>
    [Parameter] public bool Multiple { get; set; }

    [Parameter] public Orientation Orientation { get; set; } = Orientation.Vertical;

    /// <summary>Whether arrow keys wrap from the last trigger to the first. Defaults to true.</summary>
    [Parameter] public bool LoopFocus { get; set; } = true;

    /// <summary>Keep closed panels in the DOM. Defaults to false.</summary>
    [Parameter] public bool KeepMounted { get; set; }

    /// <summary>Keep closed panels findable with find in page (<c>hidden="until-found"</c>). Defaults to false.</summary>
    [Parameter] public bool HiddenUntilFound { get; set; }

    internal IReadOnlyList<string> CurrentValue => _value.Value;

    public override async Task SetParametersAsync(ParameterView parameters)
    {
        // A parent closing items: pin their panels' sizes before the closed state renders. The
        // parameters are applied first, since a ParameterView can't be read after an await.
        var previous = Value;
        parameters.SetParameterProperties(this);
        if (Value is not null && !SequenceComparer<string>.Instance.Equals(Value, previous)
            && !SequenceComparer<string>.Instance.Equals(Value, _value.Value))
        {
            await PrepareClosingAsync(Value);
        }
        await base.SetParametersAsync(ParameterView.Empty);
    }

    protected override void OnParametersSet()
    {
        if (Value is not null)
        {
            _value.Sync(Value);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RovingGroup.EnsureScriptAsync(JsRuntime);
        }
    }

    private async Task PrepareClosingAsync(IReadOnlyList<string> next)
    {
        foreach (var item in _context.Items.Where(item => item.Disclosure.IsOpen && !next.Contains(item.ItemValue)))
        {
            await item.Disclosure.PrepareCloseAsync();
        }
    }

    internal async Task<bool> SetItemOpenAsync(AccordionItem item, bool open)
    {
        var current = _value.Value;
        IReadOnlyList<string> next;
        if (!open)
        {
            next = [.. current.Where(v => v != item.ItemValue)];
        }
        else
        {
            next = Multiple ? [.. current.Where(v => v != item.ItemValue), item.ItemValue] : [item.ItemValue];
        }

        var args = new ValueChangeEventArgs<IReadOnlyList<string>>(next, "trigger-press");
        await OnValueChange.InvokeAsync(args);
        if (args.Canceled)
        {
            return false;
        }

        await PrepareClosingAsync(next);
        _value.Value = next;
        await ValueChanged.InvokeAsync(next);
        StateHasChanged();
        return true;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("data-orientation", Js.Orientation(Orientation))
            .Flag("data-disabled", Disabled);
        RovingGroup.AddGroupAttributes(attributes, Js.Orientation(Orientation), LoopFocus);
    }

    protected override AccordionRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, _value.Value, Disabled, Orientation);
}

/// <summary>Groups an accordion header with its panel.</summary>
public class AccordionItem : PrimitivePart<AccordionItemRenderProps>
{
    private Disclosure? _disclosure;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] private AccordionContext Accordion { get; set; } = null!;

    /// <summary>Identifies the item in the accordion's value. Defaults to a generated id.</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Runs before this item opens or closes; call <c>Cancel()</c> to keep it.</summary>
    [Parameter] public EventCallback<OpenChangeEventArgs> OnOpenChange { get; set; }

    internal string ItemValue => Value ?? (field ??= Ids.Next("accordion-item"));

    internal Disclosure Disclosure => _disclosure!;

    internal bool IsDisabled => Disabled || Accordion.Disabled;

    internal int Index => Accordion.Items.IndexOf(this);

    internal AccordionContext Root => Accordion;

    protected override void OnInitialized()
    {
        _disclosure = new Disclosure(JsRuntime, "accordion", "accordion-panel", RequestAsync);
        Accordion.Items.Add(this);
    }

    protected override void OnParametersSet()
    {
        var open = Accordion.Value.Contains(ItemValue);
        if (open != _disclosure!.IsOpen)
        {
            _disclosure.State.Apply(open, OpenChangeReason.TriggerPress);
            if (open)
            {
                _disclosure.Starting = _disclosure.Rendered;
            }
        }
    }

    private async Task<bool> RequestAsync(bool open, OpenChangeReason reason)
    {
        if (IsDisabled && reason == OpenChangeReason.TriggerPress)
        {
            return false;
        }
        var args = new OpenChangeEventArgs(open, reason);
        await OnOpenChange.InvokeAsync(args);
        return !args.Canceled && await Accordion.SetItemOpenAsync(this, open);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, this, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("data-index", Index)
        .Flag("data-open", _disclosure!.IsOpen)
        .Flag("data-disabled", IsDisabled);

    protected override AccordionItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, _disclosure!.IsOpen, IsDisabled, Index);

    protected override void Dispose(bool disposing)
    {
        Accordion.Items.Remove(this);
        base.Dispose(disposing);
    }
}

/// <summary>A heading that labels the panel. Renders an &lt;h3&gt;.</summary>
public class AccordionHeader : PrimitivePart<AccordionItemRenderProps>
{
    [CascadingParameter] private AccordionItem Item { get; set; } = null!;

    protected override string Tag => "h3";

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("data-index", Item.Index)
        .Flag("data-open", Item.Disclosure.IsOpen)
        .Flag("data-disabled", Item.IsDisabled);

    protected override AccordionItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Item.Disclosure.IsOpen, Item.IsDisabled, Item.Index);
}

/// <summary>A button that opens and closes its item's panel.</summary>
public class AccordionTrigger : ButtonPart<AccordionItemRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;

    [CascadingParameter] private AccordionItem Item { get; set; } = null!;

    protected override string IdPrefix => "accordion-trigger";

    protected override void OnInitialized() =>
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, () => Item.Disclosure.RequestAsync(!Item.Disclosure.IsOpen, OpenChangeReason.TriggerPress));

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        Item.Disclosure.TriggerId = ButtonId;
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var disclosure = Item.Disclosure;
        AddButtonAttributes(attributes, Item.IsDisabled);
        attributes
            .Set("aria-expanded", disclosure.IsOpen ? "true" : "false")
            .Set("aria-controls", disclosure is { IsOpen: true, HasPanel: true } ? disclosure.PanelId : null)
            .Set("data-index", Item.Index)
            .Flag("data-panel-open", disclosure.IsOpen)
            .Flag("data-disabled", Item.IsDisabled)
            .Set("data-blazix-roving-item", "")
            .Set("onclick", _onClick);
    }

    protected override AccordionItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Item.Disclosure.IsOpen, Item.IsDisabled, Item.Index);
}

/// <summary>
/// The panel with an item's contents. Animate its height with
/// <c>var(--accordion-panel-height)</c> and <c>[data-starting-style]</c>/<c>[data-ending-style]</c>.
/// </summary>
public class AccordionPanel : DisclosurePanelPart<AccordionPanelRenderProps>
{
    [CascadingParameter] private AccordionItem Item { get; set; } = null!;

    internal override Disclosure Disclosure => Item.Disclosure;

    private protected override bool HiddenUntilFoundDefault => Item.Root.HiddenUntilFound;

    protected override bool IsPresent => base.IsPresent || Item.Root.KeepMounted;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddPanelAttributes(attributes);
        attributes
            .Set("role", "region")
            .Set("aria-labelledby", Item.Disclosure.TriggerId)
            .Set("data-index", Item.Index)
            .Set("data-orientation", Js.Orientation(Item.Root.Orientation))
            .Flag("data-disabled", Item.IsDisabled);
    }

    protected override AccordionPanelRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Item.Disclosure.IsOpen, Item.Index);
}
