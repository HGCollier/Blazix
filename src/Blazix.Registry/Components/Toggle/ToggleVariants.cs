namespace Blazix.Registry;

public enum ToggleVariant
{
    Default,
    Outline,
}

public enum ToggleSize
{
    Default,
    Sm,
    Lg,
}

/// <summary>
/// The class recipe for Toggle and ToggleGroupItem.
/// Keep class names as complete string literals: Tailwind finds them by scanning source files.
/// </summary>
public static class ToggleVariants
{
    private const string Base =
        "group/toggle inline-flex items-center justify-center gap-1 rounded-lg text-sm font-medium whitespace-nowrap transition-all outline-none " +
        "hover:bg-muted hover:text-foreground focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 " +
        "disabled:pointer-events-none disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-destructive/20 " +
        "aria-pressed:bg-muted aria-pressed:inset-ring aria-pressed:inset-ring-ring/50 data-[state=on]:bg-muted data-[state=on]:inset-ring data-[state=on]:inset-ring-ring/50 dark:aria-invalid:ring-destructive/40 aria-invalid:focus-visible:ring-destructive/60 dark:aria-invalid:focus-visible:ring-destructive/60 " +
        "[&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4";

    public static string Get(ToggleVariant variant = ToggleVariant.Default, ToggleSize size = ToggleSize.Default, string? @class = null) =>
        ClassNames.Cn(
            Base,
            variant switch
            {
                ToggleVariant.Outline => "border border-input bg-transparent hover:bg-muted",
                _ => "bg-transparent",
            },
            size switch
            {
                ToggleSize.Sm =>
                    "h-7 min-w-7 rounded-[min(var(--radius-md),12px)] px-2.5 text-[0.8rem] has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 " +
                    "[&_svg:not([class*='size-'])]:size-3.5",
                ToggleSize.Lg => "h-9 min-w-9 px-2.5 has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2",
                _ => "h-8 min-w-8 px-2.5 has-data-[icon=inline-end]:pr-2 has-data-[icon=inline-start]:pl-2",
            },
            @class);

    internal static string Name(ToggleVariant variant) => variant == ToggleVariant.Outline ? "outline" : "default";

    internal static string Name(ToggleSize size) => size switch
    {
        ToggleSize.Sm => "sm",
        ToggleSize.Lg => "lg",
        _ => "default",
    };
}
