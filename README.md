# Blazix

Headless Blazor primitives on NuGet, plus styled components you copy into your app.
Use the primitives on their own, or build on the styled ones. Built only with .NET tooling: no Node.js, npm or `package.json` anywhere.

| Ships as | What it is |
|---|---|
| **`Blazix.Primitives`** (NuGet library) | Behaviour + accessibility, zero styles. |
| **`Blazix.Cli`** (NuGet .NET tool) | Copies styled Tailwind components into your project as source you own. |

The docs app in [`docs/`](docs) is the documentation site: installation, theming, the CLI, and a page per component with live examples, their source and an API reference.
The site has two doc sets, each with its own sidebar, reached from the header's Components and Primitives links. **Components** documents the styled components. **Primitives**, under `/docs/primitives`, documents the primitives: an introduction and installation page, a handbook (styling, animation, composition, customization, accessibility), and a page per primitive with live Tailwind examples, its anatomy, and each part's parameters, data attributes and CSS variables. A component page links to the primitive it's built on, and the reverse.

---

## Using it

### Option A: headless only

```bash
dotnet add package Blazix.Primitives
```

```razor
@using Blazix.Primitives

<MenuRoot>
    <MenuTrigger class="my-button">Song</MenuTrigger>
    <MenuPopup class="my-menu" SideOffset="4">
        <MenuItem class="my-item" OnClick="AddToLibrary">Add to library</MenuItem>
        <MenuSubmenuRoot>
            <MenuSubmenuTrigger class="my-item">Add to playlist</MenuSubmenuTrigger>
            <MenuPopup class="my-menu">
                <MenuItem class="my-item">Get Up!</MenuItem>
            </MenuPopup>
        </MenuSubmenuRoot>
        <MenuSeparator class="my-separator" />
        <MenuRadioGroup @bind-Value="sort">
            <MenuRadioItem class="my-item" Value="date"><MenuRadioItemIndicator>•</MenuRadioItemIndicator> Date</MenuRadioItem>
            <MenuRadioItem class="my-item" Value="name"><MenuRadioItemIndicator>•</MenuRadioItemIndicator> Name</MenuRadioItem>
        </MenuRadioGroup>
        <MenuCheckboxItem class="my-item" @bind-Checked="shuffle">
            <MenuCheckboxItemIndicator>✓</MenuCheckboxItemIndicator> Shuffle
        </MenuCheckboxItem>
    </MenuPopup>
</MenuRoot>
```

Each primitive is a folder in [`src/Blazix.Primitives`](src/Blazix.Primitives), with the same parts (`MenuRoot`, `MenuTrigger`, `MenuPortal`, `MenuPositioner`, `MenuPopup`...):
Accordion, Alert Dialog, Autocomplete, Avatar, Button, Checkbox, Checkbox Group, Collapsible, Combobox, Context Menu, Dialog, Drawer, Field, Fieldset, Form, Input, Menu, Menubar, Meter, Navigation Menu, Number Field, OTP Field, Popover, Preview Card, Progress, Radio, Radio Group, Scroll Area, Select, Separator, Slider, Switch, Tabs, Toast, Toggle, Toggle Group, Toolbar and Tooltip.
Utilities are there too: `CspProvider`, `DirectionProvider`, `UseRender`, `AttributeBuilder` and `MediaQuery`.
A few more helpers the styled components need: `KeyboardShortcut`, `BrowserClipboard` and `BrowserCookies`.
Questionnaire is a headless multi-step form, with its own parts (`QuestionnaireRoot`, `QuestionnaireItem`, `QuestionnaireChoice`...).

You don't add a `<script>` tag. Each component's JS module ships inside the package and is imported the first time it's used.
Style the parts with plain CSS, CSS isolation, Tailwind or anything else. They render data attributes and CSS variables to style against. The common ones:

| Hook | Meaning |
|---|---|
| `data-open` / `data-closed` | open state of a popup, panel or its trigger (a popup stays `data-closed` while it animates out) |
| `data-popup-open` | on a trigger while its popup is open |
| `data-starting-style` / `data-ending-style` | where enter transitions start, and where exit transitions go |
| `data-highlighted`, `data-disabled`, `data-checked` / `data-unchecked` | item and control state |
| `data-side`, `data-align` | where a popup ended up after collision handling (`top`, `bottom`, `left`, `right`, `inline-start`, `inline-end`) |
| `--anchor-width`, `--anchor-height` | the trigger's size, e.g. for `min-width` |
| `--available-width`, `--available-height` | room left in the viewport, for `max-height` |
| `--transform-origin` | for scale animations that grow out of the trigger |

The styled components in [`src/Blazix.Registry/Components`](src/Blazix.Registry/Components) are a complete example of styling against these hooks with Tailwind.

### Composition: `Render`

Every part that renders an element takes a `Render` fragment, so you can swap in your own element or component.
React's `asChild` clones the child and merges props, which Blazor can't do. The Blazor equivalent is the render-function form:
the part gives you `props.Attributes` (id, role, ARIA, `data-*`, its event handlers) and you spread them onto whatever you render.

```razor
<MenuTrigger>
    <Render Context="trigger">
        <MyIconButton @attributes="trigger.Attributes" Icon="more" />
    </Render>
</MenuTrigger>

<MenuItem OnClick="Track">
    <Render Context="item">
        <a @attributes="item.Attributes" href="/settings">Settings</a>   @* a real link item *@
    </Render>
</MenuItem>
```

The props also carry the part's state, e.g. `Open` on a trigger, or `Highlighted`, `Checked` and `Disabled` on a menu item.

Rules:
- **Spread `props.Attributes` onto the focusable element.** If you render a component, it must forward them to its root, usually with `CaptureUnmatchedValues` + `@attributes`.
- **Attributes you put on the part are merged into `props.Attributes`**: your `class` and `data-*` are kept, your event handlers run before the part's, and your `style` comes after the part's. An attribute you write *after* the spread on your own element replaces the part's.
- **You don't set `NativeButton`.** A `<span>` or `<a>` trigger needs `role="button"`, a tab stop, Enter/Space activation and `aria-disabled` added, and a real `<button>` must *not* get them. The browser reports the element after the first interactive render and the part re-renders to match. Set it only to skip that check.
- **Name each `Context`** when you nest them (`Context="item"`, `Context="popup"`...). Blazor's default name `context` collides.

To build parts of your own with the same `Render` support, use `UseRender`, or derive from `PrimitivePart<TRenderProps>` and build attributes with `AttributeBuilder`.

### Option B: styled components

From your Blazor app's folder:

```bash
dnx Blazix.Cli init                # once per project
dnx Blazix.Cli add dropdown-menu   # copies DropdownMenu*.razor + dependencies
dotnet watch
```

`dnx` comes with the .NET 10 SDK and runs a NuGet tool without installing it, so it plays the role of `npx`.
If you'd rather pin the version per repo: `dotnet new tool-manifest && dotnet tool install Blazix.Cli`, then `dotnet blazix add ...`.

`init` changes your project like this. Every change is a plain file you can read and edit:

- Adds `Tailwind.targets` and imports it in your `.csproj`. Tailwind now builds with your project (see [No Node](#no-nodejs-anywhere)).
- Adds `TailwindWatcher.cs` plus one line in `Program.cs`. Tailwind recompiles as you edit, so hot reload shows new classes.
- Moves your existing `wwwroot/app.css` to `Styles/site.css` and imports it, so none of your styles are lost.
- Adds `Styles/theme.css`: design tokens (colours, radius, font, dark mode). Theme "neutral", font Inter. Re-theme it here; any theme that uses the same variable names pastes straight in. It asks for Inter and falls back to the system font, so load Inter for the exact look (the comment at the top shows how).
- Adds `Styles/Vendor/tw-animate.css` and `Styles/Vendor/variants.css`: two third-party CSS packages, vendored unmodified with their MIT notices. They provide the `animate-in`/`fade-in-0`/`zoom-in-95` utilities and the `data-open`/`data-closed`/`data-disabled` variants the components use. `init` replaces them, so don't edit them.
- Adds `@using <YourApp>.Components.UI` to `Components/_Imports.razor`, and a global using for `Blazix.Primitives` to your `.csproj`.
- Adds the `Blazix.Primitives` package, at the same version as the CLI.
- Writes `blazix.json`, which records your namespace, components folder and what you've installed.

Then:

```razor
<DropdownMenu>
    <DropdownMenuTrigger>Actions</DropdownMenuTrigger>
    <DropdownMenuContent Align="Align.End">
        <DropdownMenuLabel>Order</DropdownMenuLabel>
        <DropdownMenuItem OnClick="Refund">Refund <DropdownMenuShortcut>⌘R</DropdownMenuShortcut></DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem Destructive="true" OnClick="Cancel">Cancel order</DropdownMenuItem>
    </DropdownMenuContent>
</DropdownMenu>
```

The copied files are yours. Change classes, add variants, delete what you don't need.
To pick up upstream changes later, run `add <item> --overwrite` and review the diff in git.

**Each component renders `data-slot`, `data-variant`, `data-size` and `data-inset` attributes**, so your own CSS can target any part.
That includes the components with no primitive underneath, such as Sidebar, Skeleton, Card, Table, Spinner and Kbd.
Parameters are PascalCase, variants are enums (`Variant="ButtonVariant.Outline"`), a few boolean props stand in for a variant (`Destructive="true"`), and `DropdownMenuTrigger` is styled as an outline button by default.
`Cn()` (clsx + tailwind-merge in one) joins class lists and resolves Tailwind conflicts so the later class wins, so `Class="px-4"` overrides a component's `px-2.5`.

`dnx Blazix.Cli list` shows every item. Chart isn't included yet, there's no Sonner (Toast covers it), and Code Block has no Markdown example (it needs Streamdown).

---

## Accessibility

The primitives, the styled components and the docs site target WCAG 2.2 AA.

- **Primitives** follow each WAI-ARIA Authoring Practices pattern: roles and states rendered by Blazor, keyboard support and focus management. A `<label for>` or a `<label>` around the control names a Checkbox, Switch, Radio or Slider, even though the element that takes focus isn't one a label reaches, and a Select's or Combobox's list of options is named after its label. An `aria-label` or `aria-labelledby` you pass always wins.
- **`theme.css`** meets the contrast AA asks for: 4.5:1 for muted and destructive text, 3:1 for input borders, focus rings and the marks that show state (checked, pressed, selected). Keep that when you re-theme; the comment at the top lists what to check. In Windows' high contrast mode, focus and highlighted items show as outlines.
- **A Field** ties its `FieldDescription` and `FieldError` to its control with `aria-describedby`.
- **Your part**: name icon-only buttons and fields with no visible label (`aria-label`), give images `alt` text (`alt=""` when the text beside them says the same), and give anything that resizes or moves by dragging a single-tap alternative, such as buttons that call `ResizablePanelGroup.SetLayoutAsync`.

---

## No Node.js anywhere

| Concern | How it's handled |
|---|---|
| **Primitive JS** | Hand-written ES modules in `src/Blazix.Primitives/wwwroot` (one per component that needs the browser, plus shared ones in `core/`), with `// @ts-check` + JSDoc types, so editors type-check them with no build step. `dotnet pack` puts them in the `.nupkg` as static web assets (`_content/Blazix.Primitives/...`), and each component imports its module on first use. |
| **Tailwind** | `Tailwind.targets` downloads the official **standalone Tailwind CLI**, a single native binary, on the first build. The version is pinned, and it's cached per user under `%LOCALAPPDATA%\tailwindcss\<version>` or `~/.local/share/tailwindcss/<version>`. It compiles `Styles/app.css` → `wwwroot/app.css` before every build, skips the step when nothing changed, and minifies in Release. Publish output is pre-compressed like any other static asset. |
| **Hot reload** | `TailwindWatcher` (Development only) runs `tailwindcss --watch` next to your app. Add a class to a `.razor` file and Razor hot reload updates the markup, the watcher recompiles the CSS, and `dotnet watch` pushes the new stylesheet without reloading the page. The watcher reads stdin, so it exits when the app does, even if the app is killed. |
| **The CLI** | A .NET tool on NuGet. `dnx` runs it. |
| **CI / build servers** | Nothing to install: `dotnet build` fetches Tailwind like a package. Air-gapped? Set `<TailwindExecutable>` to a binary you provide. Don't need CSS? `<TailwindEnabled>false</TailwindEnabled>`. |

Overrides go in your `.csproj`: `TailwindVersion`, `TailwindExecutable`, `TailwindInput`, `TailwindOutput`, `TailwindEnabled`.
If your CSS scans files outside the project with `@source` (a shared component library, a `.Client` project), list them in `<TailwindSources>../Shared/**/*.razor;../Shared/**/*.cs</TailwindSources>` so changes to them rebuild the CSS too.
It's a property, not items, because IDEs take `.razor` items for components of the project.

---

## Working on the library

```bash
dotnet watch --project docs/Blazix.Docs
```

It's laid out as a registry that is the source of truth, a docs app that renders it, and a CLI that copies it.

- **Registry** (`src/Blazix.Registry`): `Components/` holds the styled components, `Setup/` what `init` copies, and `registry.json` the items. It's a Razor class library that is never packed. It compiles against `Blazix.Primitives`, `docs/` references it, and `Blazix.Cli` embeds these exact files at build time, so what you test is what ships.
- **Docs** (`docs/`): the docs site, a Blazor Web App that runs on WebAssembly. It's set up the way `init` sets up an app, but uses `src/Blazix.Registry` directly: `Blazix.Docs/Styles/app.css` imports `src/Blazix.Registry/Setup` and adds `@source` for `src/Blazix.Registry/Components` and the client. Edits to registry components hot reload through the project reference.
  - `docs/Blazix.Docs` is the host, the project you run: it prerenders each page, serves the files and runs Tailwind (`Program.cs`, `Components/App.razor`, `Styles/`, `wwwroot/`). `docs/Blazix.Docs.Client` has everything else, which runs in the browser. Running the client by itself shows nothing: it has no page of its own. The paths below are in the client.
  - `dotnet publish docs/Blazix.Docs` trims the client and compiles it ahead of time to WebAssembly, which needs `dotnet workload install wasm-tools`. Building doesn't.
  - `Components/Pages/Docs/` has the pages. `Components/Pages/Docs/Components/` has one per component.
  - `Examples/` has the examples, named in kebab case for previews (`button-demo` → `ButtonDemoExample.razor`). `<ComponentPreview Name="button-demo" />` shows one with its source.
  - The site embeds the pages, examples and registry files at build time, so pages can show their source and search can find text in them.
  - API Reference sections are read by reflection over the registry assembly, with its XML doc comments (embedded in the client, since a browser has no files to read), so they can't drift from the code. The client keeps both libraries whole when trimming (`TrimmerRootAssembly`) for that; apps don't need to.
  - `Primitives/` has the primitives' docs: `Pages/` (one per primitive, plus the handbook and the utilities) and `Examples/` (`<ComponentPreview Name="primitives/accordion-hero" />` shows `AccordionHeroExample.razor`). It's outside `Components/` so that only `Blazix.Primitives` is imported there: the registry's tags (`AccordionItem`, `DialogTrigger`...) would clash with the primitives'.
  - A primitive page lists each part with `<PartReference Type="typeof(AccordionRoot)">`, which reads the part's summary, element and parameters from `Blazix.Primitives` and its XML doc comments. The page lists the part's data attributes and CSS variables as `<StyleHook>` rows, since they're set across C# and JS.
- **Primitives** (`src/Blazix.Primitives`): edits to `.cs` files hot reload through the project reference too. After editing a JS module, refresh the page; browsers cache ES modules per page load.
- **Adding a component**: put its files in `src/Blazix.Registry/Components` in the `Blazix.Registry` namespace (the CLI rewrites it to the app's), and add an entry (files and dependencies) to [`src/Blazix.Registry/registry.json`](src/Blazix.Registry/registry.json). Then add its examples to `docs/Blazix.Docs.Client/Examples`, a page to `docs/Blazix.Docs.Client/Components/Pages/Docs/Components`, and a link to `DocsNav.Components`.
- **Adding a primitive**: put it in its own folder in `src/Blazix.Primitives`, then add a page and its examples to `docs/Blazix.Docs.Client/Primitives` and a link to `DocsNav.Primitives`. Document its data attributes and CSS variables in tables on the page.

Testing a release locally before you publish:

```bash
dotnet pack -c Release                     # → artifacts/packages/*.nupkg
cd ../some-test-app
dotnet new nugetconfig
dotnet nuget add source ../blazix/artifacts/packages -n blazix-local
dnx Blazix.Cli init && dnx Blazix.Cli add dropdown-menu
```

## Releasing

Both packages share one version (`<Version>` in `Directory.Build.props`), and they always release together.
CLI `0.3.0` adds `Blazix.Primitives 0.3.0` and copies components written against exactly that version.
The data attributes and CSS variables above are public API, so treat changes to them as breaking (SemVer).

Push a tag like `v0.2.0` and [`.github/workflows/release.yml`](.github/workflows/release.yml) packs both packages at that version and pushes them to NuGet. It needs a `NUGET_API_KEY` secret.

---

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

## Enter and exit animations

Opening mounts a popup; closing keeps it mounted until its exit animation finishes, then removes it.
Style it with keyframes, transitions or `@starting-style`:

```css
/* Keyframes (the styled components do this with tw-animate-css:
   data-open:animate-in data-open:zoom-in-95 data-closed:animate-out data-closed:zoom-out-95) */
.menu               { animation: menu-in 160ms ease-out; }
.menu[data-closed]  { animation: menu-out 120ms ease-in forwards; }

/* Transitions */
.menu                                                { transition: opacity 150ms, transform 150ms; }
.menu[data-starting-style], .menu[data-ending-style] { opacity: 0; transform: scale(.95); }
```

- On close, Blazor renders `data-closed` and `data-ending-style` and makes the popup `inert`. The trigger says `aria-expanded="false"` immediately, so assistive tech and stray clicks see a closed popup. The browser side waits for the popup's own animations or transitions to finish, then Blazor removes it.
- Reopening mid-exit cancels the exit and keeps the same element.
- A popup that's already open on its root's first render appears as it is, with no `data-starting-style`. So when a Dialog turns into a Drawer at a breakpoint, the drawer doesn't slide in. Keyframe animations still play.
- Infinite animations inside the popup (a spinner) are ignored, and a timeout based on the computed durations is the safety net.
- The styled components animate the same way whatever the reduced-motion setting. Add `motion-reduce:animate-none` to a component if you want it to open and close instantly then.

## Accessibility

Each primitive follows its WAI-ARIA pattern and keyboard behaviour: roles and states, labelling, roving focus, typeahead, focus trapping and return, and dismissal by Escape, outside press and Tab.
Every role and ARIA state is rendered by Blazor.
The styled components use Tailwind's `outline-hidden`, so focus stays visible in Windows High Contrast (forced colours).

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
  Blazix.Cli/                → NuGet tool (`dnx Blazix.Cli ...`): init / add / list, embeds Blazix.Registry/
  Blazix.Registry/           source of truth for what the CLI copies (never packed)
    registry.json            items, files, dependencies
    Components/              ← what `add` copies: styled components (Tailwind classes)
    Setup/                   ← what `init` copies
      Tailwind.targets, TailwindWatcher.cs
      theme.css              design tokens (the default theme)
      Vendor/                tw-animate-css + variants.css, unmodified
docs/
  Blazix.Docs/               the docs site's host (run this): prerendering, Tailwind, static files
  Blazix.Docs.Client/        the docs site on WebAssembly: pages, examples, uses Blazix.Registry/ from source
    Primitives/              the primitives' pages and examples (for Blazix.Primitives)
```

## Not done yet

- **The components built on third-party libraries** (listed above), and primitives for those libraries.
- **Automated tests.** Browser checks of every primitive (keyboard, focus, ARIA, state sync, animations) and of the docs site ran while building this, but they aren't in the repo yet. bUnit for ARIA output and state, and Playwright for .NET for keyboard, focus and positioning, would need no Node.
- **`blazix diff <item>`**, to show upstream changes before overwriting.
- **Interactive Auto apps.** The docs run the components on WebAssembly (prerendered, trimmed and AOT compiled when published), and they were built on Interactive Server; Auto, which switches between the two, isn't tested. Components used on the client belong in the `.Client` project (`init --project`), and Tailwind needs `@source` pointing at it.
- **Platforms.** Tested on Windows x64. Tailwind asset names for macOS and Linux follow the release naming. Alpine/musl needs `<TailwindExecutable>`.
