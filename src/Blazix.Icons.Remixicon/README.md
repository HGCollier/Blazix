# Blazix.Icons.Remixicon

[Remix Icon](https://remixicon.com) for Blazor, from `remixicon 4.8.0`: line and fill icons on a 24 grid, named as Remix names them (`ArrowDownSLine`, `ArrowDownSFill`). Each icon is rendered as inline SVG by the `Icon` component in [Blazix.Icons](https://www.nuget.org/packages/Blazix.Icons).

```bash
dotnet add package Blazix.Icons.Remixicon
```

```razor
@using Blazix.Icons
@using Blazix.Icons.Remixicon

<Icon Data="RemixIcons.ArrowDownSLine" class="size-4" />
<Icon Data="RemixIcons.ArrowDownSLine" Label="Open menu" />
```

- Without `Label` an icon is decoration, hidden from assistive tech (`aria-hidden="true"`): use it next to text that says the same thing.
- With `Label` it's an image with that name (`role="img"`, `aria-label`): use it for an icon that's the only content of a button or link.
- It takes the text color (`currentColor`), and any attribute you pass (`class`, `style`, `width`) goes on the `<svg>`.
- Each icon is a property with no backing field, so trimming (Blazor WebAssembly) keeps only the icons your app uses.

These are for your own markup. `dnx Blazix.Cli add` copies components that draw these icons too, and installs this package for the set in `blazix.json`'s `iconLibrary`.

## Why 4.8.0

Remix Icon 4.8.0 is the last release under Apache-2.0. From 4.9.0 (January 2026) Remix Icon has its own license, the Remix Icon License v1.0, which doesn't allow distributing the icons as an icon library (sections 3.1 and 3.2), as this package would. So this package stays on 4.8.0. Icons added in later releases aren't in it.

## License

Blazix's code is MIT. The icons are Apache-2.0: see `ICONS-LICENSE.txt` in the package.
