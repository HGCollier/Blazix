using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record MenubarRenderProps(IReadOnlyDictionary<string, object> Attributes, Orientation Orientation, bool Modal, bool HasSubmenuOpen)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="MenubarRoot"/> to the menus inside it.</summary>
public sealed class MenubarContext
{
    private readonly MenubarRoot _root;
    private readonly List<MenuRoot> _menus = [];

    internal MenubarContext(MenubarRoot root) => _root = root;

    public bool Disabled => _root.Disabled;

    public bool Modal => _root.Modal;

    public Orientation Orientation => _root.Orientation;

    /// <summary>Whether one of the menus is open.</summary>
    public bool HasOpenMenu => _menus.Exists(menu => menu.IsOpen);

    internal string Id => _root.ElementId;

    internal void Register(MenuRoot menu)
    {
        if (!_menus.Contains(menu))
        {
            _menus.Add(menu);
        }
    }

    internal void Unregister(MenuRoot menu) => _menus.Remove(menu);

    /// <summary>One menu open at a time: opening one closes the others, and hovering switches between them.</summary>
    internal async Task MenuOpenChangedAsync(MenuRoot menu, bool open)
    {
        if (open)
        {
            foreach (var other in _menus.Where(other => other != menu && other.IsOpen).ToList())
            {
                await other.RequestOpenChangeAsync(false, OpenChangeReason.SiblingOpen);
            }
        }
        _root.Refresh();
    }

    /// <summary>Left and Right inside an open menu move to the neighbouring menu and open it.</summary>
    internal async Task NavigateAsync(MenuRoot from, int direction)
    {
        var enabled = _menus.Where(menu => !menu.IsDisabled || menu == from).ToList();
        var index = enabled.IndexOf(from);
        if (index < 0 || enabled.Count < 2)
        {
            return;
        }
        var count = enabled.Count;
        var next = enabled[(((index + direction) % count) + count) % count];
        await from.RequestOpenChangeAsync(false, OpenChangeReason.SiblingOpen);
        next.PendingFocus = "first";
        await next.RequestOpenChangeAsync(true, OpenChangeReason.ListNavigation);
    }
}

/// <summary>
/// The container for menus that are always visible, like an application's menu bar.
/// Its triggers share one tab stop with arrow keys between them; once a menu is
/// open, hovering another trigger switches to it, and Left/Right move between the menus.
/// </summary>
public class MenubarRoot : PrimitivePart<MenubarRenderProps>
{
    private readonly MenubarContext _context;
    private readonly RovingGroup _roving;
    private string _id = "";

    public MenubarRoot()
    {
        _context = new MenubarContext(this);
        _roving = new RovingGroup(StateHasChanged, () => Disabled);
    }

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    /// <summary>Whether the menus are modal while open. Defaults to true.</summary>
    [Parameter] public bool Modal { get; set; } = true;

    [Parameter] public bool Disabled { get; set; }

    [Parameter] public Orientation Orientation { get; set; } = Orientation.Horizontal;

    /// <summary>Whether arrow keys wrap from the last trigger to the first. Defaults to true.</summary>
    [Parameter] public bool LoopFocus { get; set; } = true;

    internal string ElementId => _id;

    internal void Refresh() => StateHasChanged();

    protected override void OnParametersSet() =>
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("menubar"));

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RovingGroup.EnsureScriptAsync(JsRuntime);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        Cascade(builder, _context, inner => Cascade(inner, _roving, BuildPart, isFixed: true));

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes
            .Set("id", _id)
            .Set("role", "menubar")
            .Set("aria-orientation", Js.Orientation(Orientation))
            .Set("data-orientation", Js.Orientation(Orientation))
            .Flag("data-modal", Modal)
            .Flag("data-has-submenu-open", _context.HasOpenMenu)
            .Flag("data-disabled", Disabled);
        RovingGroup.AddGroupAttributes(attributes, Js.Orientation(Orientation), LoopFocus);
    }

    protected override MenubarRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Orientation, Modal, _context.HasOpenMenu);
}
