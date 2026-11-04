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
| Background | Flat colour, configurable in Settings (default: the olive grey of the reference). |

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
| Icon | Default = extracted from the Location. *Change icon…* picks a `.png/.jpg/.ico/.bmp` image or an `.exe/.dll/.ico` resource (`path,index` is accepted). *Use default* clears the override. |

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

### 2.3 Special items

*Add special item ▸* inserts a ready-made shortcut for a Windows location or tool.
They are ordinary shortcuts afterwards (editable, removable). Catalog, updated for
Windows 10/11 while keeping the reference set:

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

## 3. Grid interactions

| Action | Result |
| --- | --- |
| Left click an icon | Launch (shortcut) or open popup (group). Option: double-click instead. Repeated clicks within 400 ms launch once. |
| Right click an icon | Item context menu (§4). |
| Right click empty space | Bar context menu (§4). |
| Drag an icon | Reorder: a drop indicator shows the insertion point; dropping inserts there. |
| Drag an icon onto a group | Moves it into the group (the group highlights while hovering). |
| Drop files/folders/URLs from Explorer or a browser | Creates one shortcut per dropped path at the drop position. |
| Mouse wheel | Scrolls when the bar is shorter than its content. |

## 4. Context menus

Icon menu (as in the screenshot):

```
New shortcut…
New group…
Add special item        ▸
────────────────────────
Remove…
Add to Multi Launch group    (becomes "Remove from Multi Launch group" when it is a member; hidden for groups)
────────────────────────
Properties
```

Empty-space menu:

```
New shortcut…
New group…
Add special item        ▸
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
| Background colour | olive grey |
| Start with Windows | off — Phase 3 (Run registry key) |
| Show/hide hotkey | none — Phase 3 |

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
                "backgroundColor": "#73806E" },
  "window": { "left": 100, "top": 100, "width": 520, "height": 180 },
  "items": [
    { "$type": "shortcut", "id": "…", "name": "WinSCP", "location": "U:\\E\\WinSCP\\WinSCP.exe",
      "parameters": "", "iconPath": null, "multiLaunch": true },
    { "$type": "group", "id": "…", "name": "Tools", "iconPath": null,
      "items": [ { "$type": "shortcut", "…": "…" } ] }
  ]
}
```

## 9. Icon extraction

Order of attempts, with an in-memory cache keyed by path and size:

1. Explicit image file (`.png`, `.jpg`, `.bmp`, `.gif`, `.ico` — largest matching frame).
2. `path,index` icon resources via `SHDefExtractIcon` (any size up to 256 px).
3. `IShellItemImageFactory` for files, folders, `.lnk` and CLSID parsing names — gives
   the same high-resolution icon Explorer shows.
4. System image list (`SHGetFileInfo` + `SHGetImageList`) by extension for paths that
   do not exist yet, and a browser icon for `http(s):` links.
5. A built-in placeholder glyph.

## 10. Project layout

```
DesktopIconGrid.slnx
src/DesktopIconGrid.Core/      models, ordering/grouping logic, JSON storage, launch info — no WPF, testable on any OS
src/DesktopIconGrid/           WPF app: window, grid control, dialogs, shell interop, view models
tests/DesktopIconGrid.Core.Tests/   xunit tests for the core library
```

## 11. Delivery phases

**Phase 1 — usable launcher**
Borderless bar (grip, close, resize, options arrow), flowing icon grid, shortcuts with
the full properties dialog, icon extraction, click to launch, drag to reorder, drop
from Explorer, context menus, settings dialog, JSON persistence, window placement.

**Phase 2 — groups, special items, multi launch**
Group items with popup grid and drag in/out, *Add special item* catalog,
Multi Launch membership and launch-all.

**Phase 3 — desktop integration**
Tray icon (close hides to tray), start with Windows, global show/hide hotkey,
optional auto-hide at a screen edge.

## 12. Out of scope (for now)

Nested groups, per-item hotkeys, drag-and-drop *out* of the app to Explorer,
themes/skins beyond a background colour, multiple bars.
