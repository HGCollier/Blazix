# Blazix.Icons.FontAwesome

[Font Awesome Free](https://fontawesome.com/search?ic=free) for Blazor, from `@fortawesome/fontawesome-free 7.3.1`: the free solid, regular and brands icons: `FontAwesomeIcons.Solid.House`, `FontAwesomeIcons.Regular.Copy`, `FontAwesomeIcons.Brands.Github`, from the square 640 grid (`svgs-full`). Each icon is rendered as inline SVG by the `Icon` component in [Blazix.Icons](https://www.nuget.org/packages/Blazix.Icons).

```bash
dotnet add package Blazix.Icons.FontAwesome
```

```razor
@using Blazix.Icons
@using Blazix.Icons.FontAwesome

<Icon Data="FontAwesomeIcons.Solid.ChevronDown" class="size-4" />
<Icon Data="FontAwesomeIcons.Solid.ChevronDown" Label="Open menu" />
```

- Without `Label` an icon is decoration, hidden from assistive tech (`aria-hidden="true"`): use it next to text that says the same thing.
- With `Label` it's an image with that name (`role="img"`, `aria-label`): use it for an icon that's the only content of a button or link.
- It takes the text color (`currentColor`), and any attribute you pass (`class`, `style`, `width`) goes on the `<svg>`.
- Each icon is a property with no backing field, so trimming (Blazor WebAssembly) keeps only the icons your app uses.

These are for your own markup. The components that `dnx Blazix.Cli add` copies don't need this package: they get the icons of the set in `blazix.json`'s `iconLibrary` as inline SVG.

## Attribution

Font Awesome Free's icons are licensed under CC BY 4.0, which requires attribution: *Font Awesome Free by Fonticons, Inc. (https://fontawesome.com), licensed under CC BY 4.0 (https://fontawesome.com/license/free).* The package carries that license as `ICONS-LICENSE.txt`. Font Awesome is a trademark of Fonticons, Inc.; this package isn't made or endorsed by them.

## License

Blazix's code is MIT. The icons are CC BY 4.0: see `ICONS-LICENSE.txt` in the package.
