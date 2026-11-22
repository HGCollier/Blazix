namespace Blazix.Registry;

/// <summary>The data-variant and data-size values of the questionnaire's buttons, named as Button's are.</summary>
internal static class QuestionnaireButtons
{
    public static string Name(ButtonVariant variant) => variant.ToString().ToLowerInvariant();

    public static string Name(ButtonSize size) => size switch
    {
        ButtonSize.IconXs => "icon-xs",
        ButtonSize.IconSm => "icon-sm",
        ButtonSize.IconLg => "icon-lg",
        _ => size.ToString().ToLowerInvariant(),
    };
}
