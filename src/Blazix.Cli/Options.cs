namespace Blazix.Cli;

internal sealed record Options(List<string> Items, string? Project, string? Dir, bool Overwrite, bool SkipPackage, bool Rtl, bool Yes, string? IconLibrary = null, string? From = null, string? To = null, bool All = false)
{
    public static Options Parse(string[] args)
    {
        var items = new List<string>();
        string? project = null, dir = null;
        string? iconLibrary = null, from = null, to = null;
        bool overwrite = false, skipPackage = false, rtl = false, yes = false, all = false;
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
                case "--all" or "-a": all = true; break;
                case "--icon-library": iconLibrary = Next(args, ref i); break;
                case "--from": from = Next(args, ref i); break;
                case "--to": to = Next(args, ref i); break;
                case var a when a.StartsWith('-'): throw new CliException($"unknown option '{a}'");
                default: items.Add(args[i]); break;
            }
        }
        return new Options(items, project, dir, overwrite, skipPackage, rtl, yes, iconLibrary, from, to, all);
    }

    private static string Next(string[] args, ref int i) =>
        ++i < args.Length ? args[i] : throw new CliException($"'{args[i - 1]}' needs a value");
}
