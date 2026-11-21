using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

public sealed record PopupRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Open)
    : RenderProps(Attributes);

public sealed record PopupTriggerRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Open, bool Disabled)
    : RenderProps(Attributes);

/// <summary>How a popup holds focus and the page while it's open.</summary>
public enum PopupModal
{
    /// <summary>The page stays usable: no focus trap, no scroll lock.</summary>
    False,
    /// <summary>Focus is trapped inside, the page behind is inert and doesn't scroll, and an outside press only closes it.</summary>
    True,
    /// <summary>Focus is trapped inside, but the page can still scroll and be pressed.</summary>
    TrapFocus,
}

/// <summary>
/// The open state every popup root shares (Popover, Tooltip, Preview Card, Dialog, Menu...):
/// controlled or uncontrolled <c>Open</c>, cancelable <c>OnOpenChange</c>, presence (the popup stays
/// mounted until its exit animation ends), and the browser-side controller that reports intent
/// (Escape, outside press, hover...) for C# to decide.
/// </summary>
public abstract class PopupRootBase(string prefix) : ComponentBase, IAsyncDisposable
{
    private DotNetObjectReference<PopupInterop>? _interop;
    private JsController? _js;
    private string? _jsTriggerKey;

    [Inject] protected IJSRuntime JsRuntime { get; set; } = null!;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Whether the popup is open. Use <c>@bind-Open</c>, or leave it unset and let it manage itself.</summary>
    [Parameter] public bool Open { get; set; }

    [Parameter] public EventCallback<bool> OpenChanged { get; set; }

    /// <summary>Runs before the popup opens or closes, with the reason; call <c>Cancel()</c> to keep it as it is.</summary>
    [Parameter] public EventCallback<OpenChangeEventArgs> OnOpenChange { get; set; }

    /// <summary>Runs once the open or close animation has finished.</summary>
    [Parameter] public EventCallback<bool> OnOpenChangeComplete { get; set; }

    /// <summary>
    /// Connects triggers outside this root to it: give the same
    /// handle to the triggers, wherever they are. Several triggers can share the popup; it opens
    /// against the one that opened it, with that trigger's <c>Payload</c>.
    /// </summary>
    [Parameter] public PopupHandle? Handle { get; set; }

    /// <summary>The content to render with the active trigger's payload, instead of <see cref="ChildContent"/>.</summary>
    [Parameter] public RenderFragment<object?>? ContentTemplate { get; set; }

    /// <summary>The payload of the trigger that opened the popup.</summary>
    public object? Payload { get; private set; }

    private List<string> TriggerIds { get; } = [];

    private readonly Dictionary<string, object?> _payloads = [];

    /// <summary>"left", "right" or "none": where the active trigger is relative to the previous one.</summary>
    internal string TriggerActivationDirection { get; private set; } = "none";

    /// <summary>What the root renders inside its cascades: the content template with the payload, or the child content.</summary>
    protected RenderFragment? BodyContent => ContentTemplate is not null ? ContentTemplate(Payload) : ChildContent;

    internal void RegisterTrigger(string id, object? payload)
    {
        if (!TriggerIds.Contains(id))
        {
            TriggerIds.Add(id);
        }
        _payloads[id] = payload;
        if (!HasTrigger)
        {
            HasTrigger = true;
            TriggerId = id;
        }
        if (id == TriggerId)
        {
            Payload = payload;
        }
    }

    internal void UnregisterTrigger(string id)
    {
        TriggerIds.Remove(id);
        _payloads.Remove(id);
    }

    /// <summary>Makes this trigger the one the popup opens against, with its payload.</summary>
    internal void Activate(string triggerId)
    {
        if (triggerId == TriggerId || !TriggerIds.Contains(triggerId))
        {
            return;
        }
        var from = TriggerIds.IndexOf(TriggerId);
        var to = TriggerIds.IndexOf(triggerId);
        TriggerActivationDirection = DirectionOf(from, to);
        TriggerId = triggerId;
        Payload = _payloads.GetValueOrDefault(triggerId);
        StateHasChanged();
    }

    private static string DirectionOf(int from, int to)
    {
        if (from < 0)
        {
            return "none";
        }
        return to > from ? "right" : "left";
    }

    internal PopupState State { get; } = new();

    /// <summary>Whether the popup is open.</summary>
    public bool IsOpen => State.IsOpen;

    internal string Prefix { get; } = prefix;

    internal string TriggerId { get; set; } = Ids.Next($"{prefix}-trigger");

    internal string PopupId { get; set; } = Ids.Next($"{prefix}-popup");

    internal string HostId { get; } = Ids.Next($"{prefix}-positioner");

    internal string? BackdropHostId { get; set; }

    internal string? ArrowId { get; set; }

    internal string? TitleId { get; set; }

    internal string? DescriptionId { get; set; }

    internal bool HasTrigger { get; set; }

    internal virtual bool IsDisabled => false;

    /// <summary>The browser-side module and factory for this kind of popup.</summary>
    protected virtual string Module => "popup-root.js";

    protected virtual string Factory => "createPopupRoot";

    /// <summary>Trigger behaviors the module needs before the popup opens (hover delays...).</summary>
    private protected virtual Dictionary<string, object?> TriggerOptions() => [];

    /// <summary>Behavior options for showing the popup (modal, focus, dismissal...).</summary>
    internal abstract Dictionary<string, object?> BehaviorOptions();

    /// <summary>
    /// Changes when a behavior option changes while the popup is open (a drawer's snap point...), so the
    /// part that shows it, the popup or its viewport, sends the options again. Null when none do.
    /// </summary>
    internal virtual string? BehaviorKey => null;

    /// <summary>
    /// Whether a popup that's open on this root's first render still plays its enter transition.
    /// Defaults to false: content that mounts open appears as it is.
    /// </summary>
    private protected virtual bool AnimatesInitialOpen => false;

    /// <summary>
    /// Whether the popup's place depends on what the browser side measures (a drawer's snap points),
    /// so it can't show before .NET calls show. Anchored popups never can.
    /// </summary>
    internal virtual bool PlacedByMeasuring => false;

    protected override void OnParametersSet()
    {
        Handle?.Attach(this);
        if (State.SyncParameter(Open, AnimatesInitialOpen) && IsOpen && IsDisabled)
        {
            State.Apply(false, OpenChangeReason.Disabled);
        }
    }

    internal async Task EnsureJsAsync()
    {
        _js ??= new JsController(JsRuntime, Module, Factory);
        _interop ??= DotNetObjectReference.Create(new PopupInterop(this));
        var options = TriggerOptions();
        options["triggerIds"] = TriggerIds.ToArray();
        var key = TriggerId + "|" + string.Join(';', TriggerIds) + "|" + string.Join(',', options.Select(pair => $"{pair.Key}={pair.Value}"));
        if (_js.IsCreated)
        {
            // A new trigger element, or options that changed (a menubar switching menus on hover).
            if (_jsTriggerKey != key)
            {
                _jsTriggerKey = key;
                await _js.InvokeVoidAsync("setTrigger", TriggerId, options);
            }
            return;
        }
        _jsTriggerKey = key;
        await _js.EnsureAsync(_interop, TriggerId, options);
    }

    internal async Task InvokeJsAsync(string method, params object?[] args)
    {
        await EnsureJsAsync();
        await _js!.InvokeVoidAsync(method, args);
    }

    internal async Task<T?> InvokeJsAsync<[DynamicallyAccessedMembers(Js.Deserialized)] T>(string method, params object?[] args)
    {
        await EnsureJsAsync();
        return await _js!.InvokeAsync<T>(method, args);
    }

    /// <summary>Opens or closes the popup, as a user action would.</summary>
    public Task<bool> SetOpenAsync(bool open) => RequestOpenChangeAsync(open, OpenChangeReason.ImperativeAction);

    internal virtual async Task<bool> RequestOpenChangeAsync(bool open, OpenChangeReason reason)
    {
        if (open == State.IsOpen || (open && IsDisabled))
        {
            return false;
        }

        var args = new OpenChangeEventArgs(open, reason);
        await OnOpenChange.InvokeAsync(args);
        if (args.Canceled)
        {
            return false;
        }

        State.Apply(open, reason);
        if (!open && State.RestoresFocus)
        {
            // Hand focus back before the popup turns inert and closed, so it never drops to <body>.
            await InvokeJsAsync("restoreFocus");
        }
        await OpenChanged.InvokeAsync(open);
        StateHasChanged();
        return true;
    }

    internal async Task ShowAsync(Dictionary<string, object?> options)
    {
        State.Shown();
        await InvokeJsAsync("show", options);
        await OnOpenChangeComplete.InvokeAsync(true);
    }

    internal async Task<bool> HideAsync() => await InvokeJsAsync<bool>("hide", State.RestoresFocus);

    internal void CompleteExit()
    {
        if (State.CompleteExit())
        {
            StateHasChanged();
            _ = InvokeAsync(async () =>
            {
                await OnExitCompleteAsync();
                await OnOpenChangeComplete.InvokeAsync(false);
            });
        }
    }

    /// <summary>The popup finished closing (its exit animation is done).</summary>
    private protected virtual Task OnExitCompleteAsync() => Task.CompletedTask;

    internal virtual Task OnRequestOpenAsync(string reason, string? detail) =>
        RequestOpenChangeAsync(true, PopupState.ParseReason(reason));

    /// <summary>One of several triggers asked to open the popup: it becomes the active one.</summary>
    internal Task OnRequestOpenFromAsync(string reason, string? detail, string triggerId)
    {
        var switching = IsOpen && triggerId != TriggerId;
        Activate(triggerId);
        return switching ? Task.CompletedTask : OnRequestOpenAsync(reason, detail);
    }

    internal virtual Task OnRequestCloseAsync(string reason) =>
        RequestOpenChangeAsync(false, PopupState.ParseReason(reason));

    /// <summary>Component-specific notifications from the browser (highlight moves, menubar navigation...).</summary>
    internal virtual Task OnNotifyAsync(string name, string? value) => Task.CompletedTask;

    internal void Rerender() => StateHasChanged();

    // Calls from the browser may arrive outside the renderer's sync context.
    internal Task Dispatch(Func<Task> work) => InvokeAsync(work);

    public virtual async ValueTask DisposeAsync()
    {
        // Disposal runs while the renderer builds the batch that removes this popup. Yield, so the
        // browser gets that batch before the browser side lets go: a popup rendered in its place,
        // such as a drawer for a dialog at a breakpoint, is already showing by then.
        await Task.Yield();
        if (_js is not null)
        {
            await _js.DisposeAsync();
        }
        _interop?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The attributes every trigger renders: popup relationship and open state.</summary>
    internal void AddTriggerAttributes(AttributeBuilder attributes, string haspopup, string? triggerId = null)
    {
        // With several triggers, only the one the popup is open against is expanded.
        var open = IsOpen && (triggerId is null || triggerId == TriggerId);
        attributes
            .Set("aria-haspopup", haspopup)
            .Set("aria-expanded", open ? "true" : "false")
            .Set("aria-controls", open ? PopupId : null)
            .Flag("data-popup-open", open)
            .Flag("data-pressed", open && State.Reason == OpenChangeReason.TriggerPress);
    }

    /// <summary>Open/closed and transition attributes every popup part renders.</summary>
    internal void AddOpenAttributes(AttributeBuilder attributes) => attributes
        .Flag("data-open", IsOpen)
        .Flag("data-closed", !IsOpen)
        .Flag("data-ending-style", State.IsExiting);

    internal static string? ModalOption(PopupModal modal) => modal switch
    {
        PopupModal.True => "true",
        PopupModal.TrapFocus => "trap-focus",
        _ => null,
    };
}

/// <summary>The only surface the browser can call on a popup root.</summary>
internal sealed class PopupInterop(PopupRootBase root)
{
    [JSInvokable]
    public Task RequestOpen(string reason, string? detail) => root.Dispatch(() => root.OnRequestOpenAsync(reason, detail));

    [JSInvokable]
    public Task RequestOpenFrom(string reason, string? detail, string triggerId) =>
        root.Dispatch(() => root.OnRequestOpenFromAsync(reason, detail, triggerId));

    [JSInvokable]
    public Task RequestClose(string reason) => root.Dispatch(() => root.OnRequestCloseAsync(reason));

    [JSInvokable]
    public Task Notify(string name, string? value) => root.Dispatch(() => root.OnNotifyAsync(name, value));
}

/// <summary>
/// The popup part of a popup root: renders the internal top-layer host (the positioner for anchored
/// popups, a zero-size fixed box for dialogs) around the element you style, and shows/hides it
/// from what was rendered.
/// </summary>
public abstract class PopupPartBase<TRoot, TRenderProps> : PrimitivePart<TRenderProps>
    where TRoot : PopupRootBase where TRenderProps : RenderProps
{
    private readonly PopupPresence _presence = new();

    [CascadingParameter] protected TRoot Root { get; set; } = null!;

    /// <summary>Keep the popup in the DOM (hidden) while closed. Defaults to false.</summary>
    [Parameter] public bool KeepMounted { get; set; }

    /// <summary>Which element gets focus when the popup opens: a CSS selector inside the popup, "popup", or "none". Defaults to the first tabbable element.</summary>
    [Parameter] public string? InitialFocus { get; set; }

    /// <summary>The id of the element that gets focus when the popup closes. Defaults to the trigger.</summary>
    [Parameter] public string? FinalFocus { get; set; }

    [CascadingParameter] private PortalSettings? Portal { get; set; }

    /// <summary>Kept in the DOM while closed: by this part, or by the Portal around it.</summary>
    private bool IsKeptMounted => KeepMounted || (Portal is { KeepMounted: true } portal && ReferenceEquals(portal.Root, Root));

    /// <summary>
    /// Whether the browser shows the host the moment Blazor inserts it, before .NET calls show: a
    /// popup that positions itself and is open since its root's first render, so it has no enter
    /// transition to set up. A drawer that takes a dialog's place at a breakpoint replaces it in the
    /// same frame, with no blank frames between them.
    /// </summary>
    private bool AppearsOnInsert =>
        !Anchored && !IsKeptMounted && Root is { IsOpen: true, PlacedByMeasuring: false, State.AnimatesEnter: false };

    /// <summary>Attributes for the host when <see cref="PositionerAttributes"/> isn't set (from a Positioner part).</summary>
    protected virtual IReadOnlyDictionary<string, object>? HostAttributes => null;

    /// <summary>Whether the host is the anchored positioner (true) or a zero-size box the popup positions itself in.</summary>
    protected virtual bool Anchored => true;

    protected virtual string? PlacementKey => null;

    // What the browser was shown with: a change to the placement or to the root's options while open shows it again.
    private string? ShowKey => Root.BehaviorKey is { } behavior ? $"{PlacementKey}|{behavior}" : PlacementKey;

    protected virtual Dictionary<string, object?>? Placement() => null;

    /// <summary>Extra attributes on the internal host element (e.g. a z-index class when not using the top layer).</summary>
    [Parameter] public IReadOnlyDictionary<string, object>? PositionerAttributes { get; set; }

    protected override bool IsPresent => true;

    /// <summary>Whether this part is the popup element (its id is the popup's), rather than a wrapper around it.</summary>
    protected virtual bool OwnsPopupId => true;

    protected override void OnParametersSet()
    {
        if (OwnsPopupId)
        {
            Root.PopupId = ElementAttributes.IdOr(AdditionalAttributes, () => Root.PopupId);
        }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        _presence.Snapshot(Root.State.IsOpen, ShowKey);
        if (!Root.State.IsMounted && !IsKeptMounted)
        {
            return;
        }

        builder.OpenElement(0, "div");
        builder.AddMultipleAttributes(1, PositionerAttributes ?? HostAttributes);
        builder.AddAttribute(2, "popover", "manual");
        builder.AddAttribute(3, "id", Root.HostId);
        builder.AddAttribute(4, "role", "presentation");
        builder.AddAttribute(5, "data-blazix-popup-host", "");
        builder.AddAttribute(6, "data-open", Root.IsOpen ? "" : null);
        builder.AddAttribute(7, "data-closed", Root.IsOpen ? null : "");
        builder.AddAttribute(8, "inert", Root.State.IsExiting);
        builder.AddAttribute(9, "data-blazix-appear", AppearsOnInsert);
        AddHostAttributes(builder);
        builder.AddContent(12, (RenderFragment)BuildPart);
        builder.CloseElement();
    }

    /// <summary>Extra attributes on the host (sequence numbers 10 and 11).</summary>
    protected virtual void AddHostAttributes(RenderTreeBuilder builder)
    {
    }

    private Dictionary<string, object?> ShowOptions()
    {
        var hosts = new List<string>();
        if (Root.BackdropHostId is not null)
        {
            hosts.Add(Root.BackdropHostId);
        }
        hosts.Add(Root.HostId);

        var options = Root.BehaviorOptions();
        options["hosts"] = hosts;
        options["backdrops"] = Root.BackdropHostId is null ? Array.Empty<string>() : new[] { Root.BackdropHostId };
        options["popupId"] = Root.PopupId;
        options["positionerId"] = Anchored ? Root.HostId : null;
        options["arrowId"] = Root.ArrowId;
        options["placement"] = Anchored ? Placement() : null;
        options["finalFocusId"] = FinalFocus;
        options["animate"] = Root.State.AnimatesEnter;
        if (InitialFocus is not null)
        {
            options["initialFocus"] = InitialFocus;
        }
        return options;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender) =>
        await _presence.AfterRenderAsync(
            show: () => Root.ShowAsync(ShowOptions()),
            hide: Root.HideAsync,
            isOpen: () => Root.State.IsOpen,
            isExiting: () => Root.State.IsExiting,
            completeExit: Root.CompleteExit);
}

/// <summary>A popup positioned against its trigger: placement parameters and the positioner.</summary>
public abstract class AnchoredPopupPart<TRoot, TRenderProps> : PopupPartBase<TRoot, TRenderProps>
    where TRoot : PopupRootBase where TRenderProps : RenderProps
{
    /// <summary>Which side of the anchor to open on. It flips when there isn't room. Defaults per component (usually bottom).</summary>
    [Parameter] public Side? Side { get; set; }

    /// <summary>How to line up with the anchor along that side. Defaults per component (usually center).</summary>
    [Parameter] public Align? Align { get; set; }

    /// <summary>Gap between the anchor and the popup, in pixels.</summary>
    [Parameter] public int? SideOffset { get; set; }

    /// <summary>Shift along the anchor, in pixels.</summary>
    [Parameter] public int? AlignOffset { get; set; }

    /// <summary>Space to keep from the viewport edges, in pixels. Defaults to 5.</summary>
    [Parameter] public int? CollisionPadding { get; set; }

    /// <summary>Space between the arrow and the popup's corners, in pixels. Defaults to 5.</summary>
    [Parameter] public int ArrowPadding { get; set; } = 5;

    /// <summary>Keep the popup in view when its anchor scrolls out of it. Defaults to false.</summary>
    [Parameter] public bool Sticky { get; set; }

    /// <summary>The id of an element to position against instead of the trigger.</summary>
    [Parameter] public string? Anchor { get; set; }

    /// <summary>
    /// Whether a popup with room on neither side of its axis may move to the other axis, e.g. from
    /// below the trigger to its right.
    /// Defaults per component: popovers, tooltips and preview cards may; menus don't.
    /// </summary>
    [Parameter] public bool? FallbackAxis { get; set; }

    protected virtual Side DefaultSide => Primitives.Side.Bottom;

    protected virtual Align DefaultAlign => Primitives.Align.Center;

    protected virtual int DefaultSideOffset => 0;

    protected virtual int DefaultAlignOffset => 0;

    protected virtual int DefaultCollisionPadding => 5;

    [CascadingParameter] private PositionerSettings? PositionerSettings { get; set; }

    // A Positioner part's settings, when it belongs to this popup's root (not an outer popup's).
    private PositionerSettings? Own => PositionerSettings is { } settings && ReferenceEquals(settings.Root, Root) ? settings : null;

    /// <summary>The side asked for (this part's, else its Positioner's), before defaults.</summary>
    protected Side? RequestedSide => Side ?? Own?.Side;

    private Side ResolvedSide => RequestedSide ?? DefaultSide;

    private Align ResolvedAlign => Align ?? Own?.Align ?? DefaultAlign;

    private int ResolvedSideOffset => SideOffset ?? Own?.SideOffset ?? DefaultSideOffset;

    private int ResolvedAlignOffset => AlignOffset ?? Own?.AlignOffset ?? DefaultAlignOffset;

    private int ResolvedCollisionPadding => CollisionPadding ?? Own?.CollisionPadding ?? DefaultCollisionPadding;

    private int ResolvedArrowPadding => Own?.ArrowPadding ?? ArrowPadding;

    private bool ResolvedSticky => Sticky || (Own?.Sticky ?? false);

    private string? ResolvedAnchor => Anchor ?? Own?.Anchor;

    protected override IReadOnlyDictionary<string, object>? HostAttributes => Own?.Attributes;

    private bool ResolvedFallbackAxis => FallbackAxis ?? DefaultFallbackAxis;

    protected override string PlacementKey => string.Join(',', ResolvedSide, ResolvedAlign, ResolvedSideOffset,
        ResolvedAlignOffset, ResolvedCollisionPadding, ResolvedArrowPadding, ResolvedSticky, ResolvedAnchor, ResolvedFallbackAxis);

    /// <summary>Whether flipping may try the perpendicular sides too, by default (popups that aren't dropdowns).</summary>
    protected virtual bool DefaultFallbackAxis => false;

    protected override Dictionary<string, object?> Placement() => new()
    {
        ["side"] = Js.Side(ResolvedSide),
        ["align"] = Js.Align(ResolvedAlign),
        ["sideOffset"] = ResolvedSideOffset,
        ["alignOffset"] = ResolvedAlignOffset,
        ["collisionPadding"] = ResolvedCollisionPadding,
        ["arrowPadding"] = ResolvedArrowPadding,
        ["sticky"] = ResolvedSticky,
        ["fallbackAxis"] = ResolvedFallbackAxis,
        ["anchorId"] = ResolvedAnchor,
    };

    protected override void AddHostAttributes(RenderTreeBuilder builder) =>
        builder.AddAttribute(10, "data-blazix-positioner", "");
}

/// <summary>A button that opens and closes a popup.</summary>
public abstract class PopupTriggerPart<TRoot> : ButtonPart<PopupTriggerRenderProps> where TRoot : PopupRootBase
{
    private EventCallback<MouseEventArgs> _onClick;
    private PopupHandle? _subscribed;

    [CascadingParameter] private TRoot? CascadedRoot { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>Connects this trigger to a popup root elsewhere on the page.</summary>
    [Parameter] public PopupHandle? Handle { get; set; }

    /// <summary>Data the popup renders with (through its root's <c>ContentTemplate</c>) when this trigger opens it.</summary>
    [Parameter] public object? Payload { get; set; }

    /// <summary>The root: from the handle, else the one this trigger is inside.</summary>
    protected TRoot Root => Handle?.Root as TRoot ?? CascadedRoot
        ?? throw new InvalidOperationException($"{GetType().Name} must be inside its root, or have a Handle that a root uses.");

    /// <summary>Whether the root is known yet (a detached trigger can render before its root).</summary>
    protected bool HasRoot => Handle?.Root is TRoot || CascadedRoot is not null;

    /// <summary>The <c>aria-haspopup</c> value.</summary>
    protected abstract string HasPopup { get; }

    protected override string IdPrefix => "popup-trigger";

    protected bool IsDisabled => Disabled || (HasRoot && Root.IsDisabled);

    /// <summary>Whether the popup is open against this trigger.</summary>
    private bool IsActive => HasRoot && Root.IsOpen && Root.TriggerId == ButtonId;

    /// <summary>Whether a press toggles the popup. Hover-only triggers (tooltips) say no.</summary>
    protected virtual bool TogglesOnPress => true;

    protected override void OnInitialized() =>
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, HandleClickAsync);

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (!ReferenceEquals(_subscribed, Handle))
        {
            _subscribed?.Unsubscribe(OnHandleChanged);
            _subscribed = Handle;
            _subscribed?.Subscribe(OnHandleChanged);
        }
        if (HasRoot)
        {
            Root.RegisterTrigger(ButtonId, Payload);
        }
    }

    // A detached trigger can render before its root: render again once the root attaches.
    private void OnHandleChanged() => _ = InvokeAsync(StateHasChanged);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (HasRoot)
        {
            await Root.EnsureJsAsync();
        }
    }

    protected virtual Task HandleClickAsync(MouseEventArgs e)
    {
        if (IsDisabled || !TogglesOnPress || !HasRoot)
        {
            return Task.CompletedTask;
        }
        // Open against another trigger: move the popup here rather than closing it.
        var elsewhere = Root.IsOpen && Root.TriggerId != ButtonId;
        Root.Activate(ButtonId);
        return elsewhere ? Task.CompletedTask : Root.RequestOpenChangeAsync(!Root.IsOpen, OpenChangeReason.TriggerPress);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, IsDisabled);
        if (HasRoot)
        {
            Root.AddTriggerAttributes(attributes, HasPopup, ButtonId);
        }
        attributes.Set("onclick", _onClick);
    }

    protected override PopupTriggerRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsActive, IsDisabled);

    protected override void Dispose(bool disposing)
    {
        _subscribed?.Unsubscribe(OnHandleChanged);
        if (HasRoot)
        {
            Root.UnregisterTrigger(ButtonId);
        }
        base.Dispose(disposing);
    }
}

/// <summary>An overlay beneath the popup. Renders in the top layer, under the popup.</summary>
public abstract class PopupBackdropPart<TRoot> : PrimitivePart<PopupRenderProps> where TRoot : PopupRootBase
{
    private readonly string _hostId = Ids.Next("backdrop-host");

    [CascadingParameter] protected TRoot Root { get; set; } = null!;

    protected override void OnInitialized() => Root.BackdropHostId = _hostId;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!Root.State.IsMounted)
        {
            return;
        }
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "popover", "manual");
        builder.AddAttribute(2, "id", _hostId);
        builder.AddAttribute(3, "role", "presentation");
        builder.AddAttribute(4, "data-blazix-popup-host", "");
        builder.AddAttribute(5, "inert", Root.State.IsExiting);
        // The popup host it's beneath: one that appears on insert takes its backdrop with it.
        builder.AddAttribute(6, "data-blazix-backdrop-for", Root.HostId);
        builder.AddContent(7, (RenderFragment)BuildPart);
        builder.CloseElement();
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes.Set("role", "presentation");
        Root.AddOpenAttributes(attributes);
    }

    protected override PopupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Root.IsOpen);

    protected override void Dispose(bool disposing)
    {
        if (Root.BackdropHostId == _hostId)
        {
            Root.BackdropHostId = null;
        }
        base.Dispose(disposing);
    }
}

/// <summary>An arrow pointing at the anchor. Positioned by the popup; marked <c>data-uncentered</c> when it can't point at the anchor's center.</summary>
public abstract class PopupArrowPart<TRoot> : PrimitivePart<PopupRenderProps> where TRoot : PopupRootBase
{
    private string _id = "";

    [CascadingParameter] protected TRoot Root { get; set; } = null!;

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("popup-arrow"));
        Root.ArrowId = _id;
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        attributes.Set("id", _id).Set("aria-hidden", "true");
        Root.AddOpenAttributes(attributes);
    }

    protected override PopupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Root.IsOpen);
}

/// <summary>A heading that labels the popup (aria-labelledby).</summary>
public abstract class PopupTitlePart<TRoot> : PrimitivePart<PopupRenderProps> where TRoot : PopupRootBase
{
    private string _id = "";

    [CascadingParameter] protected TRoot Root { get; set; } = null!;

    protected override string Tag => "h2";

    protected override void OnParametersSet()
    {
        // Claim the root only when this title's id changes (the last mounted title wins), so
        // two titles, e.g. one per questionnaire step, don't keep taking it from each other.
        var id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next($"{Root.Prefix}-title"));
        if (id != _id)
        {
            _id = id;
            Root.TitleId = id;
            Root.Rerender();
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.Set("id", _id);

    protected override PopupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Root.IsOpen);
}

/// <summary>A paragraph that describes the popup (aria-describedby).</summary>
public abstract class PopupDescriptionPart<TRoot> : PrimitivePart<PopupRenderProps> where TRoot : PopupRootBase
{
    private string _id = "";

    [CascadingParameter] protected TRoot Root { get; set; } = null!;

    protected override string Tag => "p";

    protected override void OnParametersSet()
    {
        var id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next($"{Root.Prefix}-description"));
        if (id != _id)
        {
            _id = id;
            Root.DescriptionId = id;
            Root.Rerender();
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.Set("id", _id);

    protected override PopupRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Root.IsOpen);
}

/// <summary>A button that closes the popup.</summary>
public abstract class PopupClosePart<TRoot> : ButtonPart<PopupTriggerRenderProps> where TRoot : PopupRootBase
{
    private EventCallback<MouseEventArgs> _onClick;

    [CascadingParameter] protected TRoot Root { get; set; } = null!;

    [Parameter] public bool Disabled { get; set; }

    protected override string IdPrefix => $"{Root.Prefix}-close";

    protected override void OnInitialized() =>
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, () => Disabled ? Task.CompletedTask : Root.RequestOpenChangeAsync(false, OpenChangeReason.ClosePress));

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, Disabled);
        attributes.Set("onclick", _onClick);
    }

    protected override PopupTriggerRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Root.IsOpen, Disabled);
}
