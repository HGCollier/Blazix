using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record ToolbarRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Disabled, Orientation Orientation)
    : RenderProps(Attributes);

public sealed record ToolbarItemRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Disabled, Orientation Orientation, bool Focusable)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="ToolbarRoot"/>.</summary>
public sealed class ToolbarContext
{
    private readonly ToolbarRoot _root;

    internal ToolbarContext(ToolbarRoot root) => _root = root;

    public bool Disabled => _root.Disabled;

    public Orientation Orientation => _root.Orientation;
}

/// <summary>
/// A container for grouping a set of controls, such as buttons, toggle groups or menus, with one tab
/// stop and arrow keys between them (the WAI-ARIA toolbar pattern).
/// </summary>
public class ToolbarRoot : PrimitivePart<ToolbarRenderProps>
{
    private readonly ToolbarContext _context;
    private readonly RovingGroup _roving;

    public ToolbarRoot()
    {
        _context = new ToolbarContext(this);
        _roving = new RovingGroup(StateHasChanged, () => Disabled);
    }

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public Orientation Orientation { get; set; } = Orientation.Horizontal;

    /// <summary>Whether arrow keys wrap from the last item to the first. Defaults to true.</summary>
    [Parameter] public bool LoopFocus { get; set; } = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RovingGroup.EnsureScriptAsync(JsRuntime);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        Cascade(builder, _context, inner => Cascade(inner, _roving, BuildPart));

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("role", "toolbar")
            .Set("aria-orientation", Js.Orientation(Orientation))
            .Set("data-orientation", Js.Orientation(Orientation))
            .Flag("data-disabled", Disabled);
        RovingGroup.AddGroupAttributes(attributes, Js.Orientation(Orientation), LoopFocus);
    }

    protected override ToolbarRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Disabled, Orientation);
}

/// <summary>Groups several toolbar items. Disabling it disables its items.</summary>
public class ToolbarGroup : PrimitivePart<ToolbarRenderProps>
{
    private RovingGroup? _scope;

    [CascadingParameter] private ToolbarContext Toolbar { get; set; } = null!;

    [CascadingParameter] private RovingGroup Roving { get; set; } = null!;

    [Parameter] public bool Disabled { get; set; }

    protected override void OnParametersSet() => _scope ??= Roving.Scope(() => Disabled);

    private bool IsDisabled => Disabled || Toolbar.Disabled;

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _scope!, BuildPart, isFixed: true);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("role", "group")
        .Set("data-orientation", Js.Orientation(Toolbar.Orientation))
        .Flag("data-disabled", IsDisabled);

    protected override ToolbarRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsDisabled, Toolbar.Orientation);
}

/// <summary>A button in a toolbar. Stays focusable while disabled by default.</summary>
public class ToolbarButton : ButtonPart<ToolbarItemRenderProps>
{
    private RovingItem? _item;
    private EventCallback<FocusEventArgs> _onFocus;

    [CascadingParameter] private ToolbarContext Toolbar { get; set; } = null!;

    [CascadingParameter] private RovingGroup Roving { get; set; } = null!;

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Keep the button reachable with the arrow keys while disabled. Defaults to true.</summary>
    [Parameter] public bool FocusableWhenDisabled { get; set; } = true;

    protected override bool IsComposite => true;

    protected override string IdPrefix => "toolbar-button";

    private bool IsDisabled => Disabled || Roving.Disabled;

    protected override void OnInitialized() =>
        _onFocus = EventCallback.Factory.Create<FocusEventArgs>(this, () => Roving.Focused(_item!));

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        _item ??= new RovingItem(ButtonId, () => IsDisabled, () => false, () => FocusableWhenDisabled);
        _item.Id = ButtonId;
        Roving.Register(_item);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, IsDisabled, FocusableWhenDisabled);
        Roving.AddItemAttributes(attributes, _item!, _onFocus, FocusableWhenDisabled);
        attributes.Set("data-orientation", Js.Orientation(Toolbar.Orientation));
    }

    protected override ToolbarItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsDisabled, Toolbar.Orientation, FocusableWhenDisabled);

    protected override void Dispose(bool disposing)
    {
        if (_item is not null)
        {
            Roving.Unregister(_item);
        }
        base.Dispose(disposing);
    }
}

/// <summary>A link in a toolbar. Renders an &lt;a&gt;.</summary>
public class ToolbarLink : PrimitivePart<ToolbarItemRenderProps>
{
    private RovingItem? _item;
    private EventCallback<FocusEventArgs> _onFocus;
    private string _id = "";

    [CascadingParameter] private ToolbarContext Toolbar { get; set; } = null!;

    [CascadingParameter] private RovingGroup Roving { get; set; } = null!;

    protected override string Tag => "a";

    protected override void OnInitialized() =>
        _onFocus = EventCallback.Factory.Create<FocusEventArgs>(this, () => Roving.Focused(_item!));

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("toolbar-link"));
        _item ??= new RovingItem(_id, () => false, () => false);
        _item.Id = _id;
        Roving.Register(_item);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes.Set("id", _id).Set("data-orientation", Js.Orientation(Toolbar.Orientation));
        Roving.AddItemAttributes(attributes, _item!, _onFocus);
    }

    protected override ToolbarItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, false, Toolbar.Orientation, true);

    protected override void Dispose(bool disposing)
    {
        if (_item is not null)
        {
            Roving.Unregister(_item);
        }
        base.Dispose(disposing);
    }
}

/// <summary>A native input in a toolbar. Arrow keys move its caret until it reaches an end.</summary>
public class ToolbarInput : PrimitivePart<ToolbarItemRenderProps>
{
    private RovingItem? _item;
    private EventCallback<FocusEventArgs> _onFocus;
    private string _id = "";

    [CascadingParameter] private ToolbarContext Toolbar { get; set; } = null!;

    [CascadingParameter] private RovingGroup Roving { get; set; } = null!;

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Keep the input reachable with the arrow keys while disabled. Defaults to true.</summary>
    [Parameter] public bool FocusableWhenDisabled { get; set; } = true;

    protected override string Tag => "input";

    protected override bool IsVoid => true;

    private bool IsDisabled => Disabled || Roving.Disabled;

    protected override void OnInitialized() =>
        _onFocus = EventCallback.Factory.Create<FocusEventArgs>(this, () => Roving.Focused(_item!));

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("toolbar-input"));
        _item ??= new RovingItem(_id, () => IsDisabled, () => false, () => FocusableWhenDisabled);
        _item.Id = _id;
        Roving.Register(_item);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("id", _id)
            .Set("data-orientation", Js.Orientation(Toolbar.Orientation))
            .Flag("data-disabled", IsDisabled);
        if (FocusableWhenDisabled)
        {
            attributes.Set("aria-disabled", IsDisabled ? "true" : null).Set("readonly", IsDisabled ? true : null);
        }
        else
        {
            attributes.Set("disabled", IsDisabled);
        }
        Roving.AddItemAttributes(attributes, _item!, _onFocus, FocusableWhenDisabled);
    }

    protected override ToolbarItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsDisabled, Toolbar.Orientation, FocusableWhenDisabled);

    protected override void Dispose(bool disposing)
    {
        if (_item is not null)
        {
            Roving.Unregister(_item);
        }
        base.Dispose(disposing);
    }
}

/// <summary>A separator between toolbar items, oriented across the toolbar.</summary>
public class ToolbarSeparator : SimplePart
{
    [CascadingParameter] private ToolbarContext Toolbar { get; set; } = null!;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var across = Toolbar.Orientation == Orientation.Horizontal ? Orientation.Vertical : Orientation.Horizontal;
        attributes
            .Set("role", "separator")
            .Set("aria-orientation", Js.Orientation(across))
            .Set("data-orientation", Js.Orientation(across));
    }
}
