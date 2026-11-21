using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>Where an avatar's image is in loading.</summary>
public enum ImageLoadingStatus
{
    Idle,
    Loading,
    Loaded,
    Error,
}

public sealed record AvatarRenderProps(IReadOnlyDictionary<string, object> Attributes, ImageLoadingStatus ImageLoadingStatus)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="AvatarRoot"/>.</summary>
public sealed class AvatarContext
{
    private readonly AvatarRoot _root;

    internal AvatarContext(AvatarRoot root) => _root = root;

    public ImageLoadingStatus ImageLoadingStatus => _root.Status;

    internal void SetStatus(ImageLoadingStatus status) => _root.SetStatus(status);
}

/// <summary>Displays a user's profile picture, initials, or fallback icon. Renders a &lt;span&gt;.</summary>
public class AvatarRoot : PrimitivePart<AvatarRenderProps>
{
    private readonly AvatarContext _context;

    public AvatarRoot() => _context = new AvatarContext(this);

    protected override string Tag => "span";

    internal ImageLoadingStatus Status { get; private set; } = ImageLoadingStatus.Idle;

    internal void SetStatus(ImageLoadingStatus status)
    {
        if (Status == status)
        {
            return;
        }
        Status = status;
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        // Avatar's styling hooks are its parts' presence; the root has none of its own.
    }

    protected override AvatarRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Status);
}

/// <summary>
/// The image to show in the avatar. Hidden until it has loaded, so
/// the fallback shows meanwhile and stays if it fails.
/// </summary>
public class AvatarImage : PrimitivePart<AvatarRenderProps>
{
    private EventCallback<ProgressEventArgs> _onLoad;
    private EventCallback<Microsoft.AspNetCore.Components.Web.ErrorEventArgs> _onError;
    private string _id = "";
    private string? _src;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    [CascadingParameter] private AvatarContext Avatar { get; set; } = null!;

    /// <summary>The image's URL. It's loaded first, and the image renders once it has loaded.</summary>
    [Parameter] public string? Src { get; set; }

    /// <summary>Runs when the image starts loading, loads, or fails.</summary>
    [Parameter] public EventCallback<ImageLoadingStatus> OnLoadingStatusChange { get; set; }

    /// <summary>
    /// Keep the image in the DOM while it loads or after it fails, marked with <c>data-loading</c> or
    /// <c>data-error</c> (and <c>aria-hidden</c>), instead of hiding it. Defaults to false.
    /// </summary>
    [Parameter] public bool KeepMounted { get; set; }

    protected override string Tag => "img";

    protected override bool IsVoid => true;

    protected override void OnInitialized()
    {
        _onLoad = EventCallback.Factory.Create<ProgressEventArgs>(this, () => SetAsync(ImageLoadingStatus.Loaded));
        _onError = EventCallback.Factory.Create<Microsoft.AspNetCore.Components.Web.ErrorEventArgs>(this, () => SetAsync(ImageLoadingStatus.Error));
    }

    protected override async Task OnParametersSetAsync()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("avatar-image"));
        if (_src != Src)
        {
            _src = Src;
            await SetAsync(string.IsNullOrEmpty(Src) ? ImageLoadingStatus.Error : ImageLoadingStatus.Loading);
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The image may have loaded (or failed) before Blazor attached its handlers.
        if (firstRender && Avatar.ImageLoadingStatus == ImageLoadingStatus.Loading)
        {
            var status = await JsModules.InvokeAsync<string?>(JsRuntime, JsModules.Util, "imageStatus", _id);
            if (status is "loaded")
            {
                await SetAsync(ImageLoadingStatus.Loaded);
            }
            else if (status is "error")
            {
                await SetAsync(ImageLoadingStatus.Error);
            }
        }
    }

    private async Task SetAsync(ImageLoadingStatus status)
    {
        if (Avatar.ImageLoadingStatus == status)
        {
            return;
        }
        Avatar.SetStatus(status);
        await OnLoadingStatusChange.InvokeAsync(status);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var status = Avatar.ImageLoadingStatus;
        var loaded = status == ImageLoadingStatus.Loaded;
        attributes
            .Set("id", _id)
            .Set("src", Src)
            .Set("onload", _onLoad)
            .Set("onerror", _onError);
        if (KeepMounted)
        {
            attributes
                .Flag("data-loading", status == ImageLoadingStatus.Loading)
                .Flag("data-error", status == ImageLoadingStatus.Error)
                .Set("aria-hidden", loaded ? null : "true");
        }
        else
        {
            // Loads while hidden; shown once it can be displayed.
            attributes.Set("hidden", !loaded);
        }
    }

    protected override AvatarRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Avatar.ImageLoadingStatus);
}

/// <summary>
/// Rendered while the image isn't loaded: initials or an icon.
/// <see cref="Delay"/> avoids flashing it for images that load quickly.
/// </summary>
public class AvatarFallback : PrimitivePart<AvatarRenderProps>
{
    private bool _delayPassed;
    private CancellationTokenSource? _timer;

    [CascadingParameter] private AvatarContext Avatar { get; set; } = null!;

    /// <summary>How long to wait before showing the fallback, in milliseconds. Defaults to 0.</summary>
    [Parameter] public int Delay { get; set; }

    protected override string Tag => "span";

    protected override bool IsPresent => Avatar.ImageLoadingStatus != ImageLoadingStatus.Loaded && (Delay <= 0 || _delayPassed);

    protected override void OnParametersSet()
    {
        if (Delay <= 0)
        {
            _delayPassed = true;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || Delay <= 0 || _delayPassed)
        {
            return;
        }

        _timer = new CancellationTokenSource();
        try
        {
            await Task.Delay(Delay, _timer.Token);
            _delayPassed = true;
            StateHasChanged();
        }
        catch (TaskCanceledException)
        {
            // Left the page first.
        }
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        // No attributes of its own; its presence is the styling hook.
    }

    protected override AvatarRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Avatar.ImageLoadingStatus);

    protected override void Dispose(bool disposing)
    {
        _timer?.Cancel();
        _timer?.Dispose();
        base.Dispose(disposing);
    }
}
