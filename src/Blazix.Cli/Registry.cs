using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Blazix.Cli;

internal sealed record RegistryItem(string Name, string Description, string[] Files, string[] Dependencies, bool UsesPrimitives);

internal static partial class Registry
{
    // The registry project's namespace, which every embedded file is written in. Components are
    // rewritten to the app's components namespace, setup files (TailwindWatcher.cs) to its root namespace.
    private const string SourceNamespace = "Blazix.Registry";

    private static readonly Assembly Assembly = typeof(Registry).Assembly;

    public static string Read(string name)
    {
        using var stream = Assembly.GetManifestResourceStream(name) ?? throw new CliException($"registry file '{name}' is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<RegistryItem> All()
    {
        var root = JsonNode.Parse(Read("registry.json"))!["items"]!.AsObject();
        return [.. root.Select(kv => new RegistryItem(
            kv.Key,
            (string?)kv.Value!["description"] ?? "",
            [.. kv.Value["files"]!.AsArray().Select(f => (string)f!)],
            kv.Value["dependencies"]?.AsArray().Select(d => (string)d!).ToArray() ?? [],
            (bool?)kv.Value["usesPrimitives"] ?? false))];
    }

    /// <summary>The requested items plus their dependencies, dependencies first.</summary>
    public static List<RegistryItem> Resolve(IEnumerable<string> names)
    {
        var all = All().ToDictionary(i => i.Name);
        var ordered = new List<RegistryItem>();
        var seen = new HashSet<string>();

        foreach (var name in names)
        {
            Visit(name);
        }
        return ordered;

        void Visit(string name)
        {
            if (!all.TryGetValue(name, out var item))
            {
                throw new CliException($"unknown item '{name}'. Available: {string.Join(", ", all.Keys)}");
            }
            if (!seen.Add(name))
            {
                return;
            }
            foreach (var dependency in item.Dependencies)
            {
                Visit(dependency);
            }
            ordered.Add(item);
        }
    }

    public static string RewriteComponentNamespace(string source, string ns) =>
        source.Replace(SourceNamespace, ns, StringComparison.Ordinal);

    public static string RewriteRootNamespace(string source, string rootNamespace) =>
        RootNamespaceLine.Replace(source, $"$1 {rootNamespace}$2");

    [GeneratedRegex(@"^(using|namespace) Blazix\.UI\.Registry(;?)\r?$", RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RootNamespaceLine { get; }
}
