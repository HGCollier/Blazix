# Blazix.Icons.Phosphor

[Phosphor Icons](https://phosphoricons.com) for Blazor, from `@phosphor-icons/core 2.1.1`: every weight: regular (`PhosphorIcons.CaretDown`), and `…Thin`, `…Light`, `…Bold`, `…Fill`, `…Duotone`. Each icon is rendered as inline SVG by the `Icon` component in [Blazix.Icons](https://www.nuget.org/packages/Blazix.Icons).

```bash
dotnet add package Blazix.Icons.Phosphor
```

```razor
@using Blazix.Icons
@using Blazix.Icons.Phosphor

<Icon Data="PhosphorIcons.CaretDown" class="size-4" />
<Icon Data="PhosphorIcons.CaretDown" Label="Open menu" />
```

- Without `Label` an icon is decoration, hidden from assistive tech (`aria-hidden="true"`): use it next to text that says the same thing.
- With `Label` it's an image with that name (`role="img"`, `aria-label`): use it for an icon that's the only content of a button or link.
- It takes the text color (`currentColor`), and any attribute you pass (`class`, `style`, `width`) goes on the `<svg>`.
- Each icon is a property with no backing field, so trimming (Blazor WebAssembly) keeps only the icons your app uses.

These are for your own markup. The components that `dnx Blazix.Cli add` copies don't need this package: they get the icons of the set in `blazix.json`'s `iconLibrary` as inline SVG.

## License

Blazix's code is MIT. The icons are MIT: see `ICONS-LICENSE.txt` in the package.
