# Blazix.Cli

Copies Blazix's styled Blazor components into your project as source you own. No Node.js needed.

```bash
dnx Blazix.Cli init                # once: Tailwind (standalone binary), theme tokens, usings, Blazix.Primitives
dnx Blazix.Cli add dropdown-menu   # copies the component and its dependencies
dnx Blazix.Cli list                # what's available / installed
```

`dnx` ships with the .NET 10 SDK. To pin a version per repo instead:

```bash
dotnet new tool-manifest
dotnet tool install Blazix.Cli
dotnet blazix add dropdown-menu
```

Options: `--project <path>` (when a folder has several projects), `--dir <folder>` (`init`, default `Components/UI`),
`--overwrite` (`add`, replace files you already have), `--skip-package` (don't touch package references).
