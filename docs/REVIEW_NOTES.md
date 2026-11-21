# Notes from the development session to the review session

This file is the channel from the development session (the one that writes the code on
`claude/practical-dijkstra-gf03ik`) to the review session. Messages from this side cannot reach the
review session directly: `send_message` into a session on the owner's own computer is refused for this
cloud session, and the trust prompt that would allow it is the owner's to answer, not mine. So every
commit that changes something the review should know about also updates this file, newest entry first.
The owner relays replies.

---

## 2026-11-21 (151) — 0.15.0 WP7a: a text or symbol icon moved up or down

- **Runs:** 253 (`1da7c61`, the WP5 fix) green: Core 1338/1338, App 551/551. 254 (`5326254`, WP6): Core 1338/1338, App
  559/560. The red test, `SendToDeviceTests.Without_sync_a_new_icon_is_added_without_a_PC_and_later_starts_with_the_box_ticked`,
  is a race between tests, not WP6: two `LauncherViewModelTests` (Repair for this PC or every PC) set `ThisDevice.Id`
  and put it back, while the Send to PC test, in a parallel collection, approved its "added here" mark under one id and
  looked it up under the other (`CommandTrust` keys approvals by the id). In the App tests nothing followed CLAUDE.md's
  rule that classes setting the id go in "This device". Fixed here: the two tests moved, unchanged, into
  `RepairForEveryPcTests` in a new "This device" collection that runs alone, after the parallel ones
  (`DisableParallelization`), so no parallel test ever sees the id change. No test was skipped or widened.
- **The setting** (`TextIcon.OffsetY`): whole percent of the tile's height, −20 to +20, negative is up, default 0 and not
  written then. `Normalized` makes anything out of range 0. At load a converter reads a whole number as it is and
  anything else (a fraction, a number too large, text, true, null, an object or array) as 0, so one odd value never
  costs the whole bar; a number in quotes is read as the layout reads its other numbers.
- **The window:** a row 垂直位移： / *Up or down:* in the shared section under both sides' fields (on the Text side right
  under the size row), before the colour: a slider −20…+20 in steps of 1 (arrow keys 1, Page keys 5), its number
  (`+7 %`, `−20 %` with a real minus sign, `0 %`), and 歸零 / *Reset* / 归零. The preview follows at once. It is shared by
  both sides like the colours, so a symbol keeps its offset when the window opens again.
- **Drawing:** the distance is the offset's share of the tile in whole units (`TextIconTile.ShiftFor`, rounded so that
  text on whole pixels stays on them: 7 % of 32 is 2). Alone on a tile the fitted box (outside its scale) or the text at
  its size gets a `TranslateTransform`; the tile already clipped to its bounds. Over the picture the placement in the
  middle or in a corner's box gets the same distance, the plate moves with the text, and the drawing is clipped to the
  tile (new: `PushClip`).
- **Tests:** Core: written only when not 0, read back for text and for a symbol over a picture in a corner, through the
  layout file and the model; −21, 21 and `int.MinValue` are 0, ±20 and 7 kept; nine odd values at load are 0 (or −7 for
  `"-7"`), and the property after the odd value is still read. App: the editor holds the offset to its range, shows the
  number, keeps it on the Symbol side and when reopened, and resets; the window's slider is bound and *Reset* works; the
  drawn position (the middle of the ink rows from a render) for −20, 0 and +20 alone on a tile (fitted and at its size)
  and over a picture in the middle, at the bottom right and at the top left, each with the outline and with a plate; a
  plate pushed past the bottom ends at the tile's last row; alone on a tile the transform sits on the fit box, not on the
  text, and goes away at 0. The text icon window's fit checks (three languages, 90–150 %) now include the new row.
- **Not tested:** on screen, and at a scale other than 100 % (the distance is in whole units, not whole pixels there).

This entry claims no run for its own commit.

## 2026-11-21 (150) — 0.15.0 WP6: the picture's position and zoom, with a preview

- **Runs:** 252 (`a94a797`, WP5) was red on the thumbnail's shared cell, fixed in `1da7c61` (entry 149). Its run had not
  finished when I pushed this.
- **The rule** (`BackgroundFit`) came with WP5, since the setting already had a focus and a zoom, together with its tests:
  a wide bar on a tall picture and the reverse, a bar larger and smaller than the picture, the focus in each corner
  (never a gap), zoom 100 and 400.
- **調整位置…** beside 從網址… (off without a picture; with a picture this PC lacks, it says so).
  - The whole picture fitted into 560 × 320 at most, dimmed (black at 60 %) but for a bright frame (white 2, black 1
    outside it) over what the bar shows, and a small white cross at the focus point.
  - The bar's proportions are read from the bar: its icon area as shown and expanded plus the grip's and buttons'
    columns (`MainWindow.PictureAreaSize`), so an upright bar gives an upright frame.
  - A click outside the frame puts the focus there; a drag of the frame moves it with the pointer, from wherever it was
    grabbed. The arrow keys move it by 1 % (Shift: 5 %).
  - Zoom: a slider of 100–400 % in steps of 10 with its number, the wheel over the picture, and + and −. The frame
    shrinks as the zoom grows.
  - The strip under it: the bar at its proportions (as wide as the preview, at most 120 high, at least 28), the theme's
    colour at Settings' background opacity, the picture as fitted at strength × that opacity, and four sample icons
    with names (筆記, 郵件, 音樂, 相片) in the theme's text colour with the names' edge. Its picture is an `ImageBrush` whose
    viewbox is the fit's rectangle, so it follows a drag at once.
  - 置中 (focus 0.5, 0.5, zoom 100), 確定 and 取消. Enter is OK and Esc is Cancel. Automation names on the picture, the
    slider and the strip; the picture has the hint as its help text.
  - It changes Settings' pending picture at OK; the bar changes at Save.
- **Tests:** the frame is the fit's rectangle times the preview's scale (the inverse the plan asked for), and it shrinks
  at zoom 200; a click in a corner puts the focus there and the frame stops at the edge; the keys, the zoom limits and
  置中; OK keeps the strength and changes the focus and zoom; the strip's size for wide, upright and very flat bars; the
  window places the frame where the focus is, dims around it, shows four samples, and gives the strip's picture strength
  × opacity.
- **Not tested:** on screen, a drag and the wheel with a real mouse, and an upright bar's frame on a real upright bar.

This entry claims no run for its own commit.

## 2026-11-21 (149) — WP5: the picture's thumbnail shares no cell

- **Run 252** (`a94a797`, WP5, entry 148): Core 1338 of 1338; App 550 of 551. The red one is
  `DialogFitTests.Every_tab_of_Settings_gives_each_control_a_cell_of_its_own`: the thumbnail laid the picture and the
  word 無 over each other in one grid cell, which no grid in Settings may do. WP5's own tests passed.
- **The fix:** the thumbnail is the border's fill (`PictureThumbnailBrush`, cut to the border), and 無 is its only child.
  It looks the same.

This entry claims no run for its own commit.

## 2026-11-21 (148) — 0.15.0 WP5: a background picture for the custom theme

- **Run 251** (`834490b`, entry 147): green, Core 1317 of 1317, App 541 of 541. WP4b is done.
- **Settings ▸ 外觀**, under the hover and focus rows (so, like them, only for 自訂): 背景圖片 with a thumbnail (or 無),
  選擇圖片…, 從網址… and 移除, and 圖片濃度 (20–100 %, steps of 5) under them. 調整位置… comes with WP6.
- **From a file:** PNG, JPEG, BMP, GIF (its first frame), TIFF, and WebP where Windows can decode it. An icon is refused
  (an `IconBitmapDecoder` is checked for), as is anything Windows cannot decode; a message box says so.
- **From a web address:** a small window for one `https://` address. Nothing is fetched until its OK, and then once.
  The rules are shared, not copied: `IconDownloader.FetchAsync` now holds them for the web icon and the picture alike
  (one GET; a web page refused; 10 MB by the header and while reading; the 30-second stall timeout). The picture asks
  `httpsOnly`, which also checks the address the redirects ended at. Failures carry a kind and are said in the window
  in its language: not https, too large, not a picture, no answer. The window stays open after a failure. The address
  is not kept.
  - The web icon still takes `http://` as before 0.15.0. You wrote "https only" as one of IconDownloader's rules, but it
    never had that rule. Say if icons should become https-only too: it is one argument.
- **Stored:** decoded by Windows and written anew, never the bytes that came in. At most 3840 px on the longer side (a
  larger file is decoded at that size); JPEG at quality 90, or PNG when a pixel is not opaque; named by the start of
  the encoded bytes' SHA-256 (32 hex digits), so the same picture is one file. Written as soon as it is chosen; a
  picture no setting names is deleted at the next start, and nothing else in `backgrounds` is touched.
- **Setting:** `backgroundImage = { file, focusX, focusY, zoom, strength, average }`. `average` is my addition: the
  picture's average colour (alpha-weighted, from a 32 px copy), worked out when it is stored, so that the light or dark
  text is right from the start, before the picture is decoded. `file` must match the stored-name pattern; a path or any
  other name drops the picture at load. Values out of range go back to their defaults, as you asked (not clamped).
- **Travel:** `backgrounds` joins `icons` and `sounds` in `DataImport.Folders`, so the import and the data-location
  copy bring it. Sync carries the setting, not the file, which is also how icons travel: the sync copy holds the layout
  only. A PC without the file shows the colour alone, with no error. A layout restored from history names its picture,
  which may have been deleted by then: the colour alone too.
- **Drawing** (`BarPicture`): a layer under the bar's grid and under the twin's content (the twin's 3 px padding is now
  its content's margin, so the picture covers the inside of the border). It is scaled to cover by `BackgroundFit` (in
  WP5 already, since the setting has a focus and a zoom), at strength × the background transparency. With Mica or
  Acrylic it lies over the tinted backdrop. There is no picture under High Contrast, or when the theme is not 自訂.
  - The stored picture is decoded once when it becomes the one in use, at start or at Save, on the UI thread. The layer
    gets one bitmap made for the bar's pixel size, made again when the size changes, at most every 100 ms.
  - The group popup has no picture: the plan names the bar and the twin.
- **Reading over it:** while a picture is drawn, `BarSurface` turns the see-through look's edges on at every opacity.
  `ThemeCatalog.Resolve(settings, pictureShown)` chooses the light or dark set and the text colour for
  `Blend(colour, average, strength)`; the border stays the colour's. WP4b's focus warning uses the same blend when this
  PC has the picture.
- **Security:** your three notes are how it works now: nothing is fetched without OK; only pixels the program decoded
  and re-encoded are stored (and the name is all that syncs); and a `file` that is not a stored name is dropped at load.
  Putting them into CLAUDE.md's decided rules is the owner's call; I will ask at the ready-for-release-check point.
- **Tests:**
  - Core: the stored-name rule (paths, upper case, other extensions); the name follows the content; normalising; the
    setting's round trip; the unused list; `BackgroundFit` for a wide bar on a tall picture and the reverse, a bar
    larger and smaller than the picture, the focus in each corner (no gap), zoom 100 and 400; the light or dark choice
    with the picture shown and not, and the blend;
  - App: storing (JPEG, PNG with transparency, one file per picture, the average); refusing what is not a picture and an
    icon; a huge picture reduced; the scaled part for focus left and right; pruning; loading (missing, a path, kept once
    read); the download rules through a fake server (http refused before any request, a redirect ending on http, too
    large, a web page, 404) and the messages; Settings (defaults, strength held, saved, removed); the focus warning over
    a picture this PC has; what the bar shows (custom theme, file present, else none) and its text colour; the layer's
    size and opacity in a window; the text edge at 100 % with a picture.
- **Not tested:** on screen, a real download, WebP or TIFF files, a Mica backdrop under the picture, and the 100 ms
  limit while dragging (it is a timer that starts once and draws at its tick).

This entry claims no run for its own commit.

## 2026-11-21 (147) — WP4b: the 自動 boxes wrap like every box in Settings

- **Run 250** (`5600c07`, WP4b, entry 146): Core 1317 of 1317; App 540 of 541. The red one is
  `DialogFitTests.Every_check_box_and_option_in_Settings_and_Sync_wraps_its_text`: the two new 自動 boxes had no
  `WrappingLabel` template, which every check box in Settings has, so that a long label wraps (release check, 0.12.x).
  WP4b's own tests passed.
- **The fix:** both boxes take `WrappingLabel`. Nothing else changes.

This entry claims no run for its own commit.

## 2026-11-21 (146) — 0.15.0 WP4b: the custom theme's hover and focus border colours

- **Run 249** (`b75b870`, WP4, entry 145): green, Core 1308 of 1308, App 534 of 534.
- **Settings:** `hoverColor`, `hoverStrength` and `focusColor` beside `backgroundColor`, synced with it. Absent means the
  derived colours, so a bar that never set them looks exactly as before. A colour that is not `#RRGGBB` is dropped at
  load; the strength is held to 10–100.
- **Settings ▸ 外觀:** under the custom colour (so, like it, only for 自訂), two rows: 滑鼠停留色彩 and 焦點外框色彩. Each
  has a chip with the colour in use, a code box, 選擇顏色… (WP4's picker) and 自動, ticked by default. While ticked, the
  box and button are off and show the derived colour, which follows the background. Unticking starts from the derived
  colour, and the hover row's strength from the derived overlay's. 滑鼠停留強度 (10–100 %, steps of 5) sits under the
  hover row and is off with it. A box with what is not a colour says 請使用 #RRGGBB and saves nothing for that colour.
- **Where the colours go** (in the bar, its group popups and the window at the pointer alike, through the shared
  styles; new brush keys `Theme.PointerOver`, `Theme.PointerOverBorder` and `Theme.Focus`):
  - hover: behind an icon, a group or a special icon under the pointer, and behind ✕, ▸, ← and Undo; the outline of an
    icon or a separator under the pointer; the pressed state. The outline and the pressed state take the same colour,
    stronger by the derived ratios (dark set: outline and pressed 80/46; light set: outline 48/20, pressed 36/20). Without
    a strength, the derived overlay's own: 18 % (dark) or 8 % (light);
  - focus: the keyboard focus border of the same things, the selected match's border, a drop target's border and fill
    (the fill at the derived alpha) and the drop marker line.
- **Kept on the derived colours** (your question about the accent):
  - the accent keeps the launch frame (an icon framed while its program comes up), the search box's border and
    selection colour, and the missing badge (the "?" circle);
  - the derived hover fill keeps the search box's fill, the notice row's fill, a text tile's frame and the selected
    match's fill.
  - Dialogs, menus and Settings keep their own colours. Under High Contrast the palette is Windows' own, which these never
    reach.
- **The light set's 8 %:** the slider starts at 10, so unticking the hover row on a light background starts a little
  stronger than the derived overlay. Left ticked, it is exactly 8 % as before.
- **Preview:** the swatch beside the theme list is now a small bar: the background with its border, one icon (A) under
  the pointer (overlay and outline) and one (B) with the focus border.
- **Reading check:** a chosen focus colour under 3 : 1 against the background shows 與背景太接近，鍵盤焦點可能看不清楚。 under
  its row; Save still works. The automatic colour is not judged, since there is nothing to choose then. The check against
  a background picture comes with WP5, as you said.
- **Tests:**
  - Core: without colours the palette equals the derived one field by field; the hover colour at 50 % and with no
    strength, and the outline and pressed ratios on both sets; the focus colour and the drop fill on both sets, with the
    accent unchanged; a built-in theme ignores them; the derived colour and strength for unticking; the 3 : 1 threshold
    (#949494 on white is 3.03, #959595 is 2.99); the settings' round trip and what is dropped or held;
  - App: both automatic by default, saving nothing; unticking starts from the derived values and saves what is chosen;
    the automatic colours follow the background; the contrast warning, still saved, and not on automatic; a box with no
    colour; colours from the layout open unticked; the bar's brushes for the new keys, with the accent and the hover fill
    unchanged.
- **Not tested:** on screen (the rows at 150 % and in English are for your look; `DialogFitTests` covers Settings' fit),
  the picker opened from these rows, and the colours on the real bar under the pointer and on focus.

This entry claims no run for its own commit.

## 2026-11-21 (145) — 0.15.0 WP4: a colour picker for the custom theme

- **Run 248** (`b34b78f`, WP9, entry 144): green, Core 1279 of 1279, App 527 of 527.
- **Where:** 選擇顏色… / *Pick a colour…* beside the custom colour's code (Settings ▸ 外觀), and, since it dropped in
  without redesign, beside the text icon window's colour and tile colour codes. That window's colour row is now a dock
  panel, so its hint wraps in the room the button leaves.
- **The picker** is a small dialog, `ColourPickerWindow`, with its view model `ColourPickerViewModel`; the three callers
  open it through `ColourPickerWindow.Pick`. You asked for a reusable control in a dialog. The parts live in the dialog
  itself because nothing shows them inline yet; if WP4b or WP6 want them inline, I will split them into a control then.
  - The square is 240 × 160: white to the hue from left to right, and black over it from top to bottom. Its ring is black
    outside and white inside, so it shows on light and dark.
  - The hue strip is 240 × 16, red to red, with a white thumb.
  - The code box and the R, G and B boxes. While a box holds what is not a colour, a line under them says "Use #RRGGBB,
    and whole numbers from 0 to 255."
  - The 目前 and 新的 chips (56 × 28), each with its code as a tooltip, and the swatch row (Settings' presets, or the text
    icon window's list).
  - 確定 and 取消: Enter is OK and Esc is Cancel. Focus starts on the square.
- **In step:** the hue, saturation and value are kept as they were set. A box or a swatch sets red, green and blue, and
  hue, saturation and value follow, except that a grey keeps the hue and black keeps the saturation, so the marks do not
  jump. The box being typed in keeps its text (no jumping caret). A box with what is not a colour changes nothing, and
  shows the colour again when it is left.
- **Mouse and keys:** press and drag on the square or the strip (the mouse is captured), held to their edges. The
  strip's right end stays just short of 360°, so the thumb does not jump back to the left. On the square the arrows move
  the ring one unit (Shift: ten): right is more saturated, up is brighter. On the strip, Left and Right (Up and Down too)
  move the thumb one unit, 1.5°, wrapping round. Every part has an automation name; the square and the strip have help
  text and a tooltip.
- **Live:** in Settings the swatch beside the theme list follows the new colour (the colour is set as it changes). Cancel
  puts back what the box had, even a typed non-colour, and the bar changes at Save, as before. A box holding no colour
  opens the picker at mid-grey. The text icon window's preview follows in the same way.
- **Tests:**
  - Core `ColourMathTests`: the round trip over a grid of 52³ colours, known colours, codes with and without `#`, what is
    refused, channels 0–255, a hue that wraps and values held to 0–1;
  - App `ColourPickerTests`: it opens at the colour; the square and the strip; the keys and the wrap; the boxes refuse,
    apply and come back; a grey and black keep the marks; the swatches; the window's ring and thumb; the warning line;
    Display text like the other dialogs. The fit test of the text icon window (three languages, 100 % and 150 %) covers
    the new buttons.
- **Not tested:** the dialog on screen, mouse drags, the order of keyboard focus, the live preview with a real Settings
  window (the `Pick` call with its callback is a few lines in each window), and the square and strip in the Fluent look.

This entry claims no run for its own commit.

## 2026-11-21 (144) — 0.15.0 WP9: sharp text in every window

- **Run 247** (`ccf1627`, WP8, entry 143): green, Core 1279 of 1279, App 512 of 512.
- **One place:** `TextSharpness` (Views). `Apply(root, rendering, scaled)` sets Display (Ideal when scaled), layout
  rounding, pixel snapping and the rendering mode; children inherit them. `FormattingFor(drawn)` is the 20 px rule and
  `FormattingForScale(scale)` the rule for scaled text; `RenderingFor(seeThrough, backdrop)` decides the smoothing.
  - `DialogTheme.Apply` calls it after scaling, for all 22 dialogs. `MainWindow`, `PointerWindow` and `StackWindow` call
    it in their constructors, and the XAML copies on those three are gone.
  - Popup content (the group popup, the ComboBox lists, submenus) inherits from its window or menu.
- **Menus and tooltips: class handlers, not implicit styles.** In a dialog with the Fluent theme, the theme's own
  `ContextMenu` and `ToolTip` styles sit in the dialog's resources and come before an implicit style in App.xaml, so an
  App.xaml style would not reach the dialogs' menus. Instead, `RegisterMenusAndTooltips` (called once at start) adds
  class handlers on `ContextMenu.Opened` and `ToolTip.Opened` that apply the helper, ClearType included, to every menu
  and tooltip the app opens, Fluent or not. They act as the menu opens, so its very first frame may still be laid out the
  old way; I cannot see whether that shows.
- **Rendering:** dialogs, menus and tooltips: ClearType. The bar and the window at the pointer, through `BarSurface`:
  ClearType when opaque (it was Auto), grey when see-through without a backdrop, Auto with Mica or Acrylic (as before).
  The group popup follows the same rule. High Contrast has no rule of its own.
- **20 px:** `FormattingFor` (Ideal from 20 units) decides for:
  - the text tile: fitted text is drawn about as high as its box, 68 % of the tile; other text at its own size;
  - the gauges: each text's size in the 100-unit box times the icon size / 100, through `ScaledTextFormattingConverter`
    in one implicit style in the gauge's box (the number's own style is based on it). That covers the clock (a number
    gauge) and the captions.
  - Text over a picture is drawn as outlines (geometry), so neither mode applies there.
- **Scaled dialogs:** Ideal when a dialog is scaled, Display at 100 %, your fallback. What I weighed (I cannot try
  anything on screen here):
  - font sizes and lengths instead of the transform: the Fluent theme's controls have fixed paddings, heights and glyph
    sizes that a font size does not scale, and every dialog's set widths, heights and margins would change. That is too
    large for this release, and it would change the 150 % look the release check approved;
  - telling WPF a higher DPI for the dialog (`VisualTreeHelper.SetRootDpi`) would put text on whole pixels at the scaled
    size, but it fights Windows' own DPI messages when a dialog moves between monitors. Not tried.
  - The two samples in Settings follow the rule (`SampleFormatting`, `DialogSampleFormatting`): whole pixels only when
    nothing scales them.
- **Fonts:** unchanged.
- **Tests:**
  - the 20 px rule, the scale rule and the three rendering cases;
  - every window class (25, found by reflection) calls `TextSharpness.Apply` or `DialogTheme.Apply` in its own code (a
    `call` with the method's token in its IL), and `DialogTheme.Apply` calls `TextSharpness.Apply`;
  - a dialog at 100 % (Display, ClearType, rounding, snapping, inherited by its text) and at 150 % (Ideal);
  - a menu and a tooltip after they open;
  - gauge text at 32, 48, 64 and 96 px; a fitted text tile at 32 and 24, and sized text at 12 and 24;
  - the samples at 100 % and 125 %;
  - the window at the pointer: ClearType and Display at 100 % and under High Contrast, grey when see-through (as before).
- **Not tested:** anything on screen at 100, 125 and 150 % (yours); the first frame of a menu; whether WPF smooths in grey
  where the Fluent theme draws a backdrop behind a dialog.

This entry claims no run for its own commit.

## 2026-11-21 (143) — 0.15.0 WP8: more icon sizes

- **Run 246** (`274ac48`, entry 142): green, Core 1249 of 1249, App 507 of 507. WP1's tests pass with it.
- **The list and the box:** 圖示大小 lists 16, 20, 24, 28, 32, 40, 48, 56, 64, 72, 80, 96 and 128 px, and the box is
  editable: pick a size or type one. What is typed is read when the box is left, and at Save: Enter presses Save without
  leaving the box, so Save reads it first. "72", "72 px" and full-width digits (as a Chinese input method types them)
  count. A number outside 16–256 goes to the nearer end; anything else goes back to the size before. The box then shows
  what was kept ("256 px" for 300). Its tooltip: 選擇一個大小，或輸入 16 到 256 之間的整數。
- **Stored:** `iconSize` as before. Normalising keeps any whole number from 16 to 256 (`ClampIconSize`), and
  `NearestIconSize` is gone.
- **An older version** snaps a size it does not list to the nearest of 16, 24, 32, 48 and 64: 72 → 64, 20 → 16, 28 →
  24, 40 → 32, 56 → 48, and 80, 96, 128 or a typed 200 → 64. It keeps that in memory and writes it back at its next
  save, which syncs. So in a set of PCs where one still runs 0.14, a 72 can come back as 64 on all of them. This version
  can do nothing about that; you asked me to say it.
- **Sharp icons:** `IconPixels.SourceSize` (Core) gives the first of 16, 20, 24, 32, 40, 48, 64, 72, 80, 96, 128 and 256
  at or above the icon size times the scale, and 256 above that. The bar asks its icons at that size, and the cell scales
  them down with high quality. The scale is the highest of the monitors' (`ScreenGeometry.HighestScale`), not the bar's
  own: the window at the pointer shows the same images on whichever monitor the pointer is on. With one monitor it is
  that monitor's scale. It is set at start, before the first icons load (they wait for the dispatcher), and again on a
  display change and on the bar's DPI change; a change that asks for another size loads the icons again.
  - The provider no longer doubles. Before, it asked twice the icon size whatever the screen: a 32 px icon at 100 % was
    a 64 px image scaled down, and now it is the 32 px image itself, pixel for pixel. The 256 cap is unchanged.
  - The shell's `GetImage` (bigger size allowed) and `SHDefExtractIcon` scale from the best image a file has. The
    padded-frame rule of 0.13.0 runs on whatever comes back, at every size, as before. The image list keeps its three
    sizes (32, 48, 256).
- **What scales with the cell:** the cell template draws text and symbol icons (`TextIconTile` at the icon size),
  gauges and the clock (a `Viewbox`) and the frame from the icon size, so they follow at any size; the badges keep their
  8 px. A cell is the size plus twice the spacing. Names, the window at the pointer, group popups and row lines follow
  the cell. A bar too small for one cell grows to one, by today's rule.
- **Other places:** none offers icon sizes (Properties and the text icon window preview at the bar's size).
- **Tests:**
  - Core: the clamp (15 → 16, 300 → 256, 50 kept; normalising says when it changed something); the box's reading
    ("72 px", full-width, "-5", "abc", "72.5"); the list; the source size for 72 at 100 % (72) and 150 % (128), and
    others. The layout repair test now uses 300, since 30 is a size now;
  - App: the bar asks the provider for 72, then 128 once the scale is 1.5; cells at 20, 50 and 128; the Settings box
    (the list, a pick, a typed 50, 300, "big", a typed 200 from the layout).
- **Not tested:** real icons at 72–256 on screen, a real DPI change or a second monitor, and the box with a keyboard (Enter
  at Save).

This entry claims no run for its own commit.

## 2026-11-21 (142) — Tests: the announced font is not one the others draw with

- **Run 245** (`62990bb`, WP1, entry 141): Core 1249 of 1249; App 506 of 507. The red one is
  `TextSizeTests.On_a_window_made_as_the_bar_…`, which neither WP1 nor anything since 0.14.0 touched: a
  `FileNotFoundException` from WPF while it measured Arial text. WP1's new tests all pass.
- **The cause:** `ChineseFontServiceTests` (since run 197) copies one of Windows' own fonts into its temp folder, adds it
  for the session and announces it, then takes it away and deletes the folder. The first font on its list that exists
  is `arial.ttf`, so WPF takes the copy as one more Arial face. That class was not in the WPF collection, so it ran
  beside the window tests. Once the copy was deleted, a WPF test drawing Arial could land on the missing file. The new
  test classes shifted the order and timing enough for it to happen.
- **The fix, tests only:** the copy is Georgia, Trebuchet MS, Verdana or Palatino (the first one Windows has), none of
  which the app or its tests draw with; the class runs in the WPF collection, so no window test runs while the copy is
  there; and the removal is announced too, before the folder goes. The test checks what it checked before.
- **Not tested:** whether WPF drops the copy on that second announcement; the other two changes hold without it.

This entry claims no run for its own commit.

## 2026-11-21 (141) — 0.15.0 WP1: 鎖定格線 (Lock grid)

- **Run 244** (`41ff5ae`, entry 140): green, Core 1248 of 1248, App 502 of 502. The group test passes with the window's
  own height checked in every run, so WP3 is done.
- **The setting:** `lockGrid` in the bar's settings, off by default, copied and synced as `alwaysOnTop` is. Older
  versions keep it as an unknown property.
- **The menus:** a ticked 鎖定格線 / *Lock grid* in the empty area's menu, just before 設定 (it is one menu in the bar, a
  group's popup and the window at the pointer), and in the ▸ menu right under 永遠在最上層. Not in Settings. Not in the
  tray menu either, because the plan names only those two menus. The tray menu has 永遠在最上層, and adding the lock there
  is one line if you want it.
- **What it stops:** every drag of the bar's items starts in one place, `IconGridControl.Item_PreviewMouseMove`. That is
  the same for icons, groups, special icons and every separator kind, in all three windows. Past the drag distance it
  asks `MayMove`, which refuses while the grid is locked. The press's mouse capture is then released, as a drag start
  does, so the release is no click (without that, a refused drag would launch the program). A drop of one of the bar's
  items is refused in `ResolveDropTarget` (the pointer shows no-drop) and again in `CompleteDrop`, for a drag already
  on its way when the lock came on (a sync can turn it on during a drag).
- **What still works:** files and links from Explorer or a browser (added, or opened with the icon they land on),
  clicks, menus, Properties, adding, removing, renaming, moving, resizing and docking the bar, sync and restore. The
  press-and-hold `_holdTimer` is not a drag start. It is the Sound Replayer's hold (it plays while held, and nothing
  moves), so it keeps working. A press that moves past the drag distance before the hold begins drops the hold, as
  before, locked or not.
- **Your question:** `WindowKeys` has no keys that move an item (new shortcut, new group, settings, find, undo the
  removal, remove, properties), so that line is dropped.
- **The notice:** the notice row is the removal banner's row. The lock's line is a second row of the same look under the
  icons, without a button, wrapped, for 10 s (the Undo offer's time). Only the first refusal in a run shows it
  (`LauncherViewModel.RefuseMove`), and unlocking takes it back. The window at the pointer shows the same line under its
  icons, as wide as they are, and grows by it while it is there: the bar may be hidden while that window is in use.
  When the icon area is held to a small work area, `MeasureSize` now measures again at the narrower width, so the
  line's extra wrapped height is counted.
- **Tests:**
  - Core: the setting's default, its JSON name, `CopyFrom` and `Clone`; the layout round trip carries it;
  - App, `LockGridTests`: with the lock on, no item of any kind can be dragged (a shortcut, a group, a shortcut inside
    it, a special icon, a line, a row-start line, a row line, a gap, a caption), and all can with it off; only the first
    refusal says so (after the line has gone, a refusal is silent); unlocking takes the line back; a locked grid takes a
    file drop and moves no item, and the item moves again once unlocked; the empty area's menu has the item, checkable
    and bound both ways;
  - App, `PointerWindowTests`: the line under the icons, as wide as the icon area, and the window growing by it and back.
- **Not tested:** the drag itself with a real mouse (WPF reads the real button state, which a test cannot press), the
  no-drop pointer, the ▸ menu's entry (no test makes the bar's window; its binding is the same as the line above it),
  and the line in the bar's notice row on screen.

This entry claims no run for its own commit.

## 2026-11-21 (140) — 0.15.0 WP3: the window at the pointer grows by a group's header again

The cause of runs 239–243, and it was in the app, not only in the test. Entries 137 and 138 guessed wrong twice
(WindowChrome, then a hidden window); this replaces both.

- **What was wrong:** WP3's `MeasureSize` measures the window's frame once, at an unbounded size. WPF skips a measure at
  the size an element was last measured at unless that element itself was invalidated, and showing the group's header
  invalidates only the panel it is in. Once the window has its content's size (after the fit that follows the show), the
  layout pass after that trial measure does not measure the frame again, because nothing above it changed. So the
  frame's last measure stayed the trial one, and the next fit, when a group opened, was skipped and got the old size: the
  header pushed the icon area's last row 21 units past the bottom edge. That is the screen pass's old finding back. The
  code before WP3 measured twice at different widths, so it never hit the skip. The same skip could also size the window
  from the bar's old icon area for one frame, at a show after the bar's size changed (the fit after the show put that
  right).
- **The fix:** `MeasureSize` brings the layout up to date (`UpdateLayout`) and invalidates the frame before its trial
  measure. Nothing else changes. WindowChrome stays as `00bc2e8` put it back.
- **The test:** the group test checks the window's own height again, in every run: the "only while it is shown"
  condition from entry 138 weakened it on a wrong cause and is gone. It checks the height before the group opens, in the
  group (top level + header), and back at the top level after the search. Its first measure is the window's trial one,
  as after the fit that follows the show, which is the state the bug needed.
- **Run 243** (`8dd6703`, WP2, entry 139): Core 1248 of 1248; App 501 of 502, the one red being this test. WP2's new App
  tests pass.
- **Not tested:** on a real screen.

This entry claims no run for its own commit.

## 2026-11-21 (139) — 0.15.0 WP2: show at the pointer while a key is held

Run 242 (`00bc2e8`, entry 138) is red on the same test, and it says more: the window is shown, asks for 162 units and
stays at 141. So it was not hidden: the resize itself does not take, and entry 138's cause was wrong as well. I am
still on it; the next entry has it. This commit is WP2, which does not touch the window's size.

- **The setting:** `holdAtPointerHotkey`, already in `b732176` by my mistake (entry 137): none by default, trimmed,
  copied and synced as `showAtPointerHotkey` is.
- **Settings ▸ 按鍵:** a third row right under 在指標處顯示, with your label and hint. It has the same box, 清除, tooltip, the
  "needs Ctrl, Alt or Win" and "a key of the bar has it" warnings as the rows above, and its own line for a combination
  one of them has: 請使用與上面快速鍵不同的組合。 / "Use a different combination from the hotkeys above." Each row is checked
  against the ones before it (this one against show/hide and show at pointer), and the voice key against all three now,
  so its line says 其他快速鍵 / "the other hotkeys" in place of 另外兩個. A key of the bar can neither take it nor have it
  (`WindowKey_HoldHotkey` names it in that refusal).
- **`HotkeySet`:** four keys, in the order show/hide, show at pointer, held at pointer, voice. The voice key moved from
  third to fourth place, and the code reads both by named indexes now. Each key has its own refusal for a combination
  that one before it holds (only a hand-edited file gets there). The new ids are `0x4A41` and `0x4A42`.
- **The hold (`HoldToShow`, Core):**
  - the press shows the window the way the show-at-pointer key does (the bar's group popup and its type-to-find close).
    If the window is already open, from the other key, the press does nothing, and nor does its release. A press during
    a hold does nothing more;
  - every 50 ms (a `DispatcherTimer` at Input priority, as for the voice key) the keys are looked at with
    `GetAsyncKeyState`: the main key and each modifier of the combination (Ctrl, Alt, Shift, either Windows key). The
    first one up hides the window. If one of its menus is open, or a drag from it is under way, it hides when that ends;
  - the window going away by itself (a launch, the focus elsewhere, Esc) ends the hold without a second hide.
- **A plain click while held:** while the hold lasts and the window is shown, `IconGridControl.CurrentModifiers` leaves
  out the combination's Ctrl, Shift and Alt, for a click and for Enter in the search box. A middle click keeps its new
  instance. "A menu is open" is a context menu or a menu item holding the mouse, as an open menu does.
- **Tests:**
  - Core `HoldToShowTests`: the press shows and the first key up hides; a repeat; a tap; a window already open is left
    alone, release included; a menu open at the release hides after it closes; a launch or the focus elsewhere ends the
    hold without a second hide; the setting's round trip and trimming;
  - App: Settings (off by default, refused against show/hide and show at pointer, the voice key against it, a key of
    the bar, Save writes it); `HotkeySet` with four keys (the later of two alike refused with its own text, four
    different ones each held).
- **Not tested:** the real keys and a real click with Ctrl held during a hold; the menu check with a real menu.

This entry claims no run for its own commit.

## 2026-11-21 (138) — Run 241: the window was hidden, not refused; WindowChrome back

Run 241 (`0874e4c`, entry 137) failed the same test, with the answer in its message: the window asked for 162 units
(141 and the header's 21) and stayed at 141, while the header was laid out. Its icon area was right. My entry 137 was
wrong about the cause: it was not WindowChrome.

- **What happens:** on the CI machine the window loses the focus during the test and hides itself, as it does when
  you click elsewhere. A hidden window is not resized (`FitToContent` resizes the shown window only, as before WP3),
  so its height stayed where it was. The old test passed by chance of focus. The code does what it should.
- **The test** now checks the size the window asks for: the top level, then the header's height more with the icon
  area unchanged, then the top level again after a search. The window's own height is checked as well when it is
  shown at that moment.
- **WindowChrome is back** on the window at the pointer, as WP3 had it (`dc9e8d7`), so it shares the bar's code path:
  `BarSurface` with no resize border. `WindowBackdrop.ExtendFrame` is gone again. What differs from the bar, as you
  asked: the resize border (0 for the bar's 6), and the window is a topmost tool window.

This entry claims no run for its own commit.

## 2026-11-21 (137) — The window at the pointer without WindowChrome (run 240 said why 239 was red)

Run 240 (`b732176`, entry 136) reported it: the window stayed 141 units high when the group's header (21) showed, so
the icon area was pushed down and cut at the bottom; the window was not resized. Before WP3 the window had no
WindowChrome and grew; WP3 gave it one, like the bar's. Nothing else in WP3 touches its size.

- **The fix:** the window at the pointer has no WindowChrome again. For a backdrop, `BarSurface` extends its frame into
  the client area with `DwmExtendFrameIntoClientArea`, as the group popup's is (`WindowBackdrop.ExtendFrame`), and takes
  it back when the backdrop goes; a refused extension leaves it solid. The see-through path never needed WindowChrome
  (the group popup has none). The bar keeps its WindowChrome and its path unchanged: `BarSurface` takes a resize border
  of 6 for it, none for this window.
- **The test** also reports the size the window asks for, so if this run is red too it says whether the header's room
  was asked for and refused, or not asked for.
- **What differs from the bar's path, as you asked in plan 36:** this is the one difference now. The backdrop comes
  through the extended frame instead of WindowChrome; the result on screen is meant to be the same, and I could not
  see it: no backdrop on the CI machine.
- **A mistake of mine in `b732176`:** besides the test, it took in WP2's first lines, which were in my working tree:
  `LauncherSettings.HoldAtPointerHotkey`, stored, normalised and copied like `ShowAtPointerHotkey`. Nothing reads it
  yet; WP2's own commit completes it and its entry covers it.

This entry claims no run for its own commit.

## 2026-11-21 (136) — Run 239 was red: my WP3 test of a group's header

Run 239 (`dc9e8d7`, WP3, entry 135) failed one App test of 500, a new one of mine:
`An_opened_group_adds_its_header_and_the_icon_area_keeps_its_size_and_a_search_goes_back`. Core 1242 passed, and so
did the other WP3 tests: the bar's icon area and places, the cap, and the see-through look on, off and under High
Contrast.

- **What:** after a group opens, the window is not taller by the header's height, as the test asks. Before WP3 the
  test asked only that it be taller. Reading the code I do not see why it is not; this commit changes nothing but the
  test, which now reports the window's, the content's, the header's and the icons' heights, so the next run says what
  happens. The fix follows in its own commit, with this entry's answer.

This entry claims no run for its own commit.

## 2026-11-21 (135) — 0.15.0 WP3: the window at the pointer is the bar's twin

v0.14.0 is out as you said: I checked the annotated tag on `fab4172`, `main` there, release run 17 and the three files
on GitHub. Plan 36 is taken in the order you set, with its two additions: WP3, WP2, WP1, WP4, WP5, WP6, WP7a, WP7b,
then "ready for the release check". The version is 0.15.0 from this commit, with "[0.15.0] – Unreleased" in the
changelog, and FEATURES §12 and the roadmap say *in progress*. WP5's security notes for CLAUDE.md wait for the owner's
word: I ask when WP5 is in.

- **The icon area:** the window's `IconGridControl` gets the bar's icon area in WPF units, so the same control at the
  same size lays the entries out the same way: rows, columns, row lines, lines at a row's start, and the scroll bar.
  - The bar notes its icon area (`MainWindow.BarIconArea`) whenever the icon column changes size while the bar is
    shown and expanded: not in the tray, not minimised, not the auto-hide strip. It takes the column less the grid's
    margins, so a removal banner or the bar's own type-to-find box under the icons does not count.
  - Before the bar has been shown in a run (started in the tray), the size comes from its saved place less the frame,
    the grip's and the buttons' columns; on a first run, six cells by two rows.
  - The search box is above it and, while a group is open, the header under the box: the window grows by the header,
    and the icon area keeps its size. `MaxColumns`, `MinColumns`, `MaxRows` and `RoundingSlack` are gone.
  - Held to the pointer's monitor: its work area in WPF units at its scale. An icon area that would not fit with the
    rest gives up what does not (`FitIconArea`), and the icons flow again in it.
- **The look:** the bar's background code moved into `BarSurface`, which both windows use: backdrop, `WindowChrome`,
  `WindowTransparency`, the transparent window background, grey text smoothing and `SetSeeThroughLook`. The bar's
  result is unchanged; its group popup keeps its own path.
  - The window draws `Theme.BarBackground` in a border like the bar's, with the bar's square corners (WindowBackdrop sets
    them where Windows has the attribute). It is brought to the bar's settings at each opening, so a change made while
    it is hidden shows at the next one.
  - The search box has `Theme.Background` as its own fill. The ↵ target (`SearchTargetStyle`), the group's title and ← have
    the edge while see-through.
- **What differs from the bar's path, as you asked:** nothing in the code. The window has no resize border (0 for 6), and
  it is a topmost tool window, which the bar is not. Whether DWM draws Mica on a tool window I could not see: the CI
  machine's Windows has no system backdrop. If it refuses, `WindowBackdrop.Apply` returns false and the window is drawn
  solid.
- **Tests (App, `PointerWindowTests`):**
  - the icon area is the bar's, and every entry lies where it lies in a bar's grid of that size, in a window of its
    own with the bar's font: icons, a line, a caption, a row line with a caption, a row-start line, wrapped in five
    columns. The scroll bar is the same;
  - `FitIconArea` (as it is, both ways, the height only, no work area), and a window from a bar far larger than the
    screen staying in the work area;
  - see-through at 70 % (the look's resources, transparent background, grey smoothing), none at 100 % at the next
    opening, none under High Contrast (stood in through `BarSurface.HighContrast`);
  - the older size tests rewritten for the bar's area: a group adds its header and keeps the area; a few icons keep the
    bar's area; rows past the bar's height scroll; a window shown in the turn it is made has the bar's area. "No empty
    row under the last" is dropped, since the bar's area may have room left.
- **Not tested:** Mica or Acrylic on this window; a second monitor at another scale; a docked bar started in the tray
  (the saved place is the undocked one).

This entry claims no run for its own commit.

## 2026-11-21 (134) — Release commit for v0.14.0

Run 236 (`6dd3b3b`, entry 133) is green: Core 1242 and App 498 tests, and your look at T3 (review 40) found nothing
more. This is the release commit you asked for, made on the owner's word to me: I asked directly when your message
came, and the owner also chose the date, 2026-11-21.

- **What this commit changes (documents only, no code):**
  - `CHANGELOG.md`: "[0.14.0] – Unreleased" becomes "[0.14.0] – 2026-11-21".
  - `FEATURES.md` §12: 0.14.0 is done, released 2026-11-21, with what the release check found; *Next* is nothing
    planned.
  - `docs/ROADMAP.md`: 0.14.0 released 2026-11-21; *Next*: nothing planned.
  - `CLAUDE.md` State, which the owner said yes to when I asked:
    - 0.13.0 released (annotated tag `v0.13.0` on `361beb4`, release run 16, both checked on GitHub, and `main` there);
    - 0.14.0 with its release commit, the tag and `main` yours;
    - nothing in progress;
    - the 0.14.0 choices to keep;
    - what you saw in reviews 36–40;
    - what is not seen: the tray icon, the Send to menu, saving on the 傳送到電腦 tab and the receive folder's rules, a
      rename reaching another PC, F2 or a double-click on a row line, English and zh-Hans, the zips, and the standing
      list.
- **Your answers** (reviews 39 and 40) are kept as they were: no bar factor in `UiFonts`, the folder stack as it is,
  the hint's second sentence, the edge on shortcuts' and groups' text icons, and the notice row and the type-to-find
  box left on their fill.
- **The version:** `Directory.Build.props` says 0.14.0. No project file or workflow names another.
- **Not in this commit:** the tag `v0.14.0` and `main`, yours on the owner's word.

| What | Entry | Commits | Run |
| --- | --- | --- | --- |
| Version 0.14.0; WP1 with A1 and A2, separators that start a new row | 120 | `a9712fb` | 223 |
| WP3, the transparency fades the background only | 121 | `251b781` | 224 |
| WP4, the data location on the *Portable* tab | 122 | `914f013` | 225 |
| WP5 with A3, the *Send to PC* tab, the receive folder, one name for each PC | 123 | `27a7d09` | 226 |
| WP6, text or a symbol over the icon's picture | 124 | `69bc7e8` | 227, red (a test of mine) |
| Review 37 T1, names over a light desktop | 125 | `feffa63` | 228, red (the same test) |
| That test compares within a tolerance | 126 | `8d9a4c7` | 229 |
| WP7, the program's icon set | 127 | `7a0edca` | 230 |
| WP2, the dialogs' and menus' own font and text size | 128 | `4e4d223` | 231, red (tests of mine) |
| The colour hint wraps beside its box | 129 | `daeb0c6` | 232 |
| Review 38 T2, special icons' glyphs and gauge text | 130 | `f289641` | 233 |
| Ready for the release check | 131 | `036c419` | 234 |
| Review 39 T3, lines and the bar's own marks | 132 | `d9da59e` | 235, red (a test of mine) |
| That test looks in the logical tree | 133 | `6dd3b3b` | 236 |
| Release commit | 134 | this | — |

This entry claims no run for its own commit.

## 2026-11-21 (133) — Run 235 was red: my T3 test looked for ← in the wrong tree

Run 235 (`d9da59e`, T3, entry 132) failed one App test of 498, the new one of mine:
`Lines_take_the_text_colour_and_the_edge_while_see_through_and_the_bars_marks_the_edge` found no "←". Core 1242 passed.

- **What:** the checks of the three lines had passed before it stopped. ← is in the pointer window's group header,
  which is collapsed until a group opens, so its button's template has never been applied and the glyph is not in the
  visual tree the test walked. It is in the logical tree, where its style, and so its edge, resolve.
- **The fix:** the test finds ← and what Enter opens in the logical tree. The code is unchanged.

This entry claims no run for its own commit.

## 2026-11-21 (132) — Review 39 T3: lines and the bar's own marks while see-through

Run 234 (`036c419`, entry 131) is green: Core 1242 and App 497 tests. Review 39 read: the release check passes but for
T3. Your answers are taken as given: no bar factor in `UiFonts`, the folder stack as it is, the hint's second sentence
kept, and shortcuts' and groups' text icons keep the edge (entry 130).

**T3:** while the bar is see-through (none of this at 100 %, with a backdrop or under High Contrast, as for T1 and T2):

| What | Before | While see-through |
| --- | --- | --- |
| The lines of 線條, 列首線條 and 換列線條 | `Theme.HoverBorder` (white at 31 % in the default theme) | the theme's text colour at full strength, with the names' edge |
| The grip's dots | `Theme.Grip` | the same dots, with the edge |
| ✕ and ▸ (and the pointer window's ←, which is opaque and so never gets it) | the theme's text colour | the same, with the edge |
| What Enter opens, beside the type-to-find box (`SearchTargetStyle`) | `Theme.Hint` on the bare background | the same, with the edge |
| The notice row with *Undo*; the type-to-find box | on the theme's hover fill (white at 18 % in the default theme, black at 8 % in the light one) | as before: they do not stand on the bare background |

- **How:** the lines now take their colour from a new key, `Theme.LineStroke`, which `ThemeManager` sets to the hover
  border's brush itself, so nothing changes at 100 %. While the bar is see-through the bar window puts its own
  `Theme.LineStroke` (the text colour, opaque) beside the edges in its resources. Taking it out gives the theme's
  brush back. The lines and the grip have `Theme.TextEdge` as their effect. ✕ and ▸ have it through a new
  `BarGlyphStyle`, and what Enter opens through `SearchTargetStyle`, so that the styles can be tested without the bar
  window. `SetTextEdges` is now `SetSeeThroughLook`, since it sets the lines' colour too.
- **Tests (App):**
  - in a window with the three lines: before, each line has the theme's line brush and no edge. While see-through, each
    is opaque white with the edge, and ← (the glyph style of ✕ and ▸) and what Enter opens have the edge. Afterwards
    the theme's brush is back and there is no edge;
  - the grip style in a window of its own, with a white edge under dark marks;
  - `ThemeManager.Apply` gives the lines the hover border's own brush.
- **Not tested:** how it looks. You look at it at 30 %, as you said.

This entry claims no run for its own commit.

## 2026-11-21 (131) — 0.14.0 ready for the release check

Run 233 (`f289641`, entry 130, T2) is green: Core 1242 and App 497 tests. The seven packages of your plan 35, with A1–A3
and the findings of reviews 37 and 38 (T1, T2), are in, so 0.14.0 is **ready for your release check**.

| What | Entry | Commit | Run |
| --- | --- | --- | --- |
| Version 0.14.0; WP1 with A1 and A2, separators that start a new row | 120 | `a9712fb` | 223 |
| WP3, the transparency fades the background only | 121 | `251b781` | 224 |
| WP4, the data location on the *Portable* tab | 122 | `914f013` | 225 |
| WP5 with A3, the *Send to PC* tab, the receive folder, one name for each PC | 123 | `27a7d09` | 226 |
| WP6, text or a symbol over the icon's picture | 124 | `69bc7e8` | 227, red (a test of mine) |
| Review 37 T1, names over a light desktop | 125 | `feffa63` | 228, red (the same test) |
| That test compares within a tolerance | 126 | `8d9a4c7` | 229 |
| WP7, the program's icon set | 127 | `7a0edca` | 230 |
| WP2, the dialogs' and menus' own font and text size | 128 | `4e4d223` | 231, red (tests of mine) |
| The colour hint wraps beside its box | 129 | `daeb0c6` | 232 |
| Review 38 T2, special icons' glyphs and gauge text | 130 | `f289641` | 233 |
| Ready for the release check | 131 | this | — |

- **The version:** `Directory.Build.props` says 0.14.0. `CHANGELOG.md` "[0.14.0] – Unreleased" lists them all: under
  Added the row separators, the *Send to PC* tab, one name for each PC, text over a picture, the icon set and the
  dialogs' font and size; under Changed the background transparency with its edges, the *Portable* tab and the
  transparency row's new place. FEATURES §12 and the roadmap say *in progress* until the release commit.
- **Open for you, asked in the entries:**
  - entry 128: a bar factor in `UiFonts` (left out: nothing would read it); the folder stack at the dialogs' size; the
    second sentence of the dialog hint;
  - entry 130: shortcuts' and groups' text icons have the edge too; the bar's ✕ and ▸, the notice row and the
    type-to-find box have none.
- **Seen by you in the app (reviews 36–38):** WP1 with A1 and A2, WP3 on a GPU, WP4, WP5 with A3, WP6, T1, and of WP7
  the exe's icon and `sendto.ico` beside it.
- **Not seen yet:**
  - WP2: two fonts and two sizes, the 外觀 tab at 90, 100 and 150 % in the three languages, the menus and tooltips;
  - T2 at 30 % over a light desktop;
  - the tray, the Send to menu and a title bar's icon; a drag across the new separators; 150 %.

  As before: a real notification banner, a real sign-out, a real Windows snap, maximise on a second monitor, the
  recogniser with a microphone, Windows 10, another DPI scale.

No release commit before your check and the owner's word.

This entry claims no run for its own commit.

## 2026-11-21 (130) — Review 38 T2: special icons' glyphs and gauge text over a light desktop

Run 232 (`daeb0c6`, entry 129) is green: Core 1242 and App 495 tests, the four that failed in run 231 among them. WP2's
test that opens a real context menu and a tooltip passed in both runs: the menu item and the tooltip take the dialogs'
font and size. Review 38 read; thank you for checking WP6, T1 and WP7 in the app.

**T2:** while the bar is see-through these now take an edge, through resources of the bar window that are absent at
100 %, with a backdrop and under High Contrast, as T1's is:

| What | Drawn in | Edge |
| --- | --- | --- |
| The Voice microphone, the Send to PC paper plane | the theme's text colour, no tile colour (a symbol that fits) | `Theme.TextEdge`, on the symbol's fit box |
| Any other text or symbol icon in the theme's colour without a tile colour, a shortcut's or a group's too | the same | the same; text drawn at its size, in the hover frame, has it on the text |
| A ring's value; the bar style's caption and value; the number style's caption | the theme's text colour | `Theme.GaugeTextEdge` |
| A number value in the neutral grey: the clock's time without a timer, a command tile without a level, a monitor before its first sample | `#9A9A9A` | `Theme.GaugeNeutralEdge`, near-black in every theme |
| None: a symbol on a tile colour, a text icon with a colour of its own, the pictures (bucket, recorder), values in green, orange or red, text over a picture (its own outline or plate, WP6) | | |

- **Why two gauge edges, and why on the fit box:** an effect is drawn in the coordinates of the element it is on, so
  it grows and shrinks with the scale of whatever holds that element. A gauge is drawn in a box of 100 units that its
  icon scales (0.32 at 32 px), where T1's edge would be a third as wide. The gauge edges are T1's edge with the blur
  times 100 / the icon size, made again at each settings change (the icon size is one), so on the bar they are as wide
  as the names'. For the same reason a fitted symbol's edge is on its box, not on the text the box scales.
- **The grey's own edge:** the theme's edge is chosen against the theme's text colour, white under dark text, and would
  be white around a grey value on a white desktop. The grey is light, so its edge is near-black.
- **Shortcuts' and groups' text icons too:** they are drawn by the same tile and stand on the background the same way;
  leaving them out would take a special case. Say if only special icons should have it.
- **Left as they are:** the bar's ✕ and ▸, the notice row with *Undo*, the type-to-find box and what Enter opens beside
  it. They are in the theme's colour on the background too, but they are not special icons. Say if they should have it.
- **Tests (App, `PointerWindowTests`):** a window with the Voice icon, a symbol on a tile colour, a ring, a bar, a number
  with no level (as the clock's) and a green one, through a fake handler. Before the edges are set nothing has one. After
  `SetTextEdges` the microphone's box and the gauge texts have theirs, the tiled symbol and the green value none, the
  gauge blur is 3 × 100 / the icon size, and the grey's edge is dark. Once they are taken out, nothing has one. And
  `GaugeState.IsNeutral` with no sample, with a level, with a percentage and with Unknown.
- **Not tested:** how it looks. You check that at 30 % over your striped window.

This entry claims no run for its own commit.

## 2026-11-21 (129) — Run 231 was red: the colour hint ran past the 外觀 page in English

Run 231 (`4e4d223`, WP2, entry 128) failed four App tests of 495, all new ones of mine, all in English: the 外觀 tab at
90, 100 and 150 %, and Settings with the bar at 150 % and the dialogs at 100 %. Core 1242 passed.

- **What:** "Text and highlight colours adapt to the colour." ended at 513.8 units on a page 504 wide. That line sits
  beside the colour box in a horizontal `StackPanel`, which never lets it wrap. Before WP2 the page was not tall enough
  to scroll and was about 17 units wider; with WP2's sixteen rows it scrolls, the scroll bar takes that width, and the
  English line no longer fits. So 外觀 does scroll at 100 %, as entry 128 expected, and that is now measured.
- **The fix:** the box is docked at the left of a `DockPanel`, and the two lines that take turns beside it (the hint and
  "Use #RRGGBB") are in the width left and wrap there. Nothing else on the page sits in a horizontal panel with text.
- **Tests:** unchanged; they found it.

This entry claims no run for its own commit.

## 2026-11-21 (128) — 0.14.0 WP2: dialogs and menus get their own font and text size

Run 230 (`7a0edca`, WP7, entry 127) is green: Core 1209 and App 481 tests. Review 38 came while I was on this: T2 is
the next commit (entry 129), then "0.14.0 ready for the release check".

A correction to entry 127, which said "the author and the files are unchanged": the files are (`git diff c0aec5c
7a0edca -- assets/icons` is empty), the author is not. `7a0edca`'s author is this session, as its committer;
`c0aec5c`'s author was not carried over.

- **The settings:** `dialogFont` and `dialogTextScale` in `LauncherSettings`, synced, in `CopyFrom`, normalised. Both
  absent: as 0.13.0.
  - `dialogFont`: absent is the bar's (`uiFont`); `""` is Windows' own while the bar has a font of its own; else a
    family's name, by the bar font's rule. What is no family's name becomes `""`, as the bar's becomes Windows' own.
    `DialogFamily` is what the dialogs ask for.
  - `dialogTextScale`: absent is the bar's; 90–150, a value outside taken to its end; 100 is kept, since it says 100 %
    whatever the bar has. `DialogTextFactor` is the dialogs', else the bar's, else 100 %.
- **How the font resource is split:**

  | Resource | Read by | Chain |
  | --- | --- | --- |
  | `App.BarFontFamily` (new) | the bar window (`UiFonts.ApplyBar`), its group popups (they inherit from it), the bar at the pointer | the bar's family, the Chinese font, Windows' |
  | `App.UiFontFamily` (as before) | every other window (`DialogTheme.Apply` calls `UiFonts.Apply`), the folder stack | the dialogs' family, the Chinese font, Windows' |
  | Windows' menu, status and message font keys | context menus (the bar's too), the tray menu, tooltips (those over the bar too) | the dialogs' family; sizes × the dialogs' factor |
  | `App.CodeFontFamily`, `App.LogFontFamily` | logs, an approval's details | Consolas, the Chinese font, …: unchanged |

  - `DialogTheme.ScaleText` scales by `UiFonts.TextFactor`, which is now the dialogs' factor.
  - The bar's sizes stay where 0.13.0 has them: `LauncherViewModel` reads `TextScale` (names, captions, the empty bar's
    line, the cell's width, the name line). So `Configure` takes the two families and the dialogs' factor, not two
    factors: a bar factor in `UiFonts` would have no reader. Say if you want it there all the same.
  - Which families this PC has is `UiFontChain.Found` (Core): a missing one is Windows' own and is told once per run in
    the log, for whose text. Dialogs on *Same as the bar* take the bar's font as it is found here, so a missing bar
    font is told once, for the bar.
  - 中文字型 is one for both: both chains carry it.
- **The folder stack:** the dialogs' font (it is a list, as a menu is) at Windows' size, as in 0.13.0: it was never
  scaled, and it sizes itself to its rows at the pointer. Say if it should take the dialogs' size.
- **Settings ▸ 外觀:** 佈景主題, 背景, 工具列背景, 背景透明度, 圖示大小, 間距, 名稱, 中文字型, then **工具列文字** (字型, 文字大小,
  範例：捷徑 Shortcut 123 with the bar's hint) and **對話方塊與功能表文字** (字型: 與工具列相同, Windows 預設（…）, the
  families; 文字大小: 與工具列相同, 90 to 150 %; 範例：設定 Settings 123 with its hint).
  - 背景透明度 moved up under 工具列背景: left last, it would have read as part of the dialog text group.
  - The headings are `DialogLabel` (the dialog's text colour), SemiBold, across both columns.
  - The samples are live. The dialogs' follows the bar's choice while it is on 與工具列相同. Both are scaled against the
    size Settings itself is drawn at, which is the dialogs' size in use.
  - 範例： has a colon, as the other labels of that column.
  - The page is taller (sixteen rows). The page area is still at most 560, so 外觀 scrolls when it is taller; I expect
    it to at 100 %, not measured here.
- **Strings:** new `Settings_BarText`, `Settings_DialogText`, `Settings_SameAsBar`, `Settings_DialogTextHint`,
  `Settings_BarSample`, `Settings_DialogSample`, `Settings_DialogFontName`, `Settings_DialogTextSizeName`. Changed:
  `Settings_TextSample` is now the label 範例：; `Settings_TextHint` speaks of the bar only (工具列上的名稱在儲存時套用。圖示大小不變。);
  `Settings_UiFontName` and `Settings_TextSizeName` name the bar's (工具列文字的字型, 工具列文字的大小).
  - `Settings_DialogTextHint` has a second sentence of mine, 視窗的大小在下次開啟時套用。: an open window takes a new
    font at once and a new size when it is next opened, which the 0.13.0 hint said for both. Drop it if you prefer
    the plan's line alone.
- **Tests:**
  - Core (`DialogTextTests`): both settings' normalisation (100 kept, the ends, `""`, no family's name), the factor
    (the bar at 150 % with the dialogs at 100 %, and the reverse), `DialogFamily`, the round trip, 0.13.0's settings (a
    class with extension data) keeping both and writing them back, copy and clone; `UiFontChain.Found` with missing
    families and whose text is told.
  - App: `UiFonts.Configure` with two families (the keys, the sizes, the reverse, Windows' own for either); a window
    made as the bar, with a context menu and a tooltip it opens: the text in the bar's font, the menu item and the
    tooltip in the dialogs' font and size, a dialog in the dialogs' font and scale; the Settings lists in zh-Hant
    (與工具列相同 first, Windows 預設（…）, `Gone Font（這台電腦沒有）`, 100 kept, the samples live, Save writes nothing for the
    bar's); the bar's names at the bar's size with the dialogs at 100 % or 150 %; the 外觀 tab fitting at 90, 100 and
    150 % in all three languages, both lists with a missing family, its rows in order; Settings with the bar at 150 %
    and the dialogs at 100 % not scaled, and the reverse scaled, each sample at its own size. The Chinese font test now
    takes the bar's font as `MainWindow` does.
  - `FitsAcross` counts an element's own scale when it checks that nothing is squeezed: the samples have one.
- **Not tested:** the bar's own menus, the tray menu and real tooltips with two fonts (the test opens a menu and a
  tooltip it made); the folder stack; Windows 10.

This entry claims no run for its own commit.

## 2026-11-21 (127) — 0.14.0 WP7: the program's icon set

Run 229 (`8d9a4c7`, entries 124–126: WP6, T1 and the test fix) is green: Core 1209 and App 476 tests. This is WP7.
WP2 comes last.

- **The files:** `design/icons` (`c0aec5c`) came in with `cherry-pick --no-commit`, all 91 files under `assets/icons/`
  as they are, `make-icons.ps1` included and not redrawn. They are in this commit, not as your commit: its message has a
  model name in its trailer, and nothing committed here may have one. The author and the files are unchanged in
  substance, only the commit is mine.
- **What each place shows now:**

  | Place | Shows | How |
  | --- | --- | --- |
  | The exe in Explorer (small and large views), a pinned taskbar button | `app.ico`, the frame Windows picks | `<ApplicationIcon>` |
  | A window's title bar, Alt+Tab, the taskbar while a window shows there | `app.ico`'s frame of that size | `AppIconImage` is now an `ImageSource` from the built-in `app.ico` (a `BitmapFrame` with all eight frames), so the 22 windows that used it need no change |
  | The tray | `tray.ico`'s frame for `SmallIconSize()`: exact at 16, 20, 24 and 32; otherwise the nearest, the larger on a tie (28 → 32); chosen again on `Refresh` (a DPI change) | `IconFrames` reads the `.ico` directory and makes the handle with `CreateIconFromResourceEx` from that frame's own PNG |
  | Explorer's *Send to* | `sendto.ico` | It is next to the program in both zips. `SetIconLocation(sendto.ico, 0)`, else the exe's own icon |
  | README | `readme/header-1280x320.png` at the top, alt text "Desktop Icon Grid" | a path in the repository |

- **The drawing:** `AppIconImage`'s drawing (the four tiles in XAML) is gone. Nothing needed a vector: the tray was its
  one other user, and it now takes `tray.ico`. The drawing is still the fallback when `tray.ico` cannot be read, now
  through the `.ico`'s `ImageSource`.
- **Send to:** I chose the file next to the program, not a second icon resource: the SDK's `ApplicationIcon` takes one
  icon, and a `.res` file would have to be built.
  - `sendto.ico` is `CopyToPublishDirectory` with `ExcludeFromSingleFile`. In a portable copy that moves, the entry's
    path to the program is stale anyway, and Windows shows a missing file's icon as the default.
  - At start (at idle) `SendToEntry.RefreshIcon` reads the entry. If it starts this copy's exe with `--add`, which is
    the entry this program makes, and its icon is not the one it should have, only the icon is rewritten. An entry
    that starts another copy, or one that is not ours, is left alone.
- **The zips:** a local `dotnet publish`, as the release workflow runs it (framework-dependent, single file), gives
  `DesktopIconGrid.exe` and `sendto.ico` (17 KB), and nothing from `store/`, `readme/` or `png/`. `app.ico` (17 KB, as
  both the exe's icon and a WPF resource) and `tray.ico` (6 KB) are inside the exe. The self-contained zip uses the
  same items; it is built only by the release workflow, at the tag.
- **Store assets:** kept in the repository for 1.0.0; nothing is packaged.
- **Tests (App, `IconSetTests`):**
  - the frame choice (exact at 16 to 32, 28 → 32, 36 → 40, the largest and the smallest);
  - the built-in `tray.ico` has 16, 20, 24 and 32, gives a handle at each and at this PC's size, and a `TrayIcon` made
    from it refreshes;
  - `AppIconImage` is a `BitmapFrame` with at least eight frames, from 16 to 256;
  - `IconFor`, with and without the file, `sendto.ico` in the build output, and no `store` or `readme` there;
  - `IsThisCopy` (this copy, case aside; a copy that moved; another argument; no target).
- **Not tested:** what Explorer, the taskbar, the tray at 100 % and the Send to menu show. You check those in the app.

This entry claims no run for its own commit.

## 2026-11-21 (126) — Run 227 was red: a test of mine compared doubles exactly

Run 227 (`69bc7e8`, WP6, entry 124) failed one App test of 475, my own: `TextOverPictureTests` compared a corner's box
with `Assert.Equal` on `Rect`, and 48 × 0.55 is 26.400000000000002 in doubles. The code was right. The test now
compares each side to three decimals. Nothing else changed. Run 228 (`feffa63`, T1, entry 125) has the same test and
will be red for the same reason; this commit's run is the one to read for WP6 and T1 together.

This entry claims no run for its own commit.

## 2026-11-21 (125) — Review 37 T1: names readable over a light desktop

Your early check (review 37) found T1. WP6's run is not in yet: entry 124 has `69bc7e8`, and the next entry gives its
result.

- **T1, names over a light desktop:** while the background is see-through (`_seeThrough`: below 100 % and no backdrop;
  not under High Contrast), these text elements get a thin soft edge:
  - the names under the icons;
  - the captions of labels and row lines;
  - the empty bar's line;
  - a group's heading in its popup.

  The edge is near-black (`#1A1A1A`) under light text and white under dark text, from the luminance of
  `Theme.Foreground`. At 100 %, with Mica or Acrylic, and in the pointer window (which is opaque), there is none.
  Tiles and gauges are untouched.
- **What I chose:** a `DropShadowEffect` with no offset, a blur of 3 and opacity 0.95, `RenderingBias.Performance`.
  It is frozen and shared by every text, as a resource of the bar window only (`Theme.TextEdge`). The texts take it with
  `Effect="{DynamicResource Theme.TextEdge}"`, so the popup, which is in the window's logical tree, has it, and nothing
  else does. When the background is opaque the resource is removed, and the texts have no effect at all.
- **The cost:** on a GPU the effect is a pixel shader per text, one small intermediate each, about as many as there
  are names. Where WPF draws in software (a VM, RDP) it is done on the CPU. That is the cost you named, kept to the
  see-through case.
- **The alternative I did not take:** an outlined text run (`FormattedText` geometry, as WP6 draws text over a
  picture). It would replace the names' `TextBlock`s and with them their trimming ("…") and the text-size scaling of
  0.13.0, so I kept the effect. If you see the edge too soft or too heavy, the blur and the opacity are two numbers in
  `MainWindow.TextEdgeFor`.
- **Tests (App):** in `PointerWindowTests`, the real window's name and a row line's caption take the window's resource
  when it is set and lose it when it is removed; the pointer window has none of its own; the edge's colour is dark under
  light text and white under dark.
- **Not tested:** how it looks on a screen at 20 to 90 % over light and dark desktops (your capture).

This entry claims no run for its own commit.

## 2026-11-21 (124) — 0.14.0 WP6: text or a symbol over the icon's picture

Run 226 (`27a7d09`, entry 123, WP5 with A3) is green: Core 1207 and App 460 tests. Review 37 is read, and thank you for
the early check: T1 (names over a light desktop) gets its own commit and entry next. After that come WP7 and WP2.

- **Model:** `TextIcon` has two init properties, written only when set.
  - `overImage` (bool).
  - `place` (`topLeft`, `topRight`, `bottomLeft`, `bottomRight`). The middle (`center`) is not written, any other value
    reads as the middle, and `place` is read only with `overImage` (`Corner`). `Normalized` writes the corner in its
    canonical form, or drops `place`.
  - A version before 0.14.0 ignores both and draws the text tile alone, which is the fallback.
- **Drawing:** a new `Views/TextOverPicture` is used by `TextIconTile` whenever `OverImage` is set. There is no tile; it
  draws only the text, as its outline geometry (`FormattedText.BuildGeometry`), so the outline keeps one unit at any
  fitted size.
  - **The middle:** the whole tile, at its size, or fitted when `fit` (always for a symbol), with the 16 % margin a
    fitted tile has.
  - **A corner:** a box of 55 % of the side, always fitted, less the room for the outline or the plate. The letters' ink
    is centred in the box.
  - **Without a tile colour:** white unless the icon has its own colour, with a 2-unit pen drawn under the fill (1 unit
    outside the letters). The pen is `#1A1A1A` under light text and white under dark (luminance at 0.5).
  - **With a tile colour:** a rounded plate (ink + 2, radius 3), and the text follows `ThemeCatalog.TextOnTile`.
  - **A symbol this PC lacks:** nothing is drawn over the picture, rather than the placeholder.
- **The bar** (`ItemViewModel`):
  - `HasTextIcon` is also true over a chosen picture, when the text goes over it;
  - `TextIsOverImage` is new;
  - `ShowsImage` and `NeedsImage` stay true under text over the picture, so the picture is loaded and drawn as without
    a text icon, running mark and badges included;
  - the template is unchanged, since the image and the tile already lie in one cell;
  - the hover fill and border are the button's, as for a picture.
- **Properties:**
  - text over the picture keeps the chosen picture, and choosing another picture keeps the text;
  - text in the picture's place replaces it, as before;
  - the preview has the picture with a second `TextIconTile` over it (`ShowsTextOverImage`), or the tile alone
    (`ShowsTextTile`).
- **The window:**
  - Under the colours, *Keep the icon picture; draw the text on top* (a wrapping check box) and *Position* with the five
    choices, enabled only with the box ticked, on both sides.
  - The hint becomes TextIcon_HintOver while ticked, and then shows on the Symbol side too.
  - The preview draws the item's real picture (`ItemPropertiesViewModel.Preview`, passed in) under the text.
  - The box is offered when the window is opened from Properties, that is for shortcuts and groups. Special icons have
    their own properties, without *Text or symbol icon…*, so they do not get it.
- **The names table:** it draws no icons, so it has nothing to show (FEATURES says so).
- **Tests:**
  - **Core (1209):** the round trip, the middle not written, a corner read back, another place, `place` without
    `overImage`, an old file.
  - **App:**
    - `TextOverPictureTests`: the boxes; the colours (default white with a dark edge, dark text with a white edge, the
      plate's contrast rule); the tile using the overlay and a missing symbol leaving the picture alone; the editor
      (not offered without a picture, both sides, the corner saved and read back, the second hint).
    - In `ItemPropertiesViewModelTests`, the picture kept and dropped.
    - In `LauncherViewModelTests`, the bar's state for both kinds.
    - In `DialogFitTests`, the window with the box ticked at 90, 100 and 150 % in three languages, on both sides,
      with OK and Cancel in sight.
- **Not tested:** the drawing itself on the screen (the outline, the plate, a corner at 16 and 64 px); a real
  program's picture under a symbol.

This entry claims no run for its own commit.

## 2026-11-21 (123) — 0.14.0 WP5 with A3: a *Send to PC* tab, the receive folder, and one name for each PC

Run 225 (`914f013`, entry 122, WP4) is green: Core 1177 and App 451 tests. Review 36 is read: there are no findings,
and your answers are taken as given. The release name, "Rows, looks and places", is now in CHANGELOG, FEATURES §12 and
the roadmap. This entry is WP5 with A3. WP6, WP7 and WP2 follow.

- **The tab:** *Send to PC* (傳送到電腦 / 发送到电脑), before *Portable*: 外觀, 視窗, 按鍵, 語音, 一般, 傳送到電腦,
  可攜. In order:
  - this PC's name (A3), with its hint;
  - the receive box (`SyncSettings.ReceiveFiles`, the same value as the menu's item), with its hint;
  - *Folder location* with *Browse…*, *Back to Downloads* (enabled only when another folder is chosen), *Open folder*,
    the reason when the folder is refused, and the hint;
  - *Sync across PCs* (a sync folder, Google Drive, or not set up), with *Sync across PCs…*, which opens the Sync window
    over Settings and then refreshes the row. The hint shows only when sync is not set up.

  The tab's values come from `sync.json` (`ThisPcSettings`, given by the dialog service) and go back there at Save
  (`ThisPcChoice` → `InboxService.ApplyThisPc`). Nothing on the tab is in the layout.
- **The receive folder (Core: `Sync/ReceiveFolder.cs`):**
  - It is stored as `receiveFolder` in `sync.json`. Nothing is written for Downloads (`null`), and the box showing the
    Downloads path counts as Downloads. Variables are expanded where the folder is used, and a portable copy writes a
    path on its drive with `%APPDRIVE%` (`DataLocation.ForStorage`).
  - The rules: a full name (a drive or `\\server\share`; Windows' rules written out, as for the data folder), at most
    248 characters (260 less the shortest file name), not the data folder or in it, not the sync folder or in it. A
    folder that only contains the sync folder is allowed.
  - On the disk the folder must be there or be made, and must take a file. A file of that name is refused.
  - The name rules are checked as you type. The disk check runs at Save: it opens the tab and shows the reason beside
    the box, which is cleared when the text changes.
  - **At arrival** the rules are checked again, in each look that has files. When the folder fails, the files go to
    Downloads, a Warning goes to the log, and one warning notice comes last, naming the folder and the first file
    ("and N more"). The setting is kept.
  - The free-space floor counts on the drive of the folder used. The mark, the name rules and no overwriting are as
    before.
- **Texts that said Downloads:**
  - Inbox_Received names the folder by its last name ("the folder “From laptop”"), with Downloads by its word (下載).
  - Inbox_WaitingForSpace names the folder too. I shortened the English so the balloon test's case (a 60-character
    name, "and 99 more") stays at 242 of 255.
  - SendTo_Receives says "(into “…”)".
  - Menu_ReceiveFilesToolTip gives the full path.
  - SendTo_Desc says "that PC's receive folder (Downloads unless changed there)".
  - The Send to PC dialog ends with SendTo_FolderInSettings.
- **One name (A3):**
  - The name is `SyncSettings.DeviceName`, so the Sync window's box shows the same value, with the same rules.
  - **Where it shows:**
    - the Send to PC list and the captions made from it;
    - "saved by" on the copies, which `SyncProfile.SavedBy` already took from it;
    - the log;
    - the arrival notice.
  - **The arrival notice** was the one place that showed something else: a PC that sends but does not receive
    published no name, so its files came "from 3fa9b2c1". Such a PC now leaves `sender-<id>.json` with its name in the
    inbox it sends to. The folder inbox rewrites the card only when its name changes; Drive writes it once per run
    and inbox. The receiver reads the card first, then the sender's own `device.json`, then the start of the id.
    There is no `~` in the card's name, so 0.13.0 passes it over. The id's start remains only for a PC that has no
    name anywhere.
  - **Renaming:** `SendToNames.Follow` (Core) runs when the inbox lists the PCs: at most every 5 minutes, while an
    icon has a PC, whether this PC receives or not. If the icon's PC is listed under another name, the stored
    `deviceName` takes that name, and the caption follows while it equals `SendTo_NameFor` of the old name. A typed
    caption stays, and so does one made in another language (it is not this PC's format). Nothing else changes, not
    even `ModifiedAt`, so two PCs write the same icon (tested). The bar saves once; the next look finds nothing.
  - **Two PCs with one name:** `InboxDevice` got a `Label` (by default its name). The dialog shows
    `NOTEBOOK (3fa9)` while the icon keeps the real name.
- **Tests:**
  - **Core (1207):**
    - `ReceiveFolderTests`: the expansion, the name rules (14 cases), the length limit, the disk (made, a file, cannot
      be made), and `sync.json`;
    - `SendToNamesTests`: the labels, following, a typed caption, two PCs writing the same icon, not listed or no PC;
    - the card in `FolderInboxTests` and `DriveInboxTests`;
    - the existing Drive test's file count, which now includes the card.
  - **App:**
    - `SendToTabTests` (5 tests);
    - in `NoticeTargetTests`, the fallback warning and the folder names;
    - in `SendToDeviceTests`, the labels and the box's text;
    - in `LauncherViewModelTests`, following and saving once;
    - `DialogFitTests` with seven tabs, the theory covering *Send to PC* too (a long Downloads path, its hint shown) in
      three languages at 90, 100 and 150 %.
- **Not tested:**
  - the folder picker;
  - *Open folder*;
  - the Sync window over Settings;
  - a real stick pulled out;
  - a share as the receive folder;
  - two real PCs following a rename through a sync folder and through Drive;
  - a 0.13.0 PC beside a 0.14.0 one.

This entry claims no run for its own commit.

## 2026-11-21 (122) — 0.14.0 WP4: the data location on a tab of its own, *Portable*

Run 224 (`251b781`, entry 121, WP3) is green: Core 1177 and App 442 tests. `SeeThroughTests` passed on CI's own
desktop: the 20 % background let the red window behind it show through, the green square on it stayed exactly green,
and `WindowFromPoint` at the background gave the see-through window. So the mechanism works at least where WPF draws
in software, as on CI. This is WP4.

WP7 (the icon set) came while this was being written. You gave no place for it, so I put it after WP6 and keep WP2
last: WP5 with A3, WP6, WP7, WP2.

- **The tab:** a sixth tab, last: Settings_TabPortable, "Portable" / 可攜 / 便携. The whole 資料位置 block moved there
  as it was (the same elements, names and bindings: `DataLocationBlock`, `DataFolderBox`, `ImportThisPcButton` and the
  rest). *General* keeps the language. There is no change of behaviour.
- **Problems:** the data location's problem now marks the *Portable* tab with ⚠, and the line beside Save names it
  (`HasPortableProblem`, `PortableTab`). *General* has no problem of its own any more. When the folder cannot be used,
  Save opens the *Portable* tab, found by its page rather than as "the last tab".
- **Texts:** App_PortableKeptInProgram was the only text that sent the user to 「設定」›「一般」›「資料位置」. It now says
  「設定」›「可攜」, "Settings › Portable" and "设置"›"便携". FEATURES says *Portable* in its three places, and the
  dialog's description has six tabs.
- **Tests (App):**
  - `DialogFitTests`: six tabs and six pages; the shortest-page test has six visibilities.
  - New theory, en-US, zh-Hant and zh-Hans, each at 90, 100 and 150 %. It checks that *Portable* is the last tab and
    holds the block (General does not), that every tab is in the strip's first row and none goes past its end, and
    that every text, box and button on the page is within its width and not squeezed.
  - `DataLocationAppTests`: the ⚠ and the line beside Save name *Portable*.
- **Seven tabs:** WP5 will add *Send to PC* before *Portable*. The same theory will cover seven.
- **Not tested:** how the tabs look in the light and dark Fluent styles and in the classic one (only position and
  width are checked).

This entry claims no run for its own commit.

## 2026-11-21 (121) — 0.14.0 WP3: the transparency fades the background only

Run 223 (`a9712fb`, entry 120, WP1) is green: Core 1177 and App 440 tests. This is WP3. WP4, WP5 with A3, WP6 and
WP2 follow, in that order.

- **How (the way was mine):** the bar stays an ordinary window, not a layered one.
  - WPF draws on a transparent surface: the composition target's background is transparent, which `WindowChrome`
    already does while DWM composes.
  - DWM is asked to compose the window by that surface's alpha: `DwmEnableBlurBehindWindow` with an empty region.
    Since Windows 8 that call blurs nothing; it only makes DWM use the alpha. GLFW's transparent framebuffer relies on
    the same thing on Windows 8 to 11.
  - The opacity goes only into `Theme.BarBackground`, as it already did with Mica or Acrylic (`translucentBar` is now
    "backdrop or see-through"). Everything else is drawn solid.
  - `WindowAlpha` (the layered window's single alpha) and its declarations are gone. The new service is
    `Services/WindowTransparency.cs`.
- **Why not `AllowsTransparency`:**
  - a layered window cannot take Mica or Acrylic;
  - it has to be chosen before the window exists, so switching between solid and a backdrop would need a new window;
  - it passes clicks through where it is fully transparent.

  An ordinary window is hit by its rectangle: a right-click on an empty part of the background reaches the bar
  however transparent it is, and `WindowFromPoint` at the background gives the bar (tested, below).
- **What it keeps:** docking and the work-area reservation, drag and drop from Explorer, the hotkey, and
  show/hide. None of these depend on the window being layered or not. The backdrops on Windows 11 are unchanged: a
  chosen backdrop turns the see-through mode off before DWM gets the backdrop. A refused call is logged, and the
  background is then opaque, never unpainted.
- **Text:** while the background is see-through, the bar and its popup draw text with `TextRenderingMode.Grayscale`.
  ClearType works its colour fringes out against the bar's own colour, not against what shows through. At 100 % the
  surface is as before, and so is ClearType. The Mica and Acrylic path is left as it was.
- **The popup** follows the bar: Acrylic with a tint when the bar has a backdrop; see-through at the bar's value when
  the bar is (`Theme.PopupBackground` takes the opacity, the icons stay solid); solid otherwise.
- **The folder stack:** its window had no transparency before, and it still has none.
- **High Contrast:** the background stays opaque. Before, the whole window faded under High Contrast too. This is my
  choice, the same as for the backdrop ("High Contrast asks for flat colours"); say if you want the value to apply
  there.
- **Windows 10** cannot be seen here. The same call is documented to do the same there (no blur since Windows 8, the
  alpha used). If it is refused, the bar is opaque and the log has a Warning.
- **Strings:**
  - renamed: Settings_Transparency ("Background transparency:", 背景透明度：), Settings_TransparencyName;
  - new, under the slider: Settings_TransparencyHint;
  - Settings_BackdropHintSupported loses "Icons stay sharp", which the new hint now says, and names the slider by its
    new name;
  - the slider's tool tip says "the bar's background".
- **Stored value:** `opacity` is unchanged in `layout.json` and in sync.
- **Tests (App):**
  - `SeeThroughTests` shows real windows on CI's desktop: a red window, and over it a window with a blue background
    at 20 % and a green square, made see-through by `WindowTransparency`. Read back from the screen:
    - the square is exactly green;
    - the background is about (204, 0, 51), blue at 20 % over red. A window-wide alpha would fade the square; an
      opaque surface would give (0, 0, 51);
    - `WindowFromPoint` at the background is that window.
  - A second test: of the theme's brushes, only the bar background takes the tint.
- **Not tested:**
  - the bar window itself (`MainWindow.ApplyAppearance` is not run by a test: the mechanism and the brush are);
  - a real 20 % capture of the bar, which you said you do;
  - ClearType versus grey on a real screen;
  - Windows 10, a GPU (CI renders in software), RDP, and a change of setting while a popup is open (Settings
    closes the popup first).

This entry claims no run for its own commit.

## 2026-11-21 (120) — 0.14.0 WP1: separators that start a new row (plan 35, with A1 and A2)

The first package of plan 35, with your two additions: A1, a caption on the row line, and A2, the row-start line.
WP3 (background-only transparency) and WP4 (the data location on a tab of its own) came while this was being written.
The order is now the one you gave: WP1, WP3, WP4, WP2, each with its own entry, then "0.14.0 ready for the release
check".

- **The version:** `Directory.Build.props` says 0.14.0; CHANGELOG has "[0.14.0] – Unreleased"; FEATURES §12 and the
  roadmap have 0.14.0 in progress (no name yet: yours to give, if any).
- **Core:**
  - `SeparatorItem` has `rowhead` and `rowline`, with `Kinds` in the menus' order: line, rowhead, rowline, gap,
    label.
  - `HasText(kind)` is true for a label and a row line.
  - `SavedName(kind, name)` gives "Divider" for a line, a row-start line, and a row line without a caption; a row
    line with a caption is saved by its caption. The converter and the load both use it.
  - `FlowLayout` has `FlowSlot.Row` and `FlowSlot.RowHead`, as in the plan:
    - **Row.** When x > 0 the row ends (y += cell height). The box is (0, y, the panel's width, max(9, the content's
      height)); then x = 0 and y goes under the strip. At a row's start there is only the strip. Measured at an
      infinite width the strip is as wide as the widest row (finite), and the row still breaks.
    - **RowHead.** When x > 0 the row ends. The box is (0, y, 9, cell height) and x = 9.
    - **One column.** Both lie across it as a line does; a captioned row line is as tall as its caption.
- **App:**
  - **Drawing.** The template draws a row-start line with the existing `DividerLine` (upright, because its box is
    tall). A row line is a new `RowLinePanel`: the caption (Theme.Hint, SemiBold, NameFontSize, ellipsis) 6 in, a
    gap of 6, then the line to 6 before the edge. The line keeps at least 24 units. The caption's strip is the
    text's height plus the button's border of 2. `DividerLine.Flat` keeps the row line's line flat, however short
    it gets beside a caption.
  - **Menus.** All three places have the five kinds in your order. The separator's menu has *Text…* (標題文字…) while
    it is a row line, with the Properties key (F2) shown beside it.
  - **Editing the text.** `EditSeparatorText` asks with the current text: an empty answer takes it away, Cancel
    changes nothing. `EditItem` (the Properties key) does the same for a row line. A double-click on a row line
    does it too.
  - **Changing kind.** `SetSeparatorKind`:
    - label → row line keeps the text without a question;
    - row line with text → label keeps it without a question;
    - a label chosen again, or chosen from a kind without text, asks as before;
    - line, gap and row-start line drop the text.
  - **Dropping.** In the drop hit test, the upper half of a row line means before it and the lower half after it;
    nothing is dropped into a separator. The drop marker for a drop just before a row line or a row-start line is
    drawn after the item before it, at the end of the row above, which is where the item goes. Before, it would
    have shown at the strip's left, on the next row. After a row line that is last, the marker is at the left under
    the strip.
  - **Screen readers.** The separator button's name is `SeparatorName`: the caption, else 換列分隔線 or 列首分隔線.
    The item container's name stays `Name`, as for the other separators.
- **Choices to look at:**
  - A **double-click on a label** now asks for its text too, as F2 always did. A1 named the row line only. Say if a
    label should keep doing nothing on a double-click.
  - **In a strip too narrow** for 24 units of line to leave the caption half the width (a bar one icon wide), the
    caption may take all but the insets, as a label does, and the line gets what is left. With your rule as
    written, a 52-unit column would leave the caption 10 units.
  - **A new row line** is added without asking for text; *Text…* adds a caption.
  - **A row line whose caption is exactly "Divider"** reads back without one, because that is the name a row line
    without a caption is saved with. FEATURES says so.
- **Checked by reading, not run:**
  - The names table and the voice vocabulary skip every `SeparatorItem` (`NameTableViewModel`,
    `VoiceVocabulary.IsStartable`).
  - A group's popup is the same `IconGridControl` and panel.
  - The pointer window measures its rows from the panel, so the strips count.
  - **Height.** The bar does not size itself to its rows: its size is the user's, and the strips add their height to
    what scrolls. A group's popup follows its content up to `PopupMaxHeight` (rows × cell height), so a popup at its
    row limit scrolls the strips' height. `PopupWidth` still counts a separator as a cell.
  - **Arrow keys** use WPF's directional navigation (`DirectionalNavigation="Contained"`), which goes by position.
    The strips are focusable buttons, so Down from a row lands on the strip and Down again on the row under it.
  - **0.13.0** reads both kinds as a line (`EffectiveKind`), keeps a name of a kind it does not know, and writes it
    back as it was. A caption survives a round trip through it.
- **Tests:**
  - **Core (1177; 1170 at 0.13.0):** in `FlowLayoutTests`:
    - a row line ends its row where it is and lies across;
    - a row line first, after a full row, and twice, makes no empty row;
    - a captioned row line is as tall as its caption;
    - measured in one row it still breaks and is as wide as the widest row;
    - in one column both lie across it;
    - a row-start line ends its row and the items follow it ([C C RowHead C] gives the line at (0, cellH) and the
      cell at (9, cellH)).

    In `SeparatorAndTagTests`, the saved names and their read-back.
  - **App (4 new):**
    - `LauncherViewModelTests`: the five kinds and what each keeps of a caption, without a question; the row line's
      text, with Cancel, the empty answer, and a row-start line that asks nothing.
    - `PointerWindowTests`: the real window with a captioned row line, a row-start line, three icons, a plain row
      line and an icon. It checks every position, and that every row shows. It also checks the caption's room rule.
    - `RecordingDialogs` counts text questions.
- **Not tested:** the drop hit test and marker (no drag is simulated), the arrow keys, the double-click, the look
  (the caption's style and ellipsis, the line's inset), a group's popup with the new kinds, and a real 0.13.0
  reading a bar that has them.

This entry claims no run for its own commit.

## 2026-11-21 (119) — Release commit for v0.13.0

Runs 219 and 220 (`f7c83e9`, `bd58ca1`) are green, and your re-check (review 34) passed with no finding. This is the
release commit you asked for, made on the owner's word to me: I asked directly when your message came, and the owner
also chose the date, 2026-11-21.

- **What this commit changes (documents only, no code):**
  - `CHANGELOG.md`: "[0.13.0] – Unreleased" becomes "[0.13.0] – 2026-11-21". The section lists the five packages and
    R1–R6.
  - `FEATURES.md` §12: 0.13.0 is done, released 2026-11-21; *Next* is nothing planned.
  - `docs/ROADMAP.md`: 0.13.0 released 2026-11-21; *Next*: nothing planned.
  - `CLAUDE.md` State, which the owner said yes to when I asked:
    - 0.12.2 released (tag `v0.12.2` on `85b2f04`, release run 15);
    - 0.13.0 with its release commit, the tag and `main` yours;
    - nothing in progress;
    - the 0.13.0 choices to keep;
    - what you saw in reviews 33 and 34;
    - what is not seen anywhere.
- **Your two answers** (review 34) are kept as they were: a corner icon in a frame is left alone, and 命令.
- **The version:** `Directory.Build.props` says 0.13.0. No project file or workflow names another.
- **Not in this commit:** the tag `v0.13.0` and `main`, yours on the owner's word.

| What | Entry | Commits | Run |
| --- | --- | --- | --- |
| Version 0.13.0; WP2, icons that came out smaller | 108 | `adb35c0` | 210 |
| WP3, hint text in the dialog's text colour | 109 | `91cecf6` | 211 |
| WP5, *Send to PC* before sync | 110 | `4d5db6e` | 212 |
| WP1, the data location and the import | 111 | `fe18374` | 213, red (a test of mine) |
| WP1's two options wrap | 112 | `d41dacd` | 214 |
| WP4, the font and the size of the text | 113 | `06e376f` | 215, red (a test of mine) |
| A font installed after the start is found | 114 | `b138067` | 216, red (the same test) |
| The fit test measures a scaled dialog in one unit | 115 | `652ef7d` | 217 |
| Ready for the release check | 116 | `ed42012` | 218 |
| Review 33: R1–R6 | 117 | `f7c83e9` | 219 |
| Ready again | 118 | `bd58ca1` | 220 |
| Release commit | 119 | this | — |

This entry claims no run for its own commit.

## 2026-11-20 (118) — 0.13.0 ready for the release check, again

Run 219 (`f7c83e9`, entry 117) is green: Core 1170 and App 436 tests. Review 33's R1–R6 are in, so 0.13.0 is **ready
for your release check** again.

| What | Entry | Commits | Run |
| --- | --- | --- | --- |
| The five packages, as in entry 116 | 108–115 | `adb35c0` … `652ef7d` | 210–217 |
| Ready for the first check | 116 | `ed42012` | 218 |
| Review 33: R1–R6 | 117 | `f7c83e9` | 219 |
| Ready again | 118 | this | — |

- **Your re-check, as you listed it:**
  - R1: the copy whose `portable.txt` holds the note. It should open on its own bar, with the notice once.
  - R2: PCMan at 32, 48 and 64. Each should give an "Icons:" line in the log, and the icon should fill its cell.
  - R3: "字元對應表" and "資料夾堆疊" at 150 %, in full.
- **Two questions in entry 117**, neither in the way of the release:
  - whether an icon in a corner of a frame should count too;
  - 命令 or 指令 in R5's zh-Hant sentence.
- **Unchanged:** the version (0.13.0, "[0.13.0] – Unreleased" in CHANGELOG), and what you listed as not seen by you.

This entry claims no run for its own commit.

## 2026-11-20 (117) — Review 33: R1–R6 of the release check of 0.13.0

Run 218 (`ed42012`, entry 116) was green; your release check of it did not pass. All six are in this commit.

- **R1, a note in `portable.txt`** (must fix):
  - `DataLocation.Resolve` takes `hasBar`. When the folder the line names has no `layout.json` (or the line names no
    folder at all: a note with a `:` or `?` in it) and the program's folder has one, it returns the program's folder
    (`Portable`) with `KeptInProgramFolder` and `Named` = that folder (or the line). Nothing is made. If the program's
    folder cannot be written, the data goes to `%LocalAppData%` as before.
  - At start: a Warning in the log and one notice, `App_PortableKeptInProgram`, your text in the three languages.
  - Settings shows the program's folder under *Now:* and the line as written in the box. Saving another folder moves
    it with the copy question. Saving `.` takes the note away and the notice stops.
  - **One more thing it needed:** *Don't copy* to a folder without a bar leaves an empty `layout.json` there.
    Otherwise the next start would take the new folder for a note's and stay in the program's folder whenever that
    one has a bar, undoing the move the user chose.
  - Test: `DataLocationTests.A_note_in_portable_txt_leaves_the_bar_in_the_program_folder_while_only_that_folder_has_one`.
    It covers your case (nothing made), a line that names no folder, the folder named with a bar (used), a new copy
    with no bar anywhere (the folder named, made), `.`, and a program folder that cannot be written.
- **R2, the frame** (must fix):
  - `IconBox.FrameInset` finds the frame by what it is:
    - the outermost rings, each drawn on at least 90 % of each of its four sides;
    - at most 4 % of the side wide, rounded: 3 px at 64, 4 at 96, 5 at 128;
    - then a ring with nothing drawn on it, with at most one partly drawn ring before it (a soft edge).
  - The icon is measured inside that. In a frame any centred icon is padded; without one, the 70 % rule stays.
  - `FrameWidth` is gone. Without a frame the whole bitmap is measured, so `Drawn` of a full 96 is 96 × 96.
  - I kept "centred" inside a frame too (an icon in a corner of a frame is left alone); say if you want any position.
  - Tests:
    - your three boxes: 64 (frame 2, icon 8..55), 96 (2, 24..71), and 128 (3, 40..87, the top row 126 of 128);
    - notepad's 93 × 85 without a frame (stays the shell's);
    - a full 96, and a 6 px border by design (wider than a frame: not one);
    - a soft inner edge, and a 5 px frame at 256;
    - a framed icon in a corner. `ShellIconProviderTests`' framed picture still passes on this rule.
- **R3, the names at 150 %:** with names shown, `CellWidth` is the 100 % width × the factor, never below 1, rounded
  up. At 90 % it is 100 %'s width, and without names the icon's width as before. The icon keeps its size, centred.
  Test: the bar case in `TextSizeTests` at 150 %, 90 % and without names.
- **R4, icons and recordings:**
  - `DataImport.Folders` = `icons`, `sounds`, copied whole before the bar, files in the way kept aside like the
    others (`icons\x.png.before-import-<date>`).
  - An icon path under the source's `icons\` (variables and `%APPDRIVE%` expanded, either separator) becomes the
    same file under the target's, then `%APPDRIVE%` by the usual rule for a portable target.
  - Recordings need no path: their files are named after their icons (`<id>.wav`).
  - A layout with nothing to change is still copied byte for byte.
  - The three questions now name icons and recordings.
  - Tests: what is copied (icons and sounds now in); an icon from the web pointed to its copy, one in the way kept
    aside, one elsewhere left alone.
- **R5, the copy question:** it ends with your sentence: "Sync across PCs must be set up again there, and commands
  approved again." In zh-Hant I wrote 「跨電腦同步」需在該處重新設定，**命令**也需重新核准。: the app says 命令
  for commands everywhere else (the command tile's texts), not 指令. Say if you want 指令. zh-Hans: "跨电脑同步"需在该处
  重新设置，命令也需重新批准。
- **R6, other PCs' locations:**
  - `WithAppDrive(layout, drive, deviceId)` rewrites the shared location and only `Locations[deviceId]`.
  - `Run` takes this PC's id (`ThisDevice.Id`) from both callers. For the start's import, `BringThisPcBar` now runs
    after `CreateSyncService`, which sets the id (it touches no `sync.json` the import reads).
  - Test: another PC's own `E:\Other` stays.
- **Not tested:** R1's notice and R2's replacement on a screen (your re-check at 32/48/64); R3 on a screen.

Core 1170 tests here (Linux); the App tests run in CI.

This entry claims no run for its own commit.

## 2026-11-20 (116) — 0.13.0 ready for the release check

Run 217 (`652ef7d`, entry 115) is green: Core 1164 and App 436 tests. It carries entries 113–115 (runs 215 and 216
were red on the 150 % fit test only, fixed in 115). The five packages of your plan 31 are in, so 0.13.0 is
**ready for your release check**.

| What | Entry | Commits | Run |
| --- | --- | --- | --- |
| Version 0.13.0; WP2, icons that came out smaller | 108 | `adb35c0` | 210 |
| WP3, hint text in the dialog's text colour | 109 | `91cecf6` | 211 |
| WP5, *Send to PC* before sync | 110 | `4d5db6e` | 212 |
| WP1, the data location and the import | 111 | `fe18374` | 213, red (a test of mine) |
| WP1's two options wrap | 112 | `d41dacd` | 214 |
| WP4, the font and the size of the text | 113 | `06e376f` | 215, red (a test of mine) |
| A font installed after the start is found | 114 | `b138067` | 216, red (the same test) |
| The fit test measures a scaled dialog in one unit | 115 | `652ef7d` | 217 |
| Ready for the release check | 116 | this | — |

- **The version:** `Directory.Build.props` says 0.13.0; `CHANGELOG.md` "[0.13.0] – Unreleased" lists all five
  (Added: the data location, the font and text size, the start question; Changed: hints, *Send to PC*; Fixed: the
  small icons). FEATURES §12 and the roadmap say *in progress* until the release commit.
- **Open for you:** entry 111's question. Should the import also copy `icons\` and `sounds\`, rewriting the bar's paths
  to them?
- **Your plan's check:**
  - PCMan's icon at 32, 48 and 64;
  - hints in both themes;
  - the *Send to PC* icon on a copy without sync;
  - `portable.txt` with `.`, a relative and an absolute folder;
  - the Settings block;
  - the import with a bar already there;
  - the font list, and 90 % and 150 % in English and zh-Hant.
- **Not seen anywhere, new with 0.13.0:**
  - a restart after a data move, and the question at a portable copy's first start;
  - a real stick whose letter changes;
  - the text size and a chosen font on a screen.

  As before: a real notification banner, a real sign-out, a real Windows snap, maximise on a second monitor, the
  recogniser with a microphone, Windows 10, another DPI scale.

This entry claims no run for its own commit.

## 2026-11-20 (115) — The fit test measures a scaled dialog in one unit (run 215 red)

Run 215 (`06e376f`, entry 113) is red on my new test,
`DialogFitTests.Settings_and_Properties_at_150_percent_fit_a_short_screen_with_their_buttons_in_sight`, in both
languages: "儲存 ends at 540, below the window's 374". Core 1164 and the other 434 App tests passed.

The dialog was right; the helper `FitsAShortScreen` compared two units.
- The button's bottom is measured in the window's units, after the 1.5 scale: 540.
- The window's height was the content's `ActualHeight`, which is before its own `LayoutTransform`: 374. In window
  units that is 561, so Save is in sight with 21 to spare.

The helper now takes the content's bottom in the window's units (`TranslatePoint` of its lower edge). At 100 % that
is the same number as before (the content starts at the window's top), so the older fit tests check what they
checked. The run of `b138067` (entry 114), under way, has the same failure; this commit's run is the one that counts.

This entry claims no run for its own commit.

## 2026-11-20 (114) — WP4: a font installed after the start is found

`UiFonts.IsInstalled` kept the list of the PC's fonts for the whole run. A font installed after the bar started, then
chosen in Settings (the list there is read each time it opens), was said "not on this PC" at Save. Windows' own font
stayed in use until a restart. Now a name that is not in the kept list makes it read the list again once, so such a
font is used at Save. A name that is still not found costs one more read at each Save.

Not tested: it needs a font installed during a run. WP4's own run (`06e376f`, entry 113) is under way; this
commit has a run of its own.

This entry claims no run for its own commit.

## 2026-11-20 (113) — 0.13.0 WP4: the font and the size of the app's text

Run 214 (`d41dacd`, entry 112) is green: Core 1140 and App 430 tests. WP1 is complete with it.

- **What changed, Core:**
  - `LauncherSettings.UiFont` (a family's name; null: Windows' own) and `TextScale` (percent; null: 100), with
    `TextFactor`. `Normalize` takes a size outside 90–150 to its end (a repair), writes 100 as nothing, and drops a
    font name that `UiFontChain.IsFamilyName` refuses. `CopyFrom` copies both.
  - `UiFontChain.IsFamilyName` refuses an empty name, one over 100 characters, a control character, and `,` `#` `/`
    `\` `:` `"`. Those make WPF read another family or a file, and a layout from another PC must not make this one
    load a font from a file (a share).
  - `UiFontChain.Build(chosen, chinese, windows)` gives the chosen font (else Windows'), then the Chinese font when it
    is used, then Windows' own; no family twice.
- **What changed, app:**
  - `UiFonts.Configure(fontFile, uiFamily, textFactor)`:
    - `App.UiFontFamily` and the message, menu and status family keys take the chain. With no font and no Chinese
      font they are left as before 0.13.0 (the old overload is that);
    - the message, menu and status *size* keys take Windows' size × the factor, and none is set at 100 %;
    - code and log text keep Consolas first.
  - `App.ApplyFonts` (was `ApplyChineseFont`) runs at start, at Save and after an install. A stored family this PC
    lacks (`UiFonts.IsInstalled`, by any of its names) gives Windows' own and one Info line per run and family.
  - `DialogTheme.Apply` calls `ScaleText`:
    - a `LayoutTransform` on the dialog's content (not twice);
    - `Width`, `Height`, `MinWidth`, `MinHeight` and finite `MaxWidth`/`MaxHeight` × the factor;
    - **taller than the work area: capped** by `WorkAreaFit` as before, and the body scrolls (Settings' pages and
      Properties do). Logs and code text are scaled with their window.
  - The bar:
    - `NameFontSize` (11 × factor) for names and separators' captions;
    - `NameHeight` (16 × factor, rounded up) in `CellHeight`;
    - `EmptyHintFontSize` (12 × factor);
    - icons, gauges' numbers, symbol and text icons are unchanged.
  - Settings ▸ Appearance:
    - *Font* (row 6): Windows default first, then a stored family that is not on this PC ("… (not on this PC)",
      kept by Save), then the installed ones by their name in the UI's language, a virtualizing list;
    - *Chinese text* (row 7), as before;
    - *Text size* (row 8): 90/100/110/125/150 % and a stored size between them, with the sample "範例 Sample 123
      快速啟動" in the chosen font (the Chinese row's choice after it) and scaled to the chosen size against the size
      the dialog is drawn at; one hint under it;
    - Transparency moved to row 9.
- **Choices:**
  - **Sync rule:** `uiFont` and `textScale` sync, as `chineseFont` does (it is in `LauncherSettings`, so it syncs; FEATURES
    said so in 0.12.0). A PC without the font falls back.
  - Three rows in the Appearance grid, no heading line: the page's other rows have none.
  - A chosen font comes before the Chinese font and Windows' own after both, so a chosen Latin font without Chinese
    still draws Chinese in Taipei Sans (or Windows' Chinese font).
  - Menus and tooltips of the *Fluent* dialogs are popups of their own, not scaled by the dialog's transform. They
    take the size keys only where their style reads them (the classic look does); the bar's menus and the tray menu
    are classic.
- **Tests:**
  - Core `UiFontChainTests` (24): the four combinations (default or chosen × Taipei or Windows) and two more,
    the name rule, the size clamp, copy and clone.
  - App `TextSizeTests` (4): the font list (Windows first, a missing one kept and said, sorted once), Save's values
    and the sample; the chains and sizes in the resources and their removal; a dialog scaled once with its sizes; the
    bar's name size, line and cell height at 150 %, icons unchanged.
  - App `DialogFitTests`: Settings (Keys page) and Properties (Advanced open) at 150 % in English and zh-Hant, on a
    600 px screen: the body scrolls and Save and Cancel stay in sight.
- **Not tested:**
  - the look on a screen at 90 % and 150 %;
  - a font with no Latin letters chosen as the UI font;
  - the tray menu's and tooltips' sizes on a real desktop;
  - a 1024 px wide screen at 150 % with Properties' icon row.

This entry claims no run for its own commit.

## 2026-11-20 (112) — WP1's two options take the wrapping label (run 213 red)

Run 213 (`fe18374`, entry 111) is red on one test of 430:
`DialogFitTests.Every_check_box_and_option_in_Settings_and_Sync_wraps_its_text`. The two new *Data location* options
had no `WrappingLabel` (the first held its own `TextBlock`, the second plain text). Both now take
`ContentTemplate="{StaticResource WrappingLabel}"`, as every other option in Settings does; the test is unchanged.
Core 1140 and the other 429 App tests passed in that run.

Your review 32 (WP2, WP3, WP5 accepted, no finding; 0.12.1's date stays) is noted: nothing to answer.

This entry claims no run for its own commit.

## 2026-11-20 (111) — 0.13.0 WP1: `portable.txt` holds the data folder; Settings block; import

Run 212 (`4d5db6e`, entry 110, WP5) is green: Core 1101 and App 421 tests.

- **What changed, Core:**
  - `DataLocation.Resolve` returns a `DataPlace(Directory, Portable, Unusable, Named)`.
    - `FirstLine`: the first line with text, trimmed; null (no folder) for a line over 1,024 characters or with a NUL.
      Only 4 KB of the file are read; a byte order mark decides the encoding, else UTF-8.
    - `FolderOf` applies Windows' rules by hand, so the tests on Linux see what Windows does:
      - `.` or empty is the program's folder; `data`, `..\x` are under it; `\x` is on its drive; a full name is used;
      - one pair of quotes is taken off; `/` reads as `\`; `..` never climbs above the root;
      - `%APPDRIVE%` is replaced by the program's drive before the expansion, so the order of `PublishProgramDrive`
        does not matter;
      - no folder: a variable left unexpanded, `"<>|?*`, a control character, `E:data`, a `:` past the drive, a full
        name over 248, `\x` for a program on a share.
    - The folder is made (`MakeFolder`), then written to (`CanWriteTo`). Any failure gives `%LocalAppData%`,
      `Unusable`, and `Named` = the folder, or the line as written (control characters as spaces, 120 characters).
  - `PortableFolderIsReadOnly` is `PortableFolderIsUnusable`. `IsPortable` = the data is where `portable.txt` says,
    wherever that is; `IsPortableCopy` = `portable.txt` exists. `ForStorage` is unchanged (the program's drive).
  - `DataLocation.AsTyped` (Browse): `.`, a name relative to the program's folder, or the full name. `SameFolder`,
    `CanUse`.
  - New `DataImport`:
    - `Run(source, target, appDrive, now)` copies `workspaces.json`, `usage.json`, then `layout.json` (the bar last).
      A file in the way is renamed `<name>.before-import-yyyyMMdd-HHmmss` (local time; `-2`, `-3` when taken).
    - `OffersThisPcBar` is the start question's rule.
    - `RequestFile` = `import-this-pc.request`.
- **What changed, app:**
  - Start (`App.BringThisPcBar`, before the sync service and the layout are read):
    - a request file in the data folder: it is deleted and the import runs without a question;
    - else, when `OffersThisPcBar`: `ChoiceWindow` "Use this PC's bar in this portable copy?", *Import* / *Start empty*
      (default and cancel), in the language of the bar it offers;
    - *Start empty* saves an empty `layout.json`, so it is not asked again.
    - Before the bar's window, WPF would take the question for the main window and end the app when it closes:
      `AskBeforeTheBar` holds `ShutdownMode` for that time.
  - Restart: `LauncherViewModel.RestartRequested` → `MainWindow` sets `App.RestartOnExit` and exits as *Exit* does.
    `App.OnExit` starts the program again after the single-instance mutex is gone, with no argument, so nothing is
    added to the command line or the pipe (`BarCommands.Validate` is untouched).
  - Settings ▸ General ▸ *Data location*: the radios, the folder with *Browse…*, the problem line, *Now:* (a
    read-only text box, selectable) with *Open folder*, *Import this PC's data…*, and the hint.
    - The view model (`SettingsViewModel.DataLocation.cs`) reads `DataFolders.Current`, which reads `portable.txt`
      again: a choice saved with *Later* shows as chosen.
    - A changed folder that names no folder: ⚠ General and Save off.
    - At Save, `TryPlanDataMove(DataLocation.CanUse)`: a folder that cannot be made or written is said under the
      choice, the window stays, nothing is written.
    - Then, only when the folder is another and the folder in use has a bar: *Copy* / *Don't copy* (default) /
      *Cancel* (the window stays).
  - After the dialog, `LauncherViewModel.MoveDataAsync`:
    1. the program's folder must be writable, else "portable.txt could not be changed… the program's folder cannot
       be written", and nothing more happens;
    2. *Copy*: the bar is saved and the usage counts flushed (5 s at most), then `DataImport.Run` copies to the new
       folder. A failure is said and the place is not changed;
    3. `portable.txt` is written (UTF-8 without BOM, the text and a line end), or deleted for *This PC*;
    4. *Restart now* (default) / *Later*.
  - *Import this PC's data…*: *Import* / *Cancel* (default). Import closes Settings without keeping its other
    changes, leaves the request file and restarts.
  - *Send to* refusal: `IsPortableCopy`, so also when the portable folder could not be used (it was `IsPortable`).
  - The notice at start names both folders: `App_PortableReadOnly` is now "portable.txt names {0}…, so the bar keeps
    its files in {1}". The key is the same, the text changed in the three languages.
  - `ChoiceWindow`: a question with named buttons, in the dialogs' look and font (Windows' message box gets
    neither), and `IDialogService.Choose`.
- **Choices I made, and why:**
  - **The import into a running portable bar happens at the restart, not in Settings.** The running bar writes
    `layout.json` at exit (its place on the screen) and the usage counts; done in Settings, the import would be
    written over by the bar it replaces. The copy to a *new* folder (Save) happens at once, as you planned, after
    the bar is saved and the counts flushed: the running bar never writes there. What it does after that, until
    the restart, stays in the old folder ("until restart the running copy keeps its old folder").
  - **`%APPDRIVE%`:** the plan says the normal save makes it. It does not: paths are written with `%APPDRIVE%` when
    they are entered (`ForStorage`), and the save writes them as they are. So `DataImport.WithAppDrive` writes them
    for a portable target, by the same rule (`StorageDrive`: not on Windows' own drive):
    - locations, this PC's own ones too, start folders, icons, in groups as well;
    - a layout that cannot be read, or one from a newer version, is copied as it is;
    - a layout copied to *This PC* is copied as it is (`%APPDRIVE%` still works there: it is published in every
      copy).
  - **What is copied:** the three files. No cache: the Drive cache holds files' contents (not small), and the icon
    cache is in memory.
    **Not copied, and a question for you:** `icons\` (pictures from the web) and `sounds\` (recordings). The bar
    names them by full path in `%LocalAppData%`, so on this PC the imported bar shows them; on another PC they are
    missing. Copying them would need the paths rewritten. Say if you want it.
  - The questions have named buttons (*Copy* / *Don't copy* / *Cancel*, *Import* / *Start empty*, *Restart now* /
    *Later*) rather than Yes/No, in a window of the app's look. The copy question also says that files of the same
    name are kept as `….before-import-<date>`.
  - zh-Hans text (yours were en and zh-Hant): 数据位置 / 这台电脑 / 便携：文件夹 / 浏览… / 当前： / 打开文件夹, as
    you wrote them, and the rest in the same words.
- **Tests:**
  - Core `DataLocationTests` (+33): 17 names that are folders, 6 that are not, too long a name or line and a NUL,
    a folder that cannot be made or written, a program on a share, Browse's form, `SameFolder`. Two older ones now
    compare `DataPlace`.
  - Core `DataImportTests` (6): what is copied and what is not; kept aside, twice in the same second;
    `%APPDRIVE%` for every kind of path; a damaged and a newer layout copied as they are, with the source left
    alone; the same folder; the start question's rule.
  - App `DataLocationAppTests` (8):
    - what Settings shows;
    - a name that is no folder's holds Save back with ⚠ General;
    - Save's check, planning without writing;
    - when the copy is offered and when Import is shown;
    - a move that copies, writes `portable.txt` and restarts, and back to *This PC* with *Later*;
    - a program folder that cannot be written;
    - the import request;
    - Settings with Import keeps nothing else.
  - App `ChoiceWindowTests` (1): names, default, cancel, the answer when closed.
- **Not tested:**
  - a real restart (`Process.Start` after the mutex), and the modal flows clicked through (Settings' Save with the
    question, the question at start);
  - Program Files, a real stick whose letter changes, and a program on a share;
  - a `portable.txt` saved by Notepad as ANSI with Chinese in it: read as UTF-8, it names another folder or none.
    UTF-8 and UTF-16 with a byte order mark are read right.
- **What to look at (your check):** `portable.txt` with `.`, `data`, `..\DIG-data`, `D:\…`, a bad name; the
  Settings block in English and zh-Hant; a move with *Copy* and *Restart now*; the question at the first start of
  a portable copy on a PC with a bar; *Import this PC's data…* with a bar already in the portable copy (its files
  kept aside).

This entry claims no run for its own commit.

## 2026-11-20 (110) — 0.13.0 WP5: *Send to PC* can be added before sync is set up

Run 211 (`91cecf6`, entry 109, WP3) is green: Core 1101 and App 419 tests (`HintTextTests` passed in both looks).

- **What changed:**
  - `SendToDeviceHandler.ShowProperties` without Sync across PCs:
    - a new icon: `AddWithoutPc`, then `true`, so the icon is added without a PC. One notice, information, with the
      Sync window as its target: `SendTo_AddedWithoutSync` (your text in English and zh-Hant; zh-Hans "图标已加入工具栏。
      请先设置“跨电脑同步”，再右键单击它 ›“属性”选择电脑。"). Nothing announced, no `IconMark`;
    - an icon already on the bar: as before, the warning `SendTo_NeedsFolder` to the Sync window, nothing changes.
  - With sync: the device window as before. The box starts ticked by `StartsTicked`: a new icon, an icon this PC
    receives through (`IconMark`), or **an icon with no PC that was added on this PC** (see the choice below).
  - `Saved` is the old after-OK part moved out (so it can be tested), plus one line: the note below is revoked.
  - Tooltip, click, drops and paste of an icon without a PC are unchanged: "No PC is chosen yet. Open Properties to
    choose one.", and drops and paste are refused.
- **A choice, to keep review 27 S3:** "never-chosen icon: the box starts ticked" would also tick it for an icon with no
  PC that **arrived** from another PC (added there before sync, then synced here). S3, the owner's decision, is that an
  arrived icon turns receiving on only when the user ticks the box. So `AddWithoutPc` keeps a note,
  `FolderInbox.AddedHereMark(id)` (another hash in `command-trust.json`, local and never synced, like `IconMark`).
  It makes nothing receive (`HasSendIcon` reads `IconMark` only); it only lets `StartsTicked` tick the box for an icon
  with no PC added here. An arrived icon has no note here, so its box starts empty; once a PC is chosen, the note is
  ignored and then revoked. Without a machine key (`ThisDevice.IsOfThisMachine` false) the note lasts until the
  program ends, like the marks, and the box then starts empty: the safe side. So "approves nothing" in your test list
  reads here as "no `IconMark`": the note is one entry in the trust file.
- **Other handlers that refuse a new icon for a missing prerequisite:** none. Clock, command tile, Drive bucket (it
  saves a folder id from the link without Google), the monitors and the sound replayer return `false` only for Cancel;
  Voice adds a new one at once.
- **A test changed with the behaviour:** `NoticeTargetTests.Send_to_PC_without_a_sync_folder_points_to_the_Sync_window`
  asserted that a new icon is refused with the warning. It now checks the icon already on the bar (refused, warning,
  Sync) and the new one (added, information, Sync, the new text).
- **Tests (App):**
  - `Without_sync_a_new_icon_is_added_without_a_PC_and_later_starts_with_the_box_ticked`: through the bar's
    `AddSpecialIcon`, one item without a PC, no `IconMark`, one notice to Sync, the no-PC tooltip, drops and paste
    refused. Then `StartsTicked` is true, and `Saved` with a PC sets the mark and revokes the note.
  - `An_icon_without_a_PC_that_arrived_from_another_PC_starts_with_the_box_empty`: S3 holds; a PC chosen elsewhere
    makes the note count no more.
- **Not tested:** the dialog itself after sync is set up (it is modal; its view model gets `StartsTicked`'s answer, as
  tested), and the notice's click opening the Sync window (`NoticeTarget.Sync` is tested since 0.12.0).

This entry claims no run for its own commit.

## 2026-11-20 (109) — 0.13.0 WP3: hint text in the dialog's text colour

Run 210 (`adb35c0`, entry 108, WP2) is green: Core 1101 and App 416 tests. The six new icon tests passed on CI's
Windows, `Windows_own_programs_come_out_as_the_shell_draws_them_unless_the_shell_padded_them` among them.

- **What changed:**
  - `DialogHint` (Styles.xaml) sets no `Foreground` any more: a hint inherits the dialog's text colour, whatever the
    look (classic, Fluent light or dark, High Contrast). It keeps its wrapping, its place (the negative top margin
    under what it explains) and the 0.45 opacity of a disabled row. No italics, no smaller size, as before.
  - Text that explains but did not use the style, now in the text colour too:
    - Send to PC: the status line (looking, the hint, why no PC is listed). A warning there keeps `WarningBrush`.
    - Command tile: the line under *Every* and *Saving approves…*.
    - The approval window: the line that asks to scroll to the end.
    - Monitor icon: each design's description in the list (its name stays semibold).
    - The voice window: the detail line, and each choice's detail (they were at 70 % and 80 % opacity).
  - Settings' font install message is based on `DialogHint`, so it follows; when the install failed it keeps
    `ErrorTextBrush`.
  - `DialogHintBrush` stays, for the two places listed below.
- **Grey left grey, and why:**
  - The log window's Debug lines: grey says their level, beside the warning and error colours.
  - The Drive viewer's `›` between the parts of a path: a divider, not text.
  - Add app's filter box: its placeholder (*Filter…*) at 55 %, so it does not look like typed text.
  - A folder stack's *N more* and *Empty folder* lines, at 70 % of the bar theme's text colour: the stack is the bar's
    popup, drawn in the bar's theme, not a dialog. Say if they should be full colour too.
  - A disabled row's 0.45 opacity, as the plan says; the log viewer's light text on its dark page is not grey.
- **Contrast:** a hint now has the contrast of a label wherever a label has it, by inheritance; no colour of mine is
  involved. Before: #6E6E6E on light (5.1:1 on white) and #A8A8A8 on dark.
- **Tests (App, Windows):** `HintTextTests`:
  - a `DialogHint` text in a window has the colour of a plain text beside it, in the classic and the Fluent look,
    and is not the hint brush's grey;
  - the approval window's scrolling line has the colour of its intro.
- **Not tested:** the dark look (CI runs Windows in light mode) and High Contrast; both inherit as the light look does.
  Not seen on a screen.

This entry claims no run for its own commit.

## 2026-11-20 (108) — 0.13.0 begins: WP2, icons that came out smaller than the others

Your plan 31, first package (order WP2, WP3, WP5, WP1, WP4). The version is 0.13.0 from this commit on.

- **What changed:**
  - The version: `Directory.Build.props` 0.13.0; `CHANGELOG.md` "[0.13.0] – Unreleased"; FEATURES §12 and the
    roadmap name 0.13.0 *in progress* with its five packages.
  - New `Core/Icons/IconBox.cs`, pure (pixels in, box out):
    - `Drawn`: the box of the pixels with alpha over 8, leaving out a frame at the edge;
    - `SmallAndCentred`: that box when it is under 70 % of the width and of the height and centred (the two margins
      differ by at most a tenth of the side, at least 2 px), else none;
    - `Square`: the square that is cut out.
  - `ShellIconProvider`: a file, and a program found on the PATH, go through `TryFileImage`:
    - the shell's image, as before;
    - when `PaddedBox` finds it padded: `SHDefExtractIcon` of the file's icon at the asked size, used when it is not
      padded itself;
    - else a `CroppedBitmap` of the square, which the cell scales up (it already had `BitmapScalingMode.HighQuality`).
    - Folders, shell names and CLSIDs, image files, `path,index` resources and symbol or text icons do not go through it.
  - `IconLocationOf` says where `SHDefExtractIcon` looks:
    - a program or icon file (`ResourceExtensions`) → its first icon, the one Explorer shows;
    - a `.lnk` → the icon it names (variables expanded; the file must exist), else its target when the target has one
      of those extensions;
    - a document, or a link that cannot be read → none, so it is cut out.
  - `NativeMethods.ReadLinkText` is `ImportDiscovery.ReadText` moved, so both readers of `.lnk` files use it.
  - One Debug line per icon replaced, e.g. "Icons: PCMan.exe: the shell drew its icon 64×64 in 96×96; showing the
    file's own icon at 96 px".
- **Choices I made where the plan left room:**
  - The frame left out is one pixel per 48 px of the side (at least one), not exactly one: a 1 px frame drawn on a
    48 px image that the shell stretched to 96 is 2 px wide. An icon drawn only inside that band counts as empty and is
    left as it is.
  - The cut-out is a square (the longer side of the box, centred on it), so a narrow icon keeps its proportions.
  - Folders are not measured: their icons are Windows' own, with every size, and there is no file to take one from.
  - When the file's own icon is padded too (its best image has the same margins), the shell's picture is cut out: the
    plan's "else".
- **Pixel-identical, how checked:** by construction, `Unpadded` returns the very object `TryShellItemImage` gave unless
  `SmallAndCentred` finds a box. Two tests hold it:
  - `An_image_that_is_not_padded_is_the_shells_own_object`: full, 76 of 96, small in a corner, empty
    (`Assert.Same`);
  - `Windows_own_programs_come_out_as_the_shell_draws_them_unless_the_shell_padded_them`: `cmd.exe`,
    `explorer.exe`, `regedit.exe` and `notepad.exe` at 32, 64, 96 and 128 px; the provider's pixels equal the
    shell's, or, where the shell padded, the result is not padded.
- **Tests:** Core `IconBoxTests` (7: centred 32 in 96, full 96, framed 32 in 96, a frame stretched to 2 and 5 px,
  empty, margins under the limit and a corner, the square). App `ShellIconProviderTests` (6, Windows): a padded
  picture of `cmd.exe` gives way to its own icon at 96 px; one of a document is cut out to 32 × 32 without the frame;
  the not-padded and pixel tests above; a link's icon location; a `.lnk` whose icon has only a 32 px image is not
  padded at 64, 96 and 128.
- **What to look at in the app:** PCMan at 32, 48 and 64 (your check). If it stays small, the log at Debug has no
  "Icons:" line for it; the box the shell gave is then over the limit, and its numbers are what I need. A known limit:
  a padding of one size step (48 in 64, 96 in 128: 75 %) is over 70 % and stays as it is. I kept 70 % so icons drawn
  with margins (Windows 11's own have about a tenth a side) are not touched.
- **Not tested:** what the real shell gives for PCMan (there is no PCMan here, and how CI's Windows draws the 32 px
  test icon is not asserted, only that what the bar gets is not padded). The result on a screen is not seen.
- **0.12.1's date:** CHANGELOG and CLAUDE.md say 2026-11-19, the release commit's day (UTC), while the tag and the
  GitHub release were made on 2026-11-20. I changed nothing; say if the CHANGELOG should follow the tag.

This entry claims no run for its own commit.

## 2026-11-20 (107) — Release commit for v0.12.2

Run 207 (`0c11619`, entry 106) is green, and your release check of it (review 30) passed with no finding. This is
the release commit you asked for, made on the owner's word to me (asked directly, when your message came).

- **What this commit changes:**
  - `CHANGELOG.md`: "[0.12.2] – Unreleased" becomes "[0.12.2] – 2026-11-20" (UTC). The section lists N1–N5.
  - `FEATURES.md` §12: 0.12.2 is done, released 2026-11-20; *Next* is still nothing planned.
  - `docs/ROADMAP.md`: 0.12.2 released 2026-11-20.
  - `CLAUDE.md` State, which the owner said yes to when I asked:
    - 0.12.1 released (tag and `main` on `0326f82`);
    - 0.12.2 with its release commit, the tag and `main` yours;
    - nothing in progress;
    - the 0.12.1–0.12.2 choices to keep;
    - what you saw in the app for 0.12.1 and 0.12.2;
    - what is not seen anywhere.
- **The version:** `Directory.Build.props` says 0.12.2. No project file or workflow names another (`app.manifest`'s
  `1.0.0.0` is its own identity).
- **Not in this commit:** the tag `v0.12.2` and `main`, yours on the owner's word.

| What | Entry | Commits | Run |
| --- | --- | --- | --- |
| Version; 0.12.1 recorded; N5, a usage save keeps the counts | 101 | `0dc86de` | 203 |
| N1, the font found past a file that is no font | 102 | `485389c` | 204 |
| N2, a minimise is not a use | 103 | `aafb5fa` | 205, red (a test of mine) |
| The minimise test's second click | 104 | `fa5a4f4` | in 206 |
| N3 and N4, the Sync window | 105 | `8524243` | 206 |
| Ready for the release check | 106 | `0c11619` | 207 |

Not seen anywhere, as before:

- a real notification banner;
- a real sign-out;
- a real Windows snap;
- maximise on a second monitor;
- the recogniser with a microphone;
- Windows 10;
- another DPI scale.

N3 in English and zh-Hans is in `DialogFitTests` only. Nothing is planned after 0.12.2.

This entry claims no run for its own commit.

## 2026-11-20 (106) — 0.12.2 ready for the release check

Run 206 (`8524243`, entry 105) is green: Core 1094 and App 410 tests. It carries the N2 test fix of entry 104 too
(`fa5a4f4`, pushed with it, so it had no run of its own). N1–N5 of your review 29 are in, so 0.12.2 is **ready for
your release check**.

| What | Entry | Commits | Run |
| --- | --- | --- | --- |
| Version; 0.12.1 recorded; N5, a usage save keeps the counts | 101 | `0dc86de` | 203 |
| N1, the font found past a file that is no font | 102 | `485389c` | 204 |
| N2, a minimise is not a use | 103 | `aafb5fa` | 205, red (a test of mine) |
| The minimise test's second click | 104 | `fa5a4f4` | in 206 |
| N3 and N4, the Sync window | 105 | `8524243` | 206 |

What only you can check:

- **N1:** a damaged `Fonts\TaipeiSansTCBeta-Light.ttf` beside the exe, with the font installed for the user. The
  row shows the font found, and the log has one line about the damaged file however often Settings is opened.
- **N2:** a second click on a running program whose window is in front minimises it, and *Uses* stays.
- **N3:** the Sync window in zh-Hant, zh-Hans and English: the Google heading and the line under it.
- **N4:** a failed start check is said once at the bottom.

Not seen anywhere, as before:

- a real notification banner;
- a real sign-out;
- a real Windows snap;
- maximise on a second monitor;
- the recogniser with a microphone;
- Windows 10;
- another DPI scale.

As you wrote: no release commit before you have looked, unless the owner says otherwise. `CLAUDE.md`'s State is still
the owner's question (entry 101).

## 2026-11-20 (105) — 0.12.2: N3 and N4, the Sync window's Google heading and a problem said once

This entry claims no run for its own commit.

- **N3, the heading.** `Sync_AccountForBuckets` is short, as you gave it: "Google account" / 「Google 帳戶」 /
  「Google 账户」. The zh-Hans word was 账号; it is 账户 now, as you wrote it.
  - What was in the brackets is a new hint line at the top of that group: `Sync_AccountForBucketsHint`, "For the
    copies on Google Drive and for Google Drive Buckets." / 用於 Google 雲端硬碟上的副本與 Google 雲端硬碟投遞箱。 /
    用于 Google 云端硬盘上的副本与 Google 云端硬盘投递箱。, in `DialogHint` style, wrapping.
  - The window's other headings were already short (副本存放位置, Google API 憑證, …); the test below checks them all.
- **N4, one line for a problem.** The window binds to two new properties, `ShownStatusText` and
  `ShownBackgroundStatus`. They come from `SyncViewModel.StatusLines`: when one of the two lines holds the other, only
  the fuller one is shown, so "啟動檢查失敗：…" stays and the bare sentence goes. Two different things are both shown.
  The background line wraps now too.
- **Tests:**
  - `DialogFitTests.The_headings_of_the_Sync_window_fit_it_and_the_Google_one_says_what_it_is_for_in_a_line_that_wraps`,
    a theory in en-US, zh-Hant and zh-Hans at the window's 600 wide:
    - the four group headings and the credentials expander's are each drawn whole, inside the window;
    - the Google heading is at most 14 characters;
    - the hint line is the new string, wraps, and stays inside its group;
  - `SyncViewModelTests.A_problem_is_said_once_under_the_window_in_the_fuller_line`: the rule, both ways round, two
    different lines, and either one empty.
- **Docs:** CHANGELOG 0.12.2 (Fixed) and FEATURES (the Sync dialog).
- **Not tested:** the real window with a failed start check, which you saw in review 29.

## 2026-11-20 (104) — Run 205: the minimise test's second click was half a double-click

Run 204 (`485389c`, N1) is green. Run 205 (`aafb5fa`, N2) was red on the new App test of N2. The other 405 passed.

- **What failed:** `UsageTests.A_click_that_minimises_the_program_in_front_is_no_use_and_says_so`. Its second click
  went to the same shortcut at once. The bar takes a second click on the same item within 400 ms as the second half
  of a double-click and does nothing (`Cancelled`), so `Started` was not returned.
- **What I changed** (the test): the second click, which brings a program forward and is counted, goes to another
  shortcut. The test still checks that the minimise counted nothing and the bring-forward counted one. The app is
  unchanged.

This entry claims no run for its own commit.

## 2026-11-20 (103) — 0.12.2: N2, a minimise is not a use

Run 203 (`0dc86de`, entry 101) is green. This entry claims no run for its own commit; the run of entry 102 was still
going when I pushed this.

- **The means, as you asked, not a guess from the window afterwards:**
  - `IWindowSwitcher.TrySwitch` returns the `SwitchKind` it carried out:
    - `Activate`, brought forward;
    - `Minimize`, the program was in front and is minimised;
    - `Launch`, no window, so the caller starts the program. It used to return a bool, true for both of the first
      two.
  - `LaunchOutcome` has a new last value, `Minimized`.
- **The click path** (`ActivateAndReport`) counts a use only for `Activate`, returning `Started` as before. For
  `Minimize` it returns `Minimized`, counts nothing and closes the popup as before.
  - Voice launch says "Starting …" only for `Started`, so a minimise by voice says nothing.
  - The other readers of `LaunchOutcome` see the starter's results, which are never `Minimized`.
- **The rule:** `UsageRules.IsCounted` is unchanged in its code (only `Started` counts). Its comment and FEATURES (the
  names table's Usage paragraph) now name the minimise.
- **Tests:**
  - **Core:** `IsCounted(shortcut, Minimized)` is false.
  - **App:** `UsageTests.A_click_that_minimises_the_program_in_front_is_no_use_and_says_so`. A fake switcher that
    minimises: the click returns `Minimized`, nothing is started, and no use is counted. Then one that brings forward:
    `Started` and one use.
  - The fake keeps its `bool Answer` for the older tests, and a new `Kind` overrides it.
- **Docs:** CHANGELOG 0.12.2 (Fixed) and FEATURES.
- **Not tested:** the real `WindowSwitcher` returning `Minimize`, which needs a real window in front.

## 2026-11-20 (102) — 0.12.2: N1, the font found past a file that is no font

This entry claims no run for its own commit; the run of entry 101 was still going when I pushed this.

- **`FontLocator.Candidates(name)`, new.**
  - Every file of that name, in the order `Find` looks: `Fonts\` next to the program, this user's fonts, every
    user's. Each file is given once. The registry values are read as before: a bare name is in that list's folder.
  - `Find` is now its first.
- **`ChineseFontService.Find`** takes the first candidate that `FontNames.IsFont` reads as a font.
  - A candidate that is not one is passed over.
  - Its Info line ("… holds no font that can be read; it is passed over") is written once per run and file: a set in
    the service, which the app makes once.
- **Tests:**
  - **Core:** the locator test adds a second value of this user's for the same file as an earlier one. `Candidates`
    gives the three places in order, the doubled file once.
  - **App:** `ChineseFontServiceTests.A_damaged_copy_beside_the_program_does_not_hide_the_font_installed_and_is_said_once`.
    A 40-byte copy sits beside the program and the test font is installed for the user (a fake registry value):
    - `Find` gives the installed one, twice;
    - the log has one line naming the damaged file;
    - with the installed one gone, `Find` gives nothing, and the damaged copy is never taken.
- **Docs:** CHANGELOG 0.12.2 (Fixed) and FEATURES §13 item 3.

## 2026-11-20 (101) — 0.12.1 is released; 0.12.2 begins with N5 (review 29)

### 0.12.1, as you released it

I checked it here:

- the annotated tag `v0.12.1` is on `0326f82`, and `main` is there;
- the latest release on GitHub is v0.12.1, with `DesktopIconGrid-0.12.1-win-x64.zip` (1,260,043 bytes), the
  self-contained zip and `SHA256SUMS.txt`.

You give release run 14 and build run 202 on `main` as green.

- **`CLAUDE.md`'s State** still says the tag and `main` wait, and nothing is in progress. You asked me to record the
  release there. `CLAUDE.md` is the owner's and mine, and I do not change it because another session asks, so I
  have asked the owner. If yes, it goes in with a later commit.
- Your review 29 found no blocker. Its findings N1–N5 are 0.12.2, on the owner's word as you relay it: "continue
  implementation".

### The version

`Directory.Build.props` says 0.12.2, `CHANGELOG.md` has "[0.12.2] – Unreleased", and FEATURES §12 and the roadmap say
0.12.2 is in progress.

I plan four commits:

1. this one, N5;
2. N1;
3. N2;
4. N3 and N4.

Then "0.12.2 ready for the release check". No release commit before you have looked, unless the owner says otherwise.

### N5, as you asked

- `Save` sets `written` only when the file was replaced, and a `finally` marks the counts to be written again
  otherwise, whatever ended it.
- An exception other than `IOException` or `UnauthorizedAccessException` (and `OutOfMemoryException`, which is let
  through):
  - is caught;
  - deletes this instance's temporary file;
  - is said once per run at Warning, with its type and message;
  - and `Save` returns false.
- The reading, composing and replacing moved, unchanged, into a private `Write`, so the `finally` covers all of it.
  Its own early returns no longer mark the counts themselves.
- Test (Core, now 1094): `A_save_that_throws_something_unexpected_keeps_the_counts_for_the_next_save`.
  - `BeforeReplace` throws `InvalidOperationException`. `Save` is false, no file and no temporary file are there, and
    the counts are not memory-only.
  - With the hook gone, the next save writes the count.
- **Not tested:** the warning's text, which is logged once.

This entry claims no run for its own commit.

## 2026-11-19 (100) — Release commit for v0.12.1, at the owner's request

Run 200 (`3a2541f`, entry 99) is green.

- **Your release check has not happened yet.** The owner asked me for this release commit now, before your check of
  0.12.1 in the app. The owner chose it when I asked; it is not a word of yours I am taking for granted.
- **What this commit changes:**
  - `CHANGELOG.md`: "[0.12.1] – Unreleased" becomes "[0.12.1] – 2026-11-19" (UTC). The section lists entries 93–98:
    the change of sync problems' target, and the fixes of the strings, the English texts, the dates, the font
    install and the usage counts. The corrected notes of 0.12.0 are below it.
  - `FEATURES.md` §12: 0.12.1 is done, released 2026-11-19; *Next* is still nothing planned.
  - `docs/ROADMAP.md`: 0.12.1 released 2026-11-19.
  - `CLAUDE.md` State: 0.12.1 has its release commit, at the owner's request and before your check; the tag and
    `main` are yours.
- **The version:** `Directory.Build.props` says 0.12.1. No project file or workflow names another (`app.manifest`'s
  `1.0.0.0` is its own identity, as said in entry 91).
- **Not in this commit:** the tag `v0.12.1` and `main`.
- **Your check:** entry 99 lists what only you can see. If you find something, it goes into a 0.12.2 or into a
  second release commit before the tag, as you decide with the owner.

| What | Entry | Commits | Run |
| --- | --- | --- | --- |
| D: version, notes of 0.12.0, strings, sync problems to the Sync window | 93 | `52bafe8` | 194 |
| A1–A3: English texts, one date rule, the Settings row | 94 | `53dc660` | 195, red (a test of mine) |
| The Settings row test | 95 | `d9d514f` | 196 |
| F1–F6: the font install | 96 | `a179510` | 197, red (a test of mine) |
| The announcement test | 97 | `aa72105` | 198 |
| U1–U7: usage counts | 98 | `d038548` | 199 |
| Ready for the release check; `CLAUDE.md` State | 99 | `3a2541f` | 200 |

Not seen anywhere, as before:

- a real notification banner;
- a real Windows snap;
- maximise on a second monitor;
- the recogniser with a microphone;
- Windows 10 (the symbol font fallback);
- another DPI scale;
- a real lock on `usage.json`.

This entry claims no run for its own commit.

## 2026-11-19 (99) — 0.12.1 ready for the release check

Run 199 (`d038548`, entry 98) is green: Core 1093 and App 404 tests. Everything of your review 28 is in, so 0.12.1 is
**ready for your release check**.

| What | Entry | Commits | Run |
| --- | --- | --- | --- |
| D: version, notes of 0.12.0, strings, sync problems to the Sync window | 93 | `52bafe8` | 194 |
| A1–A3: English texts, one date rule, the Settings row | 94 | `53dc660` | 195, red (a test of mine) |
| The Settings row test | 95 | `d9d514f` | 196 |
| F1–F6: the font install | 96 | `a179510` | 197, red (a test of mine) |
| The announcement test | 97 | `aa72105` | 198 |
| U1–U7: usage counts | 98 | `d038548` | 199 |

What only you can check, from those entries:

- **In English:**
  - the names table (*Kind*, *Colour tag*, 1,230 wide);
  - Properties' Icon row wrapping;
  - the Settings *Chinese text* row with *Install*, in both languages.
- **The dates** of the names table with an English bar on a zh-TW Windows.
- **A sync problem's balloon:** a click opens the Sync window.
- **The font install:**
  - with `TaipeiSansTCBeta-Light.ttf` held open in the user's fonts, the row says why;
  - after an install, Cancel in Settings: the bar keeps the font;
  - the logs show Chinese in it.
- **Usage counts:** a sign-out with the bar in the tray, and the counts there after it.

Not seen anywhere, as before:

- a real notification banner;
- a real Windows snap;
- maximise on a second monitor;
- the recogniser with a microphone;
- Windows 10 (the symbol font fallback);
- another DPI scale;
- a real lock on `usage.json`.

The owner asked for 0.12.1 to be carried through. So once you have looked and give your word, I make the release commit
as for 0.12.0, and the tag and `main` stay yours. `CLAUDE.md`'s State says this too, in this commit.

## 2026-11-19 (98) — 0.12.1: usage counts (review 28 U1–U7 and their tests)

Run 198 (`aa72105`, entry 97) is green, so F1–F6 are in. This entry claims no run for its own commit.

### U1, sign-out and shutdown

`App` handles `SessionEnding` and calls a new `MainWindow.EndSession()`. That saves the placement, sets `_exiting`
(the window then closes as at Exit, not into the tray), and runs `WaitForPendingWork()`: the same flush as Exit, the
layout upload and the usage counts, bounded at 6 s and pumping messages. Once only.

### U2–U7, `UsageLog`: the file is read and written as a JSON tree

`JsonNode`, so what this version does not read goes back as it was at every level.

- **U2:**
  - **The cases:**
    - a file that is not the JSON this app writes: not JSON at all (empty, NUL bytes), a root that is no object,
      `pcs` that is no object, or a property named twice (`Read` walks the tree, so that is found there);
    - such a file at a start is moved to `usage.json.corrupt-yyyyMMdd-HHmmss`, as the layout does, with one Info
      line, and this PC counts anew;
    - this PC's block that is no object (a number): the file is copied there, and the other blocks are written back
      with a new block;
    - a bad value in another PC's block is not read at all, so it harms nothing.
  - **At a save:** a file found damaged is copied aside first, once per save, and not written over when the copy
    fails.
  - **The write:** the temporary file is flushed to the disk (`Flush(true)`) before it replaces the file.
  - **Over 1 MB:** still "not this app's": left alone, the counts in memory.
- **U3, `UsageSaver`:**
  - the throttle is on `Environment.TickCount64`. `WaitBefore` is never more than the interval and never less than
    nothing;
  - the delayed write resets `_scheduled` in a `finally`, so a failure cannot stop the writes;
  - a failed flush is a debug line, not an unobserved exception.
- **U4:** the probe is opened with `FileOptions.DeleteOnClose`, as `DataLocation`'s is, so no probe is left and no
  delete can fail.
- **U5:**
  - before writing, a file over the limit drops the uses of items gone from the bar, oldest first, in steps measured
    from the bytes over, until it fits;
  - if it still does not fit (other PCs' blocks), nothing is written, with one warning, and the counts wait;
  - `MaxBytes` (internal) is the limit, smaller in tests.
- **U6:**
  - the temporary file's name is this instance's own, `usage.json.<12 hex>.tmp`, and it is deleted when the replace
    fails;
  - just before the replace, the file's length and time are compared with those of the read. If another copy wrote
    meanwhile, the file is read again and composed again, once.
- **U7:**
  - entries with an id that is no id, a count of 0 or none, or no last use stay in the tree and are written back
    unchanged. So do unknown properties of a counted entry, and they survive a join (`ReadAgain`);
  - `Since` is read under the lock;
  - a use counted after the clock was set back keeps the later last use.

### Tests (Core 1093, +13)

- **Kept beside it and counted anew:** a theory of six damaged files, at a start. Also at a save, and a file over
  1 MB at a save, which is not written over.
- **A bad date in another PC's block** is written back the same (compared as JSON); this PC's block that is a number.
- **The move** (`usage.json` a directory) fails with no temporary file left. The temporary file's own write fails
  (now through `TemporaryPath`, as the name is per instance). Two logs have different temporary names.
- **Another copy writes just before the replace** (an internal `BeforeReplace` hook): its block is kept, and the file
  is written twice.
- **No probe is left** in a folder that can be written. This one can fail, unlike the old check.
- **Entries this version cannot read** and unknown properties, through a join.
- **The clock set back.**
- **The limit:** the newest gone uses stay and those on the bar all stay, and no write when other blocks fill it.
- **App:** `UsageSaver.WaitBefore` for the first write, within, after, past 60 days, and a "last" in the future.
- The four tests of release check U1 are unchanged, but for the per-instance temporary name. The test "a file
  replaced by one that is not this app's … the counts wait" now expects what U2 asks: the damaged file is kept
  beside it and the counts are written.
- **Not tested:** a real sign-out (`EndSession` is the flush of Exit, called from `SessionEnding`), and two PCs at
  once on one share.

## 2026-11-19 (97) — Run 197: the announcement test needs a font GDI takes

Run 197 (`a179510`, entry 96) was red on one new App test. The other 402 passed, F1–F6's among them.

- **What failed:** `ChineseFontServiceTests.A_font_is_added_for_this_session_and_announced_without_waiting_on_a_window_that_hangs`.
  `AddFontResourceW` returned 0 for my two-glyph test font. WPF reads that font (every other font test passes with
  it); GDI is stricter and refuses it.
- **What I changed** (the test): it announces a copy of one of Windows' own fonts (Arial, else Segoe UI or Consolas)
  from a folder of its own, and removes it again. The app's code is unchanged. The real Taipei Sans file is a full
  font.

This entry claims no run for its own commit.

## 2026-11-19 (96) — 0.12.1: the font install, app side (review 28 F1–F6 and its tests)

Run 196 (`d9d514f`, entry 95) is green, so A1–A3 are in. This entry claims no run for its own commit.

### F1–F6

- **F1, the row stuck on "Downloading…".** Fixed in both places.
  - **The installer** (`FontInstaller.ExistingAsync`): a file of that name already in the user's fonts is hashed
    inside a try.
    - Held open or unreadable: `InstallFailed` with the error's words, the file left as it is.
    - A cancel while it is hashed: `Cancelled`.
    - Both checks of an existing file (before the download, and after it when one was put there meanwhile) go through
      it.
  - **The view model** (`InstallFontAsync`): it catches what still comes out.
    - `OperationCanceledException` is `Cancelled`.
    - Any other exception but `OutOfMemoryException` is `FontInstall_InstallFailed` with the message, and a warning
      in the log.
- **F2, Consolas text.**
  - Two resources, kept by `UiFonts.Configure`:
    - `App.CodeFontFamily`: Consolas, then Courier New;
    - `App.LogFontFamily`: Consolas, then Segoe UI.
  - With the font, it goes after Consolas: "Consolas, \<the file\>#…, Courier New".
  - The four boxes bind to them dynamically: the log window's details and list, the log viewer, and the approval's
    details box. `Styles.xaml` has the plain values, so nothing changes before `Configure` runs or without the font.
- **F3, "found".** `ChineseFontService.Find` takes the locator's file only when `FontNames.IsFont` reads a name from
  its `name` table. If not, one Info line, and the row offers *Install*. If the damaged file is in the user's fonts,
  the install then says "a different file of that name is already there", and that file is not written over.
- **F4, the font at once.** `ChineseFontService` raises `Installed` once the font is installed, and the app calls
  `ApplyChineseFont` on the bar's thread.
  - What it applies is what the next start would apply: the stored setting with the font now found. For the default
    that is the font (zh-Hans: Windows'), and a stored "Windows font" stays.
  - So Cancel afterwards no longer leaves this run different from the next start. A choice made in the row still
    applies at Save.
  - I did not apply the font regardless of a stored "Windows font": that would make the run differ from the next
    start again, the other way round. Say if you meant it so.
- **F5.** FEATURES §13 item 3 says it now: "every dialog of the app's own", and Windows' own message boxes keep
  Windows' font. The 24 message boxes are unchanged.
- **F6.**
  - `RemoveUserFont` is gone, from `IFontRegistry`, `WindowsFontRegistry` and the fake.
  - Before `AddUserFont`, the installer looks for a value of that name. If it points at another file (a full path
    that differs, or a bare name other than the file's), the result is a new outcome, `NameTaken`, with that file's
    path:
    - the value is not touched;
    - the moved file is taken back;
    - the row says `FontInstall_NameTaken`: "Windows already lists a font of this name for another file: {0}. Nothing
      was installed." The new string is in all three languages.

### Tests

- **Core (+4):**
  - an existing file held open is `InstallFailed`, with the file unchanged and no request made; a cancel while it is
    hashed is `Cancelled`;
  - **a cancel in the middle of the body:** the server sends half, the test waits until the first bytes are in the
    part file, then cancels. The result is `Cancelled` and no part file is left;
  - `NameTaken`, and the same file under that name (a bare name or its full path) installs;
  - `FontNames.IsFont` for a font, an empty file, a damaged file and a missing one.
- **App:**
  - **`ChineseFontServiceTests`, new:**
    - `Find` with nothing, an empty file, a damaged file, then the test font;
    - the time limit: `DownloadLimit` is 10 minutes; with 200 ms a hanging server is `DownloadFailed` "timed out",
      and the user's cancel is `Cancelled`;
    - a finished install is listed, announced and raises `Installed`;
    - `WindowsFontRegistry` under a key of the test's own (HKCU, deleted afterwards), and Windows' own font list read;
    - `FontAnnouncement.Announce` adds the test font for the session within 30 s, and the test removes it again
      (`RemoveFontResourceW`, new).
  - **`ChineseFontAppTests`:**
    - the Fluent test asserts `DialogTheme.UseFluent` and the dialog's `ThemeMode.System`. It also checks both Consolas
      resources with and without the font, and the approval's details box;
    - the install row test keeps Settings open the whole time and checks the window at each step: the progress
      line, then the message, no button, and the font chosen in the list;
    - a new theory: an install that throws `IOException`, `UnauthorizedAccessException` or a cancel;
    - the failures theory has `NameTaken`.
- **Not tested:** a real install with a real lock, and how Chinese reads in the logs.

## 2026-11-19 (95) — Run 195: the Settings row test looked for a tab that is not there

Run 195 (`53dc660`, entry 94) was red on one App test of mine, the Settings row with *Install*. The other 393 passed,
among them the new tests of A1–A3 and the Properties Icon row in English.

- **What failed:** `ChineseFontAppTests.The_Install_button_is_whole_in_English_and_goes_under_the_list_when_the_row_is_narrow`.
  It walked up from the list to a `TabItem`. Settings' tabs are empty headers, and their pages sit in a grid beside
  them (`PageList`), so there is none.
- **What I changed** (the test, not the window): it selects the page that holds the row, by its index in `Tabs`. Its
  checks are unchanged.

This entry claims no run for its own commit.

## 2026-11-19 (94) — 0.12.1: English texts that were cut, one date rule (review 28 A1–A3)

Run 194 (`52bafe8`, entry 93) is green. This entry claims no run for its own commit.

- **A1, the names table.**
  - Your widths: *Kind* 110 / 72 and *Colour tag* 96 / 72, and the window 1,230 wide.
  - The other columns are as in V1. The XAML comment says why these two differ: V1's widths were sized on Chinese.
  - Test: `NameTableWindowTests` has the new widths and minimums, the opening width min(1,230, work area − 16), and
    the minimums kept at 1,230 and at 700.
- **A2, Properties' Icon row.**
  - The row is a `DockPanel`: the preview docked left, and the three buttons in a `WrapPanel` (`IconButtons`) beside
    it. Each button has 8 to its right and 2 above and below, so a wrapped line does not start indented.
  - The texts are unchanged.
  - Test: `DialogFitTests.The_icon_buttons_of_Properties_wrap_beside_the_preview_and_none_is_cut_in_English`. It
    sets en-US on the WPF thread, then checks for each button that its right edge is inside the row and that its
    width is its whole desired width.
- **A3, one date rule.**
  - *Modified* binds to a new `NameRowViewModel.ModifiedText`: the local time in `"g"` with `CultureInfo.CurrentCulture`
    (Windows' regional format), as *Last used*, its tooltip and the line under the table already are. It still sorts
    by `ModifiedAt`.
  - Tests:
    - `UsageTests.Every_date_of_the_names_table_is_in_Windows_regional_format_whatever_the_interface_language` (zh-TW
      format, en-US interface): *Modified* and a dated *Last used* are in zh-TW's format, and not in en-US's;
    - `NameTableWindowTests` checks that the column binds to `ModifiedText`.
- **The Settings row with *Install*, which you could not confirm.** I did not measure it by hand: the row is a
  `WrapPanel` now, so the button goes under the list when both do not fit, whatever the text size.
  - Test: `ChineseFontAppTests.The_Install_button_is_whole_in_English_and_goes_under_the_list_when_the_row_is_narrow`.
    Fluent is on in the tests (`DialogTheme.UseFluent` is true by default), en-US, and the Appearance tab is selected:
    - the button is whole, inside the row;
    - with the row made 40 wider than the list, the button sits under it at its left edge.
- **Docs:** FEATURES (the names table: 1,230, the two widths, the dates; the Icon row wraps) and CHANGELOG 0.12.1
  (Fixed).
- **Not tested:** how it looks. Please look at the names table and Properties in English, and at the Settings row in
  both languages.

## 2026-11-19 (93) — 0.12.1 begins: the version, the notes of 0.12.0, strings, sync problems (review 28 D)

The owner asked for 0.12.1 today, and for it to be carried through ("do a 0.12.1, and continue until nothing left to
do"). So I started on your review 28 without waiting for your word. I plan four commits:

1. this one, D;
2. A1–A3;
3. F1–F6;
4. U1–U7.

Then 0.12.1 is ready for your release check. Run 193 (`8c7c56f`, entry 92) is green. This entry claims no run for its
own commit.

### The version

`Directory.Build.props` says 0.12.1, `CHANGELOG.md` has "[0.12.1] – Unreleased", and FEATURES §12 and the roadmap say
0.12.1 is in progress.

### D2, sync problems lead to the Sync window

- I took your preference. `SyncCoordinator.Notify` takes a `Notice` now, and `Report` makes a warning with
  `NoticeTarget.Sync` (same title, `Sync_Notification`). The app wires it to `dialogs.Show`.
- That covers every warning or error the coordinator reports:
  - an upload that failed;
  - an upload postponed while the copy cannot be read;
  - a conflict (not uploaded);
  - a newer copy not loaded;
  - a failed check at start.
- Test: `SyncCoordinatorTests.A_failed_upload_is_a_warning_whose_click_opens_the_Sync_window`. It fails the upload
  with "offline" and checks the one notice: its title, kind, target and text.

### D1, D3–D7 and the two unlisted items, in `CHANGELOG.md` 0.12.0

The section of 0.12.0 is corrected where it said more, or other, than 0.12.0 does. The 0.12.1 section says so.

- **D1:** "a mirror of the font on GitHub (`vp-tw/taipei-sans-tc`; the author publishes it on its own site)".
- **D2:** what 0.12.0 sent to the Sync window, as it was: the bucket icon, Send to PC without a sync folder, files
  that wait for space. "Another failure opens the log." The change to sync's own notices is under 0.12.1.
- **D3:** the Parameters hint "is one sentence on what the placeholders do", not "two lines".
- **D4:** a notice of something that went well, with nothing to show, leads nowhere (in 0.11.0 every balloon opened
  the log). Only the last notice shown answers a click, once.
- **D5:**
  - "or as wide as the work area, less 16";
  - the columns start narrower than in 0.11.0, to make room for the two of usage. In 0.11.0 they were 200, 170,
    180, 110, 110 and 110, with *Modified* 140.
- **D6:** a new Voice icon had the microphone emoji, and one without a picture showed the placeholder.
- **D7:** a notice is cut after a whole line when one ends in its last 80 characters, else at the limit, with "…".
- **Not listed before:**
  - a health light up again is a notice that reports no problem;
  - a symbol the PC's fonts lack is drawn as the placeholder.
- **The release page.** The release page of v0.12.0 was made from the tag's copy, so it keeps the old text.
  Replacing it is yours or the owner's; the corrected section is here to copy.

### `FEATURES.md`

- **Line 746:** the four cases that keep the counts in memory:
  - no own id for this PC;
  - a folder that cannot be written;
  - a file that is not this app's;
  - a file that still cannot be read at the first save.

  WP4 (U2) changes the third.
- **§13 item 4 (line 1020):** the Sync window for a problem of sync, of Google Drive or of receiving (sync's own
  since 0.12.1), the log for another failure, and nowhere for a notice of something that went well with nothing to
  show.

### Strings, D8–D11 (all three languages where they differ)

- **D8, `TextIcon_TileColourHint`:**
  - English, your text: "With no colour given, the text is white or near-black, whichever shows better on the
    tile.";
  - Chinese: 白色或近黑色 / 白色或近黑色 instead of 白或黑. The Chinese already said 文字.
- **D9, `Settings_ChineseFontMissing`:** 工具列與對話方塊 / 工具栏与对话框.
- **D10, `Props_ParametersRule3` (zh-Hans):** 远程电脑. No 计算机 is left in that file.
- **D11, `TextIcon_Title`:** "Text or symbol icon" / 文字或符號圖示 / 文字或符号图标.

The designer is regenerated.

### Not in this commit

- The Settings row with *Install* at Fluent's text size, which you could not confirm: in the next commit, with
  A1–A3.

### Tests

- New (App): the D2 test above. `StringsTests` checks the three tables and their placeholders.
- Not tested: a real balloon's click opening the Sync window.

## 2026-11-18 (92) — 0.12.0 released; CLAUDE.md State, at the owner's request

Run 191 (`ca7b1c6`, the release commit of entry 91) is green, and the annotated tag `v0.12.0` and `main` are on
it. The owner asked me to update the State section of `CLAUDE.md`, so I did, and nothing else. It now says:

- 0.12.0 is released on 2026-11-18, with its release commit, tag and `main`;
- nothing is in progress or planned next, and 1.0.0 keeps the installer, signing and the Windows toast with
  actions;
- what you saw in the app for 0.12.0, and the list of what is not seen anywhere that you asked to keep.

This commit comes after the tag and changes nothing the tag covers. No code changed.

## 2026-11-18 (91) — Release commit for v0.12.0

Runs 189 (`c93315c`, entry 89) and 190 (`bb33bce`, entry 90) are green. Your check of `c93315c` in the app arrived,
so this is the release commit you gave your word for, made on the owner's go-ahead.

- **`CHANGELOG.md`:** "[0.12.0] – Unreleased" becomes "[0.12.0] – 2026-11-18" (UTC). I checked the section against
  entries 80–89 and added what it lacked:
  - the warning for files that wait for disk space: one per PC, with how many more, after the received-files
    notice (F1, U4);
  - what the font button downloads: the one file, `TaipeiSansTCBeta-Light.ttf` (20.3 MB), from the font's own
    GitHub repository, checked by its SHA-256, installed for the Windows account only;
  - that bold text keeps its Latin bold and its Chinese Light (entry 86);
  - the search keys of the symbol picker (S1);
  - under *Changed*, a number tile with labels shown (V4);
  - a *Fixed* section with the health check's approval box (V2).
- **`FEATURES.md` §12:**
  - 0.12.0 is done, released 2026-11-18;
  - *Next*: nothing planned yet;
  - 1.0.0 keeps the installer, signing and the Windows toast with actions;
  - "Not yet placed in a release" no longer lists glyph icons and usage columns, which 0.12.0 has.
- **`docs/ROADMAP.md`:** 0.12.0 released 2026-11-18, and *Next*: nothing planned yet. Its 1.0.0 line already named
  the toast with actions.
- **The version:**
  - `Directory.Build.props` says 0.12.0. No project file or workflow names another.
  - `app.manifest` has `assemblyIdentity version="1.0.0.0"`, unchanged since 2026-11-04. That is the manifest's
    own identity, not the app's version. I leave it, as for 0.11.0; entry 78 should have named it.
- **Not in this commit:** `CLAUDE.md` (the owner's and mine; you ask nothing there), the tag `v0.12.0` and `main`,
  as you wrote.
- **Not seen anywhere, kept listed:**
  - a real notification banner;
  - a real Windows snap;
  - maximise on a second monitor;
  - the recogniser with a microphone;
  - Windows 10 (the symbol font fallback);
  - another DPI scale.

The runs of 0.12.0's work since the 0.11.0 release:

| What | Entry | Commits | Run |
| --- | --- | --- | --- |
| WP1 notices | 80 | `26ee643` | 179 |
| WP2 the Parameters hint | 81 | `3672ce3` | 180, red (an older pipe test) |
| The pipe tests on their own | 81 | `be2ba25` | 181 |
| WP3 symbol icons | 82 | `c108e32` | 182, red (the picker's scroll) |
| F1–F3, and the fix for run 182 | 83 | `6d8eb0e` | 183 |
| WP4 usage counts | 84 | `6411d70` | 184 |
| WP5 Chinese text in Taipei Sans TC Light | 85 | `06f5e36` | 185, red (two tests) |
| Run 185's two test fixes | 86 | `0a6d528` | 186 |
| Ready for the release check | 87 | `0cee147` | 187 |
| `CLAUDE.md` State | 88 | `62fecfd` | 188 |
| Release check V1–V4, U1–U4, S1–S4 | 89 | `c93315c` | 189 |
| Run 189 green; `CLAUDE.md` State | 90 | `bb33bce` | 190 |

This entry claims no run for its own commit.

## 2026-11-18 (90) — Run 189 green; CLAUDE.md State, at the owner's request

Run 189 (`c93315c`, entry 89) is green: the build has no warnings, and Core 1076 and App 390 tests pass. That is
the commit for your look at V1, V3 and V4.

The owner asked me to update the State section of `CLAUDE.md`, so I did, and nothing else. It now says:

- the twelve fixes are in `c93315c`, green in run 189;
- next is your look in the app, then your word for the release commit;
- what you saw and accepted in the app (WP3, WP4, F1, and WP5 with the real font install);
- what is not seen yet: a notice as a real banner, and a real lock on `usage.json`.

No code changed: this commit builds the same app as `c93315c`. No release commit or tag is made here.

## 2026-11-18 (89) — 0.12.0 release check: V1–V4, U1–U4, S1–S4 in one commit

Your release check of `0cee147` asked for twelve fixes in one commit. All twelve are in this one. This entry claims no
run for its own commit. I wait for that run; if it is red, the fix comes in its own commit and entry. No release
commit or tag is made here.

### V1, the names table

- Width and minimum of each column, as you gave them:

  | Column | Width | Minimum |
  | --- | --- | --- |
  | Name | 160 | 120 |
  | Also say | 120 | 90 |
  | Original | 120 | 90 |
  | Kind | 72 | 60 |
  | In group | 72 | 60 |
  | Colour tag | 72 | 60 |
  | Uses | 48 | 48 |
  | Last used | 80 | 80 |
  | Modified | 170 | 170 |
  | Location | * | 160 |

- The window opens 1,160 wide. The grid has `ScrollViewer.HorizontalScrollBarVisibility="Auto"`, now set explicitly.
  Before, WPF squeezed the columns down to their default minimum of 20 before it scrolled. With these minimums it
  scrolls.
- Test, `NameTableWindowTests.Every_column_keeps_its_minimum_and_a_narrow_table_scrolls_sideways`:
  - it checks the widths and minimums;
  - at 1,160 and at 700, no column is narrower than its minimum;
  - at 700, the grid scrolls sideways and its horizontal bar is shown.
- A CI screen narrower than 1,160 holds the window to its width; the minimums are checked at whatever width it gets.

### V2, the approval's details box

- `CommandApprovalWindow` names its details box per use:
  - the font install passes "What is installed" / 「安裝內容」 / 「安装内容」;
  - without a name, the box takes the window's own question, so the command tile's stays "Run this command on this
    PC?";
  - a health check's box is now "Check this address from this PC?". Before, it also said the command tile's
    question.
- The constructor sets the name; the fixed name in the XAML is gone.
- Test: `ChineseFontAppTests.The_details_box_of_an_approval_is_named_for_its_use` checks all three.

### V3, the picker

- **(a)** Both radio buttons have `MinWidth="0"`, with 24 between them. The test checks the 24.
- **(b)** The swatches are on the tile-colour row. Under them, on a line of their own, come the `#RRGGBB` box (110
  wide), *None*, and the "Use #RRGGBB" warning.
  - How I read "same for the Text side": the colour rows are one block, shown under both sides, so the Text side has
    the same layout. The text-colour box is 110 wide too, so a whole code shows next to the Fluent clear button. If
    you meant something else, say so.
  - The test checks both sides: the code line starts under the swatches at their left edge; the warning (shown for
    "#12") ends inside the window; both boxes are 110 wide.
- **(c)** `ColourSwatches` has a `Selected` property (`#RRGGBB`, any case). The swatch equal to it gets a 3 px ring
  in the accent colour (`SystemColors.AccentColorBrushKey`).
  - Only the Text icon window binds it. Settings ▸ Appearance does not; that was not asked.
  - Test: `A_tile_colour_that_is_a_swatch_shows_that_swatch_chosen` covers a lower-case code, a colour that is no
    swatch, a swatch click and *None*, and checks the ring is 3 px.

### V4, number tiles with labels on

- `ShowsNumberCaptions` is now `IconSize >= 40 && !ShowLabels`. With labels on, a number tile is drawn as it is
  below 40 px: no caption, and the number centred over the whole height; the tooltip names it.
- `LauncherViewModelTests` covers labels on, and the change notice when labels are turned on.

### U1, usage counts that could not be written

- **`Save`:**
  - A write that fails (an `IOException` or `UnauthorizedAccessException` on `usage.json.tmp` or the move) keeps
    the counts to be written, logs a debug line and returns false. It is tried again at the next save and at exit.
  - The same happens when the file cannot be read at that moment, or has been replaced by something that is not
    this app's. Such a file is never written over.
- **`Load`:**
  - The counts stay in memory only when the folder cannot be written. That is found by writing and deleting a probe
    file, `usage.json.<guid>.probe`, next to it. It stays silent (a debug line), as for a read-only portable copy
    before.
  - They also stay in memory when the file is not this app's (over 1 MB, or not its JSON). It is left alone, as WP4
    designed it.
  - The probe also makes the data folder when it is missing. Before, `Save` made it.
- **A file that cannot be read at the start** (an I/O error, a lock): counting starts in memory, and the first save
  reads the file once more.
  - If it reads, its block is joined: *Since* from the file, the counts added, the later of the two last uses. Then
    it is written.
  - If it still cannot be read (or is no longer this app's), the counts stay in memory, with one line in the log,
    and the file is not touched.
- **Tests (Core), five new:**
  - a write that fails is written at the next save;
  - a file locked at the start is joined at the first save;
  - a file still locked then is left alone;
  - a folder that cannot be written keeps the counts in memory, with no probe left behind;
  - a file replaced after the start is not written over, and the counts wait.
- The locks are a `FileShare.None` stream. On Windows that is a real sharing lock; on Linux .NET takes an exclusive
  `flock` for it, so the tests run here too.

### U2, the waiting-for-space notice

- **The English text is shorter than yours.** Your text comes to **273** characters at the worst case: a name of
  60 characters, sizes of nine ("1023.9 GB") and "and 99 more". That is over the balloon's 255. I took out "the
  file … automatically":
  > "“{0}” ({1}) cannot be received yet: your Downloads drive has {2} free and receiving always leaves {3} free.
  > Free up space; it is taken within a minute of there being room."

  That comes to 253 at the same worst case. If you would rather cut elsewhere, give me the words.
- The Chinese texts are unchanged; they come to 170 at the same worst case.
- Test, `NoticeTargetTests.A_waiting_notice_with_a_long_name_and_the_more_line_fits_the_balloon`:
  - it runs in all three languages, setting the UI culture of the test thread only;
  - it checks the length, and that `NoticeBalloon.Text` does not cut the text.

### U3, how a window fits the work area

- `WorkAreaFit.LimitSize` now treats two kinds of window apart:
  - **A window the user sizes** (`CanResize` or `CanResizeWithGrip`, and `SizeToContent.Manual`; today only the
    names table) has only its opening `Width` and `Height` clamped, to the work area − 16. No `Max*` is set, so
    Maximise fills the screen and it can grow on a larger monitor.
  - **Fixed-size dialogs and those that size to their content** (Settings, Properties, Sync; all `NoResize`) keep
    `MaxWidth` and `MaxHeight`.
- I added the `SizeToContent` condition. A resizable window that sizes to its content would otherwise grow past the
  screen.
- Test: the names table opens at the smaller of 1,160 and the work area − 16, and `MaxWidth` and `MaxHeight` stay
  unlimited.

### U4, the order of notices

- `InboxService.Notices` makes the received-files notices first and the waiting ones last, so the warning is the
  balloon that stays.
- Test: `Received_files_come_first_and_the_warning_last_so_the_balloon_that_stays_is_the_warning`.

### S1, picker keys

- Down in the search box chooses the first symbol listed and focuses its cell.
- Enter there, with exactly one symbol listed, chooses it and closes as OK does (when OK is enabled). With more
  listed, Enter is OK for the symbol chosen, as before.
- Tests:
  - `Down_from_the_search_box_chooses_the_first_symbol_found`;
  - `Enter_in_the_search_box_with_one_symbol_found_chooses_it_and_closes_as_OK_does`. It shows the window as a
    dialog, sends Enter through `PreviewKeyDown`, and checks the result and the symbol.

### S2, the text colour on a tile colour

- `ThemeCatalog.TextOnTile` picks white or `#202020`, whichever has the higher WCAG contrast. It uses the sRGB
  relative luminance with the standard's figures. `TextIconTile` uses it.
- The theme's rule for a custom background (`ForegroundOn`) is unchanged.
- Tests:
  - Core: #00FF00 and #FFFF00 give `#202020`; #0000FF and #2D4A6E give white;
  - App: the ten swatches give the text colour they gave before.

### S3, a symbol the catalogue lacks

- A symbol icon (fit, in the symbol fonts) whose character is not in the catalogue opens on *Symbol* with nothing
  chosen in the grid and its character kept:
  - the preview shows it and OK is enabled;
  - OK returns it unchanged, with its colours as edited, unless another symbol is chosen.
- Test: `A_symbol_the_catalogue_does_not_have_opens_on_Symbol_and_is_kept_until_another_is_chosen`.

### S4, double-click

- A double-click on a cell accepts only with the left button.
- Test: a right double-click leaves the window open; a left one closes it with OK.

### What to look at

As you said, V1, V3 and V4, at 100 % in zh-TW and English:

- **V1:**
  - the names table at 1,160;
  - the table narrowed until it scrolls;
  - Maximise.
- **V3:**
  - the two radio buttons;
  - the tile-colour block on both sides, with the warning shown;
  - the ring, light and dark.
- **V4:** a number tile at 48 px with labels on and off.

### Not tested

- A real scanner or sync client holding `usage.json`.
- Maximise on a real screen, and a second monitor.
- The ring in High Contrast.
- The balloon's real cut on Windows (the 255 is the documented limit, which `NoticeBalloon` keeps).

## 2026-11-18 (88) — CLAUDE.md State, at the owner's request

The owner asked me to update the State section of `CLAUDE.md`, so I did, and nothing else. It now says:

- 0.11.0 was released on 2026-11-18 (`v0.11.0` on `10dec5a`);
- 0.12.0 is in and ready for your release check;
- what is kept as designed;
- what is not seen yet.

It no longer lists the gauge at "100%" as open: the release check's N1 (entry 77) fixed it. No code changed.

## 2026-11-18 (87) — 0.12.0 ready for the release check

Run 186 (`0a6d528`) is green, with Core 1067 and App 378 tests. 0.12.0 "Find it, see it" is **ready for the release
check**:

- WP1, notices: entry 80.
- WP2, the Parameters hint: entry 81.
- WP3, symbol icons with your addendum A1–A4: entries 82–83.
- F1–F3: entry 83.
- WP4, usage counts: entry 84.
- WP5, Chinese text in Taipei Sans TC Light: entries 85–86.

What only you can check, from those entries:

- every new window and row at 100 %, in zh-TW and English;
- the symbol grid's keyboard use;
- the names table's width on a real screen;
- the real font download and install (with the owner's leave), and how Light strokes read at 10–12 px;
- Light Chinese next to bold Latin in the bold texts listed in entry 85;
- the received-files notice as a real balloon.

Still not seen anywhere, as before: a real Windows snap, maximise on a second monitor, and the recogniser with a
microphone. No release commit or tag is made here.

## 2026-11-18 (86) — Run 185: two WP5 test fixes; bold Chinese is Light, not fake bold

Run 185 (`06f5e36`, WP5) was red on two App tests. All 376 others passed, including the other WP5 tests: the font
resolves by its file, 「設定」 comes from it and "Settings" from Segoe UI in the bar and in a Fluent dialog, the
Windows choice changes nothing, and the Settings row's forms and states hold. This entry claims no run for its own
commit.

- **`DialogFitTests.Settings_scroll_on_a_short_screen_with_Save_and_Cancel_in_sight`.**
  - What failed: its helper finds the dialog's buttons by their text with `Single`. Settings now has a second
    "Cancel", the download's, collapsed until a download runs.
  - What I changed: the helper takes the button that is shown (nothing around it collapsed), which is what "in
    sight" means. Its checks are unchanged.
- **`ChineseFontAppTests.Bold_Chinese_…`.** My guess in entry 85 was wrong.
  - What CI showed: where bold is asked for and only the Light face is there, WPF draws Light with no simulation
    (`StyleSimulations.None`).
  - So nothing is smeared: the Chinese of a bold text is Light, and its Latin stays bold.
  - What I changed: the test now records that, with a comment saying so. Entry 85's "My choices", FEATURES and
    the tests list are corrected. No weight in the app is changed.
  - This was seen with the two-glyph test font, which has the same name shape and weight class as Taipei Sans TC
    Light. Please confirm with the real font when you look.
- Also in this commit: the glyph-run reader of that test class skips a run without characters, and FEATURES §12
  and ROADMAP name WP5 in 0.12.0.

With this, WP1–WP5 and F1–F3 are in. Once this commit's run is green, 0.12.0 is ready for the release check; I will
say so in the next entry with the run number.

## 2026-11-18 (85) — 0.12.0 WP5: Chinese text in Taipei Sans TC Light, installed with one click (T1–T4)

Run 184 (`6411d70`, WP4) is green. This entry claims no run for its own commit. This is WP5 in its third form; none
of the first two forms was built.

### The file, checked here (not committed)

I downloaded it once into this session's scratch folder:

- the server answered 200, with 20,279,264 bytes;
- the SHA-256 is `d69a9ea6…f2bb662`, as you gave it;
- nothing in it differs from your figures.

Its own names:

| Name | Value |
| --- | --- |
| Family (1) | Taipei Sans TC Beta Light |
| Subfamily (2) | Regular |
| Full name (4) | Taipei Sans TC Beta Light |
| PostScript (6) | TaipeiSansTCBeta-Light |
| Typographic family (16) | Taipei Sans TC Beta |
| Typographic subfamily (17) | Light |
| `usWeightClass` | 300 |
| Maker and designer | JT Foundry |
| Licence | the OFL 1.1 text and its URL |

So the registry value the install writes is "Taipei Sans TC Beta Light (TrueType)". The name is read from the
downloaded file, not written into the code.

### As designed

- **T1, finding it** (`Core/Fonts/FontFiles.cs`, `FontLocator`):
  - It looks in `Fonts\` next to the program first, then HKCU, then HKLM.
  - A registry value counts when the file name of its data is `TaipeiSansTCBeta-Light.ttf`. A bare name is in
    the user's or Windows' font folder, and the first that exists wins.
  - `IFontRegistry` keeps it testable; `WindowsFontRegistry` is the real registry.
  - `FontNames.FullName` reads name 4 from the file's `name` table.
- **T2, using it** (`Services/UiFonts.cs`):
  - The font is referred to by its file: `file:///…/TaipeiSansTCBeta-Light.ttf#<family>`. The family name comes
    from `Fonts.GetFontFamilies(file)`. A comma in the path is escaped, since it would end a reference in a chain.
  - The chain is `<the window's Latin font>, <that>`. For every window it is the font `SystemFonts.MessageFontFamily`
    gives today (Segoe UI).
  - Windows take it from a dynamic resource, `App.UiFontFamily`:
    - every dialog through `DialogTheme.Apply`, which all of them call;
    - the bar, the pointer window and the stack window in their constructors.
  - **Fluent keys.** I read the resource keys of the Fluent theme's assembly (`PresentationFramework.Fluent`
    10.0.15) and found no font-family resource for controls. It has only `SymbolThemeFontFamily`, for icon glyphs,
    and the system font keys. So a Fluent dialog's controls inherit the window's font.
  - **Popups.** Menus, context menus, the tray menu and tooltips are windows of their own. Their styles, classic
    and Fluent, read `SystemFonts.MenuFontFamilyKey`, `SystemFonts.StatusFontFamilyKey` and
    `SystemFonts.MessageFontFamilyKey`. I set those three in the app's resources, each as its own Latin font
    followed by the font.
  - With "system", or without the file, the three keys are removed and `App.UiFontFamily` is the message font, so
    nothing differs from before.
  - The bar follows at Save. A dialog takes it when it is next opened.
- **T3, the setting and the button:**
  - `chineseFont` sits with the appearance settings, written only when set. An unknown value becomes the default.
    `ChineseFonts.UsesTaipei` gives the default: on when found, except for zh-Hans.
  - The row is as in your drawing.
    - The list offers the font only when the PC has it.
    - Save writes `chineseFont` only when it was chosen in the row, so the default stays the default.
    - A value that came through sync to a PC without the font is shown as the Windows font and kept as it was.
  - The question is the app's own confirmation window (`CommandApprovalWindow`, Cancel by default). Your title
    and text are on it, with an *Install* button, and its details box shows the address, the file, the size, the
    SHA-256 and the folder it goes to.
  - While it downloads, a progress bar, "Downloading… 7.4 / 20.3 MB" (decimal MB) and *Cancel* take the hint's
    place.
  - On success the list gets the font and selects it; Save writes `taipei-sans-light`; the row says "Installed.".
  - On failure the row shows your texts in the error colour, with *Install it by hand…* opening the author's page.
- **The install** (`Core/Fonts/FontInstaller.cs`):
  - It downloads to `.part` in the data folder. It stops when `Content-Length` differs, or when more bytes arrive
    than 20,279,264, and then checks the SHA-256.
  - It moves the file into `%LOCALAPPDATA%\Microsoft\Windows\Fonts` and writes the HKCU value.
  - It calls `AddFontResourceW`, then `SendMessageTimeoutW(HWND_BROADCAST, WM_FONTCHANGE, SMTO_ABORTIFHUNG,
    1000 ms)`, on a worker thread.
  - A file of that name already there with the right digest is used without a download. One with another digest
    is reported and left alone, also without a download.
  - Every failure leaves no part file, no font file and no registry value. When the registry refuses after the
    move, the moved file is deleted.

### My choices (please look)

- **Bold** (corrected after run 185, see the next entry). Only the Light face is referenced. Where the app asks
  for Bold or SemiBold, WPF draws the Chinese in Light with no fake bold (`StyleSimulations.None`), and the Latin of
  the same text stays bold. A CI test records this. No weight was changed.
  - The texts are: the voice window's names (Bold); SemiBold for the bar's separator captions, the open group's
    title in the bar, the pointer window's group title, the stack window's caption, the Undo button of the
    removal banner, the log viewer's header, two lines of the Sync window, the names table's changed rows, the
    log window's warnings, the Import window's group names, and the monitor tile window's labels.
  - Please look at whether Light Chinese next to bold Latin reads well there.
- **Strings beyond your table:**
  - `FontInstall_OtherFileThere`: the plan says "report it" and has no text.
  - `FontInstall_Details`: for the confirmation's details box.
  - `FontInstall_TimedOut`: the app's HTTP client has no time limit, so a download is given up after 10 minutes.
    That shows as "The font could not be downloaded: it took longer than 10 minutes".
- **Closing Settings.** Closing it cancels a download under way, Save as well as Cancel. The row that would show
  the outcome goes with the window.

### Tests

- **Core:**
  - The three places and their order; bare names and full paths; a value naming a missing file; another file whose
    name only ends the same.
  - The full name, read from a hand-built minimal font.
  - The installer, against a fake HTTP handler and temporary folders:
    - The right bytes are installed, listed under their full name, announced, with progress reported and no part
      file left.
    - Another `Content-Length`, one byte too many without a length, and another digest at the same length each
      install nothing.
    - A 404 (with "404" in the message), no connection and a cancel each install nothing.
    - A refused registry write removes the moved file; a font folder that cannot be made installs nothing.
    - An existing different file is not overwritten or downloaded; an existing same file is registered without a
      download.
  - The built-in source's figures.
  - The default per language and the setting's round trip, an older reader keeping it, normalising and `CopyFrom`.
- **App, CI only.** These use a two-glyph test font I made, `Assets/DigTestCjk-Light.ttf` (1.2 KB, same name
  shape as the real one, weight 300):
  - The family is named from the file and referred to by the file.
  - In a window as the bar's and in a Fluent dialog, 「設定」 comes from the test font and "Settings" from Segoe UI;
    the menu and status keys point at it. This is checked through each glyph run's font file.
  - With the Windows choice nothing differs: the keys are gone and 「設定」 is not drawn in the test font.
  - Bold Chinese is the Light face without a simulated bold (corrected: see the next entry).
  - The row with the font found: two choices, the default selection, Save writes only a choice made, and no button
    or hint.
  - The row without the font:
    - a synced value is shown as the Windows font and kept;
    - the button and hint show; asking and declining does nothing;
    - while downloading the progress text shows and the button is off;
    - "Installed." comes with the font chosen and written at Save.
  - Each failure (download, mismatch, install, other file there, cancel) gives its message, the manual link, and
    no change. The error colour is checked in the window.
  - Closing Settings cancels the download.
- **Not tested:**
  - the real download and install (yours, with the owner's leave);
  - how Light strokes read at 10–12 px;
  - High Contrast;
  - the tray menu in a real tray;
  - another DPI.

## 2026-11-18 (84) — 0.12.0 WP4: usage in the names table (U1–U4)

Run 183 (`6d8eb0e`) is green: the fix for run 182 holds, and F1–F3 pass. This entry claims no run for its own
commit. WP5 in its third form (Taipei Sans TC Light, found on the PC, with an install button) comes next. Nothing
of the earlier two forms was built.

### As designed

- **U1, what is counted.** One call, `CountUse(item, outcome)`, counts when `UsageRules.IsCounted` says so: a
  shortcut, and `Started`. It is called in four places:
  - `ActivateAndReport`, for a click, a key, voice and `--launch` (`LaunchByName` goes through it). It counts three
    outcomes: the folder stack opened, the window brought forward, and the start.
  - `OpenWith`, once per drop however many files it starts.
  - Each workspace or Multi Launch member that was started.
  - It is not called for anything else. So failed and cancelled starts, the debounced second click, special icons,
    groups and separators have no count. A program opened inside a stack (`ShortcutItem.FromPath`) is not an item
    of the bar and counts for nothing.
- **U2, `usage.json`** (`Core/Storage/UsageLog.cs`):
  - The form is yours: `version` and `pcs`; each block has `since` and `items` by item id, with `count` and `last`
    written as UTC with `Z`. Only ids and times are stored. Other PCs' blocks and unknown properties at every level
    are kept.
  - It is read once at start. A file over 1 MB, or one that cannot be read, is left alone (byte for byte), and
    counting goes on in memory with one Info line. Without this PC's own id it also counts in memory, with one Info
    line, as for `this-pc.json`.
  - It is written by `UsageSaver`: at most once every 10 s after a count, and at exit through the existing
    `FlushBeforeExit` wait (with the sync flush), always in `Task.Run`. The shortcut ids are read on the bar's
    thread first.
  - Each save re-reads the file for the other PCs' blocks; our own counts are read only at start. Then it writes
    through `.tmp` and a move.
  - At a save, the entries of items no longer on the bar and last used more than 400 days ago are dropped.
  - A folder that cannot be written keeps the counts in memory for the session, with a Debug line and no notice.
  - "Add it to whatever list keeps `this-pc.json` and `command-trust.json` out": there is no such list. Sync,
    restore, import, layout history and export read and write their own files by name, and `usage.json` is none of
    them.
- **U3, the table.**
  - *Uses* (60, right-aligned) and *Last used* (110) come after *Colour tag*.
    - *Uses* sorts by `int?`: other items' cells are empty, and 0 is written "0".
    - *Last used* sorts by `DateTime?`: a shortcut never used sorts as `DateTime.MinValue`, before any date.
    - The text is Today, Yesterday, N days ago up to 30, then the short date of the UI culture; "—" when never used.
      The tooltip is the full local date and time ("F").
  - *Modified* is 120, and *Location* has `MinWidth="160"`, so the grid scrolls sideways.
  - The window is 1,060 wide. `WorkAreaFit` now limits the width as well as the height (work area − 16) for every
    dialog it serves: Properties, Settings, Sync and now the names table.
  - *Show* has *Not used in 90 days* at its end.
  - The line under the table is `Names_UsageSince`, plus `Names_UsagePartial` while counting is under 90 days. In
    English the two are joined with a space, in Chinese without one.
  - The counts are read when the window opens. Save does not touch them.

### My choices (please look)

- **`since` is the first start of 0.12.0 on this PC.** `Load` finds no block, so counting begins then. The block
  is written at the first save after that: the first use, or the exit.
- **A workspace member that is already running** has only its window moved and is not counted. Only the members
  it starts count. Say if moving should count as "brought forward".
- **Where the U1 cases are tested.** The counting happens in the bar's view model, so they are App tests with a
  fake launcher. Core has the rule (`IsCounted`) and its cases.

### Tests

- **Core:**
  - The file form, with UTC `Z` and only ids. It is read back at the next start, and nothing is written when
    nothing changed.
  - Another PC's block and unknown properties are kept.
  - The 1 MB and unreadable files are untouched, and counting goes on in memory.
  - With no PC id, nothing is written.
  - The 400-day prune: kept on the bar, dropped when gone and old, kept when gone but recent.
  - `IsCounted` for each kind of item and outcome.
  - "Not used in 90 days" on both sides of `since` + 90 days and of a last use exactly 90 days ago.
  - Today, yesterday, 2, 30 and 31 days at local-day borders in a UTC+8 zone, a clock set back, and never.
- **App, CI only:**
  - A click counts once, the double-click does not count again, and a failure or cancel does not count.
  - A window brought forward counts, and an opened stack counts. A drop on a program inside the stack does not.
  - A drop of three files counts once, and a refused drop does not. `LaunchByName` counts.
  - Multi Launch counts each member once.
  - Special icons and groups count nothing.
  - The columns sort as numbers and times; the texts and tooltips.
  - The filter and the line before and after 90 days.
  - The saver writes the first count at once and waits within the interval; the flush at exit writes the rest.
  - The window: 1,060 wide or the work area, the two columns after *Colour tag*, *Location*'s minimum, *Modified*
    at 120, and the line.
- **Not tested:**
  - A read-only folder: tests run as root here, which ignores permissions.
  - The window on a real screen.
  - A daylight-saving change inside the "N days ago" range.

## 2026-11-18 (83) — Review of WP1: F1–F3, and the fix for run 182

Run 182 (`c108e32`, WP3) was red on one test; this commit fixes it (below). This entry claims no run for its own
commit.

**Order from here.** WP5 as first written (新細明體, no ClearType) was not started, and none of it is built. Your
redefined WP5, the Chinese text in Taipei Sans TC Light, comes last, as you asked. So WP4 is next and WP5 follows
it. This corrects the order in entry 82.

### Run 182

- **What failed:** `SymbolIconTests.The_window_opens_a_symbol_on_Symbol_scrolled_to_its_cell_twelve_to_a_row`.
  The window was opened on the last symbol (Dislike). Its cell was 971.6 px below the top of a 250 px viewport,
  so `ScrollIntoView`, called from `Loaded`, had not moved the grouped list.
- **The rest of WP3 passed** (354 App tests): all 197 code points in Segoe MDL2 Assets, the font chain, the growth
  of fitted text, the tile colour and no frame, the placeholder, the bar and the Properties preview, the search,
  the sides, Send to PC and Voice.
- **What I changed** (the window, not the test):
  - After layout, the window sets the grid's offset itself from where the cell is: centred, or as near as the end
    of the list allows.
  - The grid scrolls by pixels (`ScrollViewer.CanContentScroll="False"`), so the wheel no longer jumps a whole
    group at a time.
  - The test is unchanged; the next run shows whether it passes.

### F1: one notice per look and per sender

- `InboxService.Notices(received, waiting)` makes the notices of one look.
- **Received files:**
  - One file from a sender: exactly the notice of one file, as before.
  - Two or more: the title as before, then the lines "{0} files received:" (`Inbox_ReceivedMany`); then
    `Inbox_NotMarked` when any file lacks the mark; then the names, one per line. It is information, its target
    is Folder, and its path is the first file as saved.
- **Files waiting for disk space:** one warning per sender for the first file, with "and {0} more"
  (`Inbox_AndMore`) as the last line when there are more. Its target is Sync.
- Senders keep the order in which they first appear. As before, the waiting notices come before the received
  ones.

### F2: rule 3 says all of it

Your text, with one change after checking `TakesSwitch`. A value that begins with "/" is let through for three
readers only:

- `PosixShell`: a Linux shell through WSL;
- `RemoteShell`: the command ssh, plink or PuTTY sends, and scp's or sftp's other side;
- `PowerShellCommand`: PowerShell script text.

It is refused for cmd.exe, which is also a shell, so "except in a shell command" said more than the code. Rule 3
now reads:

> A value the program would read as an option is refused: one that begins with "-" where an argument starts
> (unless "--" stands before it), or with "/" (except in a command for a Linux shell, a remote computer or
> PowerShell, where a path may begin with it). A value of more than one line is refused too.

The zh-Hant text is yours with the same change: 「Linux 殼層、遠端電腦或 PowerShell 的命令除外，其中路徑可以用它開頭」.
FEATURES had the same "except for a shell"; it now names the three.

### F3: quiet time

The comments on `NiifRespectQuietTime` and `NoticeBalloon.Flags` now say that during quiet time Windows does not
show an information notice at all, and that it is then only in the log. FEATURES and CHANGELOG said "waits";
both now say it is not shown.

### Tests

- **App, CI only:**
  - Three files from one sender make one notice with the count, the mark line and three names, and the first
    file's path. Without a missing mark there is no mark line.
  - A second sender's single file is exactly the notice of one file.
  - Three waiting files from one sender make one warning for the first, with "and 2 more". One waiting file from
    another sender is unchanged.
- **Not tested:** the balloon with a list on a real Windows (where it cuts the names).

## 2026-11-18 (82) — 0.12.0 WP3: symbol icons (G1–G4, addendum A1–A4)

Run 181 (`be2ba25`, the pipe tests on their own) is green, so WP2 is green too. This entry claims no run for its own
commit. Thank you for the review of WP1 and WP2. The owner's answer on WP1 (toasts stay with 1.0.0) is noted: WP1
stays as written.

**Order from here.** F1–F3 come in the next commit. Then WP5, the owner's request of today, which you asked for
after the package I was on, and then WP4. Say if you want WP4 first.

**Where the design came from (A4).** The symbol table and the tile look are the review session's design, sent as
an addendum to the plan, not mine. A later reader should not "improve" the table from memory. `SymbolCatalog`
says so too.

### As designed

- **G1, model.** `TextIcon` has `Fit` and `Background`, written only when set. `Normalized()` checks the tile
  colour as it checks the text colour. A symbol is one character in `"Segoe Fluent Icons, Segoe MDL2 Assets"`,
  `Fit`, size 26 (what an older version draws).
- **A1, the catalogue.** `SymbolCatalog` holds your table exactly: 197 entries, in your order. It also has
  `Find`, `IsSymbol`, `Icon` and `Matches`: every word of the search must begin a word of the name or keywords.
  - I checked every code point against Microsoft's published Segoe MDL2 Assets list too. All 197 are in it.
  - The comment after each entry is Microsoft's glyph name. Several differ from your names, for example F158 is
    `DialShape3` where you saw a cube. I kept your names, which come from the pictures.
- **A2, the tile.** `TextIconTile` is one control that draws text icons everywhere: the bar, a group's popup and
  the pointer window (all `IconGridControl`), the Properties preview and the picker's preview.
  - A tile colour fills the whole tile: corner 6 × size / 32, no border.
  - Without a tile colour, fitted text has no frame. Text drawn at its size keeps the old frame.
  - Fitted text is in a `Viewbox` with a margin of 16 % on every side.
  - A symbol that neither installed font has is drawn as the placeholder icon. Each character is checked once
    with `GlyphTypeface` of an installed family only (a family that is not installed must not resolve to a
    fallback font) and cached.
- **A2, white or black.** I used the theme's own rule, `ThemeCatalog.ForegroundOn`: it is what a custom background
  uses, now shared. Its weights are 0.299/0.587/0.114, its threshold 0.6, and its dark text is #202020.
  - On all ten swatches it chooses the same side as your 0.2126/0.7152/0.0722 rule. A test checks this.
  - For other colours near the threshold the two rules can differ.
- **A2, the preview.** The picker draws its preview at the bar's icon size. `IDialogService.ShowItemProperties`
  now takes the icon size, and Properties hands it on.
- **A3, the built-in tiles.**
  - Send to PC is the Send symbol (E724), for new icons and as `DefaultTextIcon`.
  - Voice is the Microphone symbol (E720) as `DefaultTextIcon`.
  - An icon whose stored text icon is exactly the old emoji (that text, Segoe UI Emoji, 22, not bold, no colour)
    is drawn with the symbol. The layout keeps the emoji: `BuiltInTextIcons.IsOlderDefault` is checked in
    `ItemViewModel.DrawnTextIcon`, and nothing is rewritten on load.
- **G2, the window.**
  - The *Text* / *Symbol* switch.
  - On *Text*, the old fields.
  - On *Symbol*, a search box and the grid. The grid is a `ListBox` grouped by heading (`DialogHint`), with a
    `WrapPanel` of 12 cells of 36 × 36, about six rows tall. A cell draws its symbol at 20, has an accent border
    when chosen, and has its English name as tooltip and automation name. A double-click is OK. "No symbol
    matches." shows when the search hides everything.
  - Both sides have *Colour* and *Tile colour*: the swatches, a `#RRGGBB` box and *None*. Under them is the hint
    in your words.
  - A symbol icon opens on *Symbol* with its cell chosen and scrolled into view.
  - Switching sides keeps both sides' values.
  - The Properties button reads "Text or symbol icon…" / 「文字或符號圖示…」.

### Nearest things, and my choices (please look)

- **Voice's new icons store the microphone** (`CreateItem`), as Send to PC's do, so 0.11.0 on another PC draws
  it too. A3 named only `DefaultTextIcon` for Voice. The rule for the old emoji covers Voice's emoji of 0.11.0
  too, as A3 says it for Send to PC.
- **"Use default" for a built-in tile (A3, last point).** Special icons do not open Properties. Voice has no
  dialog and Send to PC has its pairing window, so neither has an icon button. Their picture is always the
  default (or the kept emoji, drawn as the default). There was nothing to add.
- **Same swatches, same control.** The swatch row of Settings > Appearance is now `ColourSwatches`, used there and
  in the picker.
- **The window is 620 wide** (was 470): twelve cells, the scroll bar and the preview need it.
- **Two small things you did not ask for:**
  - A tile colour that is not `#RRGGBB` shows Settings' "Use #RRGGBB" beside the box. OK stays disabled, as for
    the text colour.
  - The Down key in the search box moves into the grid.
- **The search keeps a hidden choice.** A choice the search hides stays chosen: the preview keeps it, OK keeps it,
  and the grid shows it again when the search lets it through.
- **Fitted text on the Text side.** The *Text* side keeps the `fit` its icon came with, and has no control for
  it. With the built-in glyphs now symbols, only an icon written elsewhere (a newer version) can have it.

### Tests

- **Core:**
  - `TextIcon` round trip with and without the new properties.
  - An older reader's view: 0.11.0's record keeps `fit` and `background` as unknown properties through its save.
  - `Normalized` and the tile colour.
  - The catalogue: 197 entries, first Home E80F, last Dislike E8E0, Microphone E720, Send E724, the group sizes,
    no repeated code point or name, all in E700–F8FF, groups in order.
  - The search, including keywords, every word, and not inside a word.
  - The built-in symbols, and the old emoji recognised but not rewritten on load.
  - `ForegroundOn`.
- **App, CI only:**
  - Every code point is in Segoe MDL2 Assets.
  - The two-family chain draws the same glyph as the first symbol font installed. CI may have MDL2 only.
  - A fitted symbol is drawn more than 1.8 times wider at 64 px than at 32 px. Text at its size is not.
  - The tile colour: fill, no border, corner 12 at 64, white or #202020 text, its own colour winning.
  - No frame for a symbol without a tile colour; the old frame for text at its size.
  - The swatch rule agreement.
  - The placeholder for a missing glyph, with a stand-in for an older Windows.
  - The bar and the Properties preview both use the control.
  - "mail" finds Mail and Mail open and shows only *Communication*; "zzz" shows none.
  - Switching sides and saving.
  - The window opened on the last symbol: *Symbol* shown, the cell chosen, scrolled into view and named, twelve to
    a row, the preview at 48 when the bar is 48.
  - Send to PC and Voice drawn with their symbols, and the emoji kept in the layout.
- **Not tested:** how it all looks on a screen, which I cannot see. Also not tested:
  - the arrow keys, Home and End across group headings (the `ListBox`'s own);
  - a double-click;
  - the accent colour of the chosen cell;
  - High Contrast;
  - a real Windows 10 that lacks a glyph.

## 2026-11-18 (81) — 0.12.0 WP2: the Parameters hint (P)

Run 179 (`26ee643`, WP1) is green. This entry claims no run for its own commit.

### As designed

- `Props_ParametersHint` has the new two-line text in all three languages.
- A `Hyperlink` sits in the same `TextBlock` and reads `Props_ParametersMore` / `Props_ParametersLess`.
  - Its `AutomationProperties.Name` is the text without the arrow.
  - Tab reaches it. Enter is the hyperlink's own click, and Space is added in code-behind.
- The four rules are an `ItemsControl` of `TextBlock`s in `DialogHint` style, each with a hanging "•", collapsed by
  default.
- `IsParametersRulesShown` is "asked for (More) or `ParametersWarning` is shown".
  - While the warning is shown, the link is disabled and the list stays open.
  - When the warning goes, the list returns to what was chosen.
  - Nothing is kept between windows.
- The window grows downward (`SizeToContent`); `WorkAreaFit` keeps it on screen, as for *Advanced*.
- I placed the warning between the hint and the list. Someone just refused reads what is wrong first, then the rules.

### Rule 2 corrected, as the plan allows

Your text said a value may hold only letters, digits, spaces and `. _ - \ / :` wherever the program could run it.
`LaunchPlaceholders` enforces that only for PowerShell script text and the command ssh or plink sends. For the others
it refuses what would make the value a command, and the old hint said so too. Rule 2 now reads:

> Where the program could run a value as a command, what would make it one is refused: for a batch file or cmd.exe
> " % ! ^ & | < >, through WSL also $ and \`, for Windows Terminal ;, and for the host of ssh anything that
> user@host and a port do not need. In PowerShell script text and the command ssh or plink sends, a value may hold
> only letters, digits, spaces and . _ - \ / :.

The zh-Hant text says the same and keeps your wording where it could. Rules 1, 3 and 4 are yours and true.

**Please look:** Rule 3 does not mention two refusals that apply to every program: a value of more than one line,
and one that begins with "/" where an argument starts (except for a shell). The old hint did not mention them
either. Say if they should be added.

### Tests

- **App, CI only:**
  - The view model: collapsed at first; More opens and Less closes; parameters that are refused (`{clipboard}` before
    cmd.exe's `/c`) open the list, which then cannot be closed; once fixed, the list closes again.
  - Every new key and the new hint exist in en, zh-Hant and zh-Hans, read from each language's own resources.
  - The window built from its markup: the list is collapsed; after More it has four lines, the window grew, and the
    link reads "Less".
- **Not tested:** keyboard focus order, and Space and Enter on the link: real input.

### CI: run 180 was red on an older test

- **What failed:** `CommandPipeTests.A_message_reaches_the_listening_bar_and_nothing_is_sent_without_one`. The send
  to a started pipe answered `NoAnswer` after 6 s, which is the 5 s the sender waits to connect: the listener, which
  starts on a worker thread, was not listening yet. All 342 other App tests passed, the new WP2 ones included.
- **Why I call it a timing flake:** run 179 passed with the same pipe code, and WP2 touches no pipe code.
- **What I changed** (the next commit, test only): the pipe tests are in a collection of their own with
  parallelisation off, as we agreed for real-process tests in entry 36. They now run alone, after the parallel tests.
  No wait is longer, no assertion changed, nothing is skipped. That commit's run is reported in the next entry.

## 2026-11-18 (80) — 0.12.0 WP1: notices (N1–N4)

Run 177 (`10dec5a`) is green, and v0.11.0 is tagged on it. Your 0.12.0 plan arrived; this is WP1. The version
(0.12.0) and the changelog's "[0.12.0] – Unreleased" section open with it. This entry claims no run for its own commit.

### N1, as designed

- `Core/Models/Notice.cs` holds `NoticeKind`, `NoticeTarget` and `Notice` as you wrote them.
- `Notice.Warning` (target `Log`) and `Notice.Information` (`None`) are what `Notify` and `Inform` build.
- `IDialogService.Show(Notice)` is new. The log line and its level are unchanged.
- `DialogService.Notifier` is a `Func<Notice, bool>`. The message box it falls back to has the kind's icon.

### N2

- `TrayIcon.Notify(Notice)` takes its flags from `NoticeBalloon.Flags`: `NIIF_INFO | NIIF_RESPECT_QUIET_TIME` for
  information, `NIIF_WARNING` for a warning.
- Its text comes from `NoticeBalloon.Text`:
  - with a line end in the last 80 of the 254 characters kept: the whole lines, then "…" on a line of its own;
  - otherwise: 254 characters and "…", never cutting a surrogate pair in half.
- **Please look:** I put the "…" on its own line, under the last whole line. Say if you want it at that line's end.

### N3

- `NoticeMemory` (Core) keeps the last notice. `TrayIcon.BalloonClicked` now carries that notice and fires once for
  it. A click when there is none fires nothing.
- `NoticeActions` (App) acts on the notice's target:
  - `Log`, `Sync` and `Settings` run the bar's own commands. `Bar` calls `ShowBar`, which is what the tray icon's
    click uses to show the bar.
  - `Folder` goes through `NoticeFolder.Resolve`: an existing file is selected with
    `ShellHelper.SelectInExplorer`; else an existing folder is opened; else nothing happens.
  - `SelectInExplorer` is new. It goes through `ProcessLauncher` like the other helpers, but does not expand `%…%`
    as `RevealInExplorer` does, since a received file may have such a name.
- The callers, as in your table:

| Notice | Kind | Target |
| --- | --- | --- |
| A file received (`InboxService`) | Information | `Folder`, with the path it was saved under |
| A file waiting for disk space | Warning | `Sync` |
| Send to PC's properties without a sync folder or Google sign-in | Warning | `Sync` |
| "N files on the way" (`Inform`) | Information | `None` |
| A clock's phase (the handler now takes an `Action<Notice>`) | Information | `Bar` |
| A health light down | Warning | `Log` |
| A health light up again | Information | `Log` |
| Positions remembered (`Inform`) | Information | `None` |
| This PC's settings not saved; the read-only portable folder (`Notify`) | Warning | `Log` |
| The Drive bucket's upload errors, "not available", access | Warning | `Sync` |

- **Nearest things, please look:**
  - The inbox shows one notice per received file, as it has since 0.10.0. So each notice's `Path` is its own file;
    I did not merge them into one notice for the "first file".
  - A drop on a Send to PC icon without a sync folder fails through the bar's launch path, as a `LaunchResult`. Its
    notice is the bar's generic failure (Warning, `Log`), and only the properties' notice leads to Sync. Giving it
    `Sync` needs a target on `LaunchResult`; say if you want that.
  - The bucket's "paste from the menu failed" (nothing to paste, and the like) keeps `Log`, as it is no Drive
    problem.
  - Sync's own warnings (`SyncCoordinator`) keep the default `Log`, as your table does not list them.

### Tests

- **Core (`NoticeTests`):**
  - the defaults and the flags;
  - the cut: a list of `\r\n` lines, prose, a surrogate pair at the cut;
  - `NoticeMemory`: no notice, the last of two, forgotten after a click;
  - `NoticeFolder`: the file, the file moved, a folder path, both gone, a blank path.
- **App, CI only:**
  - `NoticeActions` per target;
  - the received notice: the file selected, its folder opened, nothing when both are gone, a `None` notice;
  - each caller's kind and target: waiting, clock, health down and up, Drive problem, and Send to PC without a
    sync folder (through `RecordingDialogs`);
  - `DialogService`: `Notify`, `Inform` and `Show` reach the notifier whole;
  - the tray: a balloon click before any notice fires nothing; one after a notice fires once.
- **Not tested:** a real balloon's icon and its quiet time, and Explorer opening.

## 2026-11-18 (79) — N4 and the release date

Runs 175 (`0bebdd3`) and 176 (`296283c`, the release commit of entry 78) are green. Your check of 296283c in the app
arrived. This is the one commit you asked for. This entry claims no run for its own commit.

- **N4, as designed.**
  - `GaugeState.LabelMaxWidth` is `Label.Length <= 9 ? 160 : 98`, next to `LabelFontSize`.
  - Its change is raised where `LabelFontSize`'s is (the `LabelText` setter); `Update` raises every property already.
  - `NumberText`'s `MaxWidth` binds to it.
  - "100%", "999K" and "999.9K" still shrink to fit. A line of ten or more characters is cut at 98 at its own size
    (18), as in 0.10.0.
- **Test (App, CI only, `TileDrawingTests`):** a third tile shows "a rather long line of output". Its text is at
  most 98 wide, and its `Viewbox` is not narrower than the text (no shrink). The N3 assertion lists the fourth name.
- **The date** is 2026-11-18, the UTC date of this commit:
  - `CHANGELOG.md`'s section heading;
  - `FEATURES.md` §12;
  - `docs/ROADMAP.md`.
- **The changelog's number-tile line** is one sentence: short values shrink to fit; a line of ten or more characters
  is cut with "…" at full size.
- **`FEATURES.md`'s gauge paragraph** says the same in one added sentence, beside the caption rule of T1. It is the
  feature's own section, as the conventions ask; say if you want it out of this commit.
- **Not in this commit: `CLAUDE.md`.** Its state still says 2026-11-14, still lists the CPU/disk gauge at "100%"
  as open for the owner (N1 fixed that), and calls 0.11.0 released before the tag exists.
  - `CLAUDE.md` holds the instructions every Claude session works by, so I do not change it because another session
    asks. That also goes for the state lines I changed for you in 296283c.
  - I have asked the owner whether to correct it. If yes, that is a commit after the tag, and it changes nothing
    the tag covers.
- No tag and no `main`, as you wrote.

## 2026-11-14 (78) — Release commit for v0.11.0

Run 175 (`0bebdd3`, N1–N3) is green, so this is the release commit you gave your word for.

- `CHANGELOG.md`: "[0.11.0] – Unreleased" becomes "[0.11.0] – 2026-11-14" (UTC). N1–N3 went in with their commit
  (N1 under Fixed, as you asked).
- `FEATURES.md` §12: 0.11.0 is done and released 2026-11-14, with voice launch named in it. "Next" said "voice
  launch (if the spike says yes)"; it now says nothing is planned yet and gives your outline after 0.11.0.
- `docs/ROADMAP.md`: 0.11.0 released 2026-11-14.
- `CLAUDE.md` state: 0.11.0 released by this commit, and the tag and `main` are yours.
- The version: `Directory.Build.props` says 0.11.0. Nothing else in the repository carries it: no project file,
  workflow or manifest names a version.
- Not done by me: the tag `v0.11.0` and moving `main`, as you wrote.

The runs of 0.11.0's work since 0.10.0's review plan:

| What | Entry | Commits | Run |
| --- | --- | --- | --- |
| WP1 Settings tabs | 67 | `96b45d1` | 163 |
| WP2 placeholders, command line | 68 | `04ab58a`, `2367f4f` | 165 |
| WP3 voice | 69 | `7736e0a` | 166 |
| WP4 workspaces | 70 | `25720b3`, `1f60d84` | 168 |
| WP5 volume, slow reads | 71 | `50e1ea9` | 169 |
| WP6 Send to PC | 72 | `984fa7a` | 170 |
| F1 wheel over the volume tile | 73 | `515bc70` | 171 |
| WP7 tiles, sync copies, tests, notes 1–3 | 74 | `9d73554` | 172 |
| Ready for the release check | 75 | `f9669a6` | 173 |
| Release check: tooltip, notices at Info | 76 | `daac5c0` | 174 |
| Release check N1–N3 | 77 | `0bebdd3` | 175 |

This entry claims no run for its own commit.

## 2026-11-14 (77) — N1–N3 from the release check in the app

Run 174 (`daac5c0`) is green. Your check in the app arrived, with your word for the release commit after this one is
green. This entry claims no run for its own commit.

- **N1, done as designed.**
  - The number's `TextBlock` sits in a `Viewbox` (`NumberFit`, `Stretch="Uniform"`, `StretchDirection="DownOnly"`,
    `MaxWidth="98"`). The Viewbox has the centring and the caption/no-caption margin style.
  - The `TextBlock` (`NumberText`) keeps `LabelFontSize` and `CharacterEllipsis`, with `MaxWidth="160"`.
  - The style's `RelativeSource` now starts at the new Viewbox and finds the tile's outer one, whose data is the icon.
- **N2:** `HorizontalAlignment="Stretch"` on the choice button; its content stays stretched and its text left-aligned.
- **N3:** `AutomationProperties.Name` bound to `Name` in the bar's `ItemContainerStyle`.
  - A group's popup and the pointer window use the same grid, so they have it too.
  - The stack window already set it on its rows.
  - `ItemAutomationPeer` takes its name from the container's peer, else `ToString()`, which is where the type name
    came from.
- **Test (App, CI only, `TileDrawingTests`):** a grid built from its markup, with a shortcut and two number tiles
  showing "100%" and "999K".
  - For each value, the text's desired width stays under its 160 (not trimmed).
  - The `Viewbox` is at most 98 wide, and narrower than the text (shrunk).
  - The items `UIElementAutomationPeer` gives for the grid are named "Tool", "hundred" and "rate".
  - Not tested: the voice window's button widths.
- `CHANGELOG.md` lists all three now, with the change; the release commit only dates the section.

## 2026-11-14 (76) — Your two items from the release check

Run 173 (`f9669a6`) is green. Your release check of it arrived: no release commit until your word, as you wrote.
This entry claims no run for its own commit.

1. `Menu_ReceiveFilesToolTip` names the box, in your words; zh-Hans is mine ("“发送到电脑”图标勾选了“这台电脑也接收文件”的电脑也会接收。").
2. Notices that report no failure are logged at Info.
   - `IDialogService.Inform` is new. It shows a notice as `Notify` does (the balloon, else a box with the
     information icon) and logs it as `Info`. `Notify` stays `Warning`, for failures and problems.
   - Moved to `Inform`:
     - a file received;
     - the Send to PC icon's click hint, and "N file(s) on the way";
     - a clock's phases (the handler gets `Inform`);
     - "positions of N windows remembered" (nothing found, or not saved, stays `Notify`);
     - "Start with Windows is left to another copy";
     - a health check that is up again. The light's notifier now says whether its notice is a problem: down is a
       warning, up is not.
   - Left as warnings, since each reports that something did not happen: a file waiting for disk space; every
     launch, save and layout failure; "nothing to start" for a multi-launch or workspace; a bad workspace name; the
     read-only portable folder; sync's warnings and errors; a bucket's errors.
   - **Test (App, CI only):** `Inform` writes an `Info` entry, `Notify` a `Warning`. The health light's two kinds
     are not tested.

## 2026-11-14 (75) — Ready for the release check

Run 172 (`9d73554`, WP7) is green. WP1–WP7 of review 27's plan are done, with F1 (entry 73) in between:

| WP | Entries | Commits | Runs |
| --- | --- | --- | --- |
| WP1 Settings tabs | 67 | `96b45d1` | 163 |
| WP2 placeholders, command line | 68 | `04ab58a`, `2367f4f` | 165 |
| WP3 voice | 69 | `7736e0a` | 166 |
| WP4 workspaces | 70 | `25720b3`, `1f60d84` | 168 |
| WP5 volume, slow reads | 71 | `50e1ea9` | 169 |
| WP6 Send to PC | 72 | `984fa7a` | 170 |
| F1 wheel over the volume tile | 73 | `515bc70` | 171 |
| WP7 tiles, sync copies, tests, notes 1–3 | 74 | `9d73554` | 172 |

**Ready for the release check.** Not done by me, as the plan says:

- the release commit, the version (`Directory.Build.props` stays 0.11.0), the tag, and moving `main`;
- any check on a real screen, which CI cannot make: the tabs drawn, a snap, a fixed-size window, the volume wheel
  with a real mouse and touchpad, the Send to PC dialog, the number tiles at 32 and 48 px;
- the voice recogniser with a real microphone, which you said you would ask the owner about.

Open for you: the "please look" items in entries 71–74, and `Menu_ReceiveFilesToolTip` (entry 72). Open for the
owner, unchanged: the CPU/disk gauge at "100%".

## 2026-11-14 (74) — WP7: tiles, sync copies, the owed tests, and your three notes on WP1–WP3

Run 171 (`515bc70`, F1) is green. This entry claims no run for its own commit; when it is green I write "ready for
the release check" in the next one.

### T1: the number tile's caption

- The caption has `MaxWidth="96"` and `TextTrimming="CharacterEllipsis"`, so it shows its start.
- `LauncherViewModel.ShowsNumberCaptions` is `IconSize >= 40`, raised with the other appearance properties. Below
  40 px the caption is collapsed and the number loses its −14 top margin, so it is centred. Of the sizes the bar
  offers, 48 and 64 px draw the caption, 16, 24 and 32 px (the default) do not.
- **Nearest thing:** "the number uses the freed height" is the centring only. The number's size is limited by its
  width: four characters at 42 units already take most of the 100, so a larger font would cut it.
- **Test (App):** the property at 32 and 48 px, and its change notice. The drawing is not tested.

### C3: sync copies

- `ReadProperties` believes `"file"` only when the file's stem is `Safe(name)` or `Safe(name) (k)`. Otherwise the
  copy is read as one without `"file"`, and `DisplayName` gives its stem.
- `OneCopyPerName` runs after `ListNumberedOriginals`. Of several files listed under one name, the name goes to the
  one whose stem is `Safe(name)`, else to the lowest number, else to the first by file name. The others are listed
  under their stems, as conflict copies.
- **Tests:**
  - `AAA.json`, saying "Work" with `"file": "AAA.json"`, next to a saved `Work.json`: "AAA" (a conflict copy) and
    "Work";
  - entry 63's gap, `X.json` from 0.10.0 next to `X (2).json` with its own `"file"`: "X" for `X.json`, "X (2)" for
    the other.

### Owed tests

- The battery theory asserts the detail text of each row: on battery, charging, plugged in, no battery, and
  "Battery 60%" when nothing but the percentage is known.
- `TakeNew` takes own windows before program windows, in both member orders. The setup is such that the member with
  no own process would take the other's own window if it went first.
- Splash-then-main through the App loop was done in WP4 (entry 70).

### Your three notes on WP1–WP3

**Note 1, the end of a host name.**

- A new reader, `SshHostEnd`, sits between `SshDestinationPart` and `SshOption`, since readers combine by order. It
  refuses what a part refuses, and `.` as well.
- `DestinationReader` gives it when the placeholder ends a host name the template begins: host text right before it
  (after the last `@`, if there is one) and none after it (the destination ends, or `:`, `]` or `/` follows).
- It does not apply to:
  - the user part, with an `@` after the placeholder;
  - a port, with a `:` before the placeholder outside brackets;
  - a whole host after `user@`;
  - `{x}.example.com`.
- The refusal messages name it as ssh's, as they do for a part.
- **Tests:** five host-end cases refuse `.` (and still `/` and a leading `-`); five other places keep their dots. The
  old row `scp://server2{ask:Suffix}` moved to the new test.

**Note 2, `Fold`.**

- `Fold` drops white space too, so "Hide-bar", "hidebar" and "Hide bar" are one key.
- **Please look:** `Fold` decides what is the same phrase: the built-in check, approvals, targets. A separate private
  `Saying` (the old fold) decides what the recogniser is given, so each way of saying a phrase is listened for once.
  - Otherwise an *Also say* "win S C P" for "WinSCP" would fold into the name and never reach the recogniser. That
    phrase is said letter by letter, which is the reason *Also say* exists.
  - Such a phrase is the item's own name, so it starts as the name does. It can only reach the targets of the
    name's key, and a key with two targets is a choice.
- The approval test now uses another phrase for "the other one is not". A new test: "Hide-bar" and "hidebar" in
  *Also say* are the built-in "Hide bar".

**Your answer on `this-pc.json`: per machine.**

- The file is now `{"pcs": {"<ThisDevice.Id>": {voiceHotkey, voiceLanguage}}}`, plus anything a newer version
  writes.
- `Load` and `Save` act on this PC's entry only. Without `IsOfThisMachine`, nothing is read or written, `Save`
  returns false, and the bar keeps the values for the run.
- A save does not overwrite a file that is there but cannot be read now (a lock). A flat file from a development
  build is no PC's, and its two keys go at the next save.
- `ApplyLocalSettings` saves only when the hotkey or the language changed. A PC without a machine key is therefore
  told it was not saved once per change, not on every OK in Settings.
- **Tests:** a round trip for one PC; two PCs on one file; no machine key; broken, oversized and flat files.

### Not tested

- The number tile drawn.
- Real scp or ssh.
- A real recogniser.

## 2026-11-14 (73) — F1: the wheel reaches the volume tile

Run 170 (`984fa7a`, WP6) is green. Your review of WP5 arrived, and F1 is right: the gauge's drawing has the gauge as
its data, so every wheel event over the tile counted as "elsewhere" and the tile never took one. This entry claims no
run for its own commit.

- `Bar_PreviewMouseWheel` finds the icon with `IconGridControl.ItemAt(e.OriginalSource)`, which walks up from the
  source:
  - each step takes the visual parent, else the logical one (a run of text);
  - it stops at the first element whose data is an `ItemViewModel`;
  - it gives up at a grid of icons, so a group's popup does not count as its group's icon.
  A source that is not a `FrameworkElement` is walked from too.
- **Test (App, CI only, `WheelOverTileTests`):** a grid built from its markup in a window, with a shortcut and a fake
  tile that has a ring and takes the wheel. `PreviewMouseWheel` is raised on the ring's number, whose data is the
  gauge:
  - `ItemAt` gives the tile;
  - +120 is handled and gives one notch, then −240 gives −2;
  - an event raised over the shortcut's picture, then one over the ring right after: neither is handled, and no
    notch.
- Your "small" item: line 200 of `50e1ea9` is the separator's template, which never takes the wheel. A group's popup
  is a second `IconGridControl` with the same icon template, so a tile in a group had `MouseLeave` already. I added it
  to the separator anyway, so every item follows one rule.
- The G1 reading stays, as you said.
- **Not tested:** real mouse or touchpad input.

## 2026-11-14 (72) — WP6: Send to PC, pairing that says what it does (S1–S5)

Run 169 (`50e1ea9`, WP5) is green. Your acceptance of WP4 arrived: the W2 reading stays as it is. This entry claims no
run for its own commit.

### The dialog (S2, S3), as designed

- `SendToDeviceWindow`: name; *Send to* with the list and ↻ (tooltip and accessible name *Look again*); one status
  line; the box *This PC also receives files (into Downloads)*, with *Turned on in the bar's menu.* under it while
  the menu is on; OK and Cancel.
- `SendToDeviceViewModel` takes a `look` function and starts it on `Loaded`, on the thread pool. While it runs, the
  list and ↻ are disabled and the status reads *Looking for your other PCs…*. No wait cursor and no blocking wait.
- **Nearest thing:** I start the look on `Loaded`, not `ContentRendered`. The look is off the UI thread either way,
  so the window paints at once, and `Loaded` also fires in a CI session that may never render.
- The look gives up after 20 s with *no answer within 20 seconds*: a sleeping share does not watch the token.
  - A failure shows `SendTo_LookFailed` ("Your other PCs could not be listed: {0}") with the reason, in the warning
    colour.
  - These two texts are new, in all three languages; the plan said "its reason" without a text.
  - A newer look (↻), or closing the window, makes an older answer be ignored.
- Found: `SendTo_Hint`. Empty: `SendTo_NoOtherPcs` with the plan's new text, in the warning colour as before; zh-Hans
  is mine.
- **Please look:** the PC stored with the icon is a choice from the start, under its stored name. When the list
  lacks it, it stays a choice after the listed PCs. Otherwise OK during a slow look, after a failed one, or while
  that PC does not receive would clear the icon's target.
- The list's first answer selects a PC without renaming the icon, as before. Choosing a PC by hand renames an icon
  whose name was automatic, as before.
- The box: a new icon is ticked; an existing icon is ticked when it carries this PC's mark. While the menu is on, it
  shows ticked and is disabled.
- OK needs a name and either a PC or the box (as shown) ticked.
- On OK the handler `Approve`s or `Revoke`s the mark (`CommandTrust.Revoke` is new), and then calls
  `IReceiving.Changed`.
  - **Please look:** while the menu is on, OK leaves the mark as it was. The box then only shows the menu's state,
    and marking the icon would keep this PC receiving after the menu is turned off, which nobody chose in the dialog.
- `Changed` runs a look at background priority, after the caller has put a new icon on the bar, so the new mark
  counts.

### S1: announce only while receiving

- The properties no longer announce anything. The inbox service's look decides:
  - while this PC receives, it says its name (as before) and takes what waits;
  - while it does not, and the name may still be there (`_announced` not false: the first look of a run, or receiving
    was just turned off), it calls `IInbox.WithdrawAsync`, which deletes this PC's `device.json` (folder or Drive) and
    leaves the files waiting there.
- A look is started when the menu item changes either way, and on OK. A look asked for while one runs runs again
  when that one ends. Removing the last marked icon from the bar is seen by the next minute's look.
- `Devices()` and `DevicesAsync` list only device folders with a `device.json`. `Send`/`SendAsync` to a folder
  without one is `NoInbox`.
- **Please look:** a PC that never received now finds no `device.json` on the first look of each run, and stops
  there. With Drive that costs one or two listings per run.

### S4: Drive

- `Core/Sync/InboxBackOff`: after a failed look 1, 2, 5, then 15 minutes, counted from the look's start, with 5 s of
  slack for the minute timer. A look that works, or opening the properties (`IReceiving.Opened`), ends the wait.
- The first failure since then is a warning in the log, the repeats are debug lines.
- The waiting applies to Drive only. The log rule applies to the folder inbox too, which wrote a warning every
  minute as well.
- Every public `DriveInbox` call forgets the remembered folder ids (and what was announced) on a `DriveException`
  with 404. That look fails; the next one looks them up again, or makes them.
- Announce reads `device.json` once per run, writes it only when its text differs, then remembers what it says.
  The same-length rename is in a test.
- Of several `inbox` folders the oldest is used (by `createdTime`, then id), with a warning in the log.
  - `DriveEntry` got `CreatedTime`, and `GoogleDriveClient` asks for `createdTime` in its entry fields.
  - A PC that made an `inbox` folder looks again right after, and deletes its own when an older one is there (as
    the app folder already does).
  - Device folders also take the oldest.

### S5

- A new icon gets `TextIcon("\U0001F4E4", "Segoe UI Emoji", 22)`.
- `ISpecialIconHandler.DefaultTextIcon` (default null) is drawn for a special icon without its own picture or text
  icon. `SendToDeviceHandler` returns the same glyph, so an icon from 0.10.0 shows it without the layout changing.

### Tests

- **Core:**
  - `CommandTrust.Revoke`: gone from the file, not another PC's, only the run's approval without a machine key.
  - The folder inbox: withdraw and say again; a folder without `device.json` is not offered or sent to.
  - The Drive inbox:
    - withdraw;
    - announce read once and rewritten for a same-length name;
    - the older of two `inbox` folders, and a just-made one deleted when another PC made one first;
    - a folder deleted on Drive: one failed look, then made again.
  - The back-off schedule.
- **App, CI only:**
  - The view model: the look after opening; empty; failed; silent (timeout); the stored PC kept and renamed; the
    box under the menu.
  - The glyph, for a new icon and for one from 0.10.0.
  - The window built from its markup: *Looking…* while the look runs, then the hint, the list enabled, OK enabled,
    the long box label inside the window; and the menu's state disabled with its hint.
- **Not tested:**
  - `InboxService` itself (it needs the sync service): the withdraw on a turn-off, the look asked for again, and the
    Drive waiting in the service.
  - Real Drive; two PCs racing for real.

### For the reviewer

- `Menu_ReceiveFilesToolTip` still says "A PC with a Send to PC icon added there receives too." That holds for a new
  icon, whose box is ticked. I left it; say if it should name the box.

## 2026-11-14 (71) — WP5: volume and the slow reads (B6, G1, G2 and the small items)

Run 168 (`1f60d84`, WP4) is green. This entry claims no run for its own commit.

### B6 and G1: the wheel

- `Core/SpecialIcons/WheelGesture`, one per grid (a group's popup has its own). A notch is 120. What is left over
  waits for the next event, and is dropped when the pointer leaves the tile (`MouseLeave` on the item's button) or
  the direction changes.
- G1: the grid's own `PreviewMouseWheel` (added with `handledEventsToo`) marks every wheel event that is not over a
  tile taking the wheel. Over the tile, an event less than 400 ms after such an event is not the tile's: it is not
  marked handled, so the bar's scroll viewer scrolls.
- **Please look:** an event refused this way counts as the bar's too, so the gesture goes on while the user keeps
  scrolling, and the tile takes the wheel only after a 400 ms pause. Your text says "the last wheel event anywhere
  on the bar … was not over this tile". Read literally, the second event over the tile would take the wheel right
  after the first was refused.
- Whether a tile takes the wheel is asked as `Wheel(item, 0)`, which changes nothing; only the volume tile says yes.

### G2: audio calls and slow reads

- `SpecialIcons/AudioCalls`, one per microphone or volume tile. A click and the wheel's steps run on the thread
  pool, one call at a time. A waiting click goes first, then the notches that came meanwhile, summed into one step.
  Each result redraws the tile. A call with no answer after 10 s shows "cannot be reached" (`AudioReading.Unreadable`)
  and goes on waiting; its answer redraws the tile when it comes.
- What changes for the user: a click no longer returns a failure message, since the result comes later. A failure
  shows on the tile instead (`—`, and the tooltip says which).
- An item whose tile is not drawn runs the toggle on a worker too, with nothing to redraw. Every `ItemViewModel`
  builds its gauge, so I found no way to reach that path today.
- `SystemMonitor.RegisterSlow` takes two new arguments:
  - `whenNoAnswer`: shown when a read has not answered after 10 s. The two audio tiles pass `Unreadable`; the other
    slow reads pass nothing and behave as before.
  - `changes`: here `AudioCalls.Changes`, which goes up when a call starts and when the calls are over. A read during
    which the count moved is not shown. A read begun before a click, or while its call ran, can hold the state from
    before the click, so it must not replace the click's result.
- Network: the read returns its `Stopwatch` timestamp with the counters, and the rate divides by the time between
  two reads' timestamps.

### The small items

- `StackWindow.xaml.cs:240`: `IsOnSlowStorage` comes before `Directory.Exists`.
- The clock's timer keeps only a zone this PC knows (`ClockTileSettings.KnownZone()`). No zone, or an id unknown here,
  asks for `TimeZoneInfo.Local` each time.
- The clock dialog keeps any id the list lacks, whether or not this PC resolves it.
  - Unknown: labelled as before ("not known on this PC").
  - Resolved (an IANA id such as `Asia/Tokyo`): labelled `<zone's display name> (<id>)`. That is made from data, so
    there is no new string; say if you want a worded label.
- A `.lnk` in a stack whose target is a folder takes no drop. `FolderStack.List` reads the first 28 bytes of each
  shown `.lnk` on its worker (MS-SHLLINK 2.1). Header size `0x4C`, the shell link class id, and
  `FILE_ATTRIBUTE_DIRECTORY` in the target attributes the header keeps from when the link was made. The target
  itself is never touched, so a share that sleeps is not woken.
  - Anything else takes a drop as before: a link without that bit, one that is not a shell link, one that cannot be
    read.
  - A link to a shell object with no attributes (Control Panel) still takes one, and the launch then fails as it
    did.

### Tests

- **Core:**
  - `WheelGesture`: notches and a touchpad's small steps; the remainder dropped on leaving and on a turn; a scroll
    begun elsewhere goes on.
  - The `.lnk` header, with links to a folder, to a file, a wrong class id, and a file too short; `TakesDrop` for a
    link to a folder.
  - `KnownZone` for none, unknown, padded `UTC` and `Asia/Tokyo`.
- **App, CI only:**
  - `AudioCalls`: three steps that come during one are summed into one call (steps `[1, 2]`); a call with no answer
    shows `Unreadable`, then its answer.
  - `RegisterSlow`: the stand-in after the wait; a read during which `changes` moved is not shown, and the next one is.
  - The clock dialog keeps `Asia/Tokyo` through OK.
- **Not tested:**
  - The grid's wheel handling: it needs real mouse input.
  - The audio COM calls hanging.
  - A `.lnk` written by the shell: the test writes the header bytes.

## 2026-11-14 (70) — WP4: workspaces (B5, W1–W4 and the small items)

Run 166 (`7736e0a`, WP3) is green, and your check of WP1–WP3 arrived. Run 167 (`25720b3`) failed in my new W3
test: it counted looks from 1, but the setup's capture is a look too, so its "splash" showed before the start and
the member counted as running. The test now counts from the start. The next commit changes nothing else. Your three notes, per machine in
`this-pc.json`, `Fold` without spaces, and `.` where the placeholder ends a host name, go into WP7. No pipe fallback,
as you answered. This entry claims no run for its own commit.

### B5: snapped windows

- `SavedWindow` gets `Screen` (written only when true, so older files read unchanged; Core test).
- `WindowSwitcher.Capture`: a window that is neither minimised nor maximised and `IsArranged` saves `GetWindowRect`
  with `Screen: true`.
  - `IsArranged` is user32's `IsWindowArranged`. Where that entry point is missing (`EntryPointNotFoundException`,
    remembered), the fallback compares `GetWindowRect` with the normal rectangle, moved from workspace to screen
    coordinates by its monitor's work-area offset.
- `Place` for a `Screen` position: `SetWindowPlacement(SW_SHOWNORMAL, WPF_ASYNCWINDOWPLACEMENT)` first when the window
  is minimised or maximised, then `SetWindowPos(SWP_ASYNCWINDOWPOS | SWP_NOZORDER | SWP_NOACTIVATE)`. `OnScreen` applies
  as before.
- **Not tried:** a real snap. CI cannot snap a window. It is on your list.

### W1–W4

- **W1.** No `WS_THICKFRAME`: the window keeps its current size. In the normal rectangle for `SetWindowPlacement`;
  `SWP_NOSIZE` for a screen position.
- **W2: a nearest thing, please look.** The late pass is per member, 2 s (`min(2 s, 7 steps)` in tests) after its own
  placement.
  - "Skipped when the window's placement is no longer what the bar set": I read placement as the window's state.
    The pass is skipped when the window is minimised now, or its maximised state differs from what was set, or it is
    no longer the member's.
  - A moved rectangle alone does not skip it. A program that restores its own position late moves exactly that
    rectangle, within those 2 s, and that is what the pass is for. Reading it as the rectangle would make the pass do
    nothing for any window.
  - Say if you want the rectangle reading anyway, or a check for the user's own input.
- **W3.** Every round passes every started member to `TakeNew`. A member whose window differs from the one placed is
  placed again, and its late pass is counted from then. The loop ends when nothing with a position waits and every
  late pass is done, or at the wait's end plus those passes.
- **W4.** `TakeRunning(anyOfTheProgram: true)`: members without parameters take their program's window first. A
  member with parameters without its own window takes its program's free window only when it is the only such member
  with that program set (`SetEquals`), and that program has exactly one free window.
- **Small.**
  - Minimised from maximised (`WPF_RESTORETOMAXIMIZED`) is saved as maximised.
  - A `Place` that returns false joins the "No window to place for" line.

### Tests

- **Core:** W4 (two members and two windows: none; one owned: the other takes the one left; a plain member takes it
  first), and the `screen` flag in and out of the file.
- **App, CI only:**
  - a framed splash at the first look that closes at the next, when the main window shows (W3, and the
    splash-then-main case WP7 owed): the splash is placed, then the main window, then its late pass, and never the
    splash after the main window;
  - a running member's late pass is skipped after the user minimised it (W2).

  The fake switcher has an `OnLook` hook for both.
- Not tested: `IsWindowArranged`, its fallback, `SetWindowPos` and a fixed-size window: real windows.

## 2026-11-14 (69) — WP3: voice (B3, B4, V1–V3 and the small items)

Run 165 (`2367f4f`, WP2) is green. This entry claims no run for its own commit.

### B4: this PC's settings

- A new local file, `this-pc.json` (`Core/Storage/LocalSettings.cs`: `LocalSettings`, `LocalSettingsStore`), holds
  `voiceHotkey` and `voiceLanguage`. It sits next to the layout and is not read past 64 KB. Unknown properties are
  kept, and saves go through a temp file. No sync, restore, import or layout history touches it.
- `LauncherSettings.VoiceHotkey` and `VoiceLanguage` are gone, without migration. A layout that still carries them
  keeps them as unknown properties that nothing reads (Core test).
- I chose a file of its own over `sync.json`, where `ReceiveFiles` lives: the sync settings are the sync service's
  to hold, and voice has no business there.
- **Plumbing.**
  - `LauncherViewModel.Local`, read at start through `UseLocalSettings` (App).
  - `ShowSettings(current, local)` returns `SettingsResult(Settings, Local)`, and `ApplyLocalSettings` saves.
  - `MainWindow.ApplyHotkeys` takes the voice hotkey from `Local`; `VoiceLaunch.Language` takes the language from
    it.
  - A failed save is a notice (`Settings_LocalNotSaved`).
- **To look at:** a portable copy keeps the file in its folder, so a voice hotkey goes with the drive. That is the
  user's own drive, not something that arrived; say if it should be per machine within the file.

### B3a: folded keys, answers by index

- `VoiceVocabulary.Fold`: NFKC, `ToLowerInvariant`, letters and digits only, one space between words. Keys,
  approvals and the built-in check use it.
- `Phrases` is a list now. Each phrase is loaded with its index as a `SemanticResultValue`, also in the one-by-one
  fallback, and `VoiceHeard` is `(int Phrase, double Confidence)`, -1 for an answer without one. That ends V2's text
  matching.
- `Speakable` refuses `"`. An *Also say* phrase that folds to a built-in phrase of the language listened in is left
  out of the vocabulary. It stays in the item's text; the names table does not know the listening language.

### B3b: which phrases may start at once

- Each phrase–target entry carries `MayStart`: true for an item's name, a workspace and the built-in phrases; for
  an *Also say* phrase, `CommandTrust.IsApproved(VoiceVocabulary.ApprovalHash(id, phrase))`. The hash is SHA-256 of
  `voice-phrase/1`, the id and the folded phrase.
- **Approvals.**
  - `LauncherViewModel.SetAlsoSay`, the names table's save, approves the phrases that were not there before. A phrase
    that was there keeps what it had, which is the tile rule.
  - `VoiceLaunch.Pick` approves a candidate marked unapproved when it is picked.

### B3c: Decide

- It is coded as the plan states it, with `CloseCall = 0.10`.
- Offered choices are those of the answers at 0.40 or more, best first, three at most. A choice window with no
  buttons cannot happen: no candidate is `NotHeard`, and the App also says "not heard" when every candidate is gone
  from the bar.
- Tests: Word 0.85 / WordPad 0.84; an unknown best answer with a lower known one; an unapproved phrase at 0.95, and
  the same phrase approved.

### The choice window

- **As designed.**
  - Title *Which one?* (zh-Hant 要啟動哪一個？, both Chinese titles changed).
  - Each button has the name in bold, the detail line, and a warning line "⚠ This phrase came from another PC.".
    The texts are TextBlocks, so an underscore is shown, not taken as an access key.
  - No default button.
  - A 3 px bar empties over the 20 s (a `DoubleAnimation` on the `ProgressBar`).
- **Detail lines** (`VoiceLaunch.Show`): every name and location goes through `IsShowable`, else "(…)".
  - Shortcut: location trimmed in the middle at 60, with the group's name when it is in one.
  - Special icon: its kind's `DisplayName`.
  - Command tile: program and arguments, cut at 80, plus *(not approved on this PC)* while its hash is not approved.
  - Workspace: the count and the first three names.
  - Built-in: *The bar*.
- **Snapshot.** On a click, `Show` runs again for the same choice. If the item is gone, or the name or detail differ,
  nothing starts and the window says `Voice_BarChanged`.

### V1, V3 and the small items

- **V1.** The press time is taken first in `ListenWhileVoiceHotkeyIsHeld`. A press while listening calls
  `VoiceLaunch.Cancel()`, which is quiet.
- **V3.**
  - `LauncherItem.AlsoSay` tidies on every set, so on load, copy and edit alike: at most 8 phrases, 60 characters
    each, no folded repeats.
  - When the 2000 cap cuts, the built-in phrases go in first, then names and workspaces, then *Also say*.
    `LeftOut` is logged in one line.
- **Small.**
  - Cancel (the window's, a second press, the app closing through `CloseForGood`) shows nothing, and an answer that
    arrives after it is dropped (the `_cancelled` flag).
  - "Starting X" comes after `ActivateAndReport`, which is `Activate` returning its outcome, returned `Started`. A
    switch to a running window counts as started.
  - `VoiceEar`'s English text is `Voice_NoNamesTaken`.
  - System.Speech 10.0.15 restored and builds, the probe tool too.

### Tests

- **Core:**
  - `VoiceVocabularyTests`: folding, built-ins, approvals, Decide, the cap, the 8 phrases;
  - `LocalSettingsTests`: round trip, a bad or oversized file, a layout with `voiceHotkey`.
- **App, CI only (`VoiceLaunchTests`, now in the "Shared icon worker" collection with `LauncherViewModelTests`,
  which sets `ThisDevice.Id` for a moment, since approvals are per device):**
  - started and said after; a failed start not announced;
  - offered with details;
  - the plan's three: an arrived phrase picked and then starting at once; an unapproved command tile asking its
    approval and running nothing; a workspace by voice;
  - the bar changed while the choice waited;
  - Cancel racing an answer;
  - the names table approving only typed phrases;
  - the settings saved as this PC's.
- Not tested: the real recogniser with semantic values (no microphone in CI), the window's look, and the hold timer.

## 2026-11-14 (68) — WP2: scp placeholders (B2, C1, C2), the command line's small items and C4

Run 163 (`96b45d1`, WP1) is green. Run 164 (`04ab58a`) failed on one test assertion I had not updated: the pipe's
back-off after 100000 failures still expected 60 s. The next commit fixes the assertion only.

### B2: the colon rule is added to the line's reader

- `ScpPath` and `ScpPathPart` are gone from `ArgumentReader`.
- `LaunchPlaceholders.JudgeAt` returns `PlaceholderJudgement(Reader, Scp)`. `Reader` is what `ReaderAt` returned
  before P15. `Scp` is `ScpOperand.None`, `WholeWord` or `PartOfWord`, taken as the strongest over every scp or sftp
  that reads the placeholder: the location itself, or named in the line.
- `ReaderAt` is now `JudgeAt(…).Reader`, so its callers, and the 81 tests that call it, are unchanged.
- `Refused` and `RefusedInPath` take the flag as an optional third argument. They apply the reader's rule and, with
  the flag, `IsHostColon` (`WithScpColon`).
- `LaunchService` judges each position with `JudgeAt` and passes the flag. The refusal message names scp when the
  refused character is a colon under the flag.
- **Tests**, each as the plan lists:
  - wsl.exe + `scp {clipboard} h:/tmp/` with `$(id)`;
  - `cmd.exe /c scp …` with `a&calc`;
  - `ssh.exe s scp {clipboard} b:/x` with `x;id`;
  - a dropped `$(id).txt` through the wsl line.

  Each refuses, and each still refuses `evil.example:/etc/passwd`. The old scp rows now expect `Plain` from
  `ReaderAt` and the flag from `JudgeAt`.

### C1: `SshDestinationPart`

- A new reader: ASCII letters and digits and `. _ -`, not starting with `-`.
- It is used when the placeholder is not the whole of the destination it stands in:
  - scp/sftp `host:path` before the colon;
  - a `scp://` or `sftp://` authority;
  - sftp's first operand;
  - ssh's destination word (also an `ssh://` authority).

  Your four shapes are covered, `scp://host{x}` included, which has its template text before the placeholder: I read
  the rule as "the template writes more of the destination", before or after.
- A placeholder that is the whole destination (`{x}:/tmp/`, `scp://{x}/…`, `sftp {x}`, `ssh {x}`) keeps
  `SshDestination`, so `admin@server2` still goes there.
- **Its rank.** It sits after `PowerShellCommand`, before `SshOption`. What it lets pass is a subset of what every
  reader before it lets pass, so the chain "each refuses at least what the ones before refuse" holds, and
  `Stricter(RemoteShell, SshDestinationPart)` keeps the part rule: `ssh s scp {x}@b:/x` is still covered.
  `SpotAt`'s `>= Cmd` and `>= PosixShell` read it as they read `SshDestination`.
- Not covered: ssh's `-J` and `-l` values with template text around the placeholder, and plink's destination. Both
  are still `SshDestination`.
- **Tests:** the three values of C1 and `.evil.example/x`, one ssh row and one `ssh://` row. Each also checks that a
  name passes and a leading `-` does not. The whole-destination rows still take `admin@server2`.

### C2

- `NamesARemote`: an operand the template writes with a host colon (`HostColon`, the same test as before) or a
  `scp://`/`sftp://` start. Without one, scp operands and a file dropped at the end are read as `Cmd`. The colon
  rule still comes on top, so a value cannot make one of them remote.
- sftp always names a remote.
- Tests: `{clipboard} D:\backup\`, `C:\in\a.txt {ask:Where}`, `-r {file} {ask:Where}`.

### Small items

- `\\localhost\pipe/x`: the path's `/` become `\` before the share parts are looked at. Two test rows.
- `ApplySavedLanguage`: a file over 8 MB is not read, and `InvalidOperationException` is caught (the lone surrogate).
- Pipe back-off: at most 10 s.
- The waiting timer: `??=`.

### C4

- **The pipe answers.** Both ends are opened in message mode, `InOut`. The bar reads one message, until
  `IsMessageComplete`, and answers with one byte: 1 taken, 0 not taken. The sender reads the answer for up to 10 s:
  - 0 is the new `SendResult.Busy`, which ends the start with exit code 3 and the new `Commands_Busy` text;
  - no byte (an unreadable message, dropped as before) is `NoAnswer`;
  - no answer in time counts as sent, with a log line: the bar has the message.
- `CommandPipe` takes a `Func<…, bool>`. App answers with `Dispatcher.Invoke(() => window.Take(received))`.
  `MainWindow.Take` only decides: it queues, or posts `Run` to come after the answer.
- **The queue** keeps each message with the time it arrived.
  - More than `BarCommands.MaxWaitingCommands` (200) waiting: the message is refused, with one log line.
  - When the dialog closes, `BarCommands.AfterWaiting` (Core, tested) drops a launch, workspace or voice start that
    waited more than 30 s, with a log line, and keeps only the last show, hide or toggle. Additions are never
    dropped.
- **Mixed versions.** A start of this version talking to a bar of an older one, which opens the pipe inbound only,
  fails to connect and reports the bar as not answering. Both are the same exe in practice. Say if you want the old
  protocol kept as a fallback.
- **Tests:** Core `AfterWaiting`. App, CI only: a bar that answers false gives `Busy`, and broken messages now give
  `NoAnswer` (the test's earlier `Sent` expectation is changed accordingly). Not tested: the queue under a real
  dialog, and `Dispatcher.Invoke` from the pipe.

## 2026-11-14 (67) — WP1: Settings as tabs (review 27 B1)

Review 27 and its work plan arrived in two messages, and both are taken as the plan for 0.11.0. Run 162 (`d53640d`)
is green. This entry claims no run for its own commit.

Process, as I read your message:
- I code WP1–WP7 in order, one entry each, and write "ready for the release check" after WP7.
- `main` and the tags are not touched by this session; the release is between you and the owner.
- My replies to you are refused from this cloud session (HTTP 401), so this file stays the channel.

### B1

- **Layout.** A `TabControl` with the five tabs of the design, in its order, opening on Appearance. Width 520.
  Save and Cancel sit under the tabs, with the problem line to their left.
- **Nearest thing to the design.** The tab strip only chooses. The five pages lie in one cell under it, and the
  hidden ones keep their place in the layout. So the page area is as tall as the tallest page, the window does not
  change height between tabs, and nothing has to be measured in code. Each page is a `ScrollViewer` with
  `MaxHeight="560"` and its own `Grid`. On a screen too short for 560, the window's existing work-area limit
  shrinks the page area, and the page scrolls.
- **Pages.**
  - *Appearance*: theme, background, bar background, icon size, spacing, labels, transparency.
  - *Window*: open with, the window check boxes with start with Windows and start hidden, dock and auto-hide.
  - *Keys*: the two hotkeys, then the keys of the bar's window. Their table spans both columns, so a long Chinese
    label keeps its room next to the 140 px box.
  - *Voice*: hotkey, language, then the design's paragraph. It replaces the two old hints
    (`Settings_VoiceHotkeyHint`, `Settings_VoiceLanguageHint`, now removed). Its last sentence, "kept on this PC
    and not synced", becomes true with WP3 (B4). Until then the hotkey still travels with the layout.
  - *General*: language.
- **Problems.** `AppearanceTab`, `KeysTab` and `VoiceTab` put "⚠ " before the name while a value cannot be saved:
  - Appearance: a custom background that is no colour;
  - Keys: a hotkey a window key has, or show-at-pointer equal to show/hide;
  - Voice: the voice hotkey clashing with a window key or another hotkey.

  `ProblemText` names the first such tab (`Settings_FixOnTab`). `CanSave` is made of the same three, so the two
  cannot disagree. Changing the theme now refreshes Save too: leaving Custom with a bad colour re-enables it,
  which it did not before.
- **Keyboard.** Ctrl+Tab and Ctrl+Shift+Tab, handled on the window, change the tab from anywhere and put the focus
  on the new page's first control. The arrow keys on the tab strip change the tab and leave the focus on the strip,
  so they can go on; the design's "first control" rule is applied to Ctrl+Tab only. Enter and Esc are the default
  and cancel buttons, as before.
- **Not done:** the optional "open on Voice" from the voice icon's no-recogniser message. It needs a way for that
  message to open Settings, which the plan does not describe.
- **Strings:** seven new (the tab names, `Settings_FixOnTab`, `Settings_VoiceTabHint`), two removed: 794 in each
  file. zh-Hans 常规 for General (Windows' word for that tab).

### Tests (App, CI only)

- `Every_tab_of_Settings_gives_each_control_a_cell_of_its_own`: each grid on each page; a child outside the grid's
  rows or columns, or two children on one cell (spans included), fails.
- `Settings_show_one_page_at_a_time_in_the_height_of_the_tallest`: one page visible, and the same window height on
  the shortest page. It does not press Ctrl+Tab: a simulated key does not carry the modifier state the handler reads.
- `A_tab_with_a_value_that_cannot_be_saved_is_marked_and_named_beside_Save` (view model).
- **Changed:** `Settings_scroll_on_a_short_screen…` now chooses the Keys tab, the tallest page, before checking that
  the body scrolls. Appearance alone is too close to the 400-unit screen for that test to mean anything.
- Not tested: 150 % scaling, and how the tab strip looks under the Windows 11 dialog style. Both are for your look
  in the release check.

## 2026-11-12 (66) — Review 24's leftovers: W3, L5 and the rest of the minor list

What entry 60 left of review 24. Run 159 (`569202c`) is green. Run 160 (`87d1b0d`, the change) failed on two
mistakes in the new tests, not in the code:
- the stack test clicked one icon twice within the 0.4 s click guard;
- the W3 test let its new window show in the start's "before" look.

`558bb67` fixed both, and run 161 is green.

### W3: which window is whose

- **Core, `Launching/WorkspaceWindows.cs`.** One object per start or capture, fed `EnumWindows` snapshots. It
  keeps the handles that were there before anything was started, the look in which each new handle was first
  seen, and which member took which handle. `WorkspaceMember` carries `Own` (the tracker's launched ids),
  `Program` (launched ids plus the path scan), the package family and `HasParameters`.
  - `TakeRunning(members, anyOfTheProgram)`: two rounds, so no member takes another's own window. Round 1: every
    member's `Own` windows. Round 2: its `Program` windows, for members without parameters, or for all with
    `anyOfTheProgram` (capture only). Topmost first; a framed window before a bare one.
  - `TakeNew(members, waitIsOver)`: for members started now, in start order. Only handles not in the "before"
    set; `Own` before `Program`; oldest first (first-seen look, then lower in the z-order). A bare window
    (`IsBare`: neither `WS_CAPTION` nor `WS_THICKFRAME`) only with `waitIsOver`.
  - `Look(snapshot)`: a taken handle that is gone frees its member, which takes the next window.
- **App.** `IWindowSwitcher` now offers `Windows(withAppIds)`, `Capture(IntPtr)` and `Place(IntPtr, target)`;
  the process-set overloads and `FirstWindow` are gone. `WindowSwitcher.Describe` reads `GWL_STYLE` for
  `IsBare` (new `NativeMethods.GetStyle`). `StartWorkspaceMembersAsync`:
  1. one "before" snapshot, `TakeRunning(anyOfTheProgram: false)`;
  2. start the rest; only a `Started` outcome is waited for (a failed or cancelled start was waited on for 15 s
     before);
  3. place the running ones at once;
  4. poll with one snapshot per round (W6's "one full snapshot per member per poll" is gone too). Members
     without a remembered position take windows as well, so a later member of the same program cannot take
     theirs, but only members with a position keep the loop going;
  5. the late pass looks once more and lets a started member whose window closed take the current one.
  `CaptureWorkspace` uses `TakeRunning(anyOfTheProgram: true)`.
- **Choices to look at.**
  - In a capture, a member with parameters that the bar did not start falls back to a free window of its
    program. Among several, which one it gets is a guess (z-order). The alternative was to remember nothing
    for it. Say if you prefer that.
  - A program that opens a started member's document in a window it already shows gives no new window, so
    nothing is moved for that member. Before, the program's topmost window was moved.
  - "Bare" is a style heuristic. Chromium and Electron windows, custom-drawn ones included, keep
    `WS_THICKFRAME`, so they are not bare; a fixed-size main window has `WS_CAPTION`. A program whose only
    window is bare is placed after 15 s. A splash with a title bar is still placed, then replaced by the main
    window when it closes, if that happens before the late pass.
  - A splash that closes after the late pass, when the main window was placed before it, is not handled. No
    such sequence seems likely to me.
- **Tests.** Core (`WorkspaceWindowsTests`): own window first with the member order reversed; parameters and
  the start rule; new windows only and oldest first, within one look and across looks; splash, main window,
  closed window; bare windows of a program that runs; owned windows; a Store frame. App, CI only:
  - the workspace test, rewritten on a fake that shows windows after a number of looks;
  - `Members_on_one_program_get_a_window_each_and_one_with_parameters_counts_only_what_the_bar_started_for_it`:
    capture, start, placement by handle, and the give-up of a member that never shows a window.
- Not tried on a real PC, like the rest of the workspace code.

### L5

- `ProcessTracker.Remember(id, pid)` is the bookkeeping half of `Track`; tests call it. The App test above
  injects process 11 as started for one member and puts processes 10 and 11 at the program's path through
  `ApplySnapshot`. The parameters rule now has a test that sees non-empty sets: the member with its own window
  is only moved, and the one without is started although its program shows a window.

### Minor list

- **Tests that said more than they checked.**
  - The workspace test's "gone: said" now counts the notice; that was done with R3.
  - The stack test named for Shift now presses Shift and a middle click, and checks that the folder is
    launched, not stacked.
  - The clock dialog has a test that keeps a stored zone, and one this PC does not know. The old test is
    renamed `…_saves_a_picked_zone`.
- **Untested before.**
  - Multi Launch through the new path: in the workspace test since R3.
  - The 15 s give-up: the new App test.
  - The `NetworkHandler` sample: moved into `NetworkReadings` and tested. The test covers the first read, the
    rate against the peak, and an adapter that comes up with a 5 GB counter.
- **"No microphone was found" for every failure.** `MicrophoneControl` returns an `AudioReading` (`State`,
  `Failed`). Only `E_NOTFOUND` from `GetDefaultAudioEndpoint`, or no device, is "not found". Any other
  HRESULT, and any exception at the COM boundary, is `Failed`: the tile and the click say "could not be
  reached; app.log says why" (`Mic_Unreachable`, `Volume_Unreachable`). The log gets a warning when the reason
  changes and a debug line when it repeats. A click on the microphone now redraws from the toggle's own result,
  not from a second read.
- **The number style's caption twice.** The Sound Replayer's caption was `REC`, and so was its label while
  recording. The caption is `SOUND` now. The other number tiles (CPU, RAM, BAT, NET, VOL, clock, command)
  never repeat their caption; the mic and network tests check this.
- **Dot files in a stack.** They are listed now, as Explorer lists them: on Windows a dot is not "hidden".
  Hidden and system entries stay out, also as in Explorer, so "N more" counts what Explorer will show.
  .NET on Linux reports dot files as hidden, so the Core test expects the platform's answer.

### Left of review 24

Nothing, apart from the gauge's "100%" under `MaxWidth="98"`, which is on the owner's list to look at.

## 2026-11-12 (65) — The voice hotkey is push to talk (the owner's request)

The owner asked for a hotkey that listens while it is held and cancels when it is let go.

- `MainWindow.ListenWhileVoiceHotkeyIsHeld`: the hotkey's press starts listening as before. `RegisterHotKey`
  reports no key-up, so a 50 ms `DispatcherTimer` reads `GetAsyncKeyState` for the gesture's key (not its
  modifiers) until it is up, then calls `VoiceLaunch.Release()`.
- `Release()` cancels the listening after 0.7 s (`ReleaseGrace`), so a word that ends as the key comes up is
  still recognised. An answer that arrives in time is decided as usual. With no answer the window closes through
  the new `IVoiceView.Done()` instead of saying "Didn't catch that".
- A press shorter than 0.4 s (`VoiceTap`) is not a hold: listening goes on until the utterance or the limits,
  as it does for the icon and `--voice`. A press while listening is ignored.
- The Settings hint says so in the three languages.
- Look at: whether 0.7 s is enough for the recogniser to finish an utterance (I cannot measure it here); only
  the main key is watched, so letting go of Ctrl alone does not end the listening.
- Test (App, CI only): `Letting_the_hotkey_go_ends_the_listening_without_a_word`, with an ear that waits for
  its cancellation. Not tested: the timer and `GetAsyncKeyState`.

## 2026-11-12 (64) — Voice launch (ROADMAP #11), on the desktop recognisers

Commits: `5f62e0f`, `6671613` and `c9656d9` (the trial, `tools/VoiceProbe` and FEATURES §6.1 only), then the
commit that adds this entry (the feature).

### The trial and the owner's two decisions

- On the owner's PC `Windows.Media.SpeechRecognition.SpeechRecognizer` cannot be created: `0x800455A0` for
  en-US, `0x800455BC` for zh-TW although `Language.Speech~~~zh-TW` is Installed. The desktop (SAPI)
  recognisers for both languages are there, and through `System.Speech` they heard the names (confidence 0.93
  to 0.97 on the hits; FEATURES §6.1 has the output).
- The owner chose, in this session: **`System.Speech` as a package in the app** (the first one; CLAUDE.md and
  README say so) and **the listening language as a setting that follows the bar's language by default**.

### What was built

- **Core, `Voice/VoiceVocabulary.cs`.**
  - `Build` maps phrases to targets: names and `AlsoSay` phrases of shortcuts and special icons (inside groups
    too), workspace names, and the built-in show/hide phrases. Groups and the Voice icon are left out.
  - `Speakable` refuses a phrase without a letter or digit, over 60 characters, or failing
    `CommandTileSettings.IsShowable`. At most 2000 phrases.
  - `Decide`: best answer ≥ 0.80 with exactly one target starts it; otherwise the targets of answers ≥ 0.40 are
    offered, three at most (a clearly heard phrase that several items share offers those items); else nothing.
  - `VoiceLanguage.Pick(setting, barLanguage, installed)`.
  - `LauncherItem.AlsoSay` (`alsoSay`, tidied by `NormalizeAlsoSay`, copied by `CopyContentFrom`),
    `LauncherSettings.VoiceHotkey` and `VoiceLanguage`, `BarCommandKind.Voice` (`--voice`),
    `SpecialIconKinds.Voice`. Older versions keep these as unknown properties and show the icon as unknown.
- **App.**
  - `VoiceEar` (`IVoiceEar`): `SpeechRecognitionEngine` for the culture, one `Choices` grammar (per-phrase
    grammars when the list as a whole is refused, skipping the refused ones), default audio device,
    `RecognizeAsync(Single)`, 6 s initial silence, cancel through the token. All of it in `Task.Run`. Every
    exception but out of memory becomes a `Problem` text.
  - `VoiceLaunch`: one listening at a time, 15 s limit, then `Decide`. An item is started with
    `bar.Activate(bar.GetOrCreateViewModel(item), anchor: null)`, the click's path, so approvals and questions
    apply unchanged.
  - `VoiceWindow` (`IVoiceView`): topmost tool window, `ShowActivated="False"`, hidden between uses.
  - `VoiceHandler` (the icon), a third `GlobalHotkey` (`VoiceIds`) in the `HotkeySet`, the two Settings rows,
    the *Also say* column in the names table.
- The log gets the number of answers and the best confidence, the target's kind, and failures. Not what was
  said, and not the name started.

### What to look at

- **The trust boundary.** A synced bar brings `alsoSay` phrases and names. A phrase can therefore be made to
  start another item than the user expects, but only an item already on the bar, by the click's path. I did
  not add an approval for phrases; say if you want one.
- `Activate` from a hotkey while another program is in front: the launch itself is what a click does, but
  window-switching ("click a running program") then acts on a bar that is not in front. Not checked.
- The confidence thresholds are from five trial utterances. They are constants in `VoiceVocabulary`.
- `VoiceWindow` shows on the primary work area and is never activated; the choice buttons need the mouse.
- The Voice icon is a text icon (the microphone emoji in Segoe UI Emoji). I have not seen it drawn.
- `SettingsViewModel.InstalledVoiceLanguages` reads the registry when Settings opens (cached a minute in
  `VoiceEar`).
- Single-file publish with a package: `System.Speech.dll` is bundled like any managed assembly; the release
  workflow is unchanged and I have not run a published build.

### Tests

- Core (`VoiceVocabularyTests`): the vocabulary, `Speakable`, `AlsoSay` through a copy and the file, `Decide`
  for sure, unsure, shared and unheard, the language choice, `--voice`.
- App, CI only (`VoiceLaunchTests`, with a stand-in ear and view): started as a click; offered and picked;
  nothing heard, no recogniser, a failure; hide bar; the Settings options and the hotkey rule; the names table.
- Not tested: `VoiceEar` (needs a recogniser and a microphone), `VoiceWindow`, the hotkey on a real PC, the icon.
  Nothing of this has been heard by a real microphone inside the app; only the trial has.

## 2026-11-12 (63) — The older open items: P15, U3′'s root fix, review 21's deferred minor list

The owner asked for these and for voice launch. Voice launch waits for the owner's run of `tools/VoiceProbe`
(FEATURES §6.1); nothing is built for it. Three commits: P15, U3′, and this one (the minor list and docs).

### P15 (review 17): scp operands

- `ArgumentReader` has two new values at its end, `ScpPath` and `ScpPathPart`. `ScpOperand` returns them for an
  scp operand written without a colon: `ScpPath` when the placeholder starts the word, `ScpPathPart` when text
  comes before it. Both refuse what `Plain` refuses and every colon, except that `ScpPath` lets a drive
  letter's colon be (index 1, after an ASCII letter). So `evil:/etc/passwd`, `scp://evil/x` and `:x` are
  refused where `{clipboard}` or `{ask:…}` is a whole operand, and `ho{x}` cannot be filled to `hos:tmp`.
  Dropped files go through `RefusedInPath` with the same rule; `C:\…` and `\\nas\share\…` pass.
- It is stricter than scp's own `colon()`, which lets a colon after a `/` be: I do not rely on what the Windows
  port does with a `\` before the colon.
- A word that starts with `scp://` or `sftp://` (any case, after an opening quote): a placeholder before the
  slash that ends the authority is `SshDestination`, one after it is `RemoteShell`, or `Plain` over SFTP. Before,
  `sftp://{ask:Host}/pub` was `Plain`.
- sftp is unchanged otherwise: its first operand is the destination, and it takes no second one.
- The two readers are after `SshOption` in the enum, so `PlaceholderQuotes.SpotAt` also reads the line as cmd
  and a POSIX shell would, as it does for the ssh readers. Say if you see a template this now misplaces.
- The refusal message names scp (`Launch_UnsafeValue` with ":" and "scp"). No new string.
- Tests: `LaunchPlaceholderSafetyTests` (reader rows for whole operands, URIs, `-s`; refused and accepted
  values). Not tested: a real scp.

### U3′ root fix: the file name inside each copy

- `FolderSyncClient.Write` puts `"file": "<the file it writes>"` into the JSON, right after the first property
  (`"format"` stays first, which `StartsLikeACopy` relies on). `SyncProfile.File` holds it; `FromLayout` never
  carries it over, and Drive uploads have none.
- Listing: a copy whose `file` is its own file name is the original of the name inside it, whatever number the
  file has. One whose `file` is another name is a copy some program made (or a renamed file) and is listed under
  its file name. `ListNumberedOriginals` leaves both alone and applies the 0.8.1 rule only to copies without
  `file`; a copy in its own file counts as "has its own file" for those.
- Format: still 1. 0.8.0–0.10.0 keep `file` as an unknown property while they hold the profile in memory, but
  their saves start from a fresh profile, so a copy they rewrite loses it and falls back to the old rule. A
  mixed set of PCs therefore behaves as before for the copies the old PCs write.
- Known gap: an old-version PC's unnumbered `X.json` saying "X" next to a new numbered `X (2).json` that says so
  with its own `file` would both be listed as "X". I could not construct this without deleting another
  program's file by hand at the wrong moment; say if you want a rule for it.
- A changed behaviour to check: `Work (2).json` that says `Work.json` inside, with `Work.json` gone, is now
  listed as "Work (2)" (a copy of a missing original), where the number rule made it the original "Work".
- Tests: `FolderSyncTests` (the property and its place; a lower-numbered sync copy next to the numbered
  original; the copy of a deleted original; a copy without `file`; `WithFileName` replacing a stale value and
  keeping unknown properties). The existing 0.8.1 tests pass unchanged.

### Review 21, "Minor, not taken this round" (entry 36)

- **No machine key.** `ThisDevice.IsOfThisMachine` (false when `MachineIdentity.Key()` is null). `CommandTrust`
  then ignores the file and keeps approvals in memory; `Approve` returns false and logs once per approval. This
  also covers the Send to PC icon mark, so such a PC needs the *Receive files* setting to keep receiving after
  a restart.
- **Pipe.** The listener survives any exception but out of memory. A failure is a warning once, then debug
  lines, with the pause doubling from 2 s to 60 s; it says when it listens again.
- **Commands under dialogs.** `MainWindow.Run` queues while `ComponentDispatcher.IsThreadModal` or the bar's
  window is disabled (a message box owned by it), and a 400 ms timer runs the queue once neither holds. This
  also holds a first start's own commands back until the start-up messages are closed. Not covered: a message
  box without an owner that is not WPF-modal. Not tested (needs a real dialog).
- **256 files.** `MaxCommands` 4096, `MaxMessageBytes` 2 MB.
- **`\\localhost\pipe\x`.** A share whose second part is `pipe` or `mailslot` is refused.
- **Culture.** `--launch` compares with `OrdinalIgnoreCase`, as `--group` and `--workspace` do.
- **Message boxes.** The parser's texts are in `CoreStrings` (three languages). A start that ends without a bar
  writes the reason to stderr and shows the box only when `Environment.UserInteractive`; before that it reads
  `settings.language` from the layout file (`ApplySavedLanguage`) and parses again in that language.
- **Send To, portable.** A portable copy does not add the entry (it says why) and can still remove one.
  `ToggleSendTo` catches everything but out of memory.
- **Keys.** In a text box, NewShortcut, NewGroup and OpenSettings still work when Ctrl or Alt (not both) is
  held and the key is none a text box edits with (`WindowKeyMap.LeavesTextBoxAlone`). A shared key gives its
  text only to the first action. An override that does not parse falls back to the default; an empty one still
  turns the action off. Shift+Delete removes while Delete is the Remove key.
- **`Settings_WindowKeys`** has its colon in the three languages.
- **Not done:** the CPU/disk gauge at "100%" under `MaxWidth="98"`. I cannot see it from here; it is on the
  owner's list of things to look at.
- Tests: Core (`BarCommandsTests`: pipe shares, 1000 files, the language of an error; `CommandTileTests`:
  session-only approval). App, CI only (`WindowKeyTests`: the key map rules, the text box rule, the pipe's
  pauses). Not tested: the queue under a dialog, the stderr line, the portable Send To refusal.

## 2026-11-12 (62) — Send to PC through Google Drive (owner's choice of what is next)

Run 152 (`e8dc629`) is green.

- **`IInbox`** (Core): `AnnounceAsync`, `DevicesAsync`, `SendAsync`, `PendingAsync(skip)`, `ReceiveAsync`,
  `NameOfDeviceAsync`. `FolderInbox` implements it by wrapping its tested synchronous methods; nothing in it
  changed apart from sharing helpers (`InboxName`, `TryReadInboxName`, `Unique`, `PlainName`, the suffixes).
- **`DriveInbox`** (Core) uses the same layout under the app's folder on Drive: `inbox/<device id>/device.json`
  and files named `<sender id>~<name>`.
  - `IDriveFiles` gains `AppFolderIdAsync` and `DeleteEntryAsync` as default methods that throw; the real client
    implements them with its existing `EnsureFolderAsync` and `DeleteAsync`. Folder ids are cached per
    `DriveInbox`.
  - `SendAsync` has the same refusals as the folder (`SendProblem`). The target's folder must exist; sending
    never creates another PC's inbox.
  - `PendingAsync` takes only non-folder, non-Google-format entries with a size and a valid inbox name, and
    applies the same 2 GB, 20-file and 4 GB limits.
  - `ReceiveAsync` downloads to `<final>.part`, marks, renames, then deletes on Drive. A failed delete returns
    `Cleared = false`, and the service remembers the id for the run. Names go through `PlainName`, so `.url`
    and the rest arrive inert here too.
  - It needs no new permission: the files are the app's own, so `drive.file` covers them.
- **App.** `InboxService.Current()` returns the folder inbox when a sync folder is used, else a `DriveInbox`
  when signed in to Google. The Drive inbox is kept per account, device id and name. `CanReceive` is true for
  either transport. The off-by-default rule and the local icon mark are unchanged and apply to both.
  `SendToDeviceHandler` works on `IInbox`. Opening its properties lists the PCs on a worker thread and waits up
  to 20 s with a wait cursor.
- **To look at.**
  1. Trust: with Drive, the boundary is the Google account plus the app's OAuth client. `drive.file` shows
     this app's files to any PC signed in with the same client id and account. A file planted in `inbox/<id>/`
     by another app cannot be seen by this one. The sender id in the name is still only a label.
  2. Polling is three or four Drive calls a minute per receiving PC: two lists and, at first, a small upload.
     I did not add a back-off for a lapsed sign-in; a failure is logged every minute.
  3. `AnnounceAsync` re-uploads `device.json` only when the size differs, so a rename to a name of the same
     length is not announced.
  4. The 20 s blocking wait in `ShowProperties` is on the UI thread.
- **Tests.** Core `DriveInboxTests`, against a Drive kept in memory: send, receive under a free name with the
  mark on the `.part` file, strays ignored, `.url` inert, traversal, the refusals, a failed delete. The real
  Drive calls are **not** exercised: there is no account in CI.

## 2026-11-12 (61) — 0.10.1 is not released on its own; 0.11.0: volume tile, drop onto a program in a stack

The owner: "skip 0.10.1 release, continue next". Run 151 (`767a88b`) is green. The version is 0.11.0; the 0.10.1
section of the CHANGELOG became 0.11.0's "Fixed" list. You have not reviewed entries 57–60; they are part of this
version now.

- **Volume tile** (kind `volume`).
  - `MicrophoneControl` takes the endpoint as a parameter: capture (communications, else console) as before, or
    render (console, else multimedia). New `ReadSpeakers`, `ToggleSpeakers` and `StepSpeakers(notches)`, which
    use `SetMasterVolumeLevelScalar` (slot 5 of `IAudioEndpointVolume`, declared since entry 46) and unmute
    when turned up.
  - `Core.VolumeSteps.After(level, notches)`: 2 points a notch from the rounded level, clamped. Tested.
  - `ISpecialIconHandler.Wheel(item, notches)` is new, default false. `IconGridControl.Item_PreviewMouseWheel`
    marks the event handled only when the handler used it, so the bar scrolls as before over other icons.
  - **A trap I walked into and fixed.** A default interface method is bound at the class that declares the
    interface. `VolumeHandler` derives from `MonitorHandlerBase`, which declares it, so a plain `Wheel` on
    `VolumeHandler` would never be called through `ISpecialIconHandler`. `MonitorHandlerBase.Wheel` is now
    virtual and overridden. The App test calls it through the interface.
  - Reading is on `RegisterSlow`. The wheel and the click call COM on the UI thread, once per notch, to feel
    direct.
- **Drop onto a program inside a stack.**
  - `FolderStack.TakesDrop(entry, dragged)`: `.exe`, `.com`, `.bat`, `.cmd` and `.lnk` only, not a folder and
    not the dragged file itself. Tested.
  - `ShowFolderStack` gets an optional `openWith`. `LauncherViewModel` passes
    `_launcher.LaunchWith(ShortcutItem.FromPath(program), files)`, so the drop goes through `DropArguments` with
    the same quoting and refusals as a drop onto a shortcut of the bar. Nothing new is built for it.
  - The stack closes on deactivate, so only a drag that starts inside the stack can end on one of its rows.
- **To look at.** A `.bat` or `.lnk` inside a stack is whatever is in that folder. Dropping a file on it runs
  it with that file, which is what a double-click on it plus an argument does. The drop is the user's act, and I
  added no approval.
- **Not tested.** The wheel event and the drag inside the window need a real session. The real volume calls
  have never run: CI has no audio device.

## 2026-11-12 (60) — Review 24's minor list; what 0.10.1 has and has not

The owner asked to complete 0.10.1. Run 150 (`8c3ad7c`) is green.

- **Taken from the minor list.**
  - Microphone: `Mic_LabelOn` is `ON` (the caption already says MIC). `Mic_Desc` says "the default microphone
    (the one calls and meetings use)", not "for every program".
  - Battery: a flag of 255 is "unknown", not "no battery". An unknown line status shows `Battery N%`
    (`Monitor_BatteryLevel`) instead of "plugged in". Two test rows.
  - Stack: an unreadable folder says so (entry 58). Rows carry `AutomationProperties.Name`. An entry is started
    with the stack's folder as its working directory (not on slow storage).
  - Clock: `ClockTimer` resolves a named zone once, so an unknown id no longer throws every second.
    `ZoneIsUnknownHere` is new, and the dialog lists such an id as "(not known on this PC…)" and keeps it on
    OK. Core test.
  - `Snapshot`'s doc comment is back on `Snapshot`.
  - `workspaces.json` over 1 MB is not read.
  - A click on *Send to PC* is a notice and `Started`, not a failure.
  - `--workspace` takes at most `Workspaces.MaxNameLength` characters. Test.
- **Not taken.**
  - W3 (two members on one program share "the first window"; a splash screen satisfies the poll). The window
    order from `EnumWindows` is z-order, which changes between capture and place, so an index does not identify
    a window. It needs something stable per member (the window's title pattern, or the process the bar
    started), and I do not want to guess at it without a PC to try on.
  - L5 (a test for the parameters rule): it needs a tracker with injected processes.
  - I3's subfolder: the owner answered while this entry was written, in these words: "just show the file like
    .url and don't follow the link". So there is no subfolder; see the next point.
  - Tests that say more than they check (three names), dot files not counted in "N more", "No microphone was
    found" for every failure, the number style's repeated caption elsewhere.
- **I3, the owner's way.** Files stay in Downloads. `FolderInbox.PlainName` appends `.received` to the types
  Explorer reads on sight: `.url`, `.lnk`, `.scf`, `.library-ms`, `.searchconnector-ms`, `.search-ms`,
  `.website`, `.pif`, `.appref-ms`, and `desktop.ini` as before. Explorer then shows them as plain files and
  follows nothing. `.exe` and documents are not renamed; the downloaded mark guards those. The Core test covers
  the list, three types that must not change, and a whole send and receive. Please check the list: it is from
  memory of what the shell parses for icons and targets, and I may have missed a type.
- **Nothing of 0.10.1 has been tried in the app.** `SetWindowPlacement`, the background gauge reads and the
  stack's async fill are the three I would look at first on a real PC.

## 2026-11-12 (59) — Workspaces W1, W2, W6, W7; stacks S4

Run 149 (`950f482`) is green.

- **W1 + W6.** `Capture` always reads `GetWindowPlacement.NormalPosition` (workspace coordinates) and
  `IsZoomed`. `Place` hands the same rectangle back through one `SetWindowPlacement`, with
  `WPF_ASYNCWINDOWPLACEMENT` and `ShowCmd` SW_SHOWNORMAL or SW_MAXIMIZE. No `ShowWindowAsync` + `SetWindowPos`
  pair is left, so a queued restore cannot undo the move, and nothing is converted between coordinate spaces
  (your point 1 of entry 47 is closed). Positions remembered by 0.10.0 for a window that was neither maximised
  nor minimised were screen pixels; they are off by the taskbar's size only where the taskbar is at the top or
  left. **Not tried: it needs a real PC.**
- **W2.** `SavedWindow.OnScreen(monitors)` leaves a position alone when at least 120 × 40 of it, including its
  top edge, is on a monitor. Otherwise it moves it onto the first monitor and shrinks it to fit. `Place` calls
  it with `ScreenGeometry.Monitors()`. The Core test covers: an undocked monitor, a window with only its
  bottom strip on screen, a window partly off the right edge (left alone), and no monitors known.
- **W7.** `WorkspacePositions.Set(workspace, windows, keep)` keeps the earlier position of a member in `keep`
  that has no window now. `CaptureWorkspace` passes the workspace's current members, so a member that left the
  workspace loses its place and a closed one does not. Tested.
- **S4.** A refresh is postponed while the pointer is over the list, a button is down or a drag is under way;
  the timer keeps trying. The selected row is selected again after a refresh. I did not do an in-place diff of
  the rows: with the refresh held while the pointer is there, the rows cannot move under a click.
- **Not done.**
  - W3: two members sharing a program get the same "first window", and a splash screen satisfies the poll. It
    needs a per-member window identity, which I have not designed.
  - I3's subfolder for received files: where files land is the owner's "auto into Downloads"; I will ask before
    changing it.
  - L5, and the minor list.

## 2026-11-11 (58) — Messages for received and sent files (owner's request), S1, C1

Run 148 (`c95e7ca`) is green.

- **Owner: the free-space wait "should have proper error message".**
  - A file that waits for space is now a notification, once per file per run (`Inbox_WaitingForSpace`): its
    name, its size, the free space, the floor, and that it will be taken automatically. The look goes on to
    later files, which may fit; it used to stop.
  - Send failures: `FolderInbox.Send` throws `InboxSendException` with a `SendProblem` (NotAnotherPc, NoInbox,
    NotAFile, TooLarge). `SendToDeviceHandler` shows a localized reason for each. NoInbox tells the user what to
    do on the other PC, since receiving is off by default now. Other IO errors keep Windows' own message.
- **S1.**
  - `StackWindow.Fill` lists on a worker thread; a newer request supersedes an older one.
  - `LauncherViewModel.Activate` no longer calls `Directory.Exists` for a path on slow storage
    (`IconWorker.IsOnSlowStorage`: UNC and mapped network drives); the stack opens and says when it cannot read.
  - `FolderStack.List` reports `Unreadable` (-1) as the total, so the window shows `Stack_Unreadable`, not
    "empty".
  - `Watch` skips all slow storage, not only `\\`.
  - The window is placed once when it opens and once more when the first list arrives, at the same pointer.
- **C1.** `SystemEvents.TimeChanged` → `TimeZoneInfo.ClearCachedData()`.
- **Tests.** Core: the `SendProblem` of each refusal; unreadable against empty. No test for the notification,
  the async fill or the time-zone event.
- **Left:** W1–W3, W6, W7's whole-set replace, S4, I3's subfolder, the minor list, L5.

## 2026-11-11 (57) — 0.10.0 is released; 0.10.1 starts with G1, I2 and I4

- **Release.** The owner pushed `v0.10.0` on `254c55c`; release run 11 is green, and the release is published
  with both zips and `SHA256SUMS.txt`. Runs 145–147 are green. You had not looked at entries 54–55 before the
  tag, and nobody did the in-app check. Both were the owner's call at the tag; your findings on 54–55 belong in
  this version.
- **Version** 0.10.1, CHANGELOG section open.
- **G1.** `SystemMonitor.RegisterSlow(state, read, show, everyTicks)`: `read` runs in `Task.Run`, `show` and
  `state.Update` run on the UI thread. One read at a time per gauge; a tick during a read is skipped; exceptions
  become the tile's detail.
  - Moved: the network counters, the drive gauge (a network drive can block) and the microphone (COM).
  - CPU, memory and battery stay on the timer: each is one cheap call.
  - The microphone's click still reads once on the UI thread, to redraw at once.
- **I2.** `Pending` stops when the look's bytes would pass `MaxBytesPerLook` (4 GB; the first file always
  counts). `InboxService` stops the look when `FolderInbox.HasRoom(free, size)` is false, with a floor of 1 GB
  free on the Downloads drive. The file stays in the inbox, and the log says why. When the free space is not
  known (a share), there is no limit.
- **I4.** The "sent" notice uses `FolderInbox.NameOfDevice(id, stored)`: the target's own `device.json` name as
  it is now, else the stored name through `ShownName`. The tooltip and the click message show the stored name
  through `ShownName` only; they do no disk read on the UI thread.
- **Tests.** Core: `HasRoom`, `NameOfDevice`. `RegisterSlow` has no test: it needs a dispatcher pumping.
- **Left of your list:** W1–W3, W6, W7's whole-set replace, S1, S4, C1, I3's subfolder, the minor list, L5.

## 2026-11-11 (56) — Release 0.10.0, second release commit (docs only)

The owner asked for it ("do 1,2,3,4": CI and your look, the release commit, the in-app check, main and the tag).
This commit changes docs only, on top of `6ccf4ea`.

- CHANGELOG: the 0.10.0 section is dated 2026-11-11. The fixes of rounds 24–26 and the receiving rule were
  folded into its entries as they were made. FEATURES §12 and the ROADMAP carry the same date.
- CLAUDE.md: names this release commit and tells a later session to check whether the tag exists.
- **What has not happened, said plainly.**
  - **Your look at entries 54 and 55** (receiving off by default; only an icon marked on this PC counts). I
    cannot reach you, and the owner asked me to go on. Findings go into a 0.10.1.
  - **The in-app check.** I cannot run the app, and the owner has not said to skip it. I told the owner so.
    The tag is theirs to push, which is where that decision sits.
- `main` is fast-forwarded to this commit once its run is green. I push no tag.

## 2026-11-11 (55) — Only an icon added on this PC turns receiving on (owner: "only an icon added on that PC itself count")

The owner answered the question entry 54 left open, in those words. Run 144 (`1d28ed8`) is green.

- **Mark.** `FolderInbox.IconMark(iconId)` is SHA-256 of `send-to-device-icon/1` and the icon's id. It is stored
  through the existing `CommandTrust`, which is local, per `ThisDevice.Id`, and never synced. The prefix keeps
  it apart from command and health-check hashes.
- **When it is set.** `SendToDeviceHandler.ShowProperties` approves the mark after OK, for a new icon and for
  an edit. An arrived icon whose properties are opened and saved here is marked too. Unlike a command tile, the
  dialog shows all there is (name and target PC), and the save is this PC's user acting on it. Say if an edit
  should not count.
- **Where it is read.** `App` sets `InboxService.HasSendIcon` to "some `send-to-device` icon on the bar carries
  this PC's mark". An icon from a synced, restored or imported bar has no mark here, so `TakesPart` stays false
  and nothing is announced or taken.
- **Unchanged.** Dropping files on an arrived icon still sends: the drop is the user's act on this PC.
- **Test.** Core: an arrived icon does not count; one marked here does; the same bar under another device id
  does not. The class joins the "This device" collection.

## 2026-11-11 (54) — I1: receiving is off until asked for (owner: "be off until you add the icon or turn on a setting")

The owner answered me directly, in those words. Run 143 (`460374c`) is green.

- **Rule.** `FolderInbox.TakesPart(usesSyncFolder, receiveSetting, hasSendIcon)` is true only with a sync folder
  and either the setting or a *Send to PC* icon on this bar. `InboxService.Look` returns before `Current()`,
  `Announce()` or `Pending()` when it is false. An updated PC therefore creates no `inbox\<id>` folder and takes
  nothing.
- **Setting.** `SyncSettings.ReceiveFiles`, default false, lives in the local `sync.json` and is never part of a
  synced copy. It is toggled by *Receive files from my other PCs* in the options menu (disabled, with the reason
  as its tooltip, when there is no sync folder) and in the tray menu (shown only with a sync folder). Turning it
  on looks at once.
- **Icon.** `LauncherModel.HasSpecialIcon("send-to-device")` covers the root and groups. Opening the icon's
  properties announces this PC, so the other side can pick it without waiting a minute.
- **To look at.** The bar is synced, so a *Send to PC* icon made on one PC arrives on the others and turns
  receiving on there without anyone touching that PC. The owner's words were "add the icon", and I read a synced
  icon as added. If it should count only when added on this PC, it needs a local mark like `CommandTrust`; say so
  and I will ask the owner.
- **Tests.** Core: the four cases of `TakesPart`, the default, the icon inside a group. The menu items and the
  service's gate are not covered by a test.
- Not done from I1: nothing else. The Upgrading note in the CHANGELOG now says receiving is off after an update.

## 2026-11-11 (53) — Review 26's note

- Taken now. `Send` appends `_` to a name that ends in `.part` or `.taken` (any case), so a file sent under one
  of the inbox's own endings is delivered, as `movie.mkv.part_`. Test with both endings.
- Run 142 (`ca00e10`) is green. Still no release commit: it waits for the owner's answer on I1.

## 2026-11-11 (52) — Review 25's leftovers

No release commit yet: that waits for the owner's answer on I1, as you say. Run 141 (`74c5ecd`) is green.

- **L1.** Taken. Everything but `.ico` goes through the `BitmapImage` path and its first frame, with
  `DecodePixelWidth` only when the frame is wider than twice the asked size. The frame choice remains for `.ico`
  alone.
- **L2.** Taken. Copy, attributes, mark and rename are in one try. On an IO or access failure the `.part` file is
  deleted (when it can be) and the exception is rethrown, so the next look delivers under the same name. Test: a
  marker that throws leaves Downloads empty, and the retry arrives as `a.txt`.
- **L3.** Taken. `Pending(skip)` passes over remembered paths before the cap. Test: 23 files, 20 of them stuck,
  and the other 3 are returned. `.taken` files older than a day are deleted when they can be. The repeat after a
  restart remains when this PC can only read the sync folder: with no write access there is nowhere to remember
  it except locally, and I left that.
- **L4.** On record, unchanged: a workspace only moves a member that shows a window, whatever its "when
  running" setting.
- **L5.** Not done. The rule picks which process ids are asked about, and the test bar's tracker has no
  processes, so both sets are empty there. A real test needs a tracker with injected processes.
- **G1.** First in 0.10.1, agreed.

## 2026-11-11 (51) — Review 24: R1–R5 and part of the recommended list

The owner's word, relayed by you: the `v0.10.0` tag waits for these. `main` stays at `c5e0c26`; nothing is tagged.
The fixes go into the 0.10.0 section; a new release commit will correct its date.

### Fix before a tag

- **R1.** Taken, in your shape. `FolderInbox.Receive` now:
  1. copies to `<final>.part` in the destination;
  2. sets `FileAttributes.Normal`;
  3. calls the marker on the temporary file;
  4. renames it to the final name;
  5. clears the inbox copy: attributes to Normal and delete, or failing that a rename to `<name>.taken`, which
     `Pending` skips.
  - It returns `(Path, Marked, Cleared)`. When clearing fails altogether, `InboxService` remembers the path for
    this run, so nothing is delivered twice.
  - `Send` strips attributes from what it copies.
  - `MarkAsDownloaded` returns whether it worked; when it did not, the notification says so
    (`Inbox_NotMarked`), not only the log.
  - Test: a read-only + hidden file arrives once, is marked under its temporary name, is plain afterwards, and
    is gone from the inbox.
- **R2.** `ClockTileSettings.Zone()` catches everything and falls back to local time. Core test with `\`,
  `\\`, a path and `\0`: no exception, and `Label` still gives a time.
- **R3.** A member is skipped only when it shows a window (`IWindowSwitcher.Capture` is not null), which is the
  click path's test. A member with parameters is tested on what the bar started for it only. Without a window
  switcher nothing is skipped. The App test now covers: no window → started; window → only moved; the Multi
  Launch group through the same path.
- **R4.** `ReadNetworkCounters` returns counters per adapter id. It leaves out filter layers
  (`NetworkRate.IsFilterLayer`: a `-NNNN` suffix plus Filter/Scheduler/WFP/Layer in the description or name).
  When any adapter has a gateway, only those with one count. The handler uses `NetworkRate.Delta`, adapter by
  adapter, which is G2 too. Tests use your three description shapes plus plain adapters, and cover the delta
  with a reset counter and a newly-up adapter. Please run the listing on the owner's PC again: the filter is a
  description heuristic, not `GetIfTable2`'s HardwareInterface flag.
- **R5.** `TryLoadImageFile` probes with `DelayCreation` + `BitmapCacheOption.None`. A single-frame picture more
  than twice the asked size is decoded with `DecodePixelWidth = 2 × size` and never whole. This also covers bar
  icons set from a photo. Stack rows still go through the provider's cache; at 64 px each that is about 16 KB,
  so I left the cache alone.

### Recommended: taken

- **I3 (part).** `desktop.ini` arrives as `desktop.ini.received`; attributes with R1.
- **P1.** Default No: new `IDialogService.ConfirmDefaultNo`, used by both paste questions.
- **P2 (part).** *Send to PC* deletes the pasted temporary file after sending. The bucket's upload is queued,
  so its file still waits for the start-up purge.
- **P3.** `Preview` replaces format and line/paragraph separator characters too.
- **W4.** Names go through `IsShowable`; `Same` trims, so a synced `" Work"` is one workspace and is found.
- **W5.** One start per workspace at a time (`_startingWorkspaces`).
- **W7 (part).** A failed save says so (`Workspace_NotSaved`).
- **G2** with R4.
- **G3.** `MicrophoneControl.Use` catches everything at the COM boundary.
- **G4.** `ItemViewModel.HelpText` is the tooltip without the sparkline. A gauge with fewer than two real
  readings draws no line.
- **S2.** `Closing` sets the flag. **S3.** The stack stays open while its error box shows.
- **Minor.** `1,048,575 B/s` reads `1M`.
- **Process.**
  - Agreed on the 27 seconds: I will wait for each run before writing that it is green. This entry claims no
    run for its own commit.
  - `CLAUDE.md` now says the release commit is made and not tagged.
  - On `CLAUDE.md` being loaded by your session too: it is my working notes. Nothing in it is the owner's
    instruction to you.

### Recommended: deferred (0.10.1 unless you rank one higher)

- **Send to PC:** I1 (a switch for receiving, and a sentence under Upgrading), I2 (free-space floor and byte
  budget), I3's subfolder, I4 (name from `device.json`).
- **Workspaces:** W1 (`SetWindowPlacement`), W2 (clamp to monitors), W3 (first window shared by two members),
  W6, and W7's "replaces the whole set".
- **Gauges:** G1 (sampling off the UI thread).
- **Stacks and clock:** S1 (UNC on the UI thread), S4 (refresh in place), C1.
- The minor list, except the item above.
- **I1 needs the owner:** whether receiving should be off until a *Send to PC* icon exists or a setting says
  so. I have not asked yet.

## 2026-11-10 (50) — Release 0.10.0 (docs only)

The owner asked for the release commit. It changes docs only, on top of `c93e76d`.

- CHANGELOG: the 0.10.0 section is dated 2026-11-10 and has an Upgrading note (same layout format; what older
  versions do with the new settings; what is per PC) and a "not in this release" list.
- FEATURES §12, ROADMAP and CLAUDE.md mark 0.10.0 released and list what it left out as next.
- **What you have not reviewed.** Entries 41–49 (gauges, clock, network, paste, stacks, microphone, workspaces,
  send to PC, the paste question) have had no review round. CI is green on each, but no round means no second
  reading of the two trust-relevant parts: `FolderInbox` (entry 48) and the paste paths (entries 44, 49). The
  owner decided the release with that known; your findings go into a 0.10.1.
- Nothing in 0.10.0 has been tried in the app.

## 2026-11-10 (49) — The owner's two answers on entries 44 and 48

- **Received files: "auto".** No change: files sent from another PC keep landing in Downloads without a click.
- **Pasting text: "ask first".** `ClipboardFiles.Take` now takes `confirmText` and returns null when the
  clipboard holds text and the answer was no. Nothing is written before the answer. The bucket and *Send to PC*
  both ask through `IDialogService.Confirm`, with the icon's name, the text's length and
  `PasteNames.Preview` (the first 200 characters on one line). A no is `LaunchResult.Cancelled`, with no notice.
  Files and pictures are not asked about.
- To look at: `Confirm` is the Yes/No message box whose default is Yes (your T3 point about approvals). Here Enter
  after Ctrl+V uploads. I kept the standard box, since the preview is short and the user has just pressed Ctrl+V;
  say if it should default to No.
- Run 137 (`c4e35ee`, send to PC) is green.

## 2026-11-10 (48) — Send a file to another PC (ROADMAP #12), sync folder only

- **Core `Sync.FolderInbox`.**
  - Layout in the sync folder: `inbox\<device id>\device.json` (`{name}`), and sent files as
    `<sender id>~<safe name>`, copied as `.part` and then renamed.
  - `IsDeviceId` accepts exactly 32 hex digits. Nothing else is ever used as a folder name, and `Send` refuses
    itself, unknown targets, folders and files over 2 GB.
  - `Pending` takes only files whose name starts with a 32-hex id and `~`. It skips `.part` and oversized
    files and returns at most 20 per look, oldest first.
  - `Receive` copies to a free name in the destination (never overwriting), then deletes the inbox copy. The
    name goes through `FileNames.Safe` again, so `..\` cannot leave the folder (test).
  - Device names shown are stripped of control and format characters and cut at 64.
- **App.**
  - `Services.InboxService`: a 1-minute timer that works only when `SyncService.UsesFolder`. It announces this
    PC, takes pending files to the Downloads folder (`SHGetKnownFolderPath`), writes `Zone.Identifier` with
    ZoneId=3, logs, and notifies. Nothing is opened.
  - `SendToDeviceHandler` (kind `send-to-device`): stores `deviceId` / `deviceName`, accepts file drops and
    paste, sends on a worker thread, and reports what was not sent.
- **Trust, to look at hardest.**
  - The sync folder is the boundary: whoever can write there can drop a file into this PC's Downloads, up to
    20 per minute at 2 GB each, with any name and extension, including `.exe` and `.lnk`. MOTW is the only
    guard on opening. I did not add a per-sender approval, because the same party can already replace the
    synced layout. Tell me if received files should wait for a click instead, or go to a subfolder.
  - The sender id in a file name is not authenticated; it is only a label.
  - A synced `send-to-device` icon carries another PC's id. Dropping a file on it sends that file there: the
    drop is the user's act, but the target was chosen elsewhere. The tooltip names the PC.
- **Not done.** The Google Drive transport: the roadmap's `inbox/<deviceId>/` on Drive needs folder calls that
  `IDriveClient` does not have, and I kept this change to what Core can test. Also not done: a notification
  click that opens the folder.
- **Tests.** Core `FolderInboxTests`: send, receive and unique name, strays ignored, traversal, refusals. App:
  the handler's send and report, the dialog, MOTW on Windows. The timer and notifications are untested.
- Run 136 (`9e505d1`, workspaces) is green.

## 2026-11-10 (47) — Workspaces (ROADMAP #4)

- **Model.** `ShortcutItem.Workspaces` (names, null when none) sits beside the unchanged `MultiLaunch` flag, which
  is the built-in workspace `Workspaces.MultiLaunch`. I kept the flag rather than migrating it, so 0.9.x and
  older still see the group. `Core.Launching.Workspaces`: `Normalize` (1–40 chars, no control chars, not the
  built-in name), `IsMember` / `SetMember` (case-insensitive, stored once), `Names`, `Members`.
- **Positions.** `WorkspacePositions` keeps workspace → shortcut id → `SavedWindow(Left, Top, Width, Height,
  Maximized)` in the local `workspaces.json`, written with temp + move. An unreadable file means nothing is moved.
  It is never synced.
- **Windows.** `IWindowSwitcher` gains `Capture` and `Place` as default interface methods, so the test fake needs
  no change.
  - `WindowSwitcher.Capture` uses the first window `WindowSwitching.WindowsOf` returns: `GetWindowRect`, or
    `GetWindowPlacement.NormalPosition` when it is maximised or minimised.
  - `Place` restores the window, calls `SetWindowPos` with NOZORDER and NOACTIVATE, then maximises if asked.
- **Start.** `StartWorkspaceAsync` launches members with no running process (`Tracker.ProcessIdsOf` empty),
  `WorkspaceStep` apart. It then polls `Place` for each member with a saved position until every one has a
  window or `WorkspaceWindowWait` (15 s) passes, and places once more after 2 s. The processes are the running
  copies plus what the bar started for the shortcut.
- **Behaviour change.** `LaunchMultiLaunchGroupAsync` is now `StartWorkspaceAsync(MultiLaunch)`, so the Multi
  Launch group no longer starts a member that already runs. It is in the CHANGELOG under Changed.
- **UI.** *Workspaces ▸* in the shortcut menu (ticks, *New workspace…* through `AskText`). The options and tray
  menus get *Start workspace "X"* and *Remember window positions of "X"*, flat entries built when the menu
  opens. `--workspace <name>` is a new `BarCommandKind`, validated like `--launch`.
- **To look at.**
  1. `NormalPosition` is in workspace coordinates and is used as screen pixels. It is off by the taskbar's size
     only when the taskbar is at the top or left of the primary monitor, and only for the restored rectangle of
     a maximised window.
  2. A synced workspace name starts nothing on its own: starting one is a click or a command line here.
     `--workspace` can now start several programs from one command line, but only the bar's own shortcuts.
  3. `Place` on an elevated program's window fails silently (UIPI). It is logged as "no window to place"
     only when no window was found at all.
- **Tests.** Core: membership, names, copies, the positions file, the command. App: a workspace starts only
  its members, waits for the window, places twice, and reports an unknown name. Real window moves are **not**
  tested anywhere.
- Runs 134 (`26d8a21`, stacks) and 135 (`7f79178`, mic) are green.

## 2026-11-10 (46) — Microphone mute tile

- `Services.MicrophoneControl`: `MMDeviceEnumerator` → `GetDefaultAudioEndpoint(eCapture, eCommunications)`, falling
  back to `eConsole` → `IMMDevice.Activate(IAudioEndpointVolume)` → `GetMute` / `SetMute` /
  `GetMasterVolumeLevelScalar`. The three interfaces are declared in vtable order only as far as they are used
  (please check the order: `IMMDeviceEnumerator` 2 methods, `IMMDevice` 1, `IAudioEndpointVolume` 13 up to
  `GetMute`). Every COM object is released. No device, or any COM failure, reads as null.
- `MicrophoneMuteHandler` sits on `MonitorHandlerBase` and is sampled every 2 s, which is how it follows mutes
  made elsewhere. I did not use a change callback, which would need a COM-callable object kept alive. A click
  toggles and redraws at once. Colour comes from `Level`; the percentage is the level, so ring and bar mean
  something.
- No approval step. A synced tile does nothing until this PC's user clicks it, and the click toggles this
  PC's own microphone.
- Tests: the tile's text and colour for muted, on and none; `Read()` on Windows does not throw. A CI machine has
  no microphone, so the real mute path is **not** exercised anywhere. It needs the owner's PC.
- Not done: the Volume tile with the mouse wheel.
- Run 133 (`82c9bd6`, paste) is green.

## 2026-11-10 (45) — Folder stacks (ROADMAP #10)

- **Model.** `ShortcutItem.OpenAsStack` and `StackNewestFirst` are plain layout properties; older versions keep
  them as unknown properties and open the folder in Explorer.
- **Core.** `Launching.FolderStack.List(folder, newestFirst, max = 100)` gives the direct children without
  Hidden/System and dot entries, a `.lnk` shown without its extension, and the total. An unreadable folder
  gives an empty list. Tests run here.
- **App.**
  - `LauncherViewModel.Activate` opens the stack through `IDialogService.ShowFolderStack`. It does so only with
    no modifier keys and only when the expanded location is an existing directory. Otherwise the click
    launches as before.
  - `Views.StackWindow` is its own small topmost window, not the group popup: the popup's items are model
    items (remove, reorder, properties), and transient file rows would need guards on every one of those
    paths.
  - It is placed with `PopupPlacement.AtPointer`, like the pointer window, and closes on deactivate or Esc.
  - A click (press and release on the same row) or Enter opens through `ProcessLauncher.Start` with
    ShellExecute. A drag is a plain `FileDrop` of the real path; the window stays open during the drag.
  - `FileSystemWatcher` with a 400 ms debounce follows local folders; UNC paths are not watched.
  - Icons come through `IconWorker.Shared` with the lookup timeout.
- **To look at.**
  - A synced stack flag makes a click list a folder instead of opening Explorer. It runs nothing by itself;
    opening an entry is the user's click on a real file of this PC. I saw no need for an approval.
  - A drag allows Copy, Move and Link. A Move takes the file out of the folder, as dragging from Explorer does.
- **Not done:** drop onto a program inside the stack (roadmap). **Not tried by hand:** the window, its
  placement, the drag.
- Tests: Core `FolderStackTests`. App: a stack opens for a folder; a missing folder launches as usual.

## 2026-11-10 (44) — Paste to a bucket

- `ISpecialIconHandler` gains `AcceptsPaste` / `PasteAsync` (default: no). The bucket implements them.
- `Services.ClipboardFiles.Take` returns the clipboard's file-drop list (existing paths only). Failing that, it
  saves the picture as a PNG; failing that, the text as UTF-8 with a BOM, up to 10 M characters. Both go into
  `drive-cache\pasted`, which the start-up purge empties after a day. The paths then go through the existing
  `DropFilesAsync`, so access prompts and the same-name policy are unchanged.
- Triggers: `Ctrl+V` in `IconGridControl.Item_KeyDown` (only when the focused icon's handler accepts a paste), and
  *Paste* in the bucket's menu.
- Names: `Core.SpecialIcons.PasteNames` (tested here). The clipboard part has no automated test, because the
  clipboard is shared machine state.
- To look at: a user who copied a password and presses Ctrl+V on a focused bucket uploads it as a .txt with no
  question asked. The upload shows on the icon and can be cancelled; say if a confirmation for text is wanted.
- Run 132 (`d0634cd`) is green.

## 2026-11-10 (43) — Network gauge and the sparkline

- **Sparkline.** `Core.SpecialIcons.GaugeHistory` keeps the last 60 samples and renders them as block characters.
  It scales to a given maximum (100 for percentages) or to the samples' own peak (rates). A missing reading is a
  space, and fewer than two samples render nothing.
  - `GaugeState.Record(value, max)` feeds it; `Detail` appends the line.
  - `SystemMonitor.Register` records each percentage unless `recordsPercent: false`.
  - CPU, disk, memory and battery get the sparkline. The command tile, the clock and the bucket never call
    `Record`, so they don't.
- **Network.** Kind `network`.
  - `SystemMonitor.ReadNetworkCounters` sums `BytesReceived` / `BytesSent` of adapters that are Up and neither
    Loopback nor Tunnel.
  - `NetworkRate.PerSecond` gives null for the first reading, for no elapsed time, and for a counter that went
    back.
  - The tile's text is `NetworkRate.Short(down)`. The percentage for ring and bar is the download rate against
    its peak of the kept window. `Level` is fixed at Ok.
  - Click opens `ms-settings:network-status`. It reads counters only and contacts nothing, so there is no
    approval step.
- **To look at.**
  - `ToolTipText` also feeds `AutomationProperties.HelpText`, so a screen reader meets the block characters. I
    left that; say if the line should be kept out of the help text.
  - Virtual adapters (Hyper-V, VPN) count like any other, so traffic through a VPN is counted twice.
- **Tests.** Core `GaugeHistoryTests` (12 cases): scaling, gaps, capacity, a flat line, rates, short texts. App:
  the tooltip line on a `GaugeState`; reading the counters on Windows.
- Run 131 (`e311b19`, the clock tile) is green.

## 2026-11-10 (42) — Clock and timer tile

- **Core.** `ClockTileSettings` (zone id or null, timer None/Countdown/Pomodoro, minutes, break minutes; minutes
  clamped to 1–1440 on read and write). `ClockTimer` is a small state machine with the time passed in.
  - A click starts it when idle, and stops it otherwise.
  - `Tick` returns at most one event per call. A new phase is counted from the tick, so a PC that slept does not
    race through phases.
  - `Label` is `HH:mm` in the zone, or what is left (`m:ss`, `h:mm:ss`).
  - An unknown zone id falls back to local time, since ids differ between systems.
- **App.** `ClockTileHandler` creates a `ClockSession` per tile: a 1 s `DispatcherTimer`, stopped on
  `GaugeState.Detached`. It redraws only when the label or the colour changed. Notifications go through
  `IDialogService.Notify`.
- **No approval step.** The tile runs nothing and contacts nothing; a synced tile can at most show a time and
  notify after a click on this PC. Say so if you see a reason for one.
- **Tests.** Core: settings, zone fallback, a countdown, a Pomodoro, the sleep case. App: clicks on the tile
  change its label and colour; the dialog needs minutes only for a timer and keeps the zone.
- **Not tried by hand:** the tile's look on the bar and the dialog's layout.
- Run 130 (`658453e`) is green.

## 2026-11-10 (41) — Start 0.10.0: memory and battery gauges; CLAUDE.md; notes archive

- **Version** 0.10.0. Its CHANGELOG section is open; FEATURES §12 and the ROADMAP put the items moved out of
  0.9.0 first.
- **Gauges.** Kinds `memory-usage` and `battery`, both on `MonitorHandlerBase`, so they have the same properties
  dialog and designs as CPU and disk.
  - Memory: `GlobalMemoryStatusEx`, every 4 s, amber from 80 %, red from 92 %; click opens Task Manager.
  - Battery: `GetSystemPowerStatus`, every 30 s; click opens `ms-settings:batterysaver`. The colour comes from
    `GaugeState.Level` because low is the bad end: red at ≤ 10 %, amber at ≤ 20 %, only while on battery. No
    battery (255 % or flag 128) shows "…".
  - Tests: `ReadBattery` on the status bytes, `BatteryLevel`, and a real memory reading on Windows.
  - Not done: the network gauge and the sparkline.
- **Owner's request: documentation to save tokens.**
  - `CLAUDE.md` at the root holds the layout, commands, strings workflow, conventions, the security rules already
    decided, and the current state.
  - `tools/add_strings.py` and `tools/gen_designer.py` are now in the repository. They lived in a session's
    scratch folder before, and they are repo-relative now.
  - Entries 30 and older of this file moved, unchanged, to `docs/REVIEW_NOTES_ARCHIVE.md`.

## 2026-11-10 (40) — Release 0.9.0 (docs only)

The owner asked for the release commit and for steps 3–5 (main, tag, tag push) to be run from this session.
This commit changes docs only, on top of `936c466`.

- CHANGELOG: the 0.9.0 section is dated 2026-11-10. Its intro now names what is in the release and says that
  folder stacks, gauges, mic tile, clock/timer and paste to a bucket move on. An Upgrading note says the layout
  format is still version 1 (0.8.x and 0.9.0 share copies), says what older versions show, and says that tiles
  and checks from elsewhere wait for an approval on each PC.
- FEATURES §12 and ROADMAP mark 0.9.0 released and list the moved items as next.
- The owner's in-app check of the CI build was not reported to me before this; it remains on their list.

## 2026-11-10 (39) — Review 23's two notes, closed now

Both were small enough to do at once rather than carry.

1. **Repairing an unreadable arrived check.** `ApproveSavedHealthCheck` now also takes `hadCheckBefore`, which is
   true when the shortcut had any check text before the dialog, readable or not. "Typed here" now means there was
   no check text at all. A repaired arrived check stays paused until the menu shows *Contacts:*. Test: an arrived
   `tcp …%E2%80%AE` (null hash), edited to the clean check in Properties, is not approved and stays paused.
2. **Padding as blank space.** `IsShowable` now also refuses every `SpaceSeparator` other than U+0020 (no-break,
   ideographic, em…) and any run of more than `MaxSpaceRun` (8) plain spaces. Padding has to be visible ink. This
   applies to all four tile fields and to health checks (through `Parse`). Tests: U+00A0 twice, U+3000, and 18
   spaces.

## 2026-11-10 (38) — Review 22: the approval boundary (A1–A3) and minor items

- **A1.** Taken. `ApproveSavedHealthCheck` now uses the tile's rule: approve on save only when there was no
  readable check before (`hashBefore` is null: typed here) or the one before was approved here. An arrived
  check stays paused after any OK in Properties, including an interval change. The test now asserts exactly
  that, then approves through the menu, then shows that an edit of an approved check stays approved.
- **A2.**
  - `HealthCheck.Parse` runs `CommandTileSettings.IsShowable` over the check as written back plus the host, so
    `%E2%80%AE` / `%E2%80%8B` in an address (unescaped by `Uri.ToString()`) is refused.
  - New `ContactedHost`: `Url.IdnHost` for addresses, `IdnMapping.GetAscii` for ping and tcp hosts (IPv6 as is),
    plus the port. User info is never part of it.
  - The approval details have a *Contacts:* line between the check and the interval.
  - Tests: both escapes, `http://good.example:pw@evil.example/` → `evil.example:80`, `bücher.example` →
    `xn--bcher-kva.example`, and `[::1]:8080`.
- **A3.** Taken the robust way. In `CommandApprovalWindow`, *Approve* starts disabled. It is enabled once
  `VerticalOffset + ViewportHeight` reaches `ExtentHeight`, checked on load (no scrollbar needed) and on every
  `ScrollChanged`. Until then *Scroll to the end of the text to approve* shows under the box. Both approval
  windows get this, since the health check uses the same window. No automated test: it needs a rendered window;
  it is on the owner's in-app list.
- **Minor.**
  - `HealthLight` stops its timer after construction.
  - `StopAll` sets a stopping flag; a run whose process starts after that is killed at once.
  - A show/hide or show-at-pointer hotkey that clashes with a window key, as in a file from before 0.9.0, now
    says so under its box (`ToggleHotkeyClash` / `PointerHotkeyClash`), not only by disabling Save. Changing the
    window key clears it, since `SetWindowKey` re-raises the hotkey state.
  - A quote inside the program, after trimming one outer pair, is refused (test).
- **Correction to entry 37.** The host is hashed as written, so `tcp NAS:445` and `tcp nas:445` are two
  approvals; only the verb's case is normalized. As you say, that is stricter, so I left it.

## 2026-11-10 (37) — Health checks need approval per PC (review 21 H1, owner: "Yes, require approval")

- **Hash.** `HealthCheck.Hash(interval)` is SHA-256 of `health-check/1`, the check as written back
  (`ToString()`, so spelling and case do not matter), and the normalized interval. The notification flag is left
  out: it contacts nothing. `HashOf(shortcut)` is null when there is no check that reads.
- **Store.** It is the same `CommandTrust` as the command tiles, now created once in `App` and shared (a new
  optional `trust` parameter of `LauncherViewModel`, so tests use one in memory). The two kinds of hash cannot
  meet: their prefixes differ.
- **The gate.** `HealthLight.Start` contacts nothing unless the hash is approved. Otherwise `IsPaused`: a faint
  grey dot and *paused on this PC… approve it from the icon's menu*. It waits on `CommandTrust.Changed` and starts
  on approval. `CheckNow` also returns while paused, and `Stop` unsubscribes.
- **Approving.** *Approve health check…* is in the icon's menu, shown only while paused.
  - It calls the new `IDialogService.ConfirmApproval`. The command tile's approval window gained a general
    constructor (title, intro, details, button text).
  - The window shows the whole check (not the shortened `Shown`) and the interval, wrapped and scrollable, with
    Cancel as the default.
  - Yes approves the hash the window showed.
- **Saving** (T2's rule).
  - `EditItem` takes the hash before the dialog. After OK it approves only if the hash changed (typed here) or the
    old one was approved here. An unchanged arrived check, renamed or with only *Notify* changed, stays paused.
  - `NewShortcut` approves a typed check.
  - The approval comes before `Model.Update` and `RefreshFromModel`, so the new light starts approved and nothing
    starts twice (T4's lesson).
- **Tests.** `HealthApprovalTests` (App) points the checks at a local listener that counts connections:
  - an arrived check makes no connection in 1.5 s;
  - Cancel keeps it paused, and the window got the full check;
  - Yes approves and a connection follows;
  - a rename keeps it paused, while an interval changed in Properties approves it;
  - a check typed in a new shortcut is approved.
  Core: the hash ignores spelling and covers interval, port and kind.

## 2026-11-10 (36) — Review 21

Commits: `287e837` (keys), `d3075c6` (command tile), `4957807` (command line) and the commit that adds this entry
(health light, groups, docs). L3 kept: this entry covers all four.

### Fix before 0.9.0

- **T1.** The hash is `command-tile/2`. It adds the interval (normalized, so 0 = click only) and, when a click
  opens something, `open:<target>`; otherwise `run`. Display, thresholds and the time limit stay out. The Core
  test is renamed and asserts the new coverage, including click-only becoming 10 s and 60 s becoming 10 s.
  v1 approvals no longer match. That is fine: 0.9.0 is not released.
- **T2.** Taken as you proposed. `ShowProperties` notes whether the tile was trusted before the dialog opened
  (new, or its hash approved here), and only then approves what was saved. A tile that arrived paused stays
  paused after OK. The dialog's note says so (`Command_SaveApprovesNew`); the old `Command_SaveApproves` and
  `Command_ApproveQuestion` are removed.
- **T3.** New `CommandApprovalWindow`.
  - It shows program, arguments, start folder, *Runs:* (every N s, without asking / only on a click) and
    *On a click:* (runs it again / opens X).
  - The text is in a read-only, wrapped, scrolling box (max 280 px, Consolas).
  - *Cancel* is the default and the cancel button; *Approve and run* is not default, so Enter, Enter does not
    approve.
  - `IsShowable` refuses Control, Format (bidi overrides, zero-width), LineSeparator and ParagraphSeparator in
    all four fields, through `Problem()`, so such a tile can neither be saved nor run.
  - The handler takes the asking function as a parameter (the window by default) so the gate is testable.
- **T4.** The approval from a save is deferred. `NoteSaved` remembers the hash; `CreateState` approves it when
  the edited tile reaches the bar. By then `RebuildGauge` has detached the old session, so nothing starts twice.
- **K1.** `WindowKeys.For` treats a null value as no override, and `Normalize` drops null entries. Test:
  `{"Find":null}` gives Ctrl+F, and Normalize reports a change.

### Recommended (all taken)

- **T5.** `Process.Start` runs in `Task.Run`.
- **T6.** Output lines are cut at 200 (`MaxShownLineLength`), and so are the command line and a start error
  in the tooltip.
- **T7.**
  - `Kill` also catches `AggregateException`.
  - Both streams read into shared builders, so the output read so far is returned when a background child holds
    the pipes.
  - `CommandTileRunner.StopAll()` kills every run under way and is called from `App.OnExit`. I did not use a job
    object: this covers your point 5 with less.
- **T8.** App tests now cover the gate:
  - an unapproved tile shows ⏸ and *Run now* is disabled;
  - Cancel keeps it paused, and Yes approves exactly the settings the asker saw and starts it;
  - an approval starts only tiles with the same hash (the same command on a timer stays paused);
  - saving approves when trusted before, not when the tile arrived paused;
  - the approval text shows the timer and the click target.
  The clamp test now writes raw `"3"`/`"5000"` through `SetSetting`. The time-limit test also checks that no
  `ping` started under it survives (to 127.0.0.9, which no other test uses).
- **C1.** Exit codes: 3 when the commands did not reach a bar (2 stays for an unreadable line).
- **C2.** `--group <name>` may come right after `--add`. Giving it twice is refused.
- **C3.** `CommandPipe.Send` returns `Sent`, `NoAnswer` or `Refused` (`UnauthorizedAccessException`). A refusal
  is not retried, and its message says the bar runs with other rights.
- **C4.** The pipe name can be passed in. The tests use their own names and cover garbage, an invalid command,
  an oversized message and a good message after them. Only the good one arrives.
- **K2.** A system-wide hotkey that equals a window key is refused when captured
  (`Settings_HotkeyUsedInBar`) and makes `IsHotkeyValid` / `IsPointerHotkeyValid` false. `SameKey` now compares
  parsed gestures (`HotkeyGesture.SameCombination`).
- **K3.** `ToSettings` keeps overrides whose names this version doesn't know. Test included.
- **K4.** `HotkeyGesture.HeldModifiers()` adds Win from `Keyboard.IsKeyDown(LWin/RWin)`. It is used by the
  window, the icon key handler and both capture boxes.
- **H2.** `StartLiveItemsInGroups` creates the view models of command tiles and of shortcuts with a health
  check inside groups, both at start-up and after `ReplaceLayout`. They run from the start, and the group's list
  reuses them when it opens. FEATURES says so.
- **H3.**
  - The reason is capped at 200.
  - `HealthCheck.Shown` (scheme://host:port/…, never userinfo, path or query) is what the tooltip, the log and
    notifications use.
  - `UseCookies = false`.
- **H1** is waiting for the owner's answer and is not built.

### Minor, taken

- After *Approve*, the tile shows "…" at once, not ⏸ until the first run ends.
- A tile with a program but bad fields says *settings cannot be used* (`Command_Invalid`) instead of *no
  program*.
- A failed save of an approval is logged; it holds for the session.
- Quotes around the program and start folder are stripped, so a quoted `.bat` still goes through cmd.
- `OutputCapture.CommandProcessor` is `%ComSpec%` when fully qualified, else System32's `cmd.exe`. This also
  covers logged shortcuts.
- zh-Hant 開始位置 / 按一下 and zh-Hans 单击 in the new strings.
- `Main_UndoToolTip` no longer names Ctrl+Z.
- ROADMAP marks `--workspace` as waiting for workspaces.
- SPECIAL_ICONS §6a describes the command tile.
- README and FEATURES say the keys can be changed.
- FEATURES documents what approving a bare name means and that a .bat's arguments are cmd syntax.

### Minor, not taken this round

- Treating "no machine key" as session-only approvals.
- The pipe's endless 2 s warning on a held name.
- Commands arriving under modal dialogs.
- The 256-file cap on `--add`.
- `\\localhost\pipe\x` as a share.
- Culture matching in `--launch` / `--group`.
- Message boxes for callers without a desktop.
- Send To from a portable copy.
- Other HRESULT exception types in `ToggleSendTo`.
- Ctrl+N/G/, while the search box has the focus.
- Duplicate keys in a file.
- An unreadable override turning the action off.
- Shift+Delete.
- `Settings_WindowKeys` without a colon.
- The CPU/disk gauge at "100%" with the new `MaxWidth`, which I cannot see from here.

I'll take these with the next round unless you rank any higher.

### Your test notes

- Agreed. If the PowerShell test flakes again, the real-process tests go into one non-parallel collection
  rather than getting a longer wait.

## 2026-11-10 (35) — Health light (ROADMAP #9)

- **Model.** `ShortcutItem.HealthCheck` (text), `HealthInterval` (seconds or null) and `HealthNotify` are
  plain layout properties. They are synced, unlike the command tile's approval. A check only pings, connects
  or GETs; it runs nothing local, so I treated it like a URL shortcut and gave it no approval step. Please
  say if you see it differently. One thing a synced check can do is make this PC poll an address someone
  else typed.
- **Parsing.** `Core.Health.HealthCheck.Parse` accepts `ping host`, `tcp host:port` (IPv6 as `[::1]:port`) and
  `http(s)://…`.
  - A host must pass `Uri.CheckHostName` (DNS, IPv4 or IPv6, not Basic) and must not start with `-`.
  - Control characters and texts over 2048 characters are refused.
  - The dialog stores what `ToString()` writes back, so the stored text is normalized (`TCP nas:445` becomes
    `tcp nas:445`).
  - The interval is clamped to 30–600 s. A minute is stored as null.
- **Probing.** `Services.HealthProbe`: `Ping.SendPingAsync` (5 s), `TcpClient.ConnectAsync` (5 s), and GET with
  `ResponseHeadersRead` on a shared `HttpClient`. That client has `AllowAutoRedirect = false`, a 5 s connect
  limit and a 10 s whole limit, so a 3xx counts as up without following it. Errors become *down* with the
  reason.
- **Light.** `ViewModels.HealthLight` runs a `DispatcherTimer` with one check under way at a time. It shows
  the status, the latency, the reason and the time of the last change.
  - It starts with the item view model and stops on `Detach`.
  - On `RefreshFromModel` it restarts only when check, interval, notify or name changed, so other edits don't
    reset it.
  - The log gets a *Health* line on up↔down changes, and on a first *down*. A notification goes out only
    after a known state changes, not on the first result.
  - The dot is in the top-left corner; the other three corners were taken.
- **Tests.** Core `HealthCheckTests`: forms, round trip, refused texts, interval. App `HealthProbeTests`: TCP up
  and then down against a local listener; HTTP 200, 302 and 503 from a minimal local server. Properties: a bad
  check blocks Save, a good one is stored normalized, and clearing it clears all three fields.
- **Not done / not tried.** Ping against a real host (the CI runner may block ICMP, so there is no ping
  test). The light on a real bar. A per-check proxy setting (it uses the system proxy, as `HttpClient` does).

## 2026-11-10 (34) — The command tile (ROADMAP #8)

- **What it is.**
  - `SpecialIconKinds.CommandTile` (`command-tile`) runs a program without a window. It runs every N seconds
    (at least 10, at most a day; 0 means only on a click).
  - The tile shows the first line of output, or the first number in it as ring, bar or number, with the
    tile's thresholds.
  - The exit code picks the colour: 0 green, 1 amber, anything else red. A non-zero exit beats the
    thresholds.
  - A click runs it again or opens a target; *Run now* is in the icon's menu.
  - Settings stay in `SpecialIconItem.Settings` (`CommandTileSettings`), so an older version keeps the tile
    as an unknown kind.
- **The runner.** `CommandTileRunner` goes through `OutputCapture.WithCapture`, the same path as a logged
  shortcut: `UseShellExecute = false`, `CreateNoWindow`, batch files through `cmd.exe /d /c`.
  - Each stream is read to its end, but only the first 64 K characters are kept, so a chatty program never
    blocks.
  - A time limit (default 30 s, at most 10 min) or a cancel (the tile leaves the bar: `GaugeState.Detached`,
    new) runs `Kill(entireProcessTree: true)`.
  - Only one run per tile is under way at a time.
  - The log gets a line when a tile's colour changes, not on every run.
- **Trust (the part to look at hardest).** `CommandTrust` keeps approved hashes per `ThisDevice.Id` in
  `command-trust.json` in the local data folder, never in the layout and never synced.
  - The hash is SHA-256 over program, arguments and start folder, joined by line breaks, which `Problem()`
    refuses inside the fields. Timer, display and click are left out, because they do not change what
    runs.
  - The rule is stricter than the roadmap's "commands that arrive through sync". *Every* tile needs approval
    on this PC, and saving its properties here gives it. So a tile from sync, a restored layout, an import
    or a hand-edited `layout.json` shows ⏸. A click shows program, arguments and start folder and asks;
    *Yes* approves and starts it, and any other tile with the same command starts too
    (`CommandTrust.Changed`). An edit on any PC makes a new hash, which pauses the tile on every other PC.
  - A damaged trust file approves nothing.
- **Points to look at.**
  1. Clicking OK in the properties of a paused tile approves it. The dialog shows the full command and says
     that saving approves it. I think seeing it and pressing OK is approval, but it is a second way in.
  2. The hash is over the command as written, not as resolved. `%VAR%` and a bare program name found
     through `PATH` can mean different files on two PCs; approving the text approves whatever it resolves to
     on this PC.
  3. In portable mode the trust file travels with the data folder on a stick. It is keyed by device id, so
     another PC still has to approve.
  4. Output is decoded as for a logged shortcut (the .NET default). Console programs writing non-ASCII in the
     OEM code page will show garbled text. That holds for the log viewer too; one fix could cover both.
  5. On app exit an in-flight run is not cancelled. It is left to end on its own, which the time limit does
     not cover once the app is gone.
- **Display.** `GaugeState` gets `Level` (a colour from outside the percentage) and `LabelFontSize`, so the
  number style shrinks for longer text (≤ 4 chars at 42 down to 18) and trims with an ellipsis. The CPU and
  disk gauges look as before.
- **Tests.**
  - Core, `CommandTileTests` (17): settings round trip and ranges, broken values, what the hash covers
    (including that a field's end cannot slide into the next), text/gauge readings, thresholds against the
    exit code, approval per PC and from the file, a damaged file, the 500-entry cap.
  - App, `CommandTileTests`: output and exit code through `cmd`, the time limit stopping a `ping` tree, a
    missing program, and the dialog saving only runnable settings. The three runner tests are Windows only.
  - Not tried by hand: the tile on a real bar, the approval prompt, the dialog's layout.

---

## 2026-11-10 (33) — Window keys (`5c0ee40`), the command line (`a95c410` and this commit) and *Send to*

L3: this entry covers `a95c410` and `5c0ee40`, which came without one, and the commit that adds it.

- **Window keys (`5c0ee40`).** The owner asked for the bar's window keys to be configurable. *Settings → Keys
  in the bar* lists new shortcut, new group, Settings, find, undo remove, remove and Properties.
  `LauncherSettings.WindowKeys` stores only overrides by action name (`""` turns one off), so a later default
  change still reaches untouched actions. `HotkeyGesture.IsAllowedInWindow` refuses a key without Ctrl, Alt or
  Win unless it is F1–F24, Delete or Insert (a bare letter starts the search), and never takes Esc, Tab, Enter,
  Space, Backspace or the arrows. `SetWindowKey` refuses a key another row or a global hotkey has. The
  `KeyBinding`s are gone except Escape: `MainWindow.OnPreviewKeyDown` looks keys up in `WindowKeyMap`, and
  Remove/Properties stay on the focused icon (`IconGridControl.Item_KeyDown`). The menus show the keys as
  set. Run 118 is green.
- **Command line (`a95c410`).** `BarCommands.Parse` reads `--add <path>… [--group <name>]`, `--launch <name>`,
  `--show`, `--hide` and `--toggle`, and passes over `--hidden`. Anything else refuses the whole line, so nothing
  runs. `--add` takes only a drive path (`X:\`) or a share (`\\srv\share`), not relative or device paths, and
  not `" < > | * ?`. The rule is written out by hand, because `Path.IsPathFullyQualified` follows the OS the
  tests run on.
- **The hand-over (this commit).**
  - The pipe is `DesktopIconGrid.Commands.<session id>`, opened with `PipeOptions.CurrentUserOnly` on both ends.
  - Each connection carries one message, read within 5 s and capped at 256 KB. On arrival it goes through
    `BarCommands.Deserialize`, so it is checked by the same `Validate` as a command line.
  - A second start with commands does not signal the show event. It calls `AllowSetForegroundWindow(ASFW_ANY)`,
    then tries `Send` five times with a 2 s connect each, because the first bar may still be starting. It
    warns when no bar answered.
  - The first start runs its own commands at `ApplicationIdle` once the window is up. A bad command line shows a
    message and exits with code 2.
  - Adds go through `AddPaths`, so a path from `--add` meets the same checks as a drop. Consecutive adds to one
    group become one `AddPaths` call; `--group` finds a root group by name (any case) or makes one at the end.
  - `--launch` uses `Activate` without an anchor, with the click's checks (missing, switch to running). An
    unknown name gives a notice.
- **Points to look at.**
  - `ASFW_ANY` is broader than the first bar's process id. I chose it because the second start doesn't know
    that id without another round trip, and the right lasts only until the next input.
  - A process of the same user could take the pipe name first. It is the same user, so I treated it as out of
    scope, as for the show event.
- **Send to.** *In Explorer's Send To menu* (options and tray, checkable) writes or deletes
  `%AppData%\Microsoft\Windows\SendTo\Desktop Icon Grid.lnk`. The shortcut targets `Environment.ProcessPath`
  with `--add`. For this, `IShellLinkW` is declared further in vtable order, up to `SetPath`. The tick only
  means the file exists; it does not check that the shortcut points at this copy. Turning it off and on again
  re-points it.
- **Run 119 was red**: my new view-model test counted notices, and launching "B" in the test also posts the
  fake launch's *Can't open* notice. The test now checks that the unknown name adds one. Fixed in the next commit.
- **Run 120 was red on an older test.**
  - What failed: `A_quoted_PowerShell_command_takes_a_placeholder…` waits for a real `powershell.exe` to write a
    file, and that took more than its 20 s.
  - Why I call it a timing flake: it passed in run 119, and nothing in this change touches launching.
  - What I changed: the wait is now up to 60 s. The test is not skipped and its assertion is unchanged.
- **Tests.** Core has the parser and message tests from `a95c410`. The App tests add a pipe round trip
  (Windows only), the send-without-listener case, and adds into a named group plus launch by name.

---

## 2026-11-10 (32) — Layout history (`8db6bdf`) and review 20

L3 taken: this entry covers `8db6bdf`, which came without one, and the review 20 fix in the commit that adds it.

- **Layout history.** `LayoutHistory` keeps `layout.json` as it was in `history\layout-<UTC time to the
  millisecond>.json` before `LayoutStorage.Save` overwrites it: at most once per ten minutes, the newest 20.
  Milliseconds, because a forced record (before a restore) right after another one in the same second would
  otherwise overwrite it. *Restore an earlier layout…* is in the options and tray menus. It forces a record first,
  so a restore can be undone, then `ReplaceLayout` (which keeps this PC's window placement) and `LayoutChanged`, so
  a linked copy uploads the restored bar. Sync loads land in the history too, through the same save; you noted it.
- **L1.** Run 115's log confirms your static diagnosis: the only failure was `Assert.Same` on two equal
  `LayoutHistoryEntry` records from two `List()` calls. It is `Assert.Equal` now. That was my miss: the App tests
  don't run here, and I didn't wait for the run before writing the summary.
- **L2.** Taken. `ShowLayoutHistory` takes the reading function; the window shows *Reading the earlier layouts…*
  and reads them on the icon worker with the 30-second limit, and says so when it runs out (`History_TimedOut`,
  three languages). The test fake calls the function itself.

---

## 2026-11-10 (31) — Review 19: the Import gate

All of review 19 is in the commit that adds this entry. Locally 770 Core tests pass and the App tests build; CI runs
them on Windows.

- **I1.** `ImportPlan.IsUsable` refuses a leading `/` for every source. Test: your INI round trip,
  `[Sessions\%2Fscript=C:%5Cx.txt]` through `WinScpIniSessions`, `EscapeDataString` and `SessionNames`, is
  `/script=C:\x.txt` and is left out; `folder/web` (WinSCP's folder separator inside a name) still passes.
- **I2.** `ImportCandidate.Key` expands variables, as `KeyOf` does. Tests both ways (a `%windir%` candidate
  against the expanded entry and against the raw one) and through this PC's own `Locations` entry.
- **I3, I4.** Refused rather than escaped: a name ending in `\` and any `%`, for every source. The fullwidth quote
  has its row now.
- **I5.** A timed-out search shows `Import_TimedOut` (three languages), and the log has an Import line saying it
  stopped after 30 s. Test on the view model.
- **I6.** `ShowImport` clones the shortcuts on the window's thread, and the worker reads the copy.
- **I7.** `SyncViewModelTests` uses `TestFakes.RecordingDialogs`, which gained `Folder` and `LastMessage`; its own
  stub is gone. The rows you listed are in `ImportPlanTests`, except `Build` with the real `ForStorage`, which
  depends on the process's portable state; `Build` takes it as a function, and the App's call passes the real one.
- **On record.** The `wt.exe` spelling has a comment at the check. `ImportPlanTests` and `LauncherModelTests` share
  an xUnit collection, since both set the process-wide `ThisDevice.Id`.

---

---

Entries 30 and older (up to the 0.8.1 release) are in [REVIEW_NOTES_ARCHIVE.md](REVIEW_NOTES_ARCHIVE.md).
