# Blazix.Cli

Copies Blazix's styled Blazor components into your project as source you own. No Node.js needed.

```bash
dnx Blazix.Cli init                # once: Tailwind (standalone binary), theme tokens, usings, Blazix.Primitives
dnx Blazix.Cli add dropdown-menu   # copies the component and its dependencies
dnx Blazix.Cli diff dropdown-menu  # what add --overwrite would change in your copy
dnx Blazix.Cli list                # what's available / installed
```

`dnx` ships with the .NET 10 SDK. To pin a version per repo instead:

```bash
dotnet new tool-manifest
dotnet tool install Blazix.Cli
dotnet blazix add dropdown-menu
```

Options: `--project <path>` (when a folder has several projects), `--dir <folder>` (`init`, default `Components/UI`),
`--rtl` (`init`, copy components right-to-left ready), `--icon-library <name>` (`init`, the components' icon set),
`--all` (`add`, every component), `--overwrite` (`add`, replace files you already have),
`--skip-package` (don't touch package references).

`dnx Blazix.Cli diff` previews what `add --overwrite` would change, before you run it. With no items, it says whether each
component you've added still matches this version's copy. With items (and their dependencies), it shows the changes to their
files as a unified diff, where `-` lines are yours and `+` lines are this version's. It keeps no record of what was copied,
so your own edits and upstream changes both show as differences.

For right-to-left apps, `init --rtl` sets `"rtl": true` in `blazix.json`, and `add` then copies components with logical
classes (`ms-2`, `start-0`, `text-start`). `dnx Blazix.Cli migrate rtl [path]` converts components you already copied.

The components draw their icons with the `Icon` component, from the set in `blazix.json`'s `"iconLibrary"`: `lucide` (the default),
`tabler`, `hugeicons`, `phosphor`, `remixicon` or `fontawesome`. `init` and `add` install that set's package (`Blazix.Icons.Lucide`,
...), and `add` writes `<Icon Data="LucideIcons.ChevronDown" />`. Pick one with `init --icon-library tabler`, and switch components
you already copied with `dnx Blazix.Cli migrate icons --to phosphor`. `add` takes no `--icon-library`: it refuses a set other than
the one in `blazix.json`. `migrate icons` asks which libraries to migrate from and to (or takes `--from` and `--to`, which must
differ), and confirms before it writes; `--yes` skips the confirmation. It moves the `<Icon>`s, and lists the icons with no
equivalent in the new library, which it leaves as they are. Copies from earlier versions draw their icons inline, as SVG:
`migrate icons` leaves those alone, and `add --overwrite` replaces a copy with one that uses `<Icon>`s.
For icons of your own, add the set's package: `dotnet add package Blazix.Icons.Lucide`.
