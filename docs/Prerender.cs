// A script, run with `dotnet run`: not native-compiled, which file-based apps are by default.
#:property PublishAot=false

// Pre-renders the published docs app into a static site for GitHub Pages:
//   dotnet run docs/Prerender.cs -- <publish dir> <output dir> [<sources dir>]
//
// The docs are a Blazor Web App whose server only prerenders pages, so the whole site can be saved as files.
// This starts the published app, saves every page as HTML (/docs/cli -> docs/cli.html, which Pages serves at
// /docs/cli), copies wwwroot, and downloads each asset a page references from the running app: MapStaticAssets
// serves fingerprinted URLs (app.<hash>.css) that aren't files on disk. WebAssembly takes over in the browser
// as it does on the real host.
//
// Pages are found by crawling links from / and from the @page directives under <sources dir> (default: docs/),
// so a page nothing links to is still saved. Any page that doesn't return 200 fails the run.

using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Blazix.Docs.Prerendering;

if (args.Length < 2)
{
    await Console.Error.WriteLineAsync("usage: dotnet run Prerender.cs -- <publish dir> <output dir> [<sources dir>]");
    return 2;
}

var publishDir = Path.GetFullPath(args[0]);
var outputDir = Path.GetFullPath(args[1]);
var sourcesDir = Path.GetFullPath(args.Length > 2 ? args[2] : AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".");
const string notFoundRoute = "/not-found";
const int port = 5287;
var origin = new Uri($"http://127.0.0.1:{port}");

if (Directory.Exists(outputDir))
{
    Directory.Delete(outputDir, recursive: true);
}
Directory.CreateDirectory(outputDir);

// 1. Everything published under wwwroot (the WebAssembly runtime, the primitives' JS modules, images...).
var wwwroot = Path.Combine(publishDir, "wwwroot");
foreach (var file in Directory.EnumerateFiles(wwwroot, "*", SearchOption.AllDirectories))
{
    // Pre-compressed copies: GitHub Pages compresses on its own and never serves these.
    if (Path.GetExtension(file) is ".br" or ".gz")
    {
        continue;
    }
    var target = Path.Combine(outputDir, Path.GetRelativePath(wwwroot, file));
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.Copy(file, target, overwrite: true);
}

// 2. The app, on a local port.
// The dotnet that runs this script, by absolute path: <dotnet root>/shared/Microsoft.NETCore.App/<version>/ is the runtime.
var dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..",
    OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
var app = Process.Start(new ProcessStartInfo(dotnet)
{
    ArgumentList = { Path.Combine(publishDir, "Blazix.Docs.dll"), "--urls", origin.ToString() },
    WorkingDirectory = publishDir,
    Environment = { ["ASPNETCORE_ENVIRONMENT"] = "Production" },
    RedirectStandardOutput = true,
    RedirectStandardError = true,
})!;
app.OutputDataReceived += (_, _) => { };
app.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 }) Console.Error.WriteLine($"  app: {e.Data}"); };
app.BeginOutputReadLine();
app.BeginErrorReadLine();

try
{
    // No automatic decompression or Accept-Encoding: assets are saved exactly as served, uncompressed.
    using var http = new HttpClient { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(60) };
    await WaitUntilUp(http, app);

    var seeds = Directory.EnumerateFiles(sourcesDir, "*.razor", SearchOption.AllDirectories)
        .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
        .SelectMany(f => Patterns.PageDirective().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
        .Where(route => !route.Contains('{') && !string.Equals(route, "/Error", StringComparison.OrdinalIgnoreCase))
        .Append("/")
        .ToHashSet(StringComparer.Ordinal);

    var pending = new Queue<string>(seeds.Order(StringComparer.Ordinal));
    var seen = new HashSet<string>(pending, StringComparer.Ordinal);
    var assets = new HashSet<string>(StringComparer.Ordinal);
    var failures = new List<string>();
    var pages = 0;

    while (pending.TryDequeue(out var route))
    {
        using var response = await http.GetAsync(route);
        var html = await response.Content.ReadAsStringAsync();
        // Every page must answer 200, except the not-found page, which may also answer 404.
        var ok = response.StatusCode == HttpStatusCode.OK || (route == notFoundRoute && response.StatusCode == HttpStatusCode.NotFound);
        if (!ok)
        {
            failures.Add($"{route}: {(int)response.StatusCode}");
            continue;
        }

        if (route == notFoundRoute)
        {
            // GitHub Pages serves 404.html for any missing path; the router then shows the not-found page.
            Write("404.html", html);
        }
        else if (route == "/")
        {
            Write("index.html", html);
        }
        else
        {
            var path = route.TrimStart('/');
            Write(path + ".html", html);
            // Pages redirects /docs to /docs/ when a docs/ folder exists, so a page with child pages is saved there too.
            Write(path + "/index.html", html);
        }
        pages++;

        foreach (var (url, isLink) in LocalUrls(html))
        {
            if (Path.HasExtension(url))
            {
                assets.Add(url);
            }
            else if (isLink && seen.Add(url))
            {
                pending.Enqueue(url);
            }
        }
    }

    // 3. Assets the pages reference that aren't files in wwwroot under that name (fingerprinted URLs).
    var downloaded = 0;
    foreach (var asset in assets.Order(StringComparer.Ordinal))
    {
        var target = Path.Combine(outputDir, Uri.UnescapeDataString(asset.TrimStart('/')));
        if (File.Exists(target))
        {
            continue;
        }
        using var response = await http.GetAsync(asset);
        if (!response.IsSuccessStatusCode)
        {
            failures.Add($"{asset}: {(int)response.StatusCode}");
            continue;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, await response.Content.ReadAsByteArrayAsync());
        downloaded++;
    }

    Console.WriteLine($"Saved {pages} pages and {downloaded} fingerprinted assets to {outputDir}");
    if (failures.Count > 0)
    {
        await Console.Error.WriteLineAsync("Failed:\n  " + string.Join("\n  ", failures));
        return 1;
    }
    return 0;
}
finally
{
    if (!app.HasExited)
    {
        app.Kill(entireProcessTree: true);
    }
    app.Dispose();
}

void Write(string relative, string content)
{
    var target = Path.Combine(outputDir, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllText(target, content);
}

static async Task WaitUntilUp(HttpClient http, Process app)
{
    for (var attempt = 0; attempt < 120; attempt++)
    {
        if (app.HasExited)
        {
            throw new InvalidOperationException($"The docs app exited with code {app.ExitCode} before it started listening.");
        }
        try
        {
            using var response = await http.GetAsync("/");
            return;
        }
        catch (HttpRequestException)
        {
            await Task.Delay(500);
        }
    }
    throw new TimeoutException("The docs app didn't start listening within 60 seconds.");
}

// Same-origin URLs in a page, as root-relative paths without query or fragment (the base href is /).
// isLink: an <a>/<link> href, which may be another page. The import map's entries are assets.
static IEnumerable<(string Url, bool IsLink)> LocalUrls(string html)
{
    var attributes = Patterns.UrlAttribute().Matches(html).Select(m => (m.Groups["url"].Value, m.Groups["attr"].Value == "href"));
    var importMap = Patterns.ImportMap().Match(html) is { Success: true } map
        ? Patterns.JsonString().Matches(map.Groups[1].Value).Select(m => (m.Groups[1].Value, false))
        : [];
    foreach (var (raw, isLink) in attributes.Concat(importMap))
    {
        var end = raw.IndexOfAny(['#', '?']);
        var url = end < 0 ? raw : raw[..end];
        if (url.Length == 0 || url.StartsWith("//", StringComparison.Ordinal) || url.Contains(':'))
        {
            continue; // fragment-only, protocol-relative, or another scheme (https:, mailto:, data:, sha256-...)
        }
#pragma warning disable S1075 // A URL path, not a file path.
        url = "/" + url.TrimStart('.', '/');
#pragma warning restore S1075
        yield return (url.Length > 1 ? url.TrimEnd('/') : url, isLink);
    }
}

namespace Blazix.Docs.Prerendering
{
    internal static partial class Patterns
    {
        // A page's route: the first line of a .razor file (ReadAllText drops a byte order mark). Not one quoted in a code sample.
        [GeneratedRegex(@"\A@page ""([^""]+)""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        public static partial Regex PageDirective();

        [GeneratedRegex(@"\b(?<attr>href|src)=""(?<url>[^""]*)""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        public static partial Regex UrlAttribute();

        [GeneratedRegex(@"<script type=""importmap""[^>]*>(.*?)</script>", RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
        public static partial Regex ImportMap();

        // The import map's paths: "./_framework/dotnet.js" and its fingerprinted target. Not its integrity hashes.
        [GeneratedRegex(@"""(\.?/?_(?:framework|content)/[^""]+)""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        public static partial Regex JsonString();
    }
}
