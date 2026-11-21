namespace Blazix.Docs.Components.Docs;

/// <summary>
/// Choices the reader makes once for the whole site: how to run the CLI, whether the
/// installation steps show the command or the manual steps, and how the primitives' examples are styled. Every
/// block that shows them follows along.
/// </summary>
internal sealed class DocsConfig
{
    /// <summary>"dnx" (run the tool without installing it) or "dotnet" (a tool installed in the repo's manifest).</summary>
    public string Runner { get; private set; } = "dnx";

    /// <summary>"cli" or "manual".</summary>
    public string InstallationType { get; private set; } = "cli";

    /// <summary>"css" (a plain stylesheet) or "tailwind": how the primitives' examples are styled.</summary>
    public string StylingMethod { get; private set; } = "css";

    public event Action? Changed;

    public void SetStylingMethod(string? method)
    {
        if (method is null || method == StylingMethod)
        {
            return;
        }
        StylingMethod = method;
        Changed?.Invoke();
    }

    public void SetRunner(string? runner)
    {
        if (runner is null || runner == Runner)
        {
            return;
        }
        Runner = runner;
        Changed?.Invoke();
    }

    public void SetInstallationType(string? type)
    {
        if (type is null || type == InstallationType)
        {
            return;
        }
        InstallationType = type;
        Changed?.Invoke();
    }

    /// <summary>A CLI command ("add button") as the chosen runner writes it.</summary>
    public static string Command(string runner, string args) =>
        runner == "dotnet" ? $"dotnet blazix {args}" : $"dnx Blazix.Cli {args}";
}
