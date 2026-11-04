# DesktopIconGrid — Feature Plan

A compact Windows launcher bar that shows your programs, folders and web links as
icons in a grid. One click launches the item. The plan below is derived from the
reference screenshots (launcher bar, context menus, *Shortcut Properties* and
*Group Properties* dialogs) and from the project brief.

Stack: .NET 10 (LTS), WPF, C#. No third-party UI packages. MVVM with a small
in-repo `ObservableObject` / `RelayCommand`. Layout persisted as JSON.

---

## 1. The launcher window

Matches the first screenshot: a borderless bar with a grip on the left, a close
button at the top-right and a small options arrow at the bottom-right.

| Area | Behaviour |
| --- | --- |
| Left grip (`‖`) | Press and drag to move the bar. Double-click toggles *always on top*. |
| Close button (`×`) | Phase 1: exits the app. Phase 3: hides the bar to the tray instead. |
| Bottom-right arrow | Opens the options menu: *Settings…*, *Launch Multi Launch group*, *About*, *Exit*. |
| Edges / corners | Resizable (WindowChrome resize border, no caption). The icon grid reflows to the new width. |
| Show/hide hotkey | A system-wide combination set in Settings (e.g. `Ctrl+Alt+Space`) hides the bar or brings it to the front from any program. |
| Background | A theme: Classic (the olive grey of the reference), Graphite, Paper, Midnight, Sand, or a Custom colour with automatically matched text and highlight colours. See [docs/THEMES.md](docs/THEMES.md). |

- Icons are laid out in reading order and **wrap** to fill the width: the number of
  columns is `floor(availableWidth / cellSize)`. Rows grow as needed; a vertical
  scrollbar appears if the bar is shorter than its content.
- No labels by default (as in the reference). Labels can be switched on in Settings.
- Position, size and *always on top* are remembered between runs. If the saved
  position is off-screen (monitor removed) the bar is moved back on screen.
- Keyboard: `Tab`/arrows move focus between icons, `Enter` launches, `Delete` removes,
  `F2` opens Properties, `Ctrl+N` new shortcut, `Ctrl+G` new group, `Ctrl+,` settings.

## 2. Items

Everything on the bar is an *item*. There are two kinds.

### 2.1 Shortcut

A launchable entry. Fields come straight from the *Shortcut Properties* dialog:

| Field | Notes |
| --- | --- |
| Name | Caption (shown as tooltip, and as label when labels are enabled). Pre-filled from the file name. |
| Location | A file, a folder or a URL. `%ENV%` variables are expanded at launch. The `…` button opens a file picker. |
| Parameters | Command-line arguments passed to the program. |
| Icon | Default = extracted from the Location. *Change icon…* picks a `.png/.jpg/.ico/.bmp` image or an `.exe/.dll/.ico` resource (`path,index` is accepted). *Text icon…* draws text instead (see below). *Use default* clears the override. |

**Text icons.** Any shortcut or group can show text on a tile instead of an image: up to
12 characters (e.g. 宋體 characters or an abbreviation) in a chosen font family, size (6–72),
weight and colour. The default family is 宋體 (SimSun), falling back to 新細明體 (PMingLiU) and
Segoe UI when it is not installed; the colour defaults to the theme's text colour. Hovering an
icon shows its name and location in a tooltip regardless of how the icon is drawn.

Extra flags kept out of the dialog's main layout but supported:

- **Multi Launch member** (toggled from the context menu, see §5).
- Launch uses `ShellExecute` semantics, so `.lnk`, documents, folders, `http(s):`,
  `mailto:`, `ms-settings:` and `shell:` locations all work.

### 2.2 Group (a folder of items)

A group is a folder: it holds its own ordered list of shortcuts. Its properties
dialog is the same form with Location and Parameters disabled:

| Field | Notes |
| --- | --- |
| Name | Required. |
| Icon | Default = the system folder icon. *Change icon…* / *Use default* as for shortcuts. |

- Clicking a group opens a **popup grid** anchored to its icon showing the group's
  items, using the same grid control and the same context menus. The popup closes when
  an item is launched or when you click outside it.
- Drag an item onto a group icon to move it into the group. Drag an item from the
  popup onto the main bar to move it out.
- Groups cannot contain other groups (one level deep). Removing a group asks for
  confirmation and removes its contents too.

### 2.3 Windows items (ready-made shortcuts)

*Add Windows item ▸* (the reference app's "Add special item") inserts a ready-made
shortcut for a Windows location or tool. They are ordinary shortcuts afterwards
(editable, removable). Catalog, updated for Windows 10/11 while keeping the reference set:

| Menu entry | Location |
| --- | --- |
| This PC | `shell:MyComputerFolder` |
| Network | `shell:NetworkPlacesFolder` |
| Network Connections | `shell:ConnectionsFolder` |
| Control Panel | `shell:ControlPanelFolder` |
| Printers | `shell:PrintersFolder` |
| Settings | `ms-settings:` |
| — | |
| Recycle Bin | `shell:RecycleBinFolder` |
| Show Desktop | `special:show-desktop` (toggles the desktop via the Shell COM object) |
| Search | `search-ms:` |
| Run… | `special:run` |
| — | |
| File Explorer | `explorer.exe` |
| Calculator | `calc.exe` |
| Notepad | `notepad.exe` |
| Paint | `mspaint.exe` |
| WordPad | `write.exe` (hidden when not installed, e.g. Windows 11 24H2) |
| Command Prompt | `cmd.exe` |
| Windows Terminal | `wt.exe` (hidden when not installed) |
| Task Manager | `taskmgr.exe` |
| — | |
| Desktop / Documents / Downloads / Pictures | the user's known folders |

`shell:` locations are launched through `explorer.exe`; `special:` locations are
handled in-app. Icons for `shell:` folders are taken from their CLSID parsing names.

### 2.4 Special icons (active icons)

A special icon has behaviour of its own: a click action, drops of files onto it, progress
and a kind-specific properties dialog. The first one is the **Google Drive Bucket**: drop
files to upload them into a Drive folder, click to open that folder in the browser.
The item model, handler interface and the bucket are specified in
[docs/SPECIAL_ICONS.md](docs/SPECIAL_ICONS.md).

## 3. Grid interactions

| Action | Result |
| --- | --- |
| Left click an icon | Launch (shortcut) or open popup (group). Option: double-click instead. Repeated clicks within 400 ms launch once. |
| Right click an icon | Item context menu (§4). |
| Right click empty space | Bar context menu (§4). |
| Drag an icon | Reorder: a drop indicator shows the insertion point; dropping inserts there. |
| Drag an icon onto a group | Moves it into the group (the group highlights while hovering). |
| Drop files/folders/URLs from Explorer or a browser | Creates one shortcut per dropped path at the drop position. |
| Drop files onto a special icon that accepts them (e.g. a Google Drive Bucket) | Handled by the icon: the bucket uploads them. |
| Press on empty background | Moves the bar, like the grip. |
| Mouse wheel | Scrolls when the bar is shorter than its content. |

## 4. Context menus

Icon menu (as in the screenshot):

```
New shortcut…
New group…
Add Windows item        ▸
Add special icon        ▸
────────────────────────
Remove…
Add to Multi Launch group    (becomes "Remove from Multi Launch group" when it is a member; hidden for groups)
Open file location
────────────────────────
Properties
```

Empty-space menu:

```
New shortcut…
New group…
Add Windows item        ▸
Add special icon        ▸
────────────────────────
Launch Multi Launch group
Settings…
```

*New shortcut…* / *New group…* open the properties dialog with an empty item and
insert it at the clicked position (or at the end). *Remove…* asks for confirmation.

## 5. Multi Launch group

A set of shortcuts that start together.

- Membership is a flag on the shortcut, toggled from the icon menu. Members show a
  small badge in the corner of their icon.
- *Launch Multi Launch group* (options menu, empty-space menu) starts every member in
  bar order with a short delay between launches. Failures are reported once, together.
- Groups cannot be members, but shortcuts inside groups can.

## 6. Properties dialogs

Both dialogs follow the reference layout: a labelled form, an icon preview with
*Change icon…* / *Use default*, and *Save* / *Cancel*.

- *Save* is enabled when Name is non-empty and, for shortcuts, Location is non-empty.
- A soft warning (not blocking) appears when a rooted Location or icon path does not
  exist, so paths on removable drives can still be saved.
- Changing the Location refreshes the icon preview and fills in the Name if it was
  still the automatic one.

## 7. Settings

| Setting | Default |
| --- | --- |
| Icon size | 32 px (choices 16 / 24 / 32 / 48 / 64) |
| Cell padding | 10 px (cell = icon size + 2 × padding) |
| Show labels | off |
| Launch on | single click (or double click) |
| Always on top | off |
| Theme | Classic (Graphite, Paper, Midnight, Sand, or Custom colour) |
| Start with Windows | off — Phase 3 (Run registry key) |
| Show/hide hotkey | none (set in Settings; needs Ctrl, Alt or Win) |

Settings live in the same JSON file as the layout and apply immediately on *Save*.

## 8. Persistence

- File: `%LocalAppData%\DesktopIconGrid\layout.json`, written atomically
  (temp file + rename) after every change and on exit.
- A corrupt file is renamed to `layout.json.corrupt-<timestamp>` and the app starts
  with an empty bar, telling the user where the backup is.
- Shape (abridged):

```json
{
  "version": 1,
  "settings": { "iconSize": 32, "cellPadding": 10, "showLabels": false,
                "doubleClickToLaunch": false, "alwaysOnTop": false,
                "theme": "classic", "backgroundColor": "#73806E" },
  "window": { "left": 100, "top": 100, "width": 520, "height": 180 },
  "items": [
    { "$type": "shortcut", "id": "…", "name": "WinSCP", "location": "U:\\E\\WinSCP\\WinSCP.exe",
      "parameters": "", "iconPath": null, "multiLaunch": true },
    { "$type": "group", "id": "…", "name": "Tools", "iconPath": null,
      "items": [ { "$type": "shortcut", "…": "…" } ] }
  ]
}
```

## 9. Google Drive sync

Keeps named copies of the settings and icons on Google Drive so several PCs can share them.

- **Copies**: a `DesktopIconGrid` folder in My Drive holds one JSON file per copy
  (`Work.json`, `Home.json`, …) containing the settings and items, plus who saved it and when.
  The window position stays on the device. Any number of copies, each with its own name.
- **Device name**: each PC has a name (defaults to the computer name) shown to other devices
  as the one that saved a copy.
- **Dialog** (options menu ▸ *Sync with Google Drive…*): connect/disconnect the Google account,
  enter the device name, list the copies with their save time and device, *Save as* a new
  copy, *Overwrite* or *Load* the selected copy, *Delete* a copy.
- **Linked copy**: saving or loading links the device to that copy. Changes are then uploaded
  automatically a few seconds after they happen, and at startup the app offers to load the
  copy when another device saved a newer version. Auto-upload never overwrites a newer copy
  from another device; it reports the conflict instead so the user chooses in the dialog.
- **Account access**: Google's OAuth flow for installed apps (browser sign-in, loopback
  redirect, PKCE) with the `drive.file` scope. Tokens are stored encrypted (DPAPI) and renewed
  automatically; the user signs in again only if Google revokes the refresh token.
- **Credentials**: Google requires an OAuth client per app, which the user creates in Google
  Cloud Console (project → enable Drive API → consent screen → Desktop app credential). The
  dialog accepts the client ID/secret or the downloaded `client_secret.json`. Publishing the
  consent screen avoids the 7-day token expiry of "Testing" status.
- Local state in `%LocalAppData%\DesktopIconGrid\sync.json` (device name, credentials, linked
  copy, last sync time) and `google-token.bin` (encrypted token).

## 10. Icon extraction

Order of attempts, with an in-memory cache keyed by path and size:

1. Explicit image file (`.png`, `.jpg`, `.bmp`, `.gif`, `.ico` — largest matching frame).
2. `path,index` icon resources via `SHDefExtractIcon` (any size up to 256 px).
3. `IShellItemImageFactory` for files, folders, `.lnk` and CLSID parsing names — gives
   the same high-resolution icon Explorer shows.
4. System image list (`SHGetFileInfo` + `SHGetImageList`) by extension for paths that
   do not exist yet, and a browser icon for `http(s):` links.
5. A built-in placeholder glyph.

## 11. Project layout

```
DesktopIconGrid.slnx
src/DesktopIconGrid.Core/      models, ordering/grouping logic, JSON storage, launch info, Google OAuth + Drive client, sync — no WPF, testable on any OS
src/DesktopIconGrid/           WPF app: window, grid control, dialogs, shell interop, view models
tests/DesktopIconGrid.Core.Tests/   xunit tests for the core library
```

## 12. Delivery phases

**Phase 1 — usable launcher** *(done)*
Borderless bar (grip, close, resize, options arrow), flowing icon grid, shortcuts with
the full properties dialog, icon extraction, click to launch, drag to reorder, drop
from Explorer, context menus, settings dialog, JSON persistence, window placement.

**Phase 2 — groups, Windows items, multi launch** *(done)*
Group items with popup grid and drag in/out, *Add Windows item* catalog,
Multi Launch membership and launch-all.

**Phase 3 — Google Drive sync** *(done)*
Named settings copies on Drive, device name, save/load/delete, linked copy with
automatic upload and startup check, OAuth with automatic token renewal.

**Phase 4 — special icons**
Special icon item kind and handler registry; Google Drive Bucket (click to open the
folder, drop files to upload) as specified in [docs/SPECIAL_ICONS.md](docs/SPECIAL_ICONS.md).

**Phase 5 — desktop integration**
Global show/hide hotkey *(done)*; tray icon (close hides to tray), start with Windows,
optional auto-hide at a screen edge.

## 13. Out of scope (for now)

Nested groups, per-item hotkeys, drag-and-drop *out* of the app to Explorer,
themes/skins beyond a background colour, multiple bars.
