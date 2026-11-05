# Changelog

All notable changes to Desktop Icon Grid. The format follows [Keep a Changelog](https://keepachangelog.com/);
version numbers follow [Semantic Versioning](https://semver.org/).

## [0.7.0] – Unreleased

"Get there faster": the first release of the plan in [docs/ROADMAP.md](docs/ROADMAP.md). The voice spike
could not be run here (no Windows, no microphone): its probe is in `tools/VoiceProbe`, and FEATURES §6.1
says how to run it and what is known so far. Nothing is built for voice until it answers.

### Added

- Clicking a program that is already running switches to it, as the taskbar does, instead of starting a
  second copy: its window comes forward (restored if minimised), a second click while it is in front
  minimises it, and with several windows each click goes to the next one. Store apps are found through
  the frame Windows draws them in, and the inbox programs that Windows 11 replaced with Store apps
  (Calculator, Notepad, Paint) are recognised by their package. Shift+click or a middle click starts
  another instance; Ctrl+Shift+click starts one as administrator. *Properties → Advanced → When it is
  running* changes this per shortcut; documents, folders and links start again by default. Checked in
  code and with tests against simulated window lists; not yet seen on a screen.
- The options every Windows `.lnk` has, under *Properties → Advanced*: *Start in* (the working folder),
  *Run* normal, minimised or maximised, *Run as administrator* (an ordinary UAC prompt), and for web links
  a browser profile of Edge or Chrome, optionally as an app window without tabs or address bar.
- Two placeholders next to `{file}`/`{files}` in *Parameters*: `{clipboard}` passes the clipboard text and
  `{ask:Question}` asks for a value when the shortcut starts; both are quoted as one argument, and Cancel
  cancels the launch. Checked in code and with tests; not yet seen on a screen.
- Type to find: while the bar has the focus, typing opens a search box at its bottom (`Ctrl+F` does too).
  The icons that match, inside groups as well, stay lit, together with the groups that hold them; the
  rest fade. A match is found by its name on the bar, the name it was created with, its file name, or
  its initials (`vsc` finds Visual Studio Code); case and full-width letters do not matter, and Chinese
  is typed straight into the box through the IME. Enter opens the framed match (its name shows beside
  the box), the arrow keys move the frame, Esc clears the text and then closes the box.
- *Show at pointer*, a second hotkey in Settings (off until a combination is set): the bar's icons open in
  a small window under the mouse with the search box ready, on whichever monitor the pointer is, also
  while the bar is hidden in the tray or tucked away at a screen edge. Groups open in place (Backspace or
  ← goes back), and the window goes away after a launch, on Esc and when another window is clicked.
  Search and the window's size and placement are covered by tests; the window, the keys and typing
  through a Chinese IME have not been seen on a screen. With an IME in Chinese mode, the first key that
  opens the bar's box may not reach the IME; the Show-at-pointer box is focused from the start.
- Launch feedback: the icon just started (or the group it sits in) is framed and pulses until the bar
  loses the foreground, normally when the program's window appears, or for five seconds at most. With
  Windows animations turned off it is only framed.
- Separators: *Add separator ▸ Line / Gap / Label…* on an icon or on empty space. A line is a thin divider
  between icons, a gap an empty cell, a label a small caption as wide as its text; in a bar one icon wide
  they lie across it. They can be dragged, switched to another kind and removed from their own menu, and
  sit in groups too. Versions before 0.7.0 keep them as items from a newer version.
- Colour tags: right-click → *Colour tag ▸* (seven colours or none), drawn as a corner on the icon. The
  names table has a *Colour tag* column and *Show:* narrows it to one colour or to the untagged items.
  Launch feedback, separators and tags are covered by tests of the layout and the view models; none of
  them has been seen on a screen yet.

### Fixed

- Settings and icon properties written by a newer version are kept when this version saves, at every
  level of `layout.json` and of the sync copies (the document, the settings, every kind of item, items
  inside groups, text icons). Before, an older PC that loaded a linked copy and saved it again dropped
  them, and the copy carried the loss to every other PC: the newer PC's language, dock edge or log
  setting was reset without a word. From this version on, every newer field passes through untouched.
- The Settings dialog changes only what it shows; before, saving it reset settings it does not show.

## [0.6.0] – 2026-11-05

Drive copies that stay current, the bar back on its own monitor, the running badge for Store apps, and
a tray icon without Windows Forms. Note for whoever tests this build: the tray icon is new code. How it
is drawn and how the shell's callbacks are decoded are tested on Windows CI, but the icon in the
notification area, its menu and its balloons have not been seen on a screen yet; neither has the bar
returning to a second monitor at another display scale.

### Changed

- The Bucket Viewer's local cache reuses a downloaded copy only while it is still the file on Drive:
  the same MD5 checksum, or the same size and modification time, recorded in a `.stamp` file next to
  the copy. Before, a copy of the same size was reused even after the file had changed.
- Names from Drive that Windows reserves for devices (`CON`, `NUL`, `COM1`, also with an extension) get
  a trailing underscore when they are downloaded, and the characters Windows forbids are replaced the
  same way wherever the code runs.
- The bar's position is saved in device pixels together with the device name of the monitor it is on,
  and restored there in pixels when that monitor is present, so display scales no longer come into it
  (a bar on a 150 % monitor next to a 100 % one came back in the wrong place or size). The WPF-unit
  values are still written for older versions and used when the monitor is gone, with the check against
  the real monitors as before. A bar that would stick out after a change of resolution is pulled inside.
- Shortcuts to Store apps and other programs reached through an app-execution alias (`wt`, `winget`,
  Python from the Store) show the running badge: the alias's reparse data names the executable in the
  package, and any process from that package folder counts, so Windows Terminal (`wt.exe` starts
  `WindowsTerminal.exe`) is covered. Before, only a program started from the bar itself was tracked.
- The tray icon talks to the shell directly (`Shell_NotifyIcon` with version-4 callbacks on a hidden
  window of its own) instead of through Windows Forms' `NotifyIcon`, so the app no longer loads Windows
  Forms or System.Drawing: a smaller self-contained build and one DPI story (the WPF manifest). Behaviour
  is unchanged: left click shows or hides the bar, right click or Shift+F10 opens the menu, a balloon
  click opens the log viewer, and the icon comes back when Explorer restarts.

### Fixed

- A docked bar that is hidden to the tray (with the hotkey, the tray menu or the close button) or started
  hidden gives its screen strip back, so maximised windows use the whole screen again; it is claimed again
  when the bar is shown. Before, windows stopped at an empty strip. A full-screen program still hides the
  bar without releasing the edge.

## [0.5.0] – 2026-11-05

The user interface speaks Traditional and Simplified Chinese as well as English. The language
follows the Windows display language unless *Settings → Language* picks one; a change applies at the
next start. Note for whoever tests this build: the translations were written without a screen, so
long Chinese labels in the fixed-width dialogs (Settings, Sync) are the first thing to check.

### Added

- *Settings → Language*: *Same as Windows*, English, 繁體中文 or 简体中文. The text lives in
  `Resources/Strings.resx` (English) with `zh-Hant` and `zh-Hans` translations, so Hong Kong and
  Macao get Traditional Chinese and Singapore gets Simplified; every other Windows language gets
  English.
- The ready-made Windows items (*This PC*, *Control Panel*, …), the special icons, the tray and
  context menus, every dialog, tooltip, status line and notification are translated. New items are
  named in the language in use when they are created; existing names are kept as they are.
- Text icons without a font of their own use 新細明體 (PMingLiU) first under a Traditional Chinese
  UI and 宋體 (SimSun) otherwise, as before. Pick a font in the *Text icon* dialog to pin one.

### Changed

- Sync status lines carry their own severity instead of being classified by their English wording,
  so warnings and failures reach the tray in every language.

### Fixed

- The tray icon's right-click menu did not open (since 0.3.0): its WPF menu was anchored in a window
  that was never shown, and WPF waits for such an anchor forever. The anchor is now a zero-size window
  moved under the pointer, which also gives the menu that monitor's DPI. If it still fails, `app.log`
  says why.
- The show/hide hotkey and a click on the tray icon bring a bar that is visible but covered by other
  windows, or collapsed as an auto-hide strip, to the front instead of hiding it; only a bar that is in
  front is hidden. Before, the first press seemed to do nothing and the second brought the bar back.
  The tray menu's *Hide bar* / *Show bar* entry does exactly what it says.
- Bringing the bar forward from a second instance no longer detaches *Always on top* from its setting.

### Not translated

- The application log (`logs\app.log` and the log viewer's entries) stays in English, as do the
  error texts Google and Windows return, so a log reads the same whatever the UI language.

## [0.4.0] – 2026-11-04

The bar can start with Windows and live at a screen edge, and removing an icon no longer asks.
Note for whoever tests this build: docking, auto-hide and the hand-off to full-screen programs talk
to the shell through `SHAppBarMessage` and have been checked in code and unit tests only, not yet on
a screen. The defaults (free window, message boxes for questions) are unchanged.

### Added

- *Settings → Window → Start with Windows* registers the app in the current user's Run key, and
  *Start hidden* keeps the bar in the tray until it is shown from the tray icon or with the hotkey.
  The entry is repaired at startup when the program was moved and removed when the setting is
  turned off. Starting the executable with `--hidden` has the same effect once.
- Failures that need no decision (a shortcut that does not start, a file that cannot be opened with
  a program, a bucket or sync upload that fails, a layout that cannot be saved) appear as tray
  notifications instead of message boxes; clicking one opens the log viewer. Questions (removing a
  group, loading a newer copy, an unreadable layout) still use a message box.
- *Remove* no longer asks. A banner under the icons offers *Undo* for ten seconds (also `Ctrl+Z`)
  and puts the icon back where it was. Removing a group still asks and cannot be undone.
- *Settings → Dock to edge* turns the bar into a desktop toolbar at the top, bottom, left or right
  edge of its screen, like the taskbar: maximised windows stop at it, it stays on top, the grip is
  hidden, and its thickness is the current height or width (drag the inner edge to change it). A
  full-screen program hides it until it is gone. *Auto-hide* collapses the bar to a two-pixel strip a
  second after the pointer leaves and brings it back when the pointer touches the edge, the hotkey is
  pressed or the bar is shown from the tray. Undocking returns the bar to where it was.

## [0.3.0] – 2026-11-04

The first release that changes what you see. Each change is a setting, and the defaults keep the
bar itself looking as it did in 0.2.0. Note for whoever tests this build: the dialogs have fixed
widths and the Fluent controls are taller than the classic ones; their layouts have been checked in
code only, not yet on a screen in light and dark mode.

### Added

- Dialogs use the Windows 11 (Fluent) look and follow the system light/dark mode; *Settings → Window*
  turns this off and brings the classic dialogs back. Hint and warning text recolour for dark surfaces.
- *Settings → Bar background*: **Mica** or **Acrylic** (Windows 11 22H2 or later) lets the desktop
  show through the bar with a blur, tinted with the theme colour; the transparency slider then sets how
  much of the blur shows while the icons stay sharp. The group popup gets the same treatment. Older
  Windows keeps the solid colour with whole-window transparency.
- Windows High Contrast: the bar switches to the system colours (and a solid background) while it is
  on, and back to the theme when it is turned off.
- Screen readers get a name for every icon (its caption, with the location and state as help text)
  and for the grip, close and options buttons; Tab now leaves the grid and reaches the close and
  options buttons.
- The Google client secret is stored encrypted for the Windows user (DPAPI) in `sync.json`; a value
  saved in clear by an earlier version is encrypted the first time 0.3.0 runs. The Sync dialog shows
  it as a password field.

### Changed

- The tray icon is rendered at the notification area's size for the screen's DPI instead of being
  scaled down from 32 px.
- Keyboard and mouse activation are told apart by the input device, not by a 150 ms timing window.
- The tray menu is a WPF menu that takes the dialogs' look (light or dark) and the screen's DPI; the
  tray icon is re-rendered when the bar moves to a screen with another scale.
- Whether a saved bar position is still on a screen is checked against the real monitors instead of
  the virtual screen's bounding box, so a bar lost in the dead corner of an L-shaped layout comes back.
  On layouts that mix display scales the check errs on the side of leaving the bar where it is.
- Errors from the sync feature that are programming mistakes are no longer hidden behind a status line.

## [0.2.0] – 2026-11-04

Reliability release after three independent code-review rounds. No new icons or dialogs; everything
that could lose data, hang the bar or mislead the user was fixed. Two things to know when upgrading
from 0.1.0:

- `layout.json` now has a sibling `layout.json.bak` (the previous save) and, when the file was
  written by a newer version, a one-time copy `layout.json.v<N>.json`.
- Items a version does not understand are kept as placeholders instead of being dropped, and the
  file keeps the newer version number, so you can go back and forth between versions safely.
- If 0.1.0 created two `DesktopIconGrid` folders in your Google Drive (it could, when two devices
  connected for the first time at the same moment), copies from both are still listed; new saves go to
  the folder that was created first, and the log notes that two folders exist.

### Changed

- Layout saves are durable: written through a temporary file with write-through and `File.Replace`,
  an unreadable file is quarantined as `layout.json.corrupt-<timestamp>` and the backup is loaded.
- Sync conflicts are decided by Google Drive's modification time plus a per-install device id, and
  copies are replaced by file id, so two copies with the same name can never be swapped.
- The running badge matches the program's full path instead of its name; File Explorer and
  Command Prompt no longer light up because the shell is running.
- Special icons always react to a single click, even when shortcuts are set to double-click.
- Hotkey registration keeps the previous key when the new one is taken, and reports the Windows error.
- Dropped file paths are quoted by Windows rules and environment variables are expanded only once.

### Added

- Single instance: starting the app again brings the running bar to the front.
- Timeouts on every network phase: sign-in, metadata, uploads, download headers, stalled bodies,
  icon downloads from the web (with a Cancel button).
- Resumable uploads follow Google's protocol: the `Range` on a 308 decides what is re-sent, a failed
  chunk probes the session before retrying, and retries back off with jitter and honour `Retry-After`.
- The pending sync upload, including one already running, is finished before the app exits.
- Mark-of-the-Web on files downloaded through the Bucket Viewer (SmartScreen and Protected View treat
  them as downloads, with Google Drive named as the source); executables are revealed in Explorer
  instead of being started.
- A revoked sign-in clears the stored token so *Connect…* reappears in the sync dialog.
- Icon lookups run on one dedicated message-pumping STA worker, so a slow or sleeping drive never
  stalls the bar; stale results are dropped and misses are retried later.
- The log records stack traces, the process scan and icon resolution run off the UI thread.
- Windows CI on every push, a release workflow on `v*` tags, this changelog, and a Windows-only test
  project for the WPF-side logic.

### Fixed

- A startup error no longer leaves a window-less process behind.
- A second *Exit* while the pending upload is being flushed is ignored; the bar is disabled meanwhile.
- Press-and-hold (Sound Replayer) cannot get stuck after a dialog interrupts it.
- Drop handling runs outside the OLE callback, so dialogs opened by a drop no longer hang Explorer.
- Run logs are complete: output is drained before the exit line is written.
- The log viewer's keyboard shortcuts work; the close button's tooltip says it hides to the tray;
  the bar no longer appears in the taskbar or Alt+Tab.
- Two devices connecting to Google Drive at the same moment no longer end up with two
  `DesktopIconGrid` folders.

## [0.1.0] – 2026-11-04

First version: launcher bar with grip, close and options buttons; shortcuts, groups (folders),
Windows items, Multi Launch; drag to reorder, drop from Explorer and browsers, right-drag to place,
left-drag to open files with a program (`{file}`/`{files}`); themes; text icons (宋體 default);
hover names; show/hide hotkey; transparency; run logs and running badge; CPU and disk gauges;
Google Drive sync with named copies and device names; Google Drive Bucket with uploads and the
Bucket Viewer; tray icon; name table editor; Sound Replayer; icons from web addresses; log viewer.
