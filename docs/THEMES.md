# Themes — Design

A theme is the set of colours the bar is drawn with: background, border, label text, the
hover and pressed highlights of icon cells, the grip dots, the accent used for keyboard
focus and the drop indicator, and the drop-target highlight. Icons themselves are never
recoloured.

Themes are chosen in *Settings → Theme*. Four built-in themes ship alongside **Classic**,
plus **Custom**, which derives a full palette from one background colour.

## Palettes

| Theme | Background | Border | Text | Hover | Grip | Accent | Intended for |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **Classic** | `#73806E` olive grey | `#333333` | white | white 18 % | white 44 % | `#4C8BF5` | The reference look; sits well on photo wallpapers. |
| **Graphite** | `#2B2B2B` | `#141414` | `#EAEAEA` | white 13 % | white 38 % | `#60A5FA` | Dark desktops and dark Windows mode. |
| **Paper** | `#F3F2EE` | `#B8B6B0` | `#202020` | black 8 % | black 33 % | `#2563EB` | Light desktops; matches light Windows mode. |
| **Midnight** | `#1C2733` | `#0D141B` | `#E3ECF5` | cyan 16 % | `#9FB7CC` 42 % | `#7DD3FC` | Blue/teal wallpapers; a cooler dark theme. |
| **Sand** | `#D8C8A4` | `#8A7A55` | `#2B2415` | black 9 % | black 35 % | `#B45309` | Warm, low-contrast light theme. |
| **Custom** | user colour | background darkened 50 % | white or `#202020` | white 18 % or black 8 % | white 44 % or black 33 % | `#4C8BF5` or `#2563EB` | Any colour; see the rule below. |

Percentages are the alpha of a white or black overlay, so highlights work on any background.

**Custom rule.** The relative luminance `0.299 R + 0.587 G + 0.114 B` of the background decides
the variant: above 0.6 the theme uses the *light* set (dark text, black overlays, blue accent),
otherwise the *dark* set (white text, white overlays, lighter blue accent). The border is the
background darkened by half. This keeps labels readable for every colour the user can pick.

Existing layouts that only had a background colour keep their look: when a layout file has no
`theme` field, the app picks *Classic* if the colour is still the default and *Custom* otherwise.

## How it works

- `ThemePalette` and `ThemeCatalog` (core library) hold the colours as hex strings and the
  custom-derivation rule; both are unit-tested and know nothing about WPF.
- `ThemeManager` (app) turns a palette into frozen brushes and writes them into the application
  resources under fixed keys (`Theme.Background`, `Theme.Border`, `Theme.Foreground`, `Theme.Hint`,
  `Theme.Hover`, `Theme.HoverBorder`, `Theme.Pressed`, `Theme.Grip`, `Theme.GripPattern`,
  `Theme.Accent`, `Theme.DropTarget`).
- Every style and template that paints the bar references those keys with `DynamicResource`, so
  switching themes restyles the bar, the group popup and the drop indicator immediately, without
  rebuilding any view. Dialogs keep the standard Windows look.
- The theme is part of the settings, so it travels with Google Drive sync copies and is applied
  when a copy is loaded.

## Adding a theme

1. Add a `ThemePalette` to `ThemeCatalog` (core) and include it in `Builtin`.
2. Nothing else: the settings dialog lists `ThemeCatalog.Builtin`, and the unit test that checks
   ids and colour formats covers the new entry.

## Possible later work

- **Translucent themes** (e.g. "Frost"): a semi-transparent background with the desktop blurred
  behind it. Needs `AllowsTransparency` on the bar window or the Windows 11 backdrop API, which
  interacts with the resize borders and the popup; kept out until the core themes have settled.
- **Cell shape** per theme (corner radius, hover outline thickness) and a **label font size**.
- **Follow Windows light/dark mode** automatically, mapping to Paper and Graphite.
