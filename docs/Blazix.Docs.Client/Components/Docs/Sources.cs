using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace Blazix.Docs.Components.Docs;

/// <summary>The source code the docs show: each example, each registry file, and registry.json (all embedded at build time).</summary>
internal static class Sources
{
    private static readonly Assembly Assembly = typeof(Sources).Assembly;
    private static readonly ConcurrentDictionary<string, string?> Cache = new();

    private static readonly Lazy<IReadOnlyDictionary<string, RegistryItem>> Items = new(() =>
    {
        using var stream = Assembly.GetManifestResourceStream("registry.json")
                           ?? throw new InvalidOperationException("registry.json isn't embedded.");
        using var document = JsonDocument.Parse(stream);
        var items = new Dictionary<string, RegistryItem>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateObject())
        {
            items[item.Name] = new RegistryItem(
                Strings(item.Value, "files"),
                Strings(item.Value, "dependencies"),
                item.Value.TryGetProperty("usesPrimitives", out var p) && p.GetBoolean());
        }
        return items;
    });

    /// <summary>The prefix of a primitive's example name ("primitives/accordion-hero"): one in Primitives/Examples.</summary>
    private const string PrimitivePrefix = "primitives/";

    /// <summary>
    /// The example component for a component's example name ("accordion-demo") or a primitive's example name
    /// ("primitives/accordion-hero"), or null if it isn't ported.
    /// </summary>
    public static Type? ExampleType(string name) => Assembly.GetType(ExampleLocation(name).Type);

    /// <summary>An example's source.</summary>
    public static string? Example(string name) =>
        Cache.GetOrAdd("example:" + name, static key => Normalized(Read(ExampleLocation(key["example:".Length..]).Resource)));

    /// <summary>
    /// A primitive example's plain CSS version (Primitives/Examples/Css), or null if it has none or isn't a
    /// primitive's.
    /// </summary>
    public static Type? CssExampleType(string name) =>
        CssExampleLocation(name) is { } location ? Assembly.GetType(location.Type) : null;

    /// <summary>The plain CSS version's markup (its .razor file).</summary>
    public static string? CssExample(string name) =>
        Cache.GetOrAdd("css-example:" + name, static key =>
            CssExampleLocation(key["css-example:".Length..]) is { } location ? Normalized(Read(location.Resource + ".razor")) : null);

    /// <summary>The plain CSS version's stylesheet (its .css file).</summary>
    public static string? CssExampleStylesheet(string name) =>
        Cache.GetOrAdd("css-example-styles:" + name, static key =>
            CssExampleLocation(key["css-example-styles:".Length..]) is { } location ? Normalized(Read(location.Resource + ".css")) : null);

    /// <summary>The file name the docs show for an example ("AccordionHero.razor"), without the Example suffix.</summary>
    public static string ExampleFileName(string name) => ExampleBaseName(name) + ".razor";

    /// <summary>The file name the docs show for a primitive example's stylesheet ("AccordionHero.css").</summary>
    public static string StylesheetFileName(string name) => ExampleBaseName(name) + ".css";

    private static string ExampleBaseName(string name) =>
        Pascal(name.StartsWith(PrimitivePrefix, StringComparison.Ordinal) ? name[PrimitivePrefix.Length..] : name);

    /// <summary>The plain CSS version's type, and its embedded files' name without the extension.</summary>
    private static (string Type, string Resource)? CssExampleLocation(string name)
    {
        if (!name.StartsWith(PrimitivePrefix, StringComparison.Ordinal))
        {
            return null;
        }
        var primitive = Pascal(name[PrimitivePrefix.Length..]);
        return ($"Blazix.Docs.Primitives.Examples.Css.{primitive}Example", $"primitive-examples/css/{primitive}Example");
    }

    /// <summary>Where an example's component and its embedded source are.</summary>
    private static (string Type, string Resource) ExampleLocation(string name)
    {
        if (name.StartsWith(PrimitivePrefix, StringComparison.Ordinal))
        {
            var primitive = Pascal(name[PrimitivePrefix.Length..]);
            return ($"Blazix.Docs.Primitives.Examples.{primitive}Example", $"primitive-examples/{primitive}Example.razor");
        }
        var example = Pascal(name);
        return ($"Blazix.Docs.Examples.{example}Example", $"examples/{example}Example.razor");
    }

    /// <summary>A registry file's source, as the CLI copies it.</summary>
    public static string? RegistryFile(string file) =>
        Cache.GetOrAdd("registry:" + file, static key => Read("registry/" + key["registry:".Length..])?.Replace("\r\n", "\n").TrimEnd('\n'));

    /// <summary>A helper component in Examples/ that a page shows in full, e.g. "FluentValidationValidator.razor".</summary>
    public static string? ExampleFile(string name) =>
        Cache.GetOrAdd("example-file:" + name, static key => Read("examples/" + key["example-file:".Length..])?.Replace("\r\n", "\n").Trim('\n'));

    /// <summary>A file from src/Blazix.Registry/Setup that a page shows (typeset.css), by its name.</summary>
    public static string? SetupFile(string name) =>
        Cache.GetOrAdd("setup:" + name, static key => Read("setup/" + key["setup:".Length..])?.Replace("\r\n", "\n").TrimEnd('\n'));

    public static RegistryItem? Item(string name) => Items.Value.GetValueOrDefault(name);

    public static string Pascal(string name) =>
        string.Concat(name.Split('-', '_', ' ').Where(p => p.Length > 0).Select(p => char.ToUpperInvariant(p[0]) + p[1..]));

    /// <summary>Source text with \n line endings and no blank lines around it.</summary>
    private static string? Normalized(string? text) => text?.Replace("\r\n", "\n").Trim('\n');

    private static string? Read(string resource)
    {
        using var stream = Assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            return null;
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string[] Strings(JsonElement element, string property) =>
        element.TryGetProperty(property, out var array) ? array.EnumerateArray().Select(e => e.GetString() ?? "").ToArray() : [];
}

/// <summary>An entry in src/Blazix.Registry/registry.json.</summary>
internal sealed record RegistryItem(string[] Files, string[] Dependencies, bool UsesPrimitives);
