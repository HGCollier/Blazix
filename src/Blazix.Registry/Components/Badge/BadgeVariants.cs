namespace Blazix.Registry;

/// <summary>
/// The class recipe for anything that should look like a badge.
/// Keep class names as complete string literals: Tailwind finds them by scanning source files.
/// </summary>
public static class BadgeVariants
{
    private const string Base =
        "group/badge inline-flex h-5 w-fit shrink-0 items-center justify-center gap-1 overflow-hidden rounded-4xl border border-transparent " +
        "px-2 py-0.5 text-xs font-medium whitespace-nowrap transition-all focus-visible:border-ring focus-visible:ring-[3px] focus-visible:ring-ring/50 " +
        "has-data-[icon=inline-end]:pr-1.5 has-data-[icon=inline-start]:pl-1.5 aria-invalid:border-destructive aria-invalid:ring-destructive/20 " +
        "dark:aria-invalid:ring-destructive/40 [&>svg]:pointer-events-none [&>svg]:size-3!";

    public static string Get(BadgeVariant variant = BadgeVariant.Default, string? @class = null) =>
        ClassNames.Cn(
            Base,
            variant switch
            {
                BadgeVariant.Secondary => "bg-secondary text-secondary-foreground [a]:hover:bg-secondary/80",
                BadgeVariant.Destructive =>
                    "bg-destructive/10 text-destructive focus-visible:ring-destructive/60 dark:bg-destructive/20 " +
                    "dark:focus-visible:ring-destructive/60 [a]:hover:bg-destructive/20",
                BadgeVariant.Outline => "border-border text-foreground [a]:hover:bg-muted [a]:hover:text-muted-foreground",
                BadgeVariant.Ghost => "hover:bg-muted hover:text-muted-foreground dark:hover:bg-muted/50",
                BadgeVariant.Link => "text-primary underline-offset-4 hover:underline",
                _ => "bg-primary text-primary-foreground [a]:hover:bg-primary/80",
            },
            @class);

    /// <summary>The value for <c>data-variant</c>.</summary>
    public static string Name(BadgeVariant variant) => variant switch
    {
        BadgeVariant.Secondary => "secondary",
        BadgeVariant.Destructive => "destructive",
        BadgeVariant.Outline => "outline",
        BadgeVariant.Ghost => "ghost",
        BadgeVariant.Link => "link",
        _ => "default",
    };
}
