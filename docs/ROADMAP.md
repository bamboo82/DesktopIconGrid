# Roadmap: 0.7.0 to 1.0.0

The plan agreed with the owner on 2026-11-05: one prerequisite fix, then twelve features and a set of
small ones, released in the order under [Releases](#releases). What is deliberately left out, and why,
is in [FEATURES.md §14](../FEATURES.md#14-out-of-scope-for-now).

How it is run:

- One release at a time. Every item lands with its tests green on `windows-latest`, and with its entry
  in the `CHANGELOG.md` section of the release in progress.
- Anything nobody has seen working on a screen gets an honest "checked in code only" line.
- Voice (#11) starts as a one-day spike during 0.7.0; its result is written into FEATURES.md (§6.1)
  before anything is built for it.
- Type and member names below come from a read of commit 69892ca; they are checked against the code as
  it is when each item starts.

## Prerequisite (0.7.0, first): unknown JSON properties are kept

`LauncherLayout.CurrentVersion` has been 1 since the first commit, and nothing in Core had
`[JsonExtensionData]`. `LauncherItemConverter` re-reads known item types through
`JsonSerializer.Deserialize(root.GetRawText(), type, options)`, so any property a newer version added
(`dockEdge`, `autoHide`, `language`, `captureLog`, …) was silently removed when an older version saved.
Sync carries the damage across PCs, because `SyncProfile` holds the whole `LauncherSettings`. Example:
PC A on 0.5.0 sets *Language*; PC B on 0.4.0 loads the linked copy, changes anything and auto-uploads;
PC A accepts the newer copy and its language is reset. Every feature below adds fields, and per-device
locations or workspace positions lost this way would break other PCs without any warning.

Fix: `[JsonExtensionData] Dictionary<string, JsonElement>?` on `LauncherLayout`, `LauncherSettings`,
`LauncherItem` and `SyncProfile`, carried through by the converter on read and write. `CurrentVersion` is
bumped only when existing data changes meaning.

Done when: a layout and a sync copy with an invented property at each level (root, settings, a shortcut,
a group, a special icon, an item inside a group) survive Load → Save and `FromJson` → `ToJson`
unchanged (Core tests).

## The list

| # | Feature | Builds on | Size |
|---|---|---|---|
| 1 | Click a running item to switch to it | running badge, `ProcessTracker` | M |
| 2 | The launch options every `.lnk` has | `LaunchPlanner`, `{file}` placeholders | S |
| 3 | Type to find; open the grid at the pointer | group popup, hotkey | M |
| 4 | Workspaces: Multi Launch that puts windows back | Multi Launch, #1, 0.6.0 device-pixel placement | M–L |
| 5 | Sync through any folder (OneDrive, Dropbox, NAS) | `IDriveClient` (five calls) | S–M |
| 6 | Shortcuts that survive other PCs | device ids, names table | M |
| 7 | *Add app…* and imports | `shell:AppsFolder`, shell icons | S–M |
| 8 | Command tile | special icons, `GaugeState`, `OutputCapture` | M |
| 9 | Health light on a shortcut | badge drawing | S |
| 10 | Folder stacks | group popup, `IconWorker` | M |
| 11 | Voice launch | names table, microphone | M + risk |
| 12 | Send a file to another PC | Drive client, device ids, tray | M |

### 1. Click a running item to switch to it

Today every click starts another instance, even while the green dot says the program is running. Do what
the taskbar does:

- Click: if the program has a window, bring it forward (restore it if minimised). Clicking again while it
  is in front minimises it. With several windows, each click goes to the next one.
- Shift+click or middle-click: new instance. Ctrl+Shift+click: run as administrator. These are the
  taskbar's keys.
- Per-shortcut option *When it is already running: switch to it / start another*. Default *switch* for
  programs, *start another* for documents, folders and links.
- Later, not in 0.7.0: live thumbnails of its windows in the tooltip (`DwmRegisterThumbnail`).

How: `RunningProcesses.ImagePaths()` returns only paths; make it return path → PIDs and merge with
`ProcessTracker._launched`. Then `EnumWindows` with the taskbar's rules: visible, not cloaked
(`DWMWA_CLOAKED`), no owner unless `WS_EX_APPWINDOW`, not `WS_EX_TOOLWINDOW`. UWP Store apps draw in an
`ApplicationFrameWindow` belonging to ApplicationFrameHost.exe; match it through the PID of its
`Windows.UI.Core.CoreWindow` child. At the click the bar received the last input, so
`SetForegroundWindow` is allowed.

Done when: with Notepad running, a click brings it forward and starts no second Notepad; Shift+click
starts a second one; Calculator (Store) switches too; the window-finder rules are a pure function with
tests against fake window lists.

### 2. The launch options every `.lnk` has

`ShortcutItem` has Location and Parameters; `LaunchPlanner` sets the working directory from the location
and never sets a verb or a window state. Add an *Advanced* section to Properties:

- Start in (empty = today's default).
- Run as administrator: `Verb = "runas"`, an ordinary UAC prompt, no tricks to avoid it.
- Run: normal, minimised or maximised (`WindowStyle`, what the `.lnk` field sets).
- Open web links in: the default browser, or a browser profile. Edge and Chrome list their profiles in
  `Local State` (`profile.info_cache`) and take `--profile-directory=…`; optionally `--app=<url>` for an
  app window.
- Two placeholders next to `{file}`/`{files}`: `{clipboard}` (the clipboard text, quoted by the same
  rules as dropped files) and `{ask:Prompt}` (asks in `InputWindow`; Cancel cancels the launch).

Done when: each option round-trips through `layout.json`; planner tests cover verb, window style, working
directory and both placeholders.

### 3. Type to find, and open the grid at the pointer

When the bar has focus (hotkey or click), the first printable key opens a small search box at the bottom
of the bar. Matches light up across all groups and the rest dim. Match on the given name, the original
name, the file name and word initials (`vsc` → Visual Studio Code). Enter launches the first match,
arrows move, Esc clears. Use a real `TextBox` so the Chinese IME works (檔案 typed directly, no pinyin
table).

A second hotkey in Settings, *Show at pointer*, opens the root grid as a popup under the mouse with the
search box focused, and closes after a launch; it also works while the bar is auto-hidden.

Scope: only the bar's items. No web search, calculator or file index.

### 4. Workspaces: Multi Launch that puts windows back

Multi Launch is one flagged set started with 300 ms between items. Make it several named sets, each
remembering where its windows go:

- *Capture positions* records each running member's main window: rectangle in device pixels, the
  monitor's device name, maximised or not (reuse the 0.6.0 `WindowPlacement` and monitor code).
- *Start* launches the members that are not running, waits up to ~15 s for each one's first window
  (#1's finder, plus child processes for launcher stubs), then moves it. Members already running are only
  moved. Programs that restore their own position late (browsers) get a second move after two seconds.
- The `multiLaunch` flag becomes membership of a set called "Multi Launch", so 0.6.0 layouts carry over.

### 5. Sync through any folder

The Google setup (Cloud project, Drive API, consent screen, publishing, client secret) is the biggest
hurdle in the app; most users already have a synced folder. `SyncService` reaches storage through
`IDriveClient`: `GetUserAsync`, `ListFilesAsync`, `UploadJsonAsync`, `DownloadTextAsync`, `DeleteAsync`.
A `FolderSyncClient` implements them on a chosen folder: durable writes like `layout.json`, the file's
last-write time in place of Drive's clock, the device id inside the JSON as today. Same copies, device
names, linked copy and conflict rule, no sign-in. `SyncService` uses `_auth` in five places; make
authorisation optional for this backend. Google stays for buckets and for anyone who prefers it.

Watch out: OneDrive and Dropbox write conflict copies (`Work-PC2.json`, `Work (1).json`); list them as
ordinary copies with their device and time so the user can see and delete them.

### 6. Shortcuts that survive other PCs

A synced bar breaks where paths differ (`U:\E\WinSCP\WinSCP.exe` here,
`C:\Program Files (x86)\WinSCP\WinSCP.exe` there).

- Missing target: the icon is dimmed with a small "?". A cheap `File.Exists` on the icon worker's thread,
  rooted local paths only; network and removable paths are skipped.
- *Repair…* proposes candidates: the same file name in a sibling folder that differs only by a version
  number (`app-1.0.9172` → `app-1.0.9203`); the same file name under `Program Files`,
  `Program Files (x86)` or `%LocalAppData%\Programs`; the executable's `App Paths` entry. The user picks
  one and chooses *this PC only* or *everywhere*.
- *This PC only* stores a per-device override inside the item (`"locations": { "<deviceId>": "…" }` —
  needs the prerequisite). It travels with the copy, so each PC finds its own path. The names table gets
  a *Missing here* filter.
- Portable mode: when `portable.txt` sits next to the exe, `layout.json`, `sync.json` and the data
  folders live next to it instead of `%LocalAppData%`, and locations can use `%APPDRIVE%` (the exe's
  drive), so a tools drive that changes letter keeps working.

### 7. *Add app…* and imports

- *Add app…*: a searchable list of every installed app, Store apps included, enumerated from
  `shell:AppsFolder` (`SHGetKnownFolderItem(FOLDERID_AppsFolder)` → `IEnumShellItems`). A Store app is
  added as `shell:AppsFolder\<AUMID>`, which the bar already launches through Explorer. Today the usual
  way to get a Store app onto the bar is a desktop shortcut made first.
- *Import ▸* with a checklist per source, each source optionally as its own group:
  - taskbar pins: `%AppData%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar` (the `.lnk`
    files);
  - desktop shortcuts;
  - Windows Terminal profiles: `settings.json` → `wt -p "<profile>"`;
  - WinSCP sessions: registry `HKCU\Software\Martin Prikryl\WinSCP 2\Sessions`, or `WinSCP.ini` next to a
    portable WinSCP → `WinSCP.exe "<session>"`;
  - PuTTY sessions: registry → `putty -load "<session>"`;
  - SSH hosts: `%UserProfile%\.ssh\config` (skip wildcard hosts) → `wt ssh <host>`.

### 8. Command tile

A special icon that runs a command on a timer (or on click) and shows the result: the first line of output
as the tile text, or a number drawn as the existing ring/bar/number gauge with user thresholds; colour
from the exit code (0 green, 1 amber, else red); a click runs a second command or opens a location; recent
runs kept as run logs (`OutputCapture`, `ProcessLogSession`). Examples: a build status, uncommitted files
in a work folder, VPN up or down, a queue length from a REST endpoint.

Trust (required): a command tile in a copy loaded from Drive or a folder would run unattended on another
PC. Commands that arrive through sync stay paused until the user approves them on that PC; approved
command hashes are kept in the local `sync.json`, never in the copy. Minimum interval 10 s,
`CreateNoWindow`, a timeout that kills the process tree.

### 9. Health light on a shortcut

Optional *Check* field on any shortcut: `ping host`, `tcp host:port`, or a URL that must answer 2xx/3xx,
every 30 s to 10 min. Shown as a second corner dot like the running badge (green, red, grey = unknown),
latency and last change in the tooltip, optional tray notification on a change. `Ping` and `HttpClient`
need no admin rights; suggest `tcp` because ICMP is often blocked. State changes go to the log under a new
*Health* category.

### 10. Folder stacks

A folder shortcut can open as a stack instead of in Explorer: the group popup showing the folder's
contents, sorted by name or newest first, *Open folder* at the bottom. Drag a file out (real files, plain
`FileDrop`); drop onto a program inside the stack to open the file with it. `FileSystemWatcher` keeps it
live; at most 100 items; icons through the `IconWorker`'s local queue; network folders refresh on open.

### 11. Voice launch (spike first)

FEATURES §6.1 already says the given names "will be the phrases for voice commands".

- Trigger: a *Voice* hotkey, or press-and-hold on a *Voice* special icon (reuse the Sound Replayer's
  press-and-hold). One utterance per trigger, never continuous listening; the recogniser stops at silence.
- Grammar: the given names plus an *Also say* column in the names table. The alias column is required: a
  Chinese recogniser handles English product names (WinSCP, VS Code) badly and the reverse too, so
  "WinSCP" needs an alias such as 檔案傳輸 or "win S C P". Built-in phrases: Multi Launch, Show bar,
  Hide bar, workspace names.
- Engine: `Windows.Media.SpeechRecognition.SpeechRecognizer` with a list constraint, offline, in the UI
  language (en-US, zh-TW, zh-CN speech packs). Needs a Windows 10 target framework
  (`net10.0-windows10.0.19041.0`), not a package. Fallback: SAPI through Microsoft's `System.Speech`
  package — ask the owner before adding it, given the no-third-party-packages rule.
- Confidence: high → the icon flashes and launches; medium → the three best matches as buttons;
  low → "Didn't catch that" on the bar. A missing speech pack links to Settings → Time & language →
  Speech.

### 12. Send a file to another PC

Linked devices already have names and ids. *Send to ▸ <device>* for dropped files (a *Send to device*
special icon or a bucket option): the file goes to `DesktopIconGrid/inbox/<deviceId>/` on Drive; the
receiving PC, while running, lists its inbox every minute; a tray notification "report.pdf from Laptop"
opens the file's folder; the file is downloaded to Downloads with Mark-of-the-Web, then deleted from the
inbox. The app created those files, so `drive.file` is enough. With #5 the same works through a synced
folder.

## Small ones (about a day each)

- Separators and labels: a thin line, a gap or a small caption (`"$type": "separator"`); older versions
  keep them as unknown items.
- Colour tags per item (corner or tint), filterable in the names table.
- Launch feedback: the clicked icon pulses until the bar loses the foreground or 5 s pass.
- Glyph icons: a picker over Segoe Fluent Icons (Segoe MDL2 Assets on Windows 10) with a tile colour,
  through the text-icon renderer.
- Paste to a bucket: select a bucket, `Ctrl+V` uploads files, an image (`Screenshot 2026-11-06 143005.png`)
  or text (`.txt`).
- More gauges: memory (`GlobalMemoryStatusEx`), network down/up (`NetworkInterface.GetIPStatistics`
  deltas), battery (`GetSystemPowerStatus`); a sparkline of the last 60 samples in every gauge's tooltip.
- Microphone mute tile through Core Audio (`IAudioEndpointVolume`), red while muted, follows mutes made
  elsewhere; a Volume tile that takes the mouse wheel.
- Clock and timer tile: time in another zone (`TimeZoneInfo`); a click starts a countdown or a 25/5
  Pomodoro with a tray notification at the end.
- Usage columns in the names table (launch count, last used, *Not used in 90 days*), kept per device in
  `usage.json` — never in the synced layout, or every click would trigger an upload.
- Layout history: the last 20 saves in `history\`, *Restore…* with a one-line summary per entry; linked
  Drive copies can list Drive `revisions` the same way.
- Command line and Send To: `--add <path> [--group Tools]`, `--launch <name>`, `--workspace <name>`,
  `--show`, `--hide`, `--toggle`, forwarded to the running instance over a named pipe with
  `PipeOptions.CurrentUserOnly`; *Send to ▸ Desktop Icon Grid* in Explorer adds the selection.
- Accent theme that follows the Windows accent colour live.
- Spring-loaded groups: while dragging files, hovering a group icon for 0.7 s opens it.
- The Windows menu: Shift+right-click shows Explorer's own context menu for the target (`IContextMenu`
  from `BHID_SFUIObject`, `IContextMenu3` message forwarding for Open with and Send to).

## Releases

- **0.7.0 "Get there faster"** (released 2026-11-05): the prerequisite, 1, 2, 3, launch feedback,
  separators, colour tags (plus the voice spike, result written down only: FEATURES §6.1).
- **0.8.0 "One bar, every PC"** (released 2026-11-06, early, to carry the fixes from the review of 0.6.0
  and 0.7.0): 5, 6, and *Add app…* from 7.
- **0.8.1**: the fixes the review of 0.8.0 placed after the release (folder sync, portable mode, *Repair…*,
  the smaller items and the placeholder follow-ups).
- **0.9.0 "The bar tells you things"**: first what moved from 0.8.0 (the imports of 7, whose readers are
  already in; layout history; command line and Send To), then 8 with the trust rule, 9, 10, more gauges,
  mic tile, clock and timer, paste to a bucket.
- **0.10.0 "Bigger moves"**: 4 (workspaces), 12 (send to a device), 11 (voice, if the spike says yes).
- **1.0.0 "Ship it"**: the existing backlog — installer (MSIX/winget), code signing, toast notifications.

Not yet placed in a release: glyph icons, usage columns, the accent theme, spring-loaded groups and the
Windows menu.
