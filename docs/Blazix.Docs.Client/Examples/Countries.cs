namespace Blazix.Docs.Examples;

/// <summary>The countries the combobox examples pick from.</summary>
internal static class Countries
{
    public sealed record Country(string Code, string Value, string Label, string Continent);

    public static readonly Country[] All =
    [
        new("ar", "argentina", "Argentina", "South America"),
        new("au", "australia", "Australia", "Oceania"),
        new("br", "brazil", "Brazil", "South America"),
        new("ca", "canada", "Canada", "North America"),
        new("cn", "china", "China", "Asia"),
        new("co", "colombia", "Colombia", "South America"),
        new("eg", "egypt", "Egypt", "Africa"),
        new("fr", "france", "France", "Europe"),
        new("de", "germany", "Germany", "Europe"),
        new("it", "italy", "Italy", "Europe"),
        new("jp", "japan", "Japan", "Asia"),
        new("ke", "kenya", "Kenya", "Africa"),
        new("mx", "mexico", "Mexico", "North America"),
        new("nz", "new-zealand", "New Zealand", "Oceania"),
        new("ng", "nigeria", "Nigeria", "Africa"),
        new("za", "south-africa", "South Africa", "Africa"),
        new("kr", "south-korea", "South Korea", "Asia"),
        new("gb", "united-kingdom", "United Kingdom", "Europe"),
        new("us", "united-states", "United States", "North America"),
    ];

    public static readonly string[] Values = [.. All.Select(c => c.Value)];

    public static Country Find(string value) => All.First(c => c.Value == value);

    public static string LabelOf(string value) => All.FirstOrDefault(c => c.Value == value)?.Label ?? value;
}
