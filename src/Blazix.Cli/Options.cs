namespace Blazix.Cli;

internal sealed record Options(List<string> Items, string? Project, string? Dir, bool Overwrite, bool SkipPackage, bool Rtl, bool Yes)
{
    public static Options Parse(string[] args)
    {
        var items = new List<string>();
        string? project = null, dir = null;
        bool overwrite = false, skipPackage = false, rtl = false, yes = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project" or "-p": project = Next(args, ref i); break;
                case "--dir": dir = Next(args, ref i); break;
                case "--overwrite": overwrite = true; break;
                case "--skip-package": skipPackage = true; break;
                case "--rtl": rtl = true; break;
                case "--yes" or "-y": yes = true; break;
                case var a when a.StartsWith('-'): throw new CliException($"unknown option '{a}'");
                default: items.Add(args[i]); break;
            }
        }
        return new Options(items, project, dir, overwrite, skipPackage, rtl, yes);
    }

    private static string Next(string[] args, ref int i) =>
        ++i < args.Length ? args[i] : throw new CliException($"'{args[i - 1]}' needs a value");
}
