# Blazix.Icons

The `Icon` component and `IconData`, which Blazix's icon set packages are written against. Add a set, which brings this package with it:

| Package | Set |
|---|---|
| `Blazix.Icons.Lucide` | [Lucide](https://lucide.dev) |
| `Blazix.Icons.Tabler` | [Tabler Icons](https://tabler.io/icons) |
| `Blazix.Icons.Hugeicons` | [Hugeicons](https://hugeicons.com) (the free set) |
| `Blazix.Icons.Phosphor` | [Phosphor Icons](https://phosphoricons.com) |
| `Blazix.Icons.Remixicon` | [Remix Icon](https://remixicon.com) 4.8.0 |
| `Blazix.Icons.FontAwesome` | [Font Awesome Free](https://fontawesome.com) |

```razor
@using Blazix.Icons
@using Blazix.Icons.Lucide

<button type="button"><Icon Data="LucideIcons.Plus" class="size-4" /> New file</button>
<button type="button"><Icon Data="LucideIcons.Settings" Label="Settings" /></button>
```

- `Label` unset: decoration, `aria-hidden="true"`. Set: an image with that name, `role="img"` and `aria-label`.
- `StrokeWidth` changes an outline icon's stroke (default 2).
- Other attributes (`class`, `style`, `width`, `data-*`) go on the `<svg>` and win over the defaults.

Each icon is a property with no backing field, so trimming keeps only the icons your app uses.
