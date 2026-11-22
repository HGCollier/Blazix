namespace Blazix.Registry;

/// <summary>
/// The sidebar's state, cascaded by <see cref="SidebarProvider"/>:
/// <c>[CascadingParameter] SidebarContext Sidebar { get; set; }</c>.
/// </summary>
public sealed class SidebarContext
{
    private readonly Func<bool, Task> _setOpen;
    private readonly Func<bool, Task> _setOpenMobile;

    internal SidebarContext(Func<bool, Task> setOpen, Func<bool, Task> setOpenMobile)
    {
        _setOpen = setOpen;
        _setOpenMobile = setOpenMobile;
    }

    /// <summary>Whether the desktop sidebar is expanded.</summary>
    public bool Open { get; internal set; }

    /// <summary>Whether the mobile sidebar (a sheet) is open.</summary>
    public bool OpenMobile { get; internal set; }

    /// <summary>Whether the screen is narrower than 768px, where the sidebar is a sheet.</summary>
    public bool IsMobile { get; internal set; }

    /// <summary>"expanded" or "collapsed".</summary>
    public string State => Open ? "expanded" : "collapsed";

    // ReSharper disable once MemberCanBePrivate.Global
    public Task SetOpenAsync(bool open) => _setOpen(open);

    public Task SetOpenMobileAsync(bool open) => _setOpenMobile(open);

    /// <summary>Opens or closes the sheet on mobile, else expands or collapses the sidebar.</summary>
    public Task ToggleAsync() => IsMobile ? SetOpenMobileAsync(!OpenMobile) : SetOpenAsync(!Open);
}
