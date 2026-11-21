using JetBrains.Annotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

/// <summary>Which way a drawer is swiped to close it (it opens the other way).</summary>
public enum DrawerSwipeDirection
{
    Down,
    Up,
    Left,
    Right,
}

/// <summary>
/// A height a bottom (or top) drawer can rest at: a fraction of the viewport's height (0 to 1), a
/// number of pixels above 1, or a CSS length in <c>px</c> or <c>rem</c>, e.g. <c>0.5</c>, <c>320</c>, <c>"20rem"</c>.
/// </summary>
public readonly record struct DrawerSnapPoint
{
    private DrawerSnapPoint(object value) => Value = value;

    /// <summary>A double or a string, as the browser resolves it.</summary>
    public object Value { get; }

    public static implicit operator DrawerSnapPoint(double value) => new(value);

    public static implicit operator DrawerSnapPoint(string value) => new(value);

    public static DrawerSnapPoint FromDouble(double value) => new(value);

    public static DrawerSnapPoint FromString(string value) => new(value);

    public override string ToString() => Convert.ToString(Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
}

/// <summary>
/// Tracks the drawers inside it, so <see cref="DrawerIndent"/> and <see cref="DrawerIndentBackground"/>
/// can react while one is open, e.g. to scale the page behind.
/// </summary>
public sealed class DrawerProvider : ComponentBase
{
    private readonly HashSet<DrawerRoot> _open = [];

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Whether any drawer inside is open.</summary>
    public bool Active => _open.Count > 0;

    internal void SetOpen(DrawerRoot drawer, bool open)
    {
        var before = Active;
        if (open)
        {
            _open.Add(drawer);
        }
        else
        {
            _open.Remove(drawer);
        }
        if (before != Active)
        {
            StateHasChanged();
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<DrawerProvider>>(0);
        builder.AddComponentParameter(1, "Value", this);
        builder.AddComponentParameter(2, "ChildContent", ChildContent);
        builder.CloseComponent();
    }
}

/// <summary>
/// A panel that slides in from an edge of the screen, and can be swiped away.
/// A dialog underneath: modal by default, with focus trapped inside. While it's
/// being dragged, the popup's <c>--drawer-swipe-movement-x/y</c> and the backdrop's
/// <c>--drawer-swipe-progress</c> follow the finger, with <c>data-swiping</c>.
/// </summary>
[PublicAPI]
public class DrawerRoot() : DialogRoot("drawer")
{
    [CascadingParameter] private DrawerProvider? Provider { get; set; }

    /// <summary>Which way the drawer is swiped to close it. Defaults to down (a bottom sheet).</summary>
    [Parameter] public DrawerSwipeDirection SwipeDirection { get; set; } = DrawerSwipeDirection.Down;

    /// <summary>
    /// Heights a bottom or top drawer rests at, from the smallest. A swipe settles on the nearest one
    /// (a flick skips ahead) or closes the drawer; the popup gets <c>--drawer-snap-point-offset</c>, the
    /// distance to translate it by, and <c>data-expanded</c> at the snap point 1.
    /// </summary>
    [Parameter] public IReadOnlyList<DrawerSnapPoint>? SnapPoints { get; set; }

    /// <summary>The snap point the drawer rests at. Use with <see cref="SnapPointChanged"/> (or <c>@bind-SnapPoint</c>) when controlled.</summary>
    [Parameter] public DrawerSnapPoint? SnapPoint { get; set; }

    [Parameter] public EventCallback<DrawerSnapPoint?> SnapPointChanged { get; set; }

    /// <summary>Called when a swipe (or closing) changes the snap point; cancel it to stay put.</summary>
    [Parameter] public EventCallback<ValueChangeEventArgs<DrawerSnapPoint?>> OnSnapPointChange { get; set; }

    /// <summary>The snap point the drawer opens at, when uncontrolled. Defaults to the first.</summary>
    [Parameter] public DrawerSnapPoint? DefaultSnapPoint { get; set; }

    /// <summary>Move one snap point at a time: the drag distance decides, not how fast it was.</summary>
    [Parameter] public bool SnapToSequentialPoints { get; set; }

    private DrawerSnapPoint? _snapPoint;
    private bool _snapSeeded;

    // Counts swipes the browser settled on a snap point, so each one renders a new BehaviorKey.
    private int _snapSwipes;

    /// <summary>The snap point the drawer rests at now, or null without snap points.</summary>
    public DrawerSnapPoint? ActiveSnapPoint => SnapPoints is { Count: > 0 } ? _snapPoint ?? InitialSnapPoint : null;

    private DrawerSnapPoint? InitialSnapPoint => DefaultSnapPoint ?? (SnapPoints is { Count: > 0 } points ? (DrawerSnapPoint?)points[0] : null);

    internal int ActiveSnapIndex => ActiveSnapPoint is { } point && SnapPoints is { } points ? IndexOf(points, point) : -1;

    internal bool IsExpanded => ActiveSnapPoint is { Value: double value } && Math.Abs(value - 1) < 1e-9;

    private static int IndexOf(IReadOnlyList<DrawerSnapPoint> points, DrawerSnapPoint point)
    {
        for (var i = 0; i < points.Count; i++)
        {
            if (points[i] == point)
            {
                return i;
            }
        }
        return -1;
    }

    private bool SnapControlled => SnapPointChanged.HasDelegate;

    private async Task SetSnapPointAsync(DrawerSnapPoint? next, string reason)
    {
        if (next == ActiveSnapPoint)
        {
            return;
        }
        var args = new ValueChangeEventArgs<DrawerSnapPoint?>(next, reason);
        await OnSnapPointChange.InvokeAsync(args);
        if (args.Canceled)
        {
            StateHasChanged();
            return;
        }
        if (!SnapControlled)
        {
            _snapPoint = next;
        }
        await SnapPointChanged.InvokeAsync(next);
        StateHasChanged();
    }

    internal string? SwipeAreaId { get; set; }

    /// <summary>Set by <see cref="DrawerVirtualKeyboardProvider"/>.</summary>
    internal bool KeyboardAware { get; set; }

    internal string SwipeDirectionName => SwipeDirection switch
    {
        DrawerSwipeDirection.Up => "up",
        DrawerSwipeDirection.Left => "left",
        DrawerSwipeDirection.Right => "right",
        _ => "down",
    };

    protected override string Module => "drawer.js";

    protected override string Factory => "createDrawer";

    // A snap point's offset is measured in the browser, so a drawer with them shows once that's done.
    internal override bool PlacedByMeasuring => SnapPoints is { Count: > 0 };

    private protected override Dictionary<string, object?> TriggerOptions() => new()
    {
        ["swipeDirection"] = SwipeDirectionName,
        ["swipeAreaId"] = SwipeAreaId,
    };

    internal override Dictionary<string, object?> BehaviorOptions()
    {
        var options = base.BehaviorOptions();
        options["snapPoints"] = SnapPoints?.Select(point => point.Value).ToArray();
        options["snapIndex"] = ActiveSnapIndex;
        options["sequential"] = SnapToSequentialPoints;
        options["keyboard"] = KeyboardAware;
        return options;
    }

    // The options above that change while open. The popup's viewport, when there is one, shows it,
    // so the key is the root's rather than the popup's.
    internal override string BehaviorKey => string.Join('|', ActiveSnapIndex, _snapSwipes, SnapToSequentialPoints, KeyboardAware,
        SnapPoints is { } points ? string.Join(';', points) : "");

    internal override async Task OnNotifyAsync(string name, string? value)
    {
        if (name == "snap" && SnapPoints is { } points && int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var index) && index >= 0 && index < points.Count)
        {
            // The browser has already moved the drawer there. Render where it rests now, even if that
            // hasn't changed (the change was canceled, or is controlled and wasn't taken), so the
            // browser moves it back.
            _snapSwipes++;
            await SetSnapPointAsync(points[index], "swipe");
            StateHasChanged();
            return;
        }
        await base.OnNotifyAsync(name, value);
    }

    internal override async Task<bool> RequestOpenChangeAsync(bool open, OpenChangeReason reason)
    {
        var changed = await base.RequestOpenChangeAsync(open, reason);
        if (changed)
        {
            Provider?.SetOpen(this, open);
        }
        if (changed && !open && SnapPoints is { Count: > 0 })
        {
            // A closed drawer opens at its default snap point again.
            await SetSnapPointAsync(InitialSnapPoint, "close");
        }
        return changed;
    }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        Provider?.SetOpen(this, IsOpen);
        if (SnapControlled)
        {
            _snapPoint = SnapPoint;
        }
        else if (!_snapSeeded)
        {
            _snapSeeded = true;
            _snapPoint = SnapPoint;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // A swipe area can open the drawer before any trigger has, so the browser side starts with the page.
        if (firstRender && SwipeAreaId is not null)
        {
            await EnsureJsAsync();
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<DrawerRoot>>(0);
        builder.AddComponentParameter(1, "Value", this);
        builder.AddComponentParameter(2, "ChildContent", (RenderFragment)base.BuildRenderTree);
        builder.CloseComponent();
    }

    public override ValueTask DisposeAsync()
    {
        Provider?.SetOpen(this, false);
        return base.DisposeAsync();
    }
}

/// <summary>
/// The drawer itself: a dialog in the top layer, marked with
/// <c>data-swipe-direction</c>, <c>data-swiping</c> while dragged and <c>data-swipe-dismiss</c> when
/// swiped away. Position it against its edge yourself, and translate it by the swipe movement variables.
/// </summary>
public class DrawerPopup : DialogPopup
{
    [CascadingParameter] private DrawerRoot Drawer { get; set; } = null!;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        base.BuildAttributes(attributes);
        attributes
            .Set("data-blazix-drawer", "")
            .Set("data-swipe-direction", Drawer.SwipeDirectionName)
            .Flag("data-expanded", Drawer.IsExpanded)
            .Flag("data-snap-points", Drawer.SnapPoints is { Count: > 0 })
            .Flag("data-nested-drawer-open", Drawer.NestedOpenCount > 0)
            .Set("style", $"--nested-dialogs:{Nested};--nested-drawers:{Nested}");
    }

    private string Nested => Drawer.NestedOpenCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The drawer's contents. Content that scrolls keeps the swipe until it's scrolled to the edge.</summary>
public class DrawerContent : SimplePart
{
    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.Set("data-drawer-content", "");
}

/// <summary>
/// An invisible area along the screen's edge where swiping opens the drawer.
/// Position it yourself; it renders <c>data-swipe-direction</c>.
/// </summary>
public class DrawerSwipeArea : PrimitivePart<PartRenderProps>
{
    private string _id = "";

    [CascadingParameter] private DrawerRoot Drawer { get; set; } = null!;

    [Parameter] public bool Disabled { get; set; }

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("drawer-swipe-area"));
        Drawer.SwipeAreaId = _id;
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Set("role", "presentation")
        .Set("data-swipe-direction", Drawer.SwipeDirectionName)
        .Flag("data-disabled", Disabled)
        .Set("style", "touch-action:none");

    protected override PartRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes);
}

/// <summary>
/// Keeps a bottom sheet's form fields usable with a software keyboard:
/// while the keyboard covers part of the screen, the
/// viewport (or the popup, without a <see cref="DrawerViewport"/>) gets <c>--drawer-keyboard-inset</c>,
/// its height, to lift the sheet by; a focused field scrolls into the visible part, and the page
/// behind doesn't scroll. Put it inside the <see cref="DrawerRoot"/>.
/// </summary>
public sealed class DrawerVirtualKeyboardProvider : ComponentBase, IDisposable
{
    [CascadingParameter] private DrawerRoot Drawer { get; set; } = null!;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized() => Drawer.KeyboardAware = true;

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, ChildContent);

    public void Dispose() => Drawer.KeyboardAware = false;
}

/// <summary>Wraps your page content, marked <c>data-active</c> while a drawer is open, e.g. to scale it back.</summary>
public class DrawerIndent : SimplePart
{
    [CascadingParameter] private DrawerProvider? Provider { get; set; }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var active = Provider?.Active ?? false;
        attributes.Flag("data-active", active).Flag("data-inactive", !active);
    }
}

#pragma warning disable S2094 // Aliases: the Drawer parts are Dialog's parts under the Drawer name.
/// <summary>A background behind the indented page, marked <c>data-active</c> while a drawer is open.</summary>
public class DrawerIndentBackground : DrawerIndent;

/// <summary>A button that opens the drawer.</summary>
public class DrawerTrigger : DialogTrigger;

/// <summary>An overlay beneath the drawer. Fade it with <c>--drawer-swipe-progress</c>.</summary>
public class DrawerBackdrop : DialogBackdrop;

/// <summary>A container for the drawer's popup.</summary>
public class DrawerViewport : DialogViewport;

/// <summary>A heading that labels the drawer.</summary>
public class DrawerTitle : DialogTitle;

/// <summary>A paragraph describing the drawer.</summary>
public class DrawerDescription : DialogDescription;

/// <summary>A button that closes the drawer.</summary>
public class DrawerClose : DialogClose;
/// <summary>A portal for the popup.</summary>
public class DrawerPortal : DialogPortal;
#pragma warning restore S2094

/// <summary>
/// Connects triggers outside the <see cref="DrawerRoot"/> to it:
/// create one, pass it to the root's and the triggers' <c>Handle</c>,
/// and open it from code with <see cref="PopupHandle.OpenAsync"/>.
/// </summary>
#pragma warning disable S2094 // A typed name for the shared handle.
public class DrawerHandle : PopupHandle;
#pragma warning restore S2094
