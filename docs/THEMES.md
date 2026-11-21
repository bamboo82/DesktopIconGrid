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
| **Custom** | user colour | background darkened 50 % | white or `#202020` | white 18 % or black 8 % | white 44 % or black 33 % | `#4C8BF5` or `#2563EB` | Any colour; see the rule below. Since 0.15.0 its hover and focus border colours can be its own (below). |

Percentages are the alpha of a white or black overlay, so highlights work on any background.

**Custom rule.** The relative luminance `0.299 R + 0.587 G + 0.114 B` of the background decides
the variant: above 0.6 the theme uses the *light* set (dark text, black overlays, blue accent),
otherwise the *dark* set (white text, white overlays, lighter blue accent). The border is the
background darkened by half. This keeps labels readable for every colour the user can pick.

**Picking the colour (0.15.0).** Beside the colour code, *Pick a colour…* opens the colour picker: a saturation and
brightness square (240 × 160) with a ring, a hue strip with a thumb, the code and its red, green and blue as boxes, the
colour it opened with and the new one side by side, and the swatches. All of them show one colour; a box with what is
not a colour changes nothing until it is left, and then shows the colour again. The arrow keys move the ring or the thumb
by one unit, ten with Shift; Enter is OK and Esc is Cancel. While it is open, the swatch beside the theme list follows
the new colour; Cancel puts back what the box had, and the bar changes at Save, as before. The conversions
(`ColourMath`: `#RRGGBB`, red-green-blue and hue-saturation-value) are in the core library and unit-tested. The text
icon window's colour and tile colour have the same button.

**Hover and focus colours (0.15.0).** Under the custom colour, *Hover colour* and *Focus border colour* each have a chip
with the colour in use, a code box, *Pick a colour…* and *Automatic*, ticked by default. While it is ticked the colour
is the one derived above and the box and button are off; unticking starts from the derived colour. *Hover strength*
(10–100 %) goes with the hover colour. They are stored as `hoverColor`, `hoverStrength` and `focusColor` (absent: the
derived ones, so a bar that never set them looks as before; anything that is not `#RRGGBB` is dropped at load) and sync
with the theme.

- The hover colour lies at its strength behind an icon, a group, a special icon or one of the bar's own buttons under the
  pointer (`Theme.PointerOver`); an item's or a separator's outline then (`Theme.PointerOverBorder`) and the pressed
  state (`Theme.Pressed`) are the same colour, stronger by the derived ratios (outline 80/46 or 48/20, pressed 80/46 or
  36/20). Without a strength, the derived overlay's own (18 % or 8 %).
- The focus colour borders keyboard focus, the selected match and a drop target, and draws the drop marker
  (`Theme.Focus`); a drop target's fill is the focus colour at the derived alpha (`Theme.DropTarget`).
- The derived fill (`Theme.Hover`) stays on the search box, the notice row and a text tile's frame, and the accent
  (`Theme.Accent`) on the launch frame, the search box's border and selection, and the missing badge.
- In the bar, its group popups and the window at the pointer alike. Dialogs, menus and Settings keep their own colours,
  and High Contrast keeps Windows'.
- A chosen focus colour under 3 : 1 against the background shows a warning under its row; Save still works.
- The theme's preview beside the list shows the background, one icon under the pointer and one with the focus border.

**Background picture (0.15.0).** Under the custom colour, *Background picture* has a thumbnail (or *None*), *Choose a
picture…*, *From a web address…* and *Remove*, and *Picture strength* (20–100 %).

- From a file: PNG, JPEG, BMP, GIF (its first frame), TIFF, and WebP where Windows can decode it; an icon is refused.
- From a web address: one `https://` address, fetched when that window's OK is pressed, once, by the web icon's download
  rules (`IconDownloader.FetchAsync`): https only, also after redirects; at most 10 MB; abandoned after 30 seconds
  without data; a web page refused; then decoded by Windows. A failure is said in the window (not https, too large, not
  a picture, no answer). The address is not kept, and the picture is never fetched again by itself: nothing at start,
  at sync or at a theme change.
- Stored: never the bytes that came in. The program decodes the picture and writes it anew into the data folder's
  `backgrounds`, at most 3840 px on its longer side, as JPEG at quality 90, or as PNG when it has transparency, named by
  its content (32 hex digits). A picture no setting names is deleted at the next start; nothing else in that folder is
  touched.
- Setting: `backgroundImage` = `{ file, focusX, focusY, zoom, strength, average }` with the theme in `layout.json`.
  `file` must be one of the names the program gives; anything else (a path, another name) drops the picture at load. A
  focus outside 0–1, a zoom outside 100–400 or a strength outside 20–100 goes back to its default (0.5, 100, 100).
  `average` is the picture's average colour, worked out when it was stored.
- It syncs as icons do: the setting travels, the file goes with the import and the data-location copy. A PC without
  the file shows the colour alone, and no error.
- Drawing: in the bar and the window at the pointer, over the theme's colour and under everything else, scaled to
  cover (at zoom 100 it just covers; the focus point sits at the centre unless that would leave a gap) and at its
  strength. The background transparency fades the colour and the picture together; with Mica or Acrylic it lies over
  the tinted backdrop the same way. Under High Contrast there is no picture. The bar keeps one bitmap for its size,
  made again at most every 100 ms while an edge is dragged.
- *Adjust position…* (0.15.0): the whole picture fitted into about 560 × 320, dimmed but for a bright frame over what
  the bar shows (the bar's own proportions, upright for an upright bar), and a small cross at the focus point. Drag the
  frame, or click anywhere, to move the focus; the arrow keys move it by 1 % (Shift: 5 %). Zoom with the slider
  (100–400 %), the wheel over the picture, or + and −, by 10; the frame shrinks as the zoom grows. Under it, a strip
  shows the bar at its proportions with the picture as fitted and a few sample icons with names, at the strength and
  background transparency Settings has, so reading over it can be judged. *Centre* puts the focus in the middle at
  zoom 100. It changes Settings' pending picture at OK; the bar changes at Save. The frame is the fit's rectangle
  (`BackgroundFit`) in the preview's units.
- Reading over it: while a picture is drawn, the see-through look's edges are on at every opacity, and the light or
  dark text is chosen for the picture's average blended into the colour by its strength. The focus colour's contrast
  warning uses the same blend.

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
  rebuilding any view. Dialogs are not themed by the palette: they use the Windows 11 Fluent look
  that follows the system light/dark mode (a setting; off brings the classic dialogs back), and
  `DialogTheme` recolours the few fixed dialog brushes (hints, warnings, swatch borders) for dark
  surfaces.
- **Backdrop** (*Settings → Bar background*). On Windows 11 22H2 or later the bar can ask DWM for a
  Mica or Acrylic backdrop (`WindowBackdrop`). The window then paints nothing itself, the root border
  uses `Theme.BarBackground`, a tint of the theme colour whose opacity is the transparency setting, and
  the icons stay sharp. The group popup gets Acrylic on its own window (`Theme.PopupBackground`). When
  Windows refuses or is older, both keys fall back to the solid colour and the whole-window layered
  alpha applies instead.
- **High Contrast**. While Windows High Contrast is on, `ThemeManager.EffectivePalette` replaces the
  palette with the system colours (window, text, highlight, frame) and the backdrop is turned off;
  the bar follows the switch live.
- The theme is part of the settings, so it travels with Google Drive sync copies and is applied
  when a copy is loaded.

## Adding a theme

1. Add a `ThemePalette` to `ThemeCatalog` (core) and include it in `Builtin`.
2. Nothing else: the settings dialog lists `ThemeCatalog.Builtin`, and the unit test that checks
   ids and colour formats covers the new entry.

## Possible later work

- **Translucent themes** are covered by the Mica/Acrylic backdrop; a "Frost" preset would only be a
  theme plus a default backdrop.
- **Cell shape** per theme (corner radius, hover outline thickness) and a **label font size**.
- **Follow Windows light/dark mode** automatically, mapping to Paper and Graphite.
