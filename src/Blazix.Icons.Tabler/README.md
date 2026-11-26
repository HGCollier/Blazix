# Blazix.Icons.Tabler

[Tabler Icons](https://tabler.io/icons) for Blazor, from `@tabler/icons 3.52.0`: outline icons on a 24 grid with 2 wide strokes, plus filled ones named `…Filled` (`TablerIcons.CircleCheckFilled`). Each icon is rendered as inline SVG by the `Icon` component in [Blazix.Icons](https://www.nuget.org/packages/Blazix.Icons).

```bash
dotnet add package Blazix.Icons.Tabler
```

```razor
@using Blazix.Icons
@using Blazix.Icons.Tabler

<Icon Data="TablerIcons.ChevronDown" class="size-4" />
<Icon Data="TablerIcons.ChevronDown" Label="Open menu" />
```

- Without `Label` an icon is decoration, hidden from assistive tech (`aria-hidden="true"`): use it next to text that says the same thing.
- With `Label` it's an image with that name (`role="img"`, `aria-label`): use it for an icon that's the only content of a button or link.
- It takes the text color (`currentColor`), and any attribute you pass (`class`, `style`, `width`) goes on the `<svg>`.
- Each icon is a property with no backing field, so trimming (Blazor WebAssembly) keeps only the icons your app uses.

These are for your own markup. `dnx Blazix.Cli add` copies components that draw these icons too, and installs this package for the set in `blazix.json`'s `iconLibrary`.

## License

Blazix's code is MIT. The icons are MIT: see `ICONS-LICENSE.txt` in the package.
