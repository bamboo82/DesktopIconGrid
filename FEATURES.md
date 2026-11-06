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
| Tray icon | While the app runs, an icon in the notification area offers: Show/Hide bar, Reset bar position (moves it to the middle of the primary screen when it is lost on a disconnected monitor), New shortcut, New group, Edit names, Launch Multi Launch group, Always on top, Settings, Sync across PCs, Log viewer, Open layout file location, About, Exit. Left-click toggles the bar. The close button hides the bar to the tray (setting). |
| Transparency | Settings → Transparency (0–80 %) lets the desktop show through the bar and the group popup; with a Mica or Acrylic background it sets how much of the blurred backdrop shows while the icons stay sharp. |
| Show/hide hotkey | A system-wide combination set in Settings (e.g. `Ctrl+Alt+Space`) hides the bar or brings it to the front from any program. |
| Show-at-pointer hotkey (0.7.0) | A second combination opens the icons in a small window under the mouse pointer with the search box focused, on the pointer's monitor and inside its work area. It works while the bar is hidden or auto-hidden; groups open in place with a back button (or Backspace); it closes after a launch, on Esc and when it loses the focus. |
| Search box (0.7.0) | Typing while the bar has the focus (or `Ctrl+F`) opens a box at the bottom of the bar; see §3. |
| Background | A theme: Classic (the olive grey of the reference), Graphite, Paper, Midnight, Sand, or a Custom colour with automatically matched text and highlight colours. See [docs/THEMES.md](docs/THEMES.md). |

- Icons are laid out in reading order and **wrap** to fill the width: the number of
  columns is `floor(availableWidth / cellSize)`. Rows grow as needed; a vertical
  scrollbar appears if the bar is shorter than its content.
- No labels by default (as in the reference). Labels can be switched on in Settings.
- Position, size and *always on top* are remembered between runs. If the saved
  position is off-screen (monitor removed) the bar is moved back on screen.
- Keyboard: `Tab`/arrows move focus between icons, `Enter` launches, `Delete` removes,
  `F2` opens Properties, `Ctrl+N` new shortcut, `Ctrl+G` new group, `Ctrl+,` settings,
  `Ctrl+F` or any letter, digit or punctuation key the search box.

## 2. Items

Everything on the bar is an *item*: shortcuts, groups, Windows items and special icons, and from 0.7.0
separators (§2.5). Any item can carry a colour tag (§2.6).

### 2.1 Shortcut

A launchable entry. Fields come straight from the *Shortcut Properties* dialog:

| Field | Notes |
| --- | --- |
| Name | Caption (shown as tooltip, and as label when labels are enabled). Pre-filled from the file name. |
| Location | A file, a folder or a URL. `%ENV%` variables are expanded at launch. The `…` button opens a file picker. |
| Parameters | Command-line arguments passed to the program. `{file}`/`{files}` take dropped files; a click leaves them out, so write them last: an option before them (`--open {file} --wait`) would take the next word on a click (0.8.1). `{clipboard}` inserts the clipboard text and `{ask:Question}` asks for a value when the shortcut is started (Cancel cancels the launch). Each value is quoted as one argument, so write a placeholder as an argument of its own, as part of an unquoted word (`--name={clipboard}`) or alone in quotes (`"{file}"`): inside quotes with other text (`"--in={file}"`, WSL's `'…'` too), or right next to a quote or another placeholder, it is refused, since its quotes would end the template's; in a quoted command for ssh's remote shell or PowerShell (`ssh host "tail {ask:File}"`), which take bare words anyway, it goes in as it is (0.8.1). All of them go in with one pass, so a value that holds a placeholder keeps it as text, and dropped files are judged where the template puts them, `{files}` as two `{file}` (0.8.1). A value is refused, before anything starts, when the program could run it: through cmd.exe (a batch file, also one found for a command without an extension, or cmd.exe itself) `" % ! ^ & | < >`, and `%` or `!` in a dropped file's name (0.8.1); as PowerShell script text anything but letters, digits, spaces and `. _ - \ / :`, and a word that starts with `-` (a parameter there); the same for the command ssh or plink sends after its destination; in ssh's destination, `-l` and `-J` (and a host written before the colon of scp's or sftp's `host:path`), only what `user@host`, `[v6]:port` and `ssh://` need, with no `-` to start the name or a part of it (0.8.1); in ssh's `-o` nothing at the start of the value or in the setting's name, nor after a setting that runs or loads something (`ProxyCommand`, `PKCS11Provider`, `Include`…), nothing in `-F` or `-I`, scp's `-S`, sftp's `-D` or `-b`, plink's `-proxycmd` or `-m`, and nothing among an option's letters, before or after the host (0.8.1); PuTTY's options after the host are its own, and a bare word there is the port (0.8.1); through a Linux shell in WSL (`wsl.exe` without `-e`/`--exec` or `--shell-type none`, System32's `bash.exe`) also `$` and `` ` ``; each placeholder is judged where it stands (0.8.1); through Windows Terminal (`wt`) also `;`; after Windows Terminal, conhost, sudo or gsudo, which hand arguments on without quotes when they hold no space, a placeholder that cmd.exe or WSL's shell reads is refused, and elsewhere a value with a quote or a tab, or one with a space that ends in `\`, and a batch file such a relay (or runas) starts is read as cmd.exe reads it (0.8.1); for cmd.exe itself, the command after `/c` (also `/d/c`, `/V:ON/C`) keeps its values' quotes, and a value before it is refused (0.8.1). A placeholder inside code for another program (`python -c`, `node -e`, `bash -c`) is code there: the template's choice. For every program, a value is refused when it has more than one line, holds `＂`, or begins with `-` where an argument starts with no `--` of the program that reads it before it (0.8.0; a wrapper's `--`, as in `wsl -- python3 {x}`, ends only the wrapper's options, 0.8.1), or there begins with `/`, except for a shell (0.8.1). |
| Icon | Default = extracted from the Location. *Change icon…* picks a `.png/.jpg/.ico/.bmp` image or an `.exe/.dll/.ico` resource (`path,index` is accepted). *Text icon…* draws text instead (see below). *From a web address…* downloads an image once, scales it to 256 px, centres it on a transparent square and keeps it as a local PNG. *Use default* clears the override. |

**Running badge and output log.** A green dot in the corner of a shortcut shows that a program
started from it is still running (processes started from the bar are followed until they exit;
executables are also matched by their full path against the running processes every few seconds,
so a program started elsewhere counts too, while a same-named program elsewhere and the Windows
shell itself do not). *Keep a log of the program's output* in the properties starts console programs and
batch files with their output redirected to `%LocalAppData%\DesktopIconGrid\logs\<name>_<time>.log`
(the ten newest per shortcut are kept) and adds *Show log…* to the context menu: a live window with
the command line, start time, output, errors and exit code.

**Text icons.** Any shortcut or group can show text on a tile instead of an image: up to
12 characters (e.g. 宋體 characters or an abbreviation) in a chosen font family, size (6–72),
weight and colour. The default family is 宋體 (SimSun), falling back to 新細明體 (PMingLiU) and
Segoe UI when it is not installed; the colour defaults to the theme's text colour. Hovering an
icon shows its name and location in a tooltip regardless of how the icon is drawn.

**Advanced** (0.7.0), the options a Windows `.lnk` has:

| Field | Notes |
| --- | --- |
| When it is running | Switch to its window, or start another (§3). Follows the Location until picked. |
| Start in | The working folder; empty uses the program's own folder. `%ENV%` variables are expanded. |
| Run | Normal window, minimised or maximised. |
| Run as administrator | An ordinary UAC prompt every time it starts. |
| Open web links in | The default browser, or a profile of Edge or Chrome (read from their `Local State`), started with `--profile-directory`; *Open as an app window* adds `--app=<url>`. |

**On other PCs** (0.8.0). A synced bar can point at a program that sits elsewhere on another PC
(`U:\E\WinSCP\WinSCP.exe` here, `C:\Program Files (x86)\WinSCP\WinSCP.exe` there):

- *Missing here*: a shortcut whose location is a path on a local fixed drive that names neither a file
  nor a folder is drawn faded with a small "?" and says so in its tooltip. The check is a quick
  existence test on the icon worker; links, `shell:`/`special:` locations, bare program names, UNC paths
  and network, removable or optical drives are never checked. A drive letter this PC does not have at all
  (another PC's `D:`, a stick that is not plugged in) is not a slow drive: Windows says so without touching
  any drive, so such a shortcut is marked.
- *Repair…* (the context menu of a missing shortcut, or a click on it) looks for the program: the same
  file in a sibling folder that differs only by its version number, newest first
  (`app-1.0.9172` → `app-1.0.9203`, also a level or two up the path); the program's `App Paths` entry;
  a folder of the same name in Program Files, Program Files (x86) and `%LocalAppData%\Programs`; then
  the same file name in each of them, up to three folders deep, with a cap on how many folders are looked
  at in each (it says so when it stopped early). The Store's `WindowsApps` folder is searched only for
  the program's own package. A match on the file name alone is listed last and not chosen for you: it
  may be another program's `launcher.exe`. *Browse…* picks one by hand. The fix applies to *this PC
  only* (the default) or to *every PC that shares the bar*, where a PC with a location of its own keeps
  it (it has one because its path differs). A *Start in* that was only the program's folder moves with the
  program: it is cleared, so every PC starts the program in its own folder, and a *Start in* that is not on
  this PC falls back to the program's folder instead of stopping the start (0.8.1).
- *This PC only* is stored in the shortcut as `"locations": { "<this PC>": "…" }` beside the common
  `location`, so it travels with the sync copy and each PC finds its own path. *This PC* is the PC and
  Windows user, keyed by a hash of Windows' machine id and the user's SID that is worked out at every
  start and never stored on the PC, so a portable copy on a stick, or data copied to another PC, does
  not carry one PC's paths to the next. Sync settings made on another PC (copied data, a stick, a cloned
  VM) get a device id and name of their own on this one. *Properties* shows the location on this PC and has the
  same *Only on this PC* box ("Other PCs keep …"). Launching, icons, the running badge and Open with all
  use the location on this PC.

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

### 2.3a Installed apps (0.8.0)

*Add app…* (icon and empty-space menus) lists every app the Start menu shows, read from
`shell:AppsFolder`, Microsoft Store apps included, with their icons; the list filters as you type (case,
width and Chinese handled as in search) and marks what is already on the bar. Ticked apps become
shortcuts named as the Start menu names them, right after the clicked icon or at the end. A desktop
program is stored as its real path, so the running badge, switching and the missing check work as for
any program: an entry that is a path (below a known folder or plain) is that path, and an entry under an
id the program registered (`Chrome`, `Microsoft.AutoGenerated.{…}`) is the `.exe` its Start menu
shortcut starts, when it starts it without arguments (Chrome's and Edge's note of where they were
started from does not count). An id whose shortcut adds arguments keeps the id and loses the badge and
switching. A Store app is stored as `shell:AppsFolder\<app id>` and started through Explorer; links of
any kind (`https:`, `steam:`) stay links. Before 0.8.0 the usual way
to get a Store app onto the bar was to make a desktop shortcut first.

### 2.4 Special icons (active icons)

A special icon has behaviour of its own: a click action, a live value, drops of files onto it
and a kind-specific properties dialog. Available now from *Add special icon ▸*:

- **CPU usage** — the processor's busy percentage, refreshed every 2 seconds; click opens Task Manager.
- **Disk usage** — how full a drive of your choice is, refreshed every 10 seconds; click opens the drive.

Both draw the value in a design you pick (ring, bar or number) with green/amber/red levels.

- **Google Drive Bucket** — a Google Drive folder. Drop files or folders on it to upload them
  (progress ring on the icon; same-name policy: keep both, replace or skip); click to open the
  folder in the browser; **Ctrl+click** or *Browse…* opens the Bucket Viewer, an Explorer-like
  window to browse the folder, open files, *Download to…* a local folder, drag items out to the
  desktop or Explorer (they are downloaded first), and drop files in to upload. Needs full Drive
  access, which the app asks for the first time (see the sync section for the credentials setup).

- **Sound Replayer** — press and hold the icon to record from the microphone (only while
  stopped); release to play the recording back in an endless loop; a single click stops it.
  A click while stopped replays the last recording. Shows REC / LOOP on the icon.

The item model, handler interface, registry and the bucket are specified in
[docs/SPECIAL_ICONS.md](docs/SPECIAL_ICONS.md).

### 2.5 Separators (0.7.0)

*Add separator ▸* on an icon (inserts after it) or on empty space (appends) offers three kinds, stored as
`"$type": "separator"` with `"kind": "line" | "gap" | "label"`:

| Kind | Looks like |
| --- | --- |
| Line | A thin divider between two icons in a row, nine units wide, or only what is left at the end of a full row (it never starts a row of its own); across the column in a bar one icon wide (docked left or right). |
| Gap | An empty cell. |
| Label | A small caption, as wide as its text; across the column in a one-icon-wide bar. *Label…* asks for the text; `F2` edits it. |

Separators have no click action and are left out of searches and of the names table; they are outlined
under the pointer so they can be found, dragged like icons, changed into another kind or removed from
their own menu, and may sit inside groups. Versions before 0.7.0 show them as items from a newer version
and keep them. Since 0.8.0 lines and gaps are saved with the name "Divider" or "Gap", which the bar ignores:
0.6.0 names every nameless item from a newer version "Group" and saves that, so lines saved nameless by
0.7.0 came back from a 0.6.0 PC called "Group". A kind this version does not know is kept and drawn as a
line.

### 2.6 Colour tags (0.7.0)

Right-click → *Colour tag ▸* gives any item one of seven colours (red, orange, yellow, green, blue, purple,
grey) or none. It is drawn as a small corner at the top left of the icon, stored as `"colorTag": "red"`,
and the names table can show only the items with one colour, or those without (§6.1). A tag name this
version does not know is kept but not drawn.

## 3. Grid interactions

| Action | Result |
| --- | --- |
| Left click an icon | Launch (shortcut) or open popup (group). Option: double-click instead (groups and special icons still act on one click). Repeated clicks within 400 ms launch once. |
| Left click a running program (0.7.0) | Like the taskbar: brings its window forward (restored if minimised); a second click while it is in front minimises it; with several windows each click goes to the next one. Store apps are found through the frame they are drawn in, also when minimised and suspended (by the app ID the frame keeps, since 0.8.0). Per shortcut, *Properties → Advanced → When it is running* can make clicks start another instead; documents, folders and links do that by default. |
| Shift+click or middle-click an icon | Starts another instance. |
| Ctrl+Shift+click an icon | Starts it as administrator (an ordinary UAC prompt). |
| Right click an icon | Item context menu (§4). |
| Right click empty space | Bar context menu (§4). |
| Drag an icon | Reorder: a drop indicator shows the insertion point; dropping inserts there. |
| Drag an icon onto a group | Moves it into the group (the group highlights while hovering). |
| Drop files/folders/URLs from Explorer or a browser | Creates one shortcut per dropped path at the drop position. |
| Drop files onto a special icon that accepts them (e.g. a Google Drive Bucket) | Handled by the icon: the bucket uploads them. |
| Press on empty background | Moves the bar, like the grip. |
| Click a shortcut missing on this PC (0.8.0) | Checks again, and if it is still not there opens *Repair…* (§2.1) instead of failing. |
| After a launch (0.7.0) | The icon (or the group it was in) is framed and pulses until the bar loses the foreground — usually when the program's window appears — or for five seconds at most. Without Windows animations it is only framed. |
| Type a name (0.7.0) | Opens the search box under the icons. Shortcuts and special icons that match, also inside groups, stay lit with the groups that hold them; everything else fades. Ranking: the name on the bar whole, at its start, at the start of a word, by word initials (`vsc` → Visual Studio Code), anywhere in it; then the original name (from the location) and the file name. Case and width are ignored, and Chinese is typed through the IME (no pinyin table). The best match is framed, and the group holding it with it; its name shows beside the box. `↓`/`↑` move the frame round the matches, `Enter` opens it (Shift and Ctrl+Shift as for a click), `Esc` clears and then closes. Only the bar's own items are searched: no web, calculator or file index. |
| Mouse wheel | Scrolls when the bar is shorter than its content. |

## 4. Context menus

Icon menu (as in the screenshot):

```
New shortcut…
New group…
Add app…                (every installed app, 0.8.0)
Add Windows item        ▸
Add special icon        ▸
Add separator           ▸   Line / Gap / Label…   (0.7.0)
────────────────────────
Remove                (Undo for ten seconds in a banner under the icons)
Add to Multi Launch group    (becomes "Remove from Multi Launch group" when it is a member; hidden for groups)
Open file location
Repair…                 (only for a shortcut missing on this PC, 0.8.0)
Colour tag              ▸   seven colours / None  (0.7.0)
────────────────────────
Properties
```

Separator menu (0.7.0): *Line*, *Gap*, *Label…* (the current kind checked), *New shortcut…*, *Remove*.

Empty-space menu:

```
New shortcut…
New group…
Add app…                (0.8.0)
Add Windows item        ▸
Add special icon        ▸
Add separator           ▸   (0.7.0)
────────────────────────
Launch Multi Launch group
Settings…
```

*New shortcut…* / *New group…* open the properties dialog with an empty item and
insert it at the clicked position (or at the end). *Remove* acts at once and offers *Undo* for ten
seconds in a banner under the icons (also `Ctrl+Z`); removing a group still asks first.

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
- *Advanced* (shortcuts): *When it is running* — switch to its window or start another (§3; until it is
  picked, the choice follows the Location and nothing is stored); start folder, window state, run as
  administrator and the browser profile for web links (§2.1).

### 6.1 Edit names table

Options menu (▸) or tray → *Edit names…* lists every item on the bar in one grid: *Given
name* (editable), *Original name* (what the item would be called automatically: the file name
of a shortcut, the kind of a special icon), *Kind*, *In group*, *Colour tag* (0.7.0), *Modified*
(when the item was added or last edited) and *Location*. *Show:* above the grid narrows it to the
items with one colour tag, or those without (0.7.0), or to the shortcuts missing on this PC (0.8.0);
separators are not listed. Click a column header
to sort; *Use original name*
resets the selected rows, *Use original names for all* resets everything; *Save* applies all
changes at once. The given names are the captions on the bar and will be the phrases for
voice commands in a later design, so this is where that vocabulary is maintained.

**Voice spike (0.7.0).** The question: does `Windows.Media.SpeechRecognition.SpeechRecognizer` with a list
constraint recognise these names in zh-TW from an unpackaged app on Windows 11 — yes or no?

**Result of the first run (2026-11-06, on the owner's PC): no, for want of the zh-TW speech pack.**

```
Windows 10.0.22631.0, 64-bit process, not packaged
Speech languages installed: en-US
Windows speech language: en-US
Failed: 0x800455BC COMException: 找不到與此錯誤碼關聯的文字。
The requested language is not supported.
ANSWER: no (see the error above)
```

Windows 11 23H2, unpackaged: the recogniser was reached, but only the en-US speech pack is installed, so
zh-TW is refused (`0x800455BC`). As planned for this case, the zh-TW speech pack becomes a documented
requirement of voice launch, and the probe runs again once the pack is installed (see below). Whether a
list constraint then recognises these names, on the device and without online-speech consent, is still
open until that second run.

What was known before running it:

- The recogniser is a WinRT API. An unpackaged .NET process reaches it through the Windows SDK projection of
  a `net10.0-windows10.0.19041.0` target framework, with no package added: `tools/VoiceProbe` compiles that
  way. It is not part of the app, its solution or its release.
- A list constraint is recognised on the device, so it should not need the online-speech consent that
  dictation needs (error `0x80045509`). To be confirmed by the probe.
- An unpackaged app declares no capabilities; its microphone access is *Settings → Privacy & security →
  Microphone → Let desktop apps access your microphone*. With that off, expect `0x80070005`.
- zh-TW must be listed in `SpeechRecognizer.SupportedGrammarLanguages`, which shows the installed speech
  packs (*Settings → Time & language → Language & region → 中文(台灣) → Speech recognition*).
- Microsoft has deprecated Windows Speech Recognition, the desktop dictation app, in favour of Voice access.
  Whether that will affect `Windows.Media.SpeechRecognition` is not stated anywhere; the probe shows the state
  of a current build.

To run it (a few minutes; again once the zh-TW speech pack is installed):

```
dotnet run --project tools/VoiceProbe -- zh-TW 記事本 小算盤 檔案總管
```

It prints the installed speech languages, compiles the list, listens three times and ends with
`ANSWER: yes` or `ANSWER: no (reason)`; the output goes here. Then: **yes** → #11 is built in 0.10.0 as
planned; **no** for want of a speech pack → that pack becomes a documented requirement and the probe is run
again; **no** for any other reason → the SAPI fallback (`System.Speech`, a package) is the owner's call.
Nothing is built for voice until the answer is in this section.

### 6.2 Log viewer

Tray → *Log viewer…* (also in the options menu) opens the application log: everything the
app did and what happened to it, newest at the bottom, live. Entries have a time (to the
millisecond), a level (Debug, Info, Warning, Error) and a category: *App* (start, layout loaded,
bar shown/hidden, position reset, exit, crashes), *Auth* (browser sign-in, granted scopes,
token refreshes, sign-out, token errors), *Sync* (connect, save/load/delete copies, auto-upload
results, conflicts, startup checks), *Drive* (API errors with status codes, resumable uploads),
*Bucket* (queued, uploaded, replaced, skipped, downloaded, cancelled, failures), *Launch*
(started, cancelled, failed shortcuts, process exits), *Layout* (added, removed, replaced
items), *Settings* (every applied change) and *Hotkey*.

Viewer features: filters by minimum level, category, time range (last 15 minutes, last hour,
today) and free text (`Ctrl+F`, `Esc` clears); *Reset* clears all filters; *Follow* keeps the
newest entry in view; a details pane shows the selected entry in full; *Copy* copies the visible
lines, `Ctrl+C` the selected ones, double-click one line; *Save as…* (`Ctrl+S`) writes the
visible entries to a text file; *Open log file* opens `app.log`; the context menu offers *Copy
line*, *Copy message*, *Show only this category* and *Reset filters*; the status line counts
warnings and errors. The last 2000 entries stay in memory and all of them are appended to
`%LocalAppData%\DesktopIconGrid\logs\app.log` (rotated to `app.previous.log` at 2 MB).

## 7. Settings

| Setting | Default |
| --- | --- |
| Icon size | 32 px (choices 16 / 24 / 32 / 48 / 64) |
| Cell padding | 10 px (cell = icon size + 2 × padding) |
| Show labels | off |
| Launch on | single click (or double click) |
| Always on top | off |
| Theme | Classic (Graphite, Paper, Midnight, Sand, or Custom colour) |
| Bar background | solid colour (Mica or Acrylic on Windows 11 22H2+, tinted with the theme) |
| Windows 11 style dialogs | on (follow the system light/dark mode) |
| Dock to edge | not docked (top, bottom, left or right edge as a desktop toolbar) |
| Auto-hide | off (with a dock edge: two-pixel strip until the pointer touches the edge) |
| Start with Windows | off (current user's Run key; repaired when the program moves). One entry serves every copy: a copy changes it only when it names that copy, leaves another copy's alone (and says so), and a portable copy adds one on a PC only after asking (0.8.1) |
| Start hidden | off (with Start with Windows: only the tray icon until shown) |
| Show/hide hotkey | none (set in Settings; needs Ctrl, Alt or Win) |
| Show-at-pointer hotkey | none (needs Ctrl, Alt or Win, and a combination other than the show/hide one) |
| Transparency | 0 % (opaque) |
| Close button hides to tray | on |
| Language | same as Windows (English, 繁體中文 or 简体中文; applies at the next start) |

Settings live in the same JSON file as the layout and apply immediately on *Save*.

## 8. Persistence

- File: `%LocalAppData%\DesktopIconGrid\layout.json`, written atomically
  (temp file + rename) after every change and on exit.
- Portable mode (0.8.0): with a file called `portable.txt` next to `DesktopIconGrid.exe`, `layout.json`,
  `sync.json`, the logs and the caches live in the program's folder instead, so a tools drive carries
  the bar from PC to PC. `%APPDRIVE%` is the program's drive (`E:`): new shortcuts to files on that
  drive are stored as `%APPDRIVE%\…`, so they keep working when the drive gets another letter, and any
  location may use it, except on Windows' own drive, whose paths are written as they are (0.8.1). A
  `portable.txt` in a folder that cannot be written (Program Files without elevation, read-only media) is
  set aside: the data goes to `%LocalAppData%`, and the app says so once at start (0.8.1). The Google
  sign-in and client secret are encrypted for the Windows user, so each PC keeps its own on the drive (the
  client secret in `sync.json` under the PC's key, the sign-in in `google-token-<PC>.bin`): on a PC new to
  the drive Google asks for a sign-in, and the client secret, once, and PCs that share the drive no longer
  sign each other out (0.8.1); a sync folder needs none. The sync settings on the drive
  belong to the PC that used them last: on each change of PC the bar takes a new sync device id and that
  PC's name (A, then B, then A again makes three ids, and a name typed by hand is not carried along).
  Nothing is lost by it: the copy it last synced is known by its revision, so it is not mistaken for
  another PC's newer one.
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

## 9. Sync across PCs (Google Drive or a synced folder)

Keeps named copies of the settings and icons where several PCs can share them: on Google Drive, or
(0.8.0) in any folder that OneDrive, Dropbox, Google Drive for desktop or a network share keeps in step.

- **Copies**: one JSON file per copy (`Work.json`, `Home.json`, …) containing the settings and items,
  plus who saved it and when — in a `DesktopIconGrid` folder in My Drive, or in the chosen folder.
  The window position stays on the device. Any number of copies, each with its own name.
- **A synced folder** (0.8.0): *Where the copies are kept → A folder…* needs no Google project and no
  sign-in. Copies are written like `layout.json` (a temporary file, flushed, then moved over the old
  one). A copy's time is the file's last-write time, which the sync programs carry between PCs, and the
  device that saved it is read from the copy, so the linked copy works as on Drive. Which copy is newer is
  decided by each save's random *revision*, not by the times: this PC remembers the revision it last wrote
  or loaded, and another PC's save with another revision is a conflict however its time compares (a
  sync program carries the writer's own time, a save made offline arrives late, and a clock can be wrong).
  Copies from before 0.8.0 have no revision and are compared by time, as before. On Google Drive, whose
  times are Drive's own, a later time counts as well: a PC still on 0.7.0 names only the properties it
  knows when it updates a copy, and Drive keeps the revision of the save before it. A linked copy that cannot
  be read (still arriving) postpones the upload by a minute instead of being overwritten.
  A copy's name that Windows does not allow in a file name is kept inside the copy (`Home: desk` is
  saved as `Home_ desk.json` and listed as *Home: desk*). The copies OneDrive or Dropbox make after a
  conflict (`Work-LAPTOP.json`, `Work (1).json`) are listed under their own file names with their device
  and time, so they can be loaded or deleted; a file that cannot be read yet (still arriving) is listed
  without a device and is never overwritten as this PC's own copy. Loading such a copy applies it but keeps
  the PC linked to the copy it was made from, the file every PC uploads to; the next upload then asks before
  it replaces another PC's save there (0.8.1). The start-up question and the "not uploaded" message name
  the linked copy's conflict copies. Choosing another place unlinks the device (after asking) and moves
  nothing; it waits for an upload that is running, and every sync operation uses the place it started
  with (0.8.1). The Google account stays available for Google Drive Buckets.
- **Device name**: each PC has a name (defaults to the computer name) shown to other devices
  as the one that saved a copy.
- **Dialog** (options menu ▸ *Sync across PCs…*, called *Sync with Google Drive…* before 0.8.0):
  where the copies are kept, connect/disconnect the Google account, enter the device name, list the
  copies with their save time and device, *Save as* a new copy, *Overwrite* or *Load* the selected
  copy, *Delete* a copy.
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
- Local state in `%LocalAppData%\DesktopIconGrid\sync.json` (device name, credentials, the sync
  folder if one is used, linked copy, last sync time) and `google-token.bin` (encrypted token). When
  another program holds `sync.json` at start (a virus scan at logon, a sync program), the session runs
  with blank sync settings kept in memory only, never written over the file, and the sync dialog says
  so; the next start reads them again (0.8.0).

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
src/DesktopIconGrid/Resources/ Styles.xaml and the UI text: Strings.resx (English) with Strings.zh-Hant.resx and Strings.zh-Hans.resx
src/DesktopIconGrid.Core/Resources/  CoreStrings.resx: names of the ready-made Windows items, in the same three languages
tests/DesktopIconGrid.Core.Tests/   xunit tests for the core library
```

Adding a string: add the key to `Strings.resx` and to both translations, add the matching property to
`Strings.Designer.cs` (Visual Studio does this on save; without it, copy a neighbouring property), then
use `{x:Static r:Strings.Key}` in XAML or `Strings.Key` in code. `StringsTests` fails the build's tests
when a translation or a `{0}` placeholder is missing.

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

**Phase 4 — special icons** *(done: framework, CPU and disk gauges, Google Drive Bucket with viewer)*
Special icon item kind and handler registry; CPU and disk usage gauges; Google Drive Bucket
(click to open the folder, drop files to upload, Ctrl+click to browse and download) as
specified in [docs/SPECIAL_ICONS.md](docs/SPECIAL_ICONS.md).

**Phase 5 — desktop integration** *(done)*
Global show/hide hotkey; tray icon with show/hide, reset position and the common commands
(close hides to tray); start with Windows and docking to a screen edge with auto-hide (0.4.0).

The releases after 0.6.0 follow the plan in [docs/ROADMAP.md](docs/ROADMAP.md), which has the full
description and the "done when" of every item:

**0.7.0 "Get there faster"** *(done)* — unknown JSON properties kept through load and save (the
prerequisite for everything after it); click a running item to switch to it; the launch options every
`.lnk` has; type to find, and open the grid at the pointer; launch feedback; separators; colour tags. The
voice spike's first answer (§6.1): no, for want of the zh-TW speech pack; it runs again once the pack is
installed.

**0.8.0 "One bar, every PC"** *(done, released early)* — sync through any folder (OneDrive, Dropbox, NAS);
shortcuts that survive other PCs (missing targets, repair, per-device locations, portable mode); *Add
app…*. It came early to carry the fixes from the review of 0.6.0 and 0.7.0, so the rest of its plan
moved to 0.9.0.

**0.8.1** *(done, released 2026-11-06)* — the fixes the review of 0.8.0 placed after the release: loading a sync
program's conflict copy, switching where the copies are kept while an upload runs, an offline network
share, portable mode on the system drive and on read-only media, the Run entry of several copies,
*Repair…* for a folder, and the placeholder checks for ssh and WSL that 0.8.0 left for later.

**0.9.0 "The bar tells you things"** — first what moved from 0.8.0: the import dialog (its readers for
Windows Terminal, SSH, WinSCP and PuTTY, the taskbar and the desktop are already in), layout history,
command line and Send To. Then the command tile, with commands from a synced copy paused until
approved on each PC; health light on a shortcut; folder stacks; more gauges; microphone tile; clock and
timer; paste to a bucket.

**0.10.0 "Bigger moves"** — workspaces (Multi Launch that puts windows back); send a file to another PC;
voice launch, if the spike says yes.

**1.0.0 "Ship it"** — the existing backlog: installer (MSIX/winget), code signing, toast notifications.

Not yet placed in a release: glyph icons, usage columns, the accent theme, spring-loaded groups and the
Windows menu (Shift+right-click).

## 13. Reliability and polish backlog

Three independent review rounds (of commits 85a52e9, 1ce4e3c and b150bbe) have been worked
through; 0.2.0 is the result (see CHANGELOG.md). Done from them: single instance with
show-on-relaunch and foreground handover, no window-less process after a startup error, unknown
item kinds and newer layout versions preserved (the version number too), durable saves with
`layout.json.bak`, sync conflicts decided by Drive's clock and a per-install device id with the
linked file id used for every overwrite, one `DesktopIconGrid` folder even when two devices
connect at once, Mark-of-the-Web with source URLs on downloads and no direct launch of
executables, timeouts on every HTTP phase, resumable uploads that honour `Range` on 308, probe
the session, back off with jitter and honour `Retry-After`, one folder listing per upload run,
revoked-token recovery, complete run logs, Windows argument quoting, hotkey registration that
keeps the old key on failure, per-instance MCI alias, the pending upload flushed on exit
(including one already in flight), icon lookups on one message-pumping STA worker with a 20 s
deadline, stale results dropped, one shell call per path, the properties preview and the
Windows-item menu icons off the UI thread, running badge by executable path (never the shell or
an app-execution alias), a release workflow with checksums and embedded symbols, and a Windows-only
test project for the WPF side (icon worker, path matching, frozen icons, sync flush, uploader
listings, properties debounce). A hung icon lookup retires the worker and a fresh one takes over
the queue, local lookups go before network ones, and settings copies are listed from every app
folder on Drive.

Still open, in the order they should be taken:

1. **Drive cache freshness** — done in 0.6.0 (a `.stamp` next to each cached copy with size, modified
   time and MD5; reserved device names get a suffix in `FileNames.Safe`).
2. **Multi-monitor** — done in 0.6.0: the placement is saved in device pixels with the monitor's device
   name and restored there with `SetWindowPos`; the any-scale check against the real monitors remains the
   fallback for older files and for a monitor that is gone.
3. **Localisation** — done in 0.5.0 (`.resx` tables with zh-Hant/zh-Hans, a Language setting, the
   text-icon default font by UI language). By design the log and the error texts from Google and
   Windows stay in English.
4. **Non-modal errors** — done in 0.4.0 (tray notifications with the log viewer one click away;
   Undo for Remove). Still open: a toast with actions instead of the classic balloon (planned for 1.0.0).
5. **Table stakes** — installer (MSIX/winget), signed releases (planned for 1.0.0); versioning from the
   tag is done by the release workflow, start with Windows and dock/auto-hide are done (0.4.0).
6. **Engineering** — done in 0.6.0: the tray icon is a `Shell_NotifyIcon` on a hidden window of its own
   (WinForms is no longer referenced), and app-execution aliases are resolved through their reparse data
   (any process from the package folder lights the badge). Still open: CI builds `main` and `claude/**`
   branches;
   restrict it to `main` plus pull requests once development moves there.

## 14. Out of scope (for now)

Left out of the 0.7.0–1.0 plan on purpose:

- **Nested groups.** Two levels of popups on a small bar get fiddly; folder stacks cover deep trees.
- **Web search, calculator or file search in the find box.** PowerToys Run and Command Palette do that
  better; the bar's search finds only the bar's items.
- **Gmail or Calendar tiles.** Restricted and sensitive Google scopes and their verification, on top of
  the hardest setup in the app.
- **A DLL plugin system.** Code loaded into the bar is a security and support burden; the command tile
  covers the need out of process.
- **A URL protocol that can launch items.** Any web page could fire it. If a protocol is ever added, it
  may only show or hide the bar.
- **Several bars at once.** Every placement, dock, auto-hide and full-screen path stabilised in 0.4–0.6
  would double. If users ask, start with profiles (switch the whole item set from the tray).
- **Low-level keyboard or mouse hooks** (hold-to-talk, hot corners). They slow all input when the app
  hangs and antivirus products flag them; `RegisterHotKey` and the AppBar edge are enough.

Still out of scope from the first plan: per-item hotkeys, themes or skins beyond the theme palette (an
accent theme is on the roadmap), and dragging the bar's own icons out to Explorer.
