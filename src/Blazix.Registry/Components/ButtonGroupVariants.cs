namespace Blazix.Registry;

/// <summary>
/// The class recipe for a group of buttons.
/// Keep class names as complete string literals: Tailwind finds them by scanning source files.
/// </summary>
public static class ButtonGroupVariants
{
    private const string Base =
        "flex w-fit items-stretch *:focus-visible:relative *:focus-visible:z-10 has-[>[data-slot=button-group]]:gap-2 " +
        "has-[select[aria-hidden=true]:last-child]:[&>[data-slot=select-trigger]:last-of-type]:rounded-r-lg " +
        "[&>[data-slot=select-trigger]:not([class*='w-'])]:w-fit [&>input]:flex-1";

    public static string Get(Orientation orientation = Orientation.Horizontal, string? @class = null) =>
        ClassNames.Cn(
            Base,
            orientation == Orientation.Vertical
                ? "flex-col *:data-slot:rounded-b-none [&>[data-slot]:not(:has(~[data-slot]))]:rounded-b-lg! " +
                  "[&>[data-slot]~[data-slot]]:rounded-t-none [&>[data-slot]~[data-slot]]:border-t-0"
                : "*:data-slot:rounded-r-none [&>[data-slot]:not(:has(~[data-slot]))]:rounded-r-lg! " +
                  "[&>[data-slot]~[data-slot]]:rounded-l-none [&>[data-slot]~[data-slot]]:border-l-0",
            @class);
}
