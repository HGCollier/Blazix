<p align="center">
  <a href="https://blazix-ui.com" target="_blank" rel="noopener">
    <img width="64" height="64" src="docs/Blazix.Docs/wwwroot/favicon.svg" alt="Blazix" />
  </a>
</p>

<h1 align="center">Blazix</h1>

<div align="center">

Unstyled Blazor primitives and beautifully designed components you copy into your app.
Built with .NET, Tailwind CSS and JavaScript modules for browser behaviour, with no Node.js or npm. Open Source. Open Code.

[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Blazix.Primitives](https://img.shields.io/nuget/v/Blazix.Primitives.svg?label=Blazix.Primitives)](https://www.nuget.org/packages/Blazix.Primitives)
[![Blazix.Cli](https://img.shields.io/nuget/v/Blazix.Cli.svg?label=Blazix.Cli)](https://www.nuget.org/packages/Blazix.Cli)
[![NuGet downloads](https://img.shields.io/nuget/dt/Blazix.Primitives.svg)](https://www.nuget.org/packages/Blazix.Primitives)
[![docs](https://github.com/HGCollier/Blazix/actions/workflows/docs.yml/badge.svg)](https://github.com/HGCollier/Blazix/actions/workflows/docs.yml)

</div>

<p align="center">
  <a href="https://blazix-ui.com">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="docs/Blazix.Docs/wwwroot/images/home-dark.png" />
      <img src="docs/Blazix.Docs/wwwroot/images/home-light.png" alt="Blazix components" />
    </picture>
  </a>
</p>

## Installation

**Styled components.** Copy them into your project as source you own:

```bash
dnx Blazix.Cli init
dnx Blazix.Cli add dropdown-menu
```

**Headless primitives.** Behaviour and accessibility, zero styles:

```bash
dotnet add package Blazix.Primitives
```

**Icons.** The components draw their icons with the `Icon` component. `dnx Blazix.Cli add` installs the package of the icon set
in `blazix.json`'s `iconLibrary`: [Lucide](https://lucide.dev) by default, or Tabler, Hugeicons, Phosphor, Remix Icon or Font Awesome
with `init --icon-library`. For icons of your own, add the set's package too:

```bash
dotnet add package Blazix.Icons.Lucide
```

No Node.js, npm or `package.json` required. Tailwind runs through its standalone CLI as part of `dotnet build`.

## Accessibility

Blazix aims for WCAG 2.2 AA and follows the [WAI-ARIA Authoring Practices](https://www.w3.org/WAI/ARIA/apg/) patterns, but it hasn't been tested with screen readers or voice control, and it hasn't been audited.

What has been checked: keyboard use, focus management, ARIA state and [axe](https://github.com/dequelabs/axe-core) scans, in Chromium and Firefox. Automated checks like axe find only some issues.

If your project needs to be accessible, use Blazix at your own risk and test it yourself with the assistive technology your users have. If you find a problem, please [open an issue](https://github.com/HGCollier/Blazix/issues).

## Documentation

Visit [blazix-ui.com/docs](https://blazix-ui.com/docs) to view the documentation for the components, and [blazix-ui.com/docs/primitives](https://blazix-ui.com/docs/primitives) for the primitives.

## Contributing

Please read the [contributing guide](CONTRIBUTING.md).

## License

Licensed under the [MIT license](LICENSE).
