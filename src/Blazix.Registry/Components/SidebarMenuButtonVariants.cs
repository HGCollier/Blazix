namespace Blazix.Registry;

/// <summary>
/// The class recipe for a sidebar menu button.
/// Keep class names as complete string literals: Tailwind finds them by scanning source files.
/// </summary>
public static class SidebarMenuButtonVariants
{
    private const string Base =
        "peer/menu-button group/menu-button flex w-full items-center gap-2 overflow-hidden rounded-md p-2 text-left text-sm " +
        "ring-sidebar-ring outline-hidden transition-[width,height,padding] group-has-data-[sidebar=menu-action]/menu-item:pr-8 " +
        "group-data-[collapsible=icon]:size-8! group-data-[collapsible=icon]:p-2! hover:bg-sidebar-accent " +
        "hover:text-sidebar-accent-foreground focus-visible:ring-2 active:bg-sidebar-accent active:text-sidebar-accent-foreground " +
        "disabled:pointer-events-none disabled:opacity-50 aria-disabled:pointer-events-none aria-disabled:opacity-50 " +
        "data-open:hover:bg-sidebar-accent data-open:hover:text-sidebar-accent-foreground data-active:bg-sidebar-accent " +
        "data-active:font-medium data-active:text-sidebar-accent-foreground [&_svg]:size-4 [&_svg]:shrink-0 " +
        "[&>span:last-child]:truncate";

    public static string Get(SidebarMenuButtonVariant variant = SidebarMenuButtonVariant.Default,
        SidebarMenuButtonSize size = SidebarMenuButtonSize.Default, string? @class = null) =>
        ClassNames.Cn(
            Base,
            variant == SidebarMenuButtonVariant.Outline
                ? "bg-background shadow-[0_0_0_1px_var(--sidebar-border)] hover:bg-sidebar-accent hover:text-sidebar-accent-foreground " +
                  "hover:shadow-[0_0_0_1px_var(--sidebar-accent)]"
                : "hover:bg-sidebar-accent hover:text-sidebar-accent-foreground",
            size switch
            {
                SidebarMenuButtonSize.Sm => "h-7 text-xs",
                SidebarMenuButtonSize.Lg => "h-12 text-sm group-data-[collapsible=icon]:p-0!",
                _ => "h-8 text-sm",
            },
            @class);
}
