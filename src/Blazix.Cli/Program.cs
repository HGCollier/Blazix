using System.Reflection;
using Blazix.Cli;

// blazix: copies styled components into a Blazor project.
//   init                 set up Tailwind (no Node), theme tokens, Blazix.Primitives and usings
//   add <item...>        copy components (and their dependencies) into your project
//   list                 show what's available
//   migrate rtl [path]   make components you already copied right-to-left ready
//   migrate icons [path] swap the icons of components you already copied to another icon library

var version = typeof(Registry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
    .InformationalVersion.Split('+')[0];

try
{
    return args switch
    {
        ["init", .. var rest] => Commands.Init(Options.Parse(rest), version),
        ["add", .. var rest] => Commands.Add(Options.Parse(rest), version),
        ["list", .. var rest] => Commands.List(Options.Parse(rest)),
        ["migrate", "rtl", .. var rest] => Commands.MigrateRtl(Options.Parse(rest)),
        ["migrate", "icons", .. var rest] => Commands.MigrateIcons(Options.Parse(rest)),
        // Not for apps: the docs build renders its RTL examples from a copy made by this.
        ["rtl-copy", var from, var to, var ns] => Commands.RtlCopy(from, to, ns),
        ["--version"] => Print(version),
        _ => Print(Commands.Usage),
    };
}
catch (CliException ex)
{
    await Console.Error.WriteLineAsync($"error: {ex.Message}");
    return 1;
}

static int Print(string text)
{
    Console.WriteLine(text);
    return 0;
}
