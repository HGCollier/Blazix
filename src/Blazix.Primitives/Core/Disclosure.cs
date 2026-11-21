using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>
/// What Collapsible and an Accordion item share: a trigger that shows and hides a panel, and the
/// panel's presence, size variables and find-in-page reveal.
/// </summary>
internal sealed class Disclosure(IJSRuntime js, string prefix, string cssPrefix, Func<bool, OpenChangeReason, Task<bool>> request)
{
    public PopupState State { get; } = new();

    public string TriggerId { get; set; } = Ids.Next($"{prefix}-trigger");

    public string PanelId { get; set; } = Ids.Next($"{prefix}-panel");

    /// <summary>The CSS variable prefix: "collapsible-panel" or "accordion-panel".</summary>
    private string CssPrefix { get; } = cssPrefix;

    public bool HasPanel { get; set; }

    /// <summary>Set on the render that opens the panel: it renders data-starting-style once.</summary>
    public bool Starting { get; set; }

    /// <summary>Whether the panel has rendered at least once, so later opens animate.</summary>
    public bool Rendered { get; set; }

    public bool IsOpen => State.IsOpen;

    public Task<bool> RequestAsync(bool open, OpenChangeReason reason) => request(open, reason);

    /// <summary>Pins the panel's size and moves focus out of it before the closed state renders.</summary>
    public async Task PrepareCloseAsync()
    {
        if (HasPanel)
        {
            await JsModules.InvokeAsync<object>(js, JsModules.Panel, "prepareClose", PanelId, CssPrefix, TriggerId);
        }
    }

    public async Task ApplyAsync(bool open, OpenChangeReason reason)
    {
        if (!open)
        {
            await PrepareCloseAsync();
        }
        State.Apply(open, reason);
        if (open)
        {
            Starting = Rendered;
        }
    }

    public Task<bool> OpenedAsync(bool animate) => JsModules.InvokeAsync<bool>(js, JsModules.Panel, "opened", PanelId, CssPrefix, animate);

    public Task<bool> ClosedAsync() => JsModules.InvokeAsync<bool>(js, JsModules.Panel, "closed", PanelId, CssPrefix);

    public Task<IJSObjectReference?> WatchFindAsync(object dotnet) =>
        JsModules.InvokeAsync<IJSObjectReference>(js, JsModules.Panel, "watchFind", PanelId, dotnet);
}

/// <summary>The panel part of Collapsible and Accordion: presence, size variables and find in page.</summary>
public abstract class DisclosurePanelPart<TRenderProps> : PrimitivePart<TRenderProps>, IAsyncDisposable where TRenderProps : RenderProps
{
    private readonly PopupPresence _presence = new();
    private DotNetObjectReference<DisclosurePanelInterop>? _interop;
    private IJSObjectReference? _findWatch;
    private bool _watching;

    /// <summary>Keep the panel in the DOM (hidden) while closed. Defaults to false.</summary>
    [Parameter] public bool KeepMounted { get; set; }

    /// <summary>
    /// Keep the closed panel findable with the browser's find in page: it renders
    /// <c>hidden="until-found"</c> and opens when a match is found in it. Implies <see cref="KeepMounted"/>.
    /// </summary>
    [Parameter] public bool? HiddenUntilFound { get; set; }

    internal abstract Disclosure Disclosure { get; }

    private protected virtual bool HiddenUntilFoundDefault => false;

    private bool Findable => HiddenUntilFound ?? HiddenUntilFoundDefault;

    protected override bool IsPresent => Disclosure.State.IsMounted || KeepMounted || Findable;

    protected override void OnParametersSet()
    {
        Disclosure.PanelId = ElementAttributes.IdOr(AdditionalAttributes, () => Disclosure.PanelId);
        Disclosure.HasPanel = true;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        _presence.Snapshot(Disclosure.State.IsOpen);
        BuildPart(builder);
    }

    /// <summary>id, hidden, open/closed and transition attributes.</summary>
    protected void AddPanelAttributes(AttributeBuilder attributes)
    {
        var state = Disclosure.State;
        object? hidden = null;
        if (!state.IsMounted)
        {
            // Blazor renders a boolean true as the bare attribute.
            hidden = Findable ? "until-found" : "";
        }
        attributes
            .Set("id", Disclosure.PanelId)
            .Set("hidden", hidden)
            .Flag("data-open", state.IsOpen)
            .Flag("data-closed", !state.IsOpen)
            .Flag("data-starting-style", Disclosure.Starting && state.IsOpen)
            .Flag("data-ending-style", state.IsExiting);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var animate = Disclosure.Rendered;
        Disclosure.Rendered = true;
        Disclosure.Starting = false;

        if (Findable && !_watching && IsPresent)
        {
            _watching = true;
            _interop ??= DotNetObjectReference.Create(new DisclosurePanelInterop(RevealAsync));
            _findWatch = await Disclosure.WatchFindAsync(_interop);
        }

        await _presence.AfterRenderAsync(
            show: () => Disclosure.OpenedAsync(animate),
            hide: Disclosure.ClosedAsync,
            isOpen: () => Disclosure.State.IsOpen,
            isExiting: () => Disclosure.State.IsExiting,
            completeExit: () =>
            {
                if (Disclosure.State.CompleteExit())
                {
                    StateHasChanged();
                }
            });
    }

    private Task RevealAsync() => InvokeAsync(async () =>
    {
        // Find in page already removed `hidden`: open without animating.
        Disclosure.Rendered = false;
        await Disclosure.RequestAsync(true, OpenChangeReason.None);
    });

    public async ValueTask DisposeAsync()
    {
        Dispose(true);
        if (_findWatch is not null)
        {
            try
            {
                await _findWatch.InvokeVoidAsync("dispose");
                await _findWatch.DisposeAsync();
            }
            catch (Exception e) when (e is JSDisconnectedException or TaskCanceledException or ObjectDisposedException)
            {
                // Gone with the circuit.
            }
        }
        _interop?.Dispose();
        GC.SuppressFinalize(this);
    }
}

internal sealed class DisclosurePanelInterop(Func<Task> reveal)
{
    [JSInvokable]
    public Task RevealAsync() => reveal();
}
