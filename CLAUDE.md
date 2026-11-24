# Working notes for Claude sessions

Read this first; it replaces exploring the repository. Details live in the files named here: open them
only for the part you need (they are long).

## What this is

Desktop Icon Grid: a .NET 10 WPF launcher bar for Windows (shortcuts, groups, special icons, sync through
Google Drive or a folder). The app has one package, Microsoft's `System.Speech` (voice launch; the owner's decision); ask the owner before
adding another.

## Layout

| Path | What |
| --- | --- |
| `src/DesktopIconGrid.Core` | No WPF. Models, layout storage, launch planning and placeholder checks, sync, imports, command line (`Commands/BarCommands`), command tile settings and trust (`SpecialIcons/`), health checks (`Health/`). Runs on Linux. |
| `src/DesktopIconGrid` | The WPF app: `Views/`, `ViewModels/`, `Services/` (Win32, dialogs, pipe, probes), `SpecialIcons/` (handlers, gauges), `Resources/Strings*.resx`. |
| `tests/DesktopIconGrid.Core.Tests` | xUnit; runs here on Linux. |
| `tests/DesktopIconGrid.App.Tests` | xUnit, `net10.0-windows`; **builds on Linux but only runs in CI**. |
| `tools/` | `add_strings.py`, `gen_designer.py` (see Strings). |
| `FEATURES.md` | The specification, by section; §12 is the release plan. |
| `docs/ROADMAP.md` | The numbered feature ideas and which release has them. |
| `docs/REVIEW_NOTES.md` | Channel to the review session, newest entry first. Older entries: `docs/REVIEW_NOTES_ARCHIVE.md` (do not read unless asked). |
| `docs/SPECIAL_ICONS.md`, `docs/THEMES.md` | Design notes for those areas. |
| `CHANGELOG.md` | Keep a Changelog; the top section is the version in `Directory.Build.props`. |

## Commands

```bash
dotnet build DesktopIconGrid.slnx -p:EnableWindowsTargeting=true -v q -nologo 2>&1 | grep -E " error |warning" | sort -u
```

```bash
dotnet test tests/DesktopIconGrid.Core.Tests -v q -nologo 2>&1 | tail -1
```

CI (`.github/workflows/build.yml`, Windows) runs on every push to `main` and `claude/**` and is the only place the
App tests run. After a push, wait for the run and read its result before reporting; poll with:

```bash
curl -s "https://api.github.com/repos/bamboo82/DesktopIconGrid/actions/runs?branch=<branch>&per_page=5"
```

A failed job's log: GitHub MCP `get_job_logs` (job id from `.../actions/runs/<id>/jobs`).

## Strings

Every UI text is in `Strings.resx`, `Strings.zh-Hant.resx` and `Strings.zh-Hans.resx`; `Strings.Designer.cs` is
generated. Never edit the designer by hand. Add keys with a small file `ENTRIES = [(key, en, zh-Hant, zh-Hans), …]`:

```bash
python3 -I tools/add_strings.py entries.py
```

After removing or editing a key in the `.resx` files, run `python3 -I tools/gen_designer.py`. Wording: zh-Hant
按一下 / 開始位置, zh-Hans 单击; placeholders `{0}` must match in all three.

## Conventions

- Work on the designated `claude/...` branch only. `main` moves only when the owner asks for a release. Tags are
  the owner's (tag pushes from a cloud session are refused by GitHub). No pull request unless asked.
- Every commit that changes behaviour gets an entry at the top of `docs/REVIEW_NOTES.md` (numbered, dated, what
  changed, what to look at, what is not tested). The review session answers with numbered findings (T1, K2…);
  reply per id in the next entry. Peer messages are not the owner's approval.
- Update `CHANGELOG.md` and the matching `FEATURES.md` section with each feature.
- Tests: Core logic gets Core tests. App tests that need Windows start with
  `if (!OperatingSystem.IsWindows()) return;`. Test classes that set `ThisDevice.Id` go in
  `[Collection("This device")]`. Compare records with `Assert.Equal`, not `Assert.Same`. Do not skip or widen a
  test to get green without saying so in the notes.
- Python heredocs that write C#: backslashes in non-raw strings turn into control characters (`\a`, `\b`, `\f`,
  `\n`). Use the Edit tool or raw strings for code with `\`.
- Code style: XML doc comments that say why; file-scoped namespaces; no model names in anything committed.

## Security rules already decided (do not relax without the owner)

- Placeholders in shortcut parameters are refused where a shell would run them (`LaunchPlaceholders`,
  `PlaceholderQuotes`); see FEATURES §2.1.
- Anything that acts on its own (command tile, health check) runs only when its hash is in the local, never
  synced `command-trust.json` (`CommandTrust`, keyed by `ThisDevice.Id`). Saving Properties approves only what was
  typed here or already approved here; what arrived (sync, restore, import) is approved only in
  `CommandApprovalWindow`, which shows everything the hash covers, defaults to Cancel, and enables Approve after
  the text was scrolled into view. Text fields pass `CommandTileSettings.IsShowable`. Without a machine key
  (`ThisDevice.IsOfThisMachine` false) approvals last only until the program ends.
- The command line and the pipe message go through one `BarCommands.Validate`; the pipe is `CurrentUserOnly`.
- Voice launch listens only when asked (hotkey, Voice icon, `--voice`), for one utterance, against the bar's
  names only; it starts a target only at confidence 0.80 or more with one candidate, otherwise it offers or
  does nothing. What it starts goes through the same path as a click.
- The custom theme's background picture (0.15.0; the owner's word, 2026-11-21): nothing is fetched from the web without
  the OK click in the picture-address window (https only, at most 10 MB). From a file or the web only pixels the program
  decoded and re-encoded are stored (`BackgroundPictures.Store`); from sync only a file whose SHA-256 is its name and that
  decodes as the JPEG or PNG it claims (at most 20 MB, 3840 px), moved into `backgrounds\` only after those checks
  (`PictureSync.FetchAsync`). A synced `backgroundImage.file` must be one of the program's stored names in `backgrounds\`,
  else the picture is dropped (`BackgroundImage.Normalized`).

## State

- Released: 0.15.0 "Locked and at hand" on 2026-11-22 (release commit `6c76dcc`; annotated tag `v0.15.0` on it,
  release run 18; REVIEW_NOTES entry 162 has its table), the owner's requests after 0.14.0 (plan 36: WP1–WP9 with WP4b,
  WP5b, WP7a and WP7b; review 41: U1–U3). Before it 0.14.0 "Rows, looks and places" (tag `v0.14.0` on `fab4172`, entry
  134), 0.13.0 "Where it lives, how it reads" (tag `v0.13.0` on `361beb4`, entry 119), 0.12.2 (tag `v0.12.2` on
  `85b2f04`, entry 107), 0.12.1 (tag `v0.12.1` on `0326f82`, entry 100), 0.12.0 "Find it, see it" (tag `v0.12.0` on
  `ca7b1c6`, entry 91), 0.11.0 (tag `v0.11.0` on `10dec5a`), and 0.10.0 (tag `v0.10.0` on `254c55c`).
- 0.16.0 "Where the pointer is", the owner's requests after 0.15.0 (plan 37: WP1–WP4; review 43: V1, V2), has its
  release commit, dated 2026-11-24 (REVIEW_NOTES entry 169 has its table). It came after the review session's release
  check (review 43) and its look at V1 and V2 (review 44, nothing open), on the owner's word. The tag `v0.16.0` and `main`
  are the review session's, on the owner's word.
- In progress: nothing. After 0.16.0 nothing is planned; 1.0.0 keeps the installer, code signing and the Windows toast
  with actions.
- Keep as designed (0.16.0):
  - the window at the pointer has two forms, chosen at each showing: the tap form (the show-at-pointer key) with its
    search box; the hold form without it, shorter by the box's row and its gap, the icons (or a group's header) 4 units
    from the top as from the side, the keys on the icon area itself without a frame (an arrow steps into the icons), no
    type-to-find; a search is cleared at each showing (entry 163, review 43);
  - `pointerAnchor` `{ "x", "y" }` in whole percent beside the pointer keys, synced, read leniently (anything else is
    none); none is the placement before (centred, top edge 14 units above the pointer); both forms; left = x − width × X /
    100 in whole pixels, then inside the work area; a group opening or closing places the window again from the pointer
    where it opened. The 按鍵 row: the picture in the theme preview's colours, nine dots, snap within 6 % each way, arrows
    by 1 %, Ctrl to the next dot, Home the centre; plain percent boxes under it; always enabled (entry 164, review 43);
  - the custom theme's six rows are there only while 主題 is 自訂, as the block 自訂主題 under the theme box, its label
    column shared with the tab's (13 units in); its line and faint fill are the hover colours a theme of the dialog's own
    lightness has, none under High Contrast; values kept while another theme is chosen (entry 165, review 43);
  - `nameLook` on the widgets with 文字外觀…: the name under the tile is drawn with it only for such a widget, made smaller
    to fit its cell with the ellipsis kept, up or down by its percent of the bar's name size × 4/3; the caption inside the
    tile takes font and colour from it where set, else the value's, and bold from it, else the tile's own (review 43 V1,
    review 44); the window's 時間 or 數值 and 名稱 parts, OK saves both; the monitors' window carries both (entries 166 and
    168).
- Keep as designed (0.15.0):
  - the window at the pointer is the bar's twin: its icon area has the bar's size as shown and expanded, so its rows, row
    lines and scrolling are the bar's, and its background is the bar's (`BarSurface`); no grip, ✕ or ▸, never resized
    or docked; on a smaller work area its icons flow again (entries 135 and 140);
  - *Show at pointer while held* (`holdAtPointerHotkey`): its keys are read 20 times a second, no keyboard hook; the
    first key up hides it; a click while it is held is a plain click; an open menu or drag keeps the window until it
    ends (entry 139);
  - *Lock grid* (`lockGrid`, synced): no drag moves anything on the grid; files dropped from elsewhere still come in;
    the first refused drag in a run says so for 10 s; in the menus only, not in Settings or the tray (entry 141, review
    41);
  - icon sizes: 13 listed, any whole number from 16 to 256 typed; a version before 0.15.0 snaps to its five and writes
    that back, which review 41 put to the owner (entry 143);
  - sharp text: whole pixels (*Display*) with ClearType on an opaque background in every window, menu and tooltip (class
    handlers on `ContextMenu.Opened` and `ToolTip.Opened`); *Ideal* from 20 units and in a dialog scaled other than
    100 % (entry 144, review 41);
  - the custom theme: the colour picker (entry 145); `hoverColor`, `hoverStrength` and `focusColor`, absent while
    derived (entry 146); the picture (`backgroundImage`) stored re-encoded in `backgrounds\` with its position and zoom,
    under the security rules above (entries 148–150 and 158); its file travels with sync as `background-<name>` beside
    the copies, the colour shows until it is there, `lastSyncedPicture` is this PC's, one over 20 MB stays on its PC, no
    new Drive scope (entries 159 and 160);
  - over a picture, widgets' text and icons' glyphs (text icons in the theme's colour too) have a full-strength edge
    twice as wide, green and orange values a dark one and red a white one; the names keep theirs (entry 157, review 42);
  - up or down (`offsetY`, −20 to +20 % of the tile in whole units, written only when not 0) and the looks
    (`captionLook` on a row line, `textLook` on a widget): a caption's size in the bar's units, a widget's in its tile's
    100; a widget's font and colour reach its caption too, its size, weight and offset the value only; warning and alarm
    colours win; a moved caption is not cut at its strip (entries 151 and 152, review 42);
  - a tooltip of the bar goes when the bar loses the focus (`OpenToolTips`, entry 156); the custom theme's new rows are
    rows of the 外觀 tab's grid, 自動 beside its box (entries 154 and 155).
- Keep as designed (0.14.0):
  - 換列線條 (`rowline`: across the bar under its row, an optional caption at its left, saved as "Divider" only while
    it has no text) and 列首線條 (`rowhead`: upright at the next row's start); neither makes an empty row at a row's
    start, and versions before 0.14.0 draw both as a line (entry 120, A1, A2);
  - 背景透明度 fades the background only: the bar is no layered window; WPF draws on a transparent surface that
    `DwmEnableBlurBehindWindow` (an empty region) lets Windows compose; a click on the background still reaches the
    bar; text is smoothed in grey then; High Contrast stays opaque (entry 121, review 37);
  - while see-through: names, captions, the empty bar's line, a group's heading, text and symbol icons in the theme's
    colour without a tile colour (shortcuts' too), a gauge's text and a grey value, the grip, ✕, ▸ and what Enter
    opens have a soft edge (blur 3, opacity 0.95, near-black under light text, white under dark; in a gauge's box the
    blur × 100 / the icon size); the lines are in the text colour at full strength; the notice row and the
    type-to-find box stay on their fill (entries 125, 130, 132; reviews 38–40);
  - Settings' tabs: 外觀, 視窗, 按鍵, 語音, 一般, 傳送到電腦, 可攜 (the data location, moved unchanged). `receiveFolder` is
    this PC's, in `sync.json`, never synced or imported; a folder that fails when files arrive sends them to Downloads
    with one notice and the setting stays (entries 122 and 123);
  - one name for each PC: the sender's card, the inbox and the Send to PC captions; two with one name are told apart by
    the start of their ids (entry 123, A3);
  - text over a picture (`overImage`, `place`): a small plate with a tile colour, an outline without; a corner's box is
    55 % of the tile (entry 124);
  - the icon set in `assets/icons`: `app.ico` for the exe and every window, `tray.ico`'s frame for the tray's size,
    `sendto.ico` beside the exe (this copy's older entry gets it); the Store and readme pictures are kept for 1.0.0 and
    not shipped (entry 127);
  - `dialogFont` and `dialogTextScale` beside the bar's `uiFont` and `textScale`: absent takes the bar's, `""` is
    Windows' own, 100 is kept; menus and tooltips take the dialogs'; no bar factor in `UiFonts`; the folder stack has the
    dialogs' font at Windows' size; the 外觀 tab scrolls at 100 % (entries 128 and 129, review 39).
- Keep as designed (0.13.0):
  - `portable.txt`'s first line names the data folder (`DataLocation.FolderOf`, Windows' rules by hand). A note in it
    (the folder it names has no bar while the program's folder has one) keeps the program's folder, makes nothing,
    and says so once (entry 117, R1). *Don't copy* leaves an empty bar in a folder without one;
  - the import copies `layout.json`, `workspaces.json`, `usage.json`, `icons\` and `sounds\` (icon paths made the
    target's; `%APPDRIVE%` only on the shared location and this PC's own), never approvals, the sign-in, `sync.json`,
    `this-pc.json`, logs, history or the Drive cache. Files in the way are kept as `….before-import-<date>`. Into a
    running portable bar it happens at the restart (the request file); the copy at Save happens at once after a flush
    (entries 111 and 117);
  - the shell's frame around a padded icon is found by its shape; inside it any centred icon counts, a corner one is
    left alone; without a frame, 70 % (entry 117, R2; review 34);
  - `uiFont` and `textScale` sync like `chineseFont`; a dialog is scaled whole and capped to the work area with a
    scrolling body; with names shown the cell's width follows the size (entries 113 and 117);
  - hints take the dialog's text colour; grey stays for the log's Debug lines, the Drive viewer's dividers, the
    filter placeholder and the folder stack's two lines (entry 109, review 32);
  - *Send to PC* added before sync keeps an `AddedHereMark`, never an `IconMark` (entry 110, review 32);
  - zh-Hant says 命令 for commands (review 34).
- Keep as designed (0.12.1–0.12.2):
  - `usage.json` is read and written as a JSON tree: what this version cannot read goes back as it was, a file that
    is not the JSON this app writes is kept as `usage.json.corrupt-<date>`, and none over 1 MB is written (entry 98);
  - a click that minimises the program in front is no use (`LaunchOutcome.Minimized`, entry 103);
  - a sync problem's notice opens the Sync window (entry 93);
  - the font is found at the first place that has a file of its name that is a font (entry 102).
- Keep as designed (0.12.0):
  - the symbol table (`SymbolCatalog`, 197 entries) and the tile look are the review session's design (entry 82);
  - usage counts stay in the local `usage.json`, never synced. Only a folder that cannot be written keeps them in
    memory; a failed write is tried again (entry 89, U1);
  - the names table's column widths and minimums, and the text on a tile colour by WCAG contrast, are the release
    check's (entry 89). The theme's rule for a custom background is separate and unchanged;
  - the Taipei Sans TC font is never committed or shipped, and it is installed only when its button is clicked
    (entry 85);
  - WP5's earlier forms (新細明體 without ClearType, a bundled font) were withdrawn by the owner.
- Seen in the app by the review session:
  - for 0.12.0: WP3, WP4, F1, and WP5 with the real font download and install (with the owner's leave); the release
    check's fixes V1, V3 and V4, and S1 with real keys;
  - for 0.12.1 (review 29): A1–A3 in English, the Install row, D2, F2, F3, and U1 with `WM_QUERYENDSESSION`;
  - for 0.12.2 (review 30): N1–N4 (N3 in zh-Hant only; English and zh-Hans in `DialogFitTests`);
  - for 0.13.0 (reviews 33 and 34, a portable copy on zh-TW Windows 11 at 100 %): `portable.txt` with `.`, a move to
    `data` with *Copy* and *Restart now*, an absolute folder in quotes, the question at a first start with *Start
    empty*, the note copy (R1); PCMan at 32, 48 and 64 (R2); hints in the light Fluent look; Settings at 150 % zh-Hant
    and 90 % English; names at 150 % (R3); *Send to PC* on a copy without sync;
  - for 0.14.0 (reviews 36–40, a portable Release build on zh-TW Windows 11 at 100 %, GPU): the three separator kinds
    and drags across them; the background at 30 % without a backdrop, sharp, icons solid; T1–T3 over white and red;
    the 可攜 and 傳送到電腦 tabs; text over a picture (a corner plate, a middle outline); the exe's and every title bar's
    icon and `sendto.ico` beside the exe; dialogs at 150 % with the bar at 100 % (Settings 780 wide, nothing cut) and
    the bar's menu at 150 %;
  - for 0.15.0 (reviews 41 and 42, a portable Release build on zh-TW Windows 11 at 100 %): the twin with the bar's rows,
    row line and picture; the hold key (gone within half a second, a 60 ms tap leaves nothing); a drag refused by the
    lock, with its notice; the bar at 40 px; sharp text in Settings, the picker, the position dialog and a tooltip, and
    Settings at 150 % (780 wide, nothing cut); the picker, the hover and focus colours with the contrast warning; the
    picture at 40 % with the desktop through it, and the position dialog; up or down at −20 and +20 %; a caption's look,
    a CPU number and the clock's time with looks (set in the layout file); U1 at 100 %; U2 over a gradient with a white
    disc at 60 %;
  - for 0.16.0 (reviews 43 and 44, a portable Release build on zh-TW Windows 11 at 100 %): the hold form without its box
    (734 × 302 against the tap form's 734 × 329), the icons at the top; the placement without an anchor in both forms,
    and anchors 0/100, 50/50 and 100/0 to the pixel in both; the 按鍵 row at 100 %; 外觀 with a built-in theme (the six
    rows gone) and with 自訂 (the block's line and fill on the light dialog); the clock's name in Georgia, bold, yellow,
    a CPU monitor's name on a plate moved down, and the clock's 時間 / 名稱 window.
- Not seen anywhere:
  - a real notification banner, a real sign-out, a real Windows snap, and maximise on a second monitor;
  - the recogniser with a microphone (the reviewer asks the owner before it opens one);
  - Windows 10 (the symbol font fallback), another DPI scale, and a real lock on `usage.json` (a scanner, a sync
    client);
  - for 0.13.0: *Import this PC's data…* and *Import* at a first start, and R4–R6 run (they read the owner's real data
    folder: the reviewer asks the owner), the *This PC* direction and a folder that cannot be used, hints in the dark
    and the classic look, Properties at 150 %, a chosen font other than the default, a real stick, Program Files;
  - for 0.14.0: the tray icon, the Send to menu, saving on the 傳送到電腦 tab and the receive folder's rules, a rename
    reaching another PC, F2 or a double-click on a row line, English and zh-Hans, the zips;
  - for 0.15.0: U3 with the pointer resting on an icon while Settings opens; the picture between two PCs (a folder or
    Drive); a real download; a file picked through the dialog; the 50 MB refusal; Mica or Acrylic under the picture and
    on the twin; mouse drags in the picker and the position dialog; a click with the hold key down; 文字外觀… from a
    widget's menu and F2 on a row line; icons at 96–256; 125 %; English and zh-Hans; the zips;
  - for 0.16.0: drags and keys on the position picture; the anchor at a screen edge; the scroll into view when 自訂 is
    chosen; the block on a dark dialog and in High Contrast; a long name shrunk to its cell; names hidden with a name look;
    the name over a picture while see-through; 150 %; English and zh-Hans; the zips.
- Nothing is open for the owner from this session.
