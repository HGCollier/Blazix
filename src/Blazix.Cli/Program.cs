using System.Reflection;
using Blazix.Cli;

// blazix: copies styled components into a Blazor project.
//   init                 set up Tailwind (no Node), theme tokens, Blazix.Primitives and usings
//   add <item...>        copy components (and their dependencies) into your project
//   list                 show what's available

var version = typeof(Registry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
    .InformationalVersion.Split('+')[0];

try
{
    return args switch
    {
        ["init", .. var rest] => Commands.Init(Options.Parse(rest), version),
        ["add", .. var rest] => Commands.Add(Options.Parse(rest), version),
        ["list", .. var rest] => Commands.List(Options.Parse(rest)),
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
