# Contributing to Blazix

Thanks for helping out. This guide covers running the repo, how it's laid out, how the primitives work, and how releases are cut.

## Getting started

You need the .NET 10 SDK. There's no Node.js, npm or `package.json` anywhere in the repo.

```bash
dotnet watch --project docs/Blazix.Docs
```

That runs the docs site, which renders every component and primitive from source, so it's where you develop and test.

## Layout

```
Blazix.slnx
Directory.Build.props        shared settings + the single version number
Directory.Packages.props     central package versions
src/
  Blazix.Primitives/         → NuGet library (headless)
    Accordion/ … Tooltip/    one folder per primitive
    Core/, UseRender/        what the parts are built on
    wwwroot/                 the JS modules (core/ is shared)
  Blazix.Cli/                → NuGet tool (`dnx Blazix.Cli ...`): init / add / list / diff, embeds Blazix.Registry/
  Blazix.Registry/           source of truth for what the CLI copies (never packed)
    registry.json            items, files, dependencies
    Components/              ← what `add` copies: styled components (Tailwind classes), a folder per item
    Icons/                   IconPlaceholder (never copied) + RegistryIcons.g.cs, the components' icons in every set and their references
    Setup/                   ← what `init` copies
      Tailwind.targets, TailwindWatcher.cs
      theme.css              design tokens (the default theme)
      Vendor/                tw-animate-css + variants.css, unmodified
  Blazix.Icons/              → NuGet library: the <Icon> component and IconData
  Blazix.Icons.Lucide/ …     → NuGet libraries, one per icon set (Lucide, Tabler, Hugeicons, Phosphor, Remixicon,
                               FontAwesome), generated on build by Blazix.Icons.Set.targets
  Blazix.Icons.Generator/    reads each set's npm tarball (no Node): the set packages' icons, RegistryIcons.g.cs
docs/
  Blazix.Docs/               the docs site's host (run this): prerendering, Tailwind, static files
  Blazix.Docs.Client/        the docs site on WebAssembly: pages, examples, uses Blazix.Registry/ from source
  Blazix.Registry.Rtl/       the registry as `add` copies it with "rtl": true, generated on build for the RTL examples
    Primitives/              the primitives' pages and examples (for Blazix.Primitives)
```

It's laid out as a registry that is the source of truth, a docs app that renders it, and a CLI that copies it.

- **Registry** (`src/Blazix.Registry`): `Components/` holds the styled components, `Setup/` what `init` copies, and `registry.json` the items. It's a Razor class library that is never packed. It compiles against `Blazix.Primitives`, `docs/` references it, and `Blazix.Cli` embeds these exact files at build time, so what you test is what ships.
- **Docs** (`docs/`): a Blazor Web App that runs on WebAssembly. It's set up the way `init` sets up an app, but uses `src/Blazix.Registry` directly: `Blazix.Docs/Styles/app.css` imports `src/Blazix.Registry/Setup` and adds `@source` for `src/Blazix.Registry/Components` and the client. Edits to registry components hot reload through the project reference.
  - `docs/Blazix.Docs` is the host, the project you run: it prerenders each page, serves the files and runs Tailwind (`Program.cs`, `Components/App.razor`, `Styles/`, `wwwroot/`). `docs/Blazix.Docs.Client` has everything else, which runs in the browser. Running the client by itself shows nothing: it has no page of its own. The paths below are in the client.
  - `dotnet publish docs/Blazix.Docs` trims the client and compiles it ahead of time to WebAssembly, which needs `dotnet workload install wasm-tools`. Building doesn't.
  - `Components/Pages/Docs/` has the pages. `Components/Pages/Docs/Components/` has one per component.
  - The examples and the site draw icons with `Blazix.Icons.Lucide` (`<Icon Data="LucideIcons.ChevronRight" />`), as an app does, so their source can be copied as it is.
  - `Examples/` has the examples, named in kebab case for previews (`button-demo` → `ButtonDemoExample.razor`). `<ComponentPreview Name="button-demo" />` shows one with its source.
  - The site embeds the pages, examples and registry files at build time, so pages can show their source and search can find text in them.
  - API Reference sections are read by reflection over the registry assembly, with its XML doc comments (embedded in the client, since a browser has no files to read), so they can't drift from the code. The client keeps both libraries whole when trimming (`TrimmerRootAssembly`) for that; apps don't need to.
  - `RtlExamples/` has the RTL examples (`button-rtl` → `ButtonRtlExample.razor`), shown with `<ComponentPreview Name="button-rtl" Direction="rtl" />`. They use `docs/Blazix.Registry.Rtl`: the registry's components run through the CLI's RTL transform (`rtl-copy`) on every build, in the namespace `Blazix.Registry.Rtl`, the way shadcn renders its RTL examples from `ui-rtl/`. Each takes the picked `Language` as a cascading parameter and keeps its strings in a `Translations<T>` (English, Arabic, Hebrew).
  - `Primitives/` has the primitives' docs: `Pages/` (one per primitive, plus the handbook and the utilities) and `Examples/` (`<ComponentPreview Name="primitives/accordion-hero" />` shows `AccordionHeroExample.razor`). It's outside `Components/` so that only `Blazix.Primitives` is imported there: the registry's tags (`AccordionItem`, `DialogTrigger`...) would clash with the primitives'.
  - A primitive page lists each part with `<PartReference Type="typeof(AccordionRoot)">`, which reads the part's summary, element and parameters from `Blazix.Primitives` and its XML doc comments. The page lists the part's data attributes and CSS variables as `<StyleHook>` rows, since they're set across C# and JS.
- **Icons** (`src/Blazix.Icons*`): `Blazix.Icons` has `<Icon Data="..." />` and `IconData`; each set package is generated into `obj/` on build from the set's npm tarball. `src/Blazix.Icons.Generator/IconSets.cs` pins each tarball's version and sha512. Tarballs are downloaded once into `artifacts/icon-cache` and checked on every use. Each icon is an expression-bodied property, so trimming keeps only the icons an app uses. Remix Icon stays on 4.8.0, the last Apache-2.0 release (see `IconSets.cs`).
- **Primitives** (`src/Blazix.Primitives`): edits to `.cs` files hot reload through the project reference too. After editing a JS module, refresh the page; browsers cache ES modules per page load.

## Adding things

- **A component**: put its files in a folder of its own in `src/Blazix.Registry/Components`, named after its registry item (`alert-dialog` → `AlertDialog/`), in the `Blazix.Registry` namespace (the CLI rewrites it to the app's). Each `.razor` file starts with `@namespace Blazix.Registry`: Razor would otherwise put every folder in a namespace of its own. Add an entry (files, by their path in `Components/`, and dependencies) to [`src/Blazix.Registry/registry.json`](src/Blazix.Registry/registry.json). Then add its examples to `docs/Blazix.Docs.Client/Examples`, an RTL example to `docs/Blazix.Docs.Client/RtlExamples`, a page to `docs/Blazix.Docs.Client/Components/Pages/Docs/Components`, and a link to `DocsNav.Components`.
- **An icon in a component**: write `<IconPlaceholder Lucide="chevron-down" Tabler="chevron-down" Hugeicons="arrow-down-01" Phosphor="caret-down" Remixicon="arrow-down-s-line" FontAwesome="chevron-down" aria-hidden="true" class="..." />`, naming the icon in all six sets as each names its files (Font Awesome: solid, unless `regular/...`). Use the icon shadcn/ui uses where it has one. Then regenerate the data with `dotnet run --project src/Blazix.Icons.Generator -- registry src/Blazix.Registry artifacts/icon-cache src/Blazix.Registry/Icons/RegistryIcons.g.cs`. `add` turns each placeholder into `<Icon Data="LucideIcons.ChevronDown" />` from the package of the set in the app's `iconLibrary`, with the using directives and package it needs ([`src/Blazix.Cli/Icons.cs`](src/Blazix.Cli/Icons.cs)). The docs preview draws the Lucide icon through the same `Icon` component. A placeholder must be `aria-hidden="true"`, named (`role` and `aria-label`, or `aria-labelledby`), or given its attributes by `@attributes`. The docs build checks this, and that every name exists.
- **RTL in a component**: write physical classes (`ml-2`, `left-0`), as shadcn/ui does; `add` with `"rtl": true` maps them ([`src/Blazix.Cli/Rtl.cs`](src/Blazix.Cli/Rtl.cs)). Mark icons that point along the line with `cn-rtl-flip`, which becomes `rtl:rotate-180` (without `"rtl"` it stays as a class no CSS matches, so `migrate rtl` can find the icon later). Primitives' JS reads the computed CSS `direction` for keys, pointer movement and placement; what C# decides while rendering takes the cascaded `TextDirection` from `DirectionProvider`.
- **A primitive**: put it in its own folder in `src/Blazix.Primitives`, then add a page and its examples to `docs/Blazix.Docs.Client/Primitives` and a link to `DocsNav.Primitives`. Document its data attributes and CSS variables in tables on the page.

Everything targets WCAG 2.2 AA, and Firefox must work as well as Chromium. Check both.

## How the primitives work

```
MenuRoot (C#)           owns state: open/closed, @bind-Open, why it closed, the highlighted item
 ├─ MenuTrigger         <button> (or your element via Render) with aria-haspopup/expanded/controls
 └─ MenuPortal          optional, for portal-style markup: popups render in the top layer instead
     └─ MenuPositioner  optional: placement settings and attributes for the popup's positioner
         └─ MenuPopup   role="menu" inside the positioner, a popover="manual" element that
                        core/position.js places against the trigger
menu.js                 keyboard, typeahead, roving focus, dismissal, hover intent
                        finds the trigger and popup by id, so Render can put them on any element
```

Every component follows the same split:

- **Blazor owns all state.** JS owns only what has to happen inside a browser event: DOM focus, `preventDefault`, pointer tracking and positioning. Blazor can't call `preventDefault` conditionally (arrow keys would scroll the page), and on Blazor Server a round trip per keystroke feels laggy. JS never opens or closes anything itself: it reports intent ("close: escape"), C# decides, and JS reflects what Blazor renders. Every focus move is reported back, so state like `props.Highlighted` stays true in C#.
- **The top layer instead of portals.** Popups are `popover="manual"` elements, so they escape `overflow: hidden` and z-index stacking but stay where you wrote them in the DOM. Cascading values, CSS isolation (`::deep`) and a `.dark` class on any wrapper all keep working. `Portal` parts exist so portal-style markup works unchanged; `KeepMounted` on them keeps a closed popup in the DOM.
- **Blazor can change things at any time.** After every Blazor DOM patch, a `MutationObserver` re-syncs focus. If Blazor removes the focused item, focus moves to the item now in its place, never to `<body>`. Disabled and added items are picked up on the next key press, and closing or disabling from code returns focus to the trigger.
- **After-render code acts on what was rendered.** Parts snapshot open and mounted state while rendering, and `OnAfterRenderAsync` uses the snapshot rather than live state. On Blazor Server an earlier batch can be acknowledged after `Open` has already flipped, and reading live state there acted on stale DOM.
- **Two runtimes can share a page.** Under Interactive Auto, or with islands that have their own render modes, Server and WebAssembly components run side by side and import the same JS modules. Page-wide state (the layer stack, the scroll lock, inert counts) lives on `globalThis` under `Symbol.for(...)`, so both runtimes see one stack. Ids from `Ids.Next` carry a `w` on WebAssembly, so the two counters never produce the same id. Modal layers leave Blazor's reconnect dialog and error bar live, so a dropped circuit can be retried while a dialog is open.
- **JS ships in the package.** The modules in `src/Blazix.Primitives/wwwroot` are hand-written ES modules with `// @ts-check` + JSDoc types, so editors type-check them with no build step. `dotnet pack` puts them in the `.nupkg` as static web assets (`_content/Blazix.Primitives/...`), and each component imports its module on first use.

### Enter and exit animations

Opening mounts a popup; closing keeps it mounted until its exit animation finishes, then removes it.

- On close, Blazor renders `data-closed` and `data-ending-style` and makes the popup `inert`. The trigger says `aria-expanded="false"` immediately, so assistive tech and stray clicks see a closed popup. The browser side waits for the popup's own animations or transitions to finish, then Blazor removes it.
- Reopening mid-exit cancels the exit and keeps the same element.
- A popup that's already open on its root's first render appears as it is, with no `data-starting-style`. So when a Dialog turns into a Drawer at a breakpoint, the drawer doesn't slide in. Keyframe animations still play.
- Infinite animations inside the popup (a spinner) are ignored, and a timeout based on the computed durations is the safety net.

## Testing a release locally

```bash
dotnet pack -c Release                     # → artifacts/packages/*.nupkg
cd ../some-test-app
dotnet new nugetconfig
dotnet nuget add source ../blazix/artifacts/packages -n blazix-local
dnx Blazix.Cli init && dnx Blazix.Cli add dropdown-menu
```

## Releasing

All the packages (Blazix.Primitives, Blazix.Cli, Blazix.Icons and the six set packages) share one version (`<Version>` in `Directory.Build.props`), and they always release together.
CLI `0.3.0` adds `Blazix.Primitives 0.3.0` and copies components written against exactly that version.
Data attributes and CSS variables are public API, so treat changes to them as breaking (SemVer).

Push a tag like `v0.2.0` and [`.github/workflows/release.yml`](.github/workflows/release.yml) packs them at that version and pushes them to NuGet. It needs a `NUGET_API_KEY` secret.

## Not done yet

- **The components built on third-party libraries**: Chart, Sonner (Toast covers it) and Code Block's Markdown example (it needs Streamdown), and primitives for those libraries.
- **Automated tests.** Browser checks of every primitive (keyboard, focus, ARIA, state sync, animations) and of the docs site ran while building this, but they aren't in the repo yet. bUnit for ARIA output and state, and Playwright for .NET for keyboard, focus and positioning, would need no Node.
- **Interactive Auto: what's untested.** Auto was checked by hand in Chromium and Firefox in apps from `dotnet new blazor --interactivity Auto`, with per-page and global (`--all-interactive`) interactivity and a trimmed Release publish. The checks covered the first load on Server, reloads on WebAssembly, navigating with a popup open, and a Server island over a WebAssembly page. Not covered: AOT in an Auto app (the docs cover AOT on WebAssembly), Safari, reconnecting a circuit after a long disconnect, and a server with several instances behind a load balancer.
- **Platforms.** Tested on Windows x64. Tailwind asset names for macOS and Linux follow the release naming. Alpine/musl needs `<TailwindExecutable>`.
