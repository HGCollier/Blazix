# Blazix.Primitives

Unstyled Blazor primitives. You bring the CSS.

They follow the WAI-ARIA Authoring Practices, but they haven't been tested with screen readers or audited. If your project needs to be accessible, test it yourself.

```bash
dotnet add package Blazix.Primitives
```

```razor
@using Blazix.Primitives

<MenuRoot>
    <MenuTrigger class="button">Song</MenuTrigger>
    <MenuPopup class="menu" SideOffset="4">
        <MenuItem class="item" OnClick="AddToLibrary">Add to library</MenuItem>
        <MenuSubmenuRoot>
            <MenuSubmenuTrigger class="item">Add to playlist</MenuSubmenuTrigger>
            <MenuPopup class="menu">
                <MenuItem class="item">Get Up!</MenuItem>
            </MenuPopup>
        </MenuSubmenuRoot>
        <MenuSeparator class="separator" />
        <MenuCheckboxItem class="item" @bind-Checked="shuffle">
            <MenuCheckboxItemIndicator>✓</MenuCheckboxItemIndicator> Shuffle
        </MenuCheckboxItem>
    </MenuPopup>
</MenuRoot>
```

Accordion, Alert Dialog, Autocomplete, Avatar, Button, Checkbox, Checkbox Group, Collapsible, Combobox, Context Menu, Dialog, Drawer, Field, Fieldset, Form, Input, Menu, Menubar, Meter, Navigation Menu, Number Field, OTP Field, Popover, Preview Card, Progress, Radio, Radio Group, Scroll Area, Select, Separator, Slider, Switch, Tabs, Toast, Toggle, Toggle Group, Toolbar and Tooltip, each split into parts (`DialogRoot`, `DialogTrigger`, `DialogPortal`, `DialogBackdrop`, `DialogPopup`...).
Plus the utilities: `CspProvider`, `DirectionProvider`, `UseRender` (`useRender`), `AttributeBuilder` (`mergeProps`) and `MediaQuery`.
Also a headless Questionnaire, with its parts (`QuestionnaireRoot`, `QuestionnaireItem`, `QuestionnaireChoice`...).

- Needs an interactive render mode: Interactive Server or WebAssembly.
- Trimmable and AOT-compatible (`IsAotCompatible`, with no trim or AOT warnings), so it works in trimmed WebAssembly apps and with WebAssembly AOT.
- No `<script>` tag: each component's JS module ships in this package and is imported on first use.
- WAI-ARIA patterns and keyboard behaviour: roles and states, roving focus, typeahead, focus trapping and return, and dismissal by Escape, outside press and Tab.
- State lives in Blazor, including what JS tracks, such as the highlighted menu item (`props.Highlighted`). Focus follows Blazor's changes: a removed item hands focus to its neighbour, and closing or disabling from code returns focus to the trigger.
- Enter/exit animations: a closing popup stays mounted with `data-closed` + `data-ending-style` until your CSS animation or transition finishes, and `data-starting-style` marks the first frame on open (except for a popup that's already open on its first render, which appears as it is).
- Popups render in the browser's top layer (Popover API), so they aren't clipped by `overflow: hidden`, yet they stay where you wrote them in the DOM, so CSS isolation and theme classes apply.

## Styling contract

The parts render data attributes and CSS variables to style against. The common ones:

| Hook | Meaning |
|---|---|
| `data-open` / `data-closed` | open state of a popup, panel or its trigger |
| `data-popup-open` | on a trigger while its popup is open |
| `data-starting-style` / `data-ending-style` | where enter transitions start, and where exit transitions go |
| `data-highlighted`, `data-disabled`, `data-checked` / `data-unchecked` | item and control state |
| `data-side`, `data-align` | a popup's final placement |
| `--anchor-width`, `--anchor-height`, `--available-width`, `--available-height`, `--transform-origin` | layout helpers on popups |

## Render your own elements (`render`, `asChild`)

Every element-rendering part takes a `Render` fragment. Spread `props.Attributes` onto your element or component:

```razor
<MenuItem OnClick="Track">
    <Render Context="item">
        <a @attributes="item.Attributes" href="/settings">Settings</a>
    </Render>
</MenuItem>
```

Attributes you put on the part are merged in: your handlers run before the part's, and your `style` comes after the part's.
A trigger rendered as something other than a `<button>` is detected and gets `role="button"`, a tab stop and Enter/Space. You only set `NativeButton` to skip that check.

Want pre-styled components instead? `dnx Blazix.Cli add dropdown-menu` copies Tailwind-styled wrappers into your project.
