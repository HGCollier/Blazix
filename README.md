<p align="center">
  <a href="https://blazix-ui.com" target="_blank" rel="noopener">
    <img width="64" height="64" src="docs/Blazix.Docs/wwwroot/favicon.svg" alt="Blazix" />
  </a>
</p>

<h1 align="center">Blazix</h1>

<div align="center">

Accessible, unstyled Blazor primitives and beautifully designed components you copy into your app.
Built with nothing but .NET. Open Source. Open Code.

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

No Node.js, npm or `package.json` required. Tailwind runs through its standalone CLI as part of `dotnet build`.

## Documentation

Visit [blazix-ui.com/docs](https://blazix-ui.com/docs) to view the documentation for the components, and [blazix-ui.com/docs/primitives](https://blazix-ui.com/docs/primitives) for the primitives.

## Contributing

Please read the [contributing guide](CONTRIBUTING.md).

## License

Licensed under the [MIT license](LICENSE).
