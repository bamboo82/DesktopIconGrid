# Changelog

All notable changes to Desktop Icon Grid. The format follows [Keep a Changelog](https://keepachangelog.com/);
version numbers follow [Semantic Versioning](https://semver.org/).

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
