using JetBrains.Annotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>Where a message scroller opens: at the top, at the end, or at the last anchored turn.</summary>
public enum MessageScrollerScrollPosition
{
    Start,
    End,
    /// <summary>The last <c>ScrollAnchor</c> item at the top, with the previous turn peeking above it. The end when the last turn fits.</summary>
    LastAnchor,
}

/// <summary>Where a message lands in the viewport when you jump to it.</summary>
public enum MessageScrollerAlign
{
    Start,
    Center,
    End,
    /// <summary>Only scroll if the message isn't fully in view, and then as little as possible.</summary>
    Nearest,
}

/// <summary>Which edge a <see cref="MessageScrollerButton"/> scrolls to.</summary>
public enum MessageScrollerDirection
{
    Start,
    End,
}

/// <summary>How a scroll moves: CSS's <c>scroll-behavior</c>.</summary>
public enum ScrollBehavior
{
    Auto,
    Smooth,
    Instant,
}

/// <summary>Options for <see cref="MessageScrollerProvider.ScrollToMessageAsync"/>.</summary>
/// <param name="Align">Where the message lands. Defaults to the top.</param>
/// <param name="Behavior">Defaults to an instant jump.</param>
/// <param name="ScrollMargin">Room to leave at the edge, in pixels. Defaults to the provider's <c>ScrollMargin</c>.</param>
public sealed record MessageScrollerScrollOptions(
    MessageScrollerAlign Align = MessageScrollerAlign.Start,
    ScrollBehavior Behavior = ScrollBehavior.Auto,
    int? ScrollMargin = null);

/// <summary>Whether there's more to scroll toward each edge.</summary>
public readonly record struct MessageScrollerScrollState(bool Start, bool End);

/// <summary>
/// Which messages the reader can see, top to bottom, and the anchored turn they're reading: the last
/// <c>ScrollAnchor</c> item to reach the reading line (<c>ScrollMargin</c> + <c>ScrollPreviousItemPeek</c> from the top).
/// </summary>
public sealed record MessageScrollerVisibilityState(string? CurrentAnchorId, IReadOnlyList<string> VisibleMessageIds)
{
    public static readonly MessageScrollerVisibilityState Empty = new(null, []);
}

public sealed record MessageScrollerButtonRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Active, MessageScrollerDirection Direction)
    : RenderProps(Attributes);

/// <summary>
/// Owns a message scroller's behavior and cascades itself to the
/// parts inside. Keep a <c>@ref</c> to it for the commands (<c>useMessageScroller</c>); render
/// <see cref="MessageScrollerScrollable"/> or <see cref="MessageScrollerVisibility"/> for its state. Renders no element.
/// </summary>
[PublicAPI]
public class MessageScrollerProvider : ComponentBase, IAsyncDisposable
{
    private JsController? _js;
    private DotNetObjectReference<MessageScrollerInterop>? _interop;
    private JsOptions? _options;
    private int _visibilityWatchers;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    /// <summary>Follow new content while the reader is at the end. Scrolling up releases it; reaching the end again re-arms it. Defaults to false.</summary>
    [Parameter] public bool AutoScroll { get; set; }

    /// <summary>Where the scroller opens once it has messages. Until then, with End or LastAnchor, the root and viewport carry <c>data-pending-scroll</c>, so you can hide them instead of flashing the top. Defaults to End.</summary>
    [Parameter] public MessageScrollerScrollPosition DefaultScrollPosition { get; set; } = MessageScrollerScrollPosition.End;

    /// <summary>How close to an edge, in pixels, still counts as at it. Defaults to 8.</summary>
    [Parameter] public int ScrollEdgeThreshold { get; set; } = 8;

    /// <summary>How much of the previous turn stays visible above an anchored turn, in pixels. Defaults to 64.</summary>
    [Parameter] public int ScrollPreviousItemPeek { get; set; } = 64;

    /// <summary>Room left above a message the scroller moves to the top, in pixels. Defaults to 0.</summary>
    [Parameter] public int ScrollMargin { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Whether there's more to scroll toward each edge.</summary>
    public MessageScrollerScrollState Scrollable { get; private set; }

    /// <summary>What the reader can see. Tracked only while a <see cref="MessageScrollerVisibility"/> is rendered.</summary>
    public MessageScrollerVisibilityState Visibility { get; private set; } = MessageScrollerVisibilityState.Empty;

    /// <summary>What the root, viewport and content carry so the browser side finds them.</summary>
    internal string Key { get; } = Ids.Next("message-scroller");

    /// <summary>Until the opening position is applied (see <see cref="DefaultScrollPosition"/>).</summary>
    internal bool PendingScroll { get; private set; }

    internal event Action? ScrollableChanged;
    internal event Action? VisibilityChanged;

    protected override void OnInitialized() => PendingScroll = DefaultScrollPosition != MessageScrollerScrollPosition.Start;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<MessageScrollerProvider>>(0);
        builder.AddComponentParameter(1, nameof(CascadingValue<>.Value), this);
        builder.AddComponentParameter(2, nameof(CascadingValue<>.IsFixed), true);
        builder.AddComponentParameter(3, nameof(CascadingValue<>.ChildContent), ChildContent);
        builder.CloseComponent();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_js is null)
        {
            await EnsureJsAsync();
            return;
        }
        var options = CurrentOptions();
        if (options != _options)
        {
            _options = options;
            await _js.InvokeVoidAsync("update", options.ToJs());
        }
    }

    /// <summary>Scrolls to the top.</summary>
    public async Task ScrollToStartAsync(ScrollBehavior behavior = ScrollBehavior.Auto)
    {
        var js = await EnsureJsAsync();
        await js.InvokeVoidAsync("scrollToStart", Name(behavior));
    }

    /// <summary>Scrolls to the end, and follows new content from there when <see cref="AutoScroll"/> is on.</summary>
    public async Task ScrollToEndAsync(ScrollBehavior behavior = ScrollBehavior.Auto)
    {
        var js = await EnsureJsAsync();
        await js.InvokeVoidAsync("scrollToEnd", Name(behavior));
    }

    /// <summary>
    /// Scrolls to the item with this <c>MessageId</c>. Before any message has rendered, the jump waits for it
    /// (and replaces the opening position). False when the messages are there but this one isn't.
    /// </summary>
    public async Task<bool> ScrollToMessageAsync(string messageId, MessageScrollerScrollOptions? options = null)
    {
        options ??= new MessageScrollerScrollOptions();
        var js = await EnsureJsAsync();
        // A dictionary, not an anonymous type: JS interop reflects over the argument, and trimming removes an
        // anonymous type's members.
        return await js.InvokeAsync<bool>("scrollToMessage", messageId, new Dictionary<string, object?>
        {
            ["align"] = options.Align.ToString().ToLowerInvariant(),
            ["behavior"] = Name(options.Behavior),
            ["scrollMargin"] = options.ScrollMargin,
        });
    }

    internal async Task ConnectAsync()
    {
        var js = await EnsureJsAsync();
        await js.InvokeVoidAsync("connect");
    }

    internal async Task PressAsync(string buttonId, MessageScrollerDirection direction, ScrollBehavior behavior)
    {
        var js = await EnsureJsAsync();
        await js.InvokeVoidAsync("press", buttonId, direction == MessageScrollerDirection.Start ? "start" : "end", Name(behavior));
    }

    internal async Task WatchVisibilityAsync()
    {
        if (_visibilityWatchers++ > 0)
        {
            return;
        }
        var js = await EnsureJsAsync();
        await js.InvokeVoidAsync("observeVisibility");
    }

    internal async Task UnwatchVisibilityAsync()
    {
        if (--_visibilityWatchers > 0 || _js is null)
        {
            return;
        }
        Visibility = MessageScrollerVisibilityState.Empty;
        await _js.InvokeVoidAsync("unobserveVisibility");
    }

    internal Task OnScrollableAsync(bool start, bool end) => InvokeAsync(() =>
    {
        Scrollable = new MessageScrollerScrollState(start, end);
        ScrollableChanged?.Invoke();
    });

    internal Task OnVisibilityAsync(string? currentAnchorId, string[] visibleMessageIds) => InvokeAsync(() =>
    {
        if (_visibilityWatchers == 0)
        {
            return;
        }
        Visibility = new MessageScrollerVisibilityState(currentAnchorId, visibleMessageIds);
        VisibilityChanged?.Invoke();
    });

    internal void OnPendingScrollCleared() => PendingScroll = false;

    private async Task<JsController> EnsureJsAsync()
    {
        if (_js is null)
        {
            _js = new JsController(JsRuntime, "message-scroller.js", "createMessageScroller");
            _interop = DotNetObjectReference.Create(new MessageScrollerInterop(this));
            _options = CurrentOptions();
        }
        await _js.EnsureAsync(Key, _interop, _options?.ToJs());
        return _js;
    }

    private JsOptions CurrentOptions() => new(
        AutoScroll,
        DefaultScrollPosition switch
        {
            MessageScrollerScrollPosition.Start => "start",
            MessageScrollerScrollPosition.LastAnchor => "last-anchor",
            _ => "end",
        },
        ScrollEdgeThreshold,
        ScrollPreviousItemPeek,
        ScrollMargin);

    private static string Name(ScrollBehavior behavior) => behavior.ToString().ToLowerInvariant();

    public async ValueTask DisposeAsync()
    {
        if (_js is not null)
        {
            await _js.DisposeAsync();
        }
        _interop?.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record JsOptions(bool AutoScroll, string DefaultScrollPosition, int ScrollEdgeThreshold, int ScrollPreviousItemPeek, int ScrollMargin)
    {
        // What message-scroller.js receives. A dictionary, not the record itself: JS interop reflects over the argument,
        // and trimming removes members nothing else uses.
        public Dictionary<string, object?> ToJs() => new()
        {
            ["autoScroll"] = AutoScroll,
            ["defaultScrollPosition"] = DefaultScrollPosition,
            ["scrollEdgeThreshold"] = ScrollEdgeThreshold,
            ["scrollPreviousItemPeek"] = ScrollPreviousItemPeek,
            ["scrollMargin"] = ScrollMargin,
        };
    }
}

internal sealed class MessageScrollerInterop(MessageScrollerProvider owner)
{
    [JSInvokable]
    public Task Scrollable(bool start, bool end) => owner.OnScrollableAsync(start, end);

    [JSInvokable]
    public Task Visibility(string? currentAnchorId, string[] visibleMessageIds) => owner.OnVisibilityAsync(currentAnchorId, visibleMessageIds);

    [JSInvokable]
    public void PendingScrollCleared() => owner.OnPendingScrollCleared();
}

/// <summary>A part inside a <see cref="MessageScrollerProvider"/>.</summary>
public abstract class MessageScrollerPart : SimplePart
{
    [CascadingParameter] private MessageScrollerProvider? CascadedProvider { get; set; }

    protected MessageScrollerProvider Provider => CascadedProvider
        ?? throw new InvalidOperationException($"{GetType().Name} must be inside a MessageScrollerProvider.");
}

/// <summary>
/// The scroller's outer element: it holds the viewport and the scroll
/// buttons. Carries <c>data-scrollable="start end"</c>, <c>data-autoscrolling</c> and <c>data-pending-scroll</c>.
/// </summary>
public class MessageScrollerRoot : MessageScrollerPart
{
    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("data-blazix-message-scroller", Provider.Key)
        .Flag("data-pending-scroll", Provider.PendingScroll);
}

/// <summary>
/// The scroll container: a focusable region labelled "Messages",
/// with the same state attributes as the root. Wheel, touch and scroll keys here release following and anchoring.
/// </summary>
public class MessageScrollerViewport : MessageScrollerPart
{
    /// <summary>Keep the reader's place when messages are added above. Defaults to true.</summary>
    [Parameter] public bool PreserveScrollOnPrepend { get; set; } = true;

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("data-blazix-message-scroller-viewport", Provider.Key)
        .Set("data-blazix-preserve-scroll", PreserveScrollOnPrepend ? null : "false")
        .Default("role", "region")
        .Default("aria-label", "Messages")
        .Default("tabindex", "0")
        .Flag("data-pending-scroll", Provider.PendingScroll);
}

/// <summary>
/// The list of messages, a <c>role="log"</c> that announces additions.
/// Put the <see cref="MessageScrollerItem"/>s directly inside. It ends with a hidden spacer that gives an
/// anchored turn room to reach the top.
/// </summary>
public class MessageScrollerContent : MessageScrollerPart
{
    /// <summary>Classes for the tail spacer.</summary>
    [Parameter] public string? SpacerClass { get; set; }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("data-blazix-message-scroller-content", Provider.Key)
        .Default("role", "log")
        .Default("aria-relevant", "additions");

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        builder.AddContent(3, ChildContent);
        builder.OpenElement(4, "div");
        builder.AddAttribute(5, "aria-hidden", "true");
        builder.AddAttribute(6, "data-message-scroller-spacer", "");
        builder.AddAttribute(7, "hidden", true);
        builder.AddAttribute(8, "class", SpacerClass);
        builder.CloseElement();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await Provider.ConnectAsync();
        }
    }
}

/// <summary>
/// One row of the list. Give it a <see cref="MessageId"/> to jump to it
/// and track its visibility, and <see cref="ScrollAnchor"/> to settle it near the top when it's added.
/// </summary>
public class MessageScrollerItem : SimplePart
{
    /// <summary>The id <c>ScrollToMessageAsync</c> and the visibility state use.</summary>
    [Parameter] public string? MessageId { get; set; }

    /// <summary>When added, scroll it to the top, with the previous turn peeking above. Defaults to false.</summary>
    [Parameter] public bool ScrollAnchor { get; set; }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("data-message-id", MessageId)
        .Set("data-scroll-anchor", ScrollAnchor ? "true" : "false");
}

/// <summary>
/// Scrolls to the end (or start) when there's more that way. Otherwise
/// it's inert with <c>data-active="false"</c>, so you can hide it with CSS while it stays mounted.
/// </summary>
public class MessageScrollerButton : ButtonPart<MessageScrollerButtonRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;

    [CascadingParameter] private MessageScrollerProvider? CascadedProvider { get; set; }

    /// <summary>The edge it scrolls to. Defaults to End.</summary>
    [Parameter] public MessageScrollerDirection Direction { get; set; } = MessageScrollerDirection.End;

    /// <summary>Defaults to Smooth.</summary>
    [Parameter] public ScrollBehavior Behavior { get; set; } = ScrollBehavior.Smooth;

    private MessageScrollerProvider Provider => CascadedProvider
        ?? throw new InvalidOperationException("MessageScrollerButton must be inside a MessageScrollerProvider.");

    private bool Active => Direction == MessageScrollerDirection.Start ? Provider.Scrollable.Start : Provider.Scrollable.End;

    protected override string IdPrefix => "message-scroller-button";

    protected override void OnInitialized()
    {
        _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, HandleClickAsync);
        Provider.ScrollableChanged += OnScrollableChanged;
    }

    private void OnScrollableChanged() => StateHasChanged();

    private Task HandleClickAsync() => Active ? Provider.PressAsync(ButtonId, Direction, Behavior) : Task.CompletedTask;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddButtonAttributes(attributes, disabled: false);
        attributes
            .Flag("inert", !Active)
            .Set("data-active", Active ? "true" : "false")
            .Set("onclick", _onClick);
        if (!Active)
        {
            attributes.Set("tabindex", "-1");
        }
    }

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        if (ChildContent is not null)
        {
            builder.AddContent(3, ChildContent);
            return;
        }
        builder.OpenElement(4, "span");
        builder.AddContent(5, Direction == MessageScrollerDirection.Start ? "Scroll to start" : "Scroll to end");
        builder.CloseElement();
    }

    protected override MessageScrollerButtonRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Active, Direction);

    protected override void Dispose(bool disposing)
    {
        if (CascadedProvider is not null)
        {
            CascadedProvider.ScrollableChanged -= OnScrollableChanged;
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Renders its content with whether there's more to scroll toward each edge,
/// again whenever that changes.
/// </summary>
public class MessageScrollerScrollable : ComponentBase, IDisposable
{
    [CascadingParameter] private MessageScrollerProvider? Provider { get; set; }

    [Parameter] public RenderFragment<MessageScrollerScrollState>? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        if (Provider is null)
        {
            throw new InvalidOperationException("MessageScrollerScrollable must be inside a MessageScrollerProvider.");
        }
        Provider.ScrollableChanged += StateHasChanged;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        builder.AddContent(0, ChildContent?.Invoke(Provider!.Scrollable));

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Provider is not null)
        {
            Provider.ScrollableChanged -= StateHasChanged;
        }
    }
}

/// <summary>
/// Renders its content with the messages in view and the current anchored turn,
/// again whenever they change. Visibility is only tracked while one is rendered.
/// </summary>
public class MessageScrollerVisibility : ComponentBase, IAsyncDisposable
{
    private bool _watching;

    [CascadingParameter] private MessageScrollerProvider? Provider { get; set; }

    [Parameter] public RenderFragment<MessageScrollerVisibilityState>? ChildContent { get; set; }

    protected override void OnInitialized()
    {
        if (Provider is null)
        {
            throw new InvalidOperationException("MessageScrollerVisibility must be inside a MessageScrollerProvider.");
        }
        Provider.VisibilityChanged += StateHasChanged;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        builder.AddContent(0, ChildContent?.Invoke(Provider!.Visibility));

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _watching = true;
            await Provider!.WatchVisibilityAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Provider is not null)
        {
            Provider.VisibilityChanged -= StateHasChanged;
            if (_watching)
            {
                await Provider.UnwatchVisibilityAsync();
            }
        }
        GC.SuppressFinalize(this);
    }
}
