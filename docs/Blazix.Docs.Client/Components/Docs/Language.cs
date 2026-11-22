using System.Globalization;

namespace Blazix.Docs.Components.Docs;

/// <summary>
/// A language an RTL example can be shown in, as shadcn's language selector has them. The preview cascades the one
/// picked (Arabic first) and sets its <c>dir</c> and <c>lang</c> around the example.
/// </summary>
public sealed record Language(string Code, string Label, TextDirection Direction)
{
    public static readonly Language English = new("en", "English", TextDirection.Ltr);
    public static readonly Language Arabic = new("ar", "Arabic (العربية)", TextDirection.Rtl);
    public static readonly Language Hebrew = new("he", "Hebrew (עברית)", TextDirection.Rtl);

    public static readonly IReadOnlyList<Language> All = [English, Arabic, Hebrew];

    public string Dir => Direction == TextDirection.Rtl ? "rtl" : "ltr";

    /// <summary>For dates and numbers: Saudi Arabic and Israeli Hebrew, with the Gregorian calendar.</summary>
    public CultureInfo Culture => Code switch
    {
        "ar" => ArabicCulture,
        "he" => CultureInfo.GetCultureInfo("he-IL"),
        _ => CultureInfo.GetCultureInfo("en-US"),
    };

    // ar-SA defaults to the Hijri calendar; the examples' dates are Gregorian.
    private static readonly CultureInfo ArabicCulture = CreateArabic();

    private static CultureInfo CreateArabic()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
        culture.DateTimeFormat.Calendar = new GregorianCalendar();
        return CultureInfo.ReadOnly(culture);
    }

    /// <summary>Arabic text writes numbers in Arabic-Indic digits (٠١٢…); .NET formats them in Latin ones.</summary>
    public string Digits(string text) =>
        Code == "ar" ? string.Concat(text.Select(c => c is >= '0' and <= '9' ? (char)('٠' + (c - '0')) : c)) : text;

    public string Digits(int number) => Digits(number.ToString(CultureInfo.InvariantCulture));

    public static Language Find(string? code) => All.FirstOrDefault(l => l.Code == code) ?? Arabic;
}

/// <summary>An RTL example's strings in each language: <c>Texts[Language]</c> gives the ones to show.</summary>
public sealed record Translations<T>(T En, T Ar, T He)
{
    public T this[Language language] => language.Code switch
    {
        "en" => En,
        "he" => He,
        _ => Ar,
    };
}
