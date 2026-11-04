# Desktop Icon Grid

A compact launcher bar for Windows. Put programs, folders and web links on it as
icons, arrange them in a grid, and open any of them with one click.

Built with .NET 10 and WPF, with no third-party packages. The Google sign-in token is encrypted
with Windows DPAPI.

## Features

- **Launcher bar** — a borderless, resizable window with a grip on the left to move it,
  a close button and an options menu. Icons flow into as many columns as fit.
- **Shortcuts** — any file, folder or URL, with optional parameters and a custom icon.
  Icons are extracted the same way File Explorer shows them, or drawn as text (for example
  宋體 characters) in a font, size and colour of your choice.
- **Groups** — a group is a folder on the bar. Click it to open a popup with its own
  grid of shortcuts; drag shortcuts in and out.
- **Windows items** — one-click entries for This PC, Control Panel, Recycle Bin,
  Show Desktop, Notepad, Command Prompt, Windows Terminal and more.
- **Multi Launch group** — flag any shortcuts and start them all together.
- **Special icons** — live gauges for CPU usage and the fill level of a drive, drawn as a
  ring, a bar or a number; and the **Google Drive Bucket**: drop files on it to upload them to a
  Drive folder, click to open the folder, Ctrl+click to browse it and copy files to your PC.
- **Sound Replayer** — a special icon: press and hold to record from the microphone, release
  to loop the recording, click to stop.
- **Edit names table** — rename all shortcuts in one sortable grid (given vs. original name);
  these names are the future voice-command phrases.
- **Log viewer** — a live, filterable log of what the app does: sign-ins, sync, Drive uploads
  and downloads, launches, settings changes, errors (tray → *Log viewer…*; also in `logs\app.log`).
- **Tray icon** — show or hide the bar, reset its position when it is lost on a disconnected
  monitor, and reach the common commands; the close button hides the bar to the tray.
- **Running badge and run logs** — a green dot shows while a shortcut's program runs; console
  programs can be started with their output captured to a log and shown live.
- **Drag and drop** — reorder icons, drop files or folders from Explorer, drop links
  from a browser.
- **Themes and settings** — Classic, Graphite, Paper, Midnight, Sand or a custom colour;
  icon size, spacing, labels, single or double click, always on top.
- **Google Drive sync** — keep named copies of your settings on Google Drive ("Work",
  "Home", …), load them on another PC, and let linked devices upload changes automatically.
- Everything is saved automatically to `%LocalAppData%\DesktopIconGrid\layout.json`.

See [FEATURES.md](FEATURES.md) for the full feature plan and delivery phases, and
[docs/SPECIAL_ICONS.md](docs/SPECIAL_ICONS.md) for the design of special icons such as the
Google Drive Bucket, and [docs/THEMES.md](docs/THEMES.md) for the themes.

## Using it

| Action | How |
| --- | --- |
| Add a shortcut | Right-click the bar → *New shortcut…*, drop a file onto the bar, or `Ctrl+N` |
| Add a group | Right-click → *New group…* or `Ctrl+G` |
| Add a Windows location or tool | Right-click → *Add Windows item ▸* |
| Add a CPU or disk gauge | Right-click → *Add special icon ▸* |
| Upload files to Google Drive | Right-click → *Add special icon ▸ Google Drive Bucket*, paste the folder link, then drop files on the icon |
| Browse and download from Google Drive | Ctrl+click a Google Drive Bucket (or right-click → *Browse…*); drag items to the desktop or use *Download to…* |
| Find a lost bar | Tray icon → *Reset bar position* |
| See what the app is doing | Tray icon → *Log viewer…* (filter by level, category, time or text; save or copy) |
| Exit | Tray icon → *Exit*, or options menu (▸) → *Exit* (the close button only hides the bar) |
| Open an item | Click it (or double-click, see Settings). `Enter` when focused |
| Rearrange | Drag an icon to a new position; the blue line shows where it lands |
| Put a shortcut in a group | Drag it onto the group icon |
| Edit an item | Right-click → *Properties*, or `F2` |
| Use text instead of an icon image | *Properties → Text icon…*: text, font (default 宋體), size, bold, colour |
| Use an image from the web as the icon | *Properties → Change icon… → From a web address…* |
| Rename many shortcuts at once | Options menu (▸) → *Edit names…* (sort by name or modified date) |
| Record and loop a sound | Right-click → *Add special icon ▸ Sound Replayer*; press and hold to record, release to loop, click to stop |
| Open a file with a program on the bar | Drag the file from Explorer onto the program's icon (`{file}`/`{files}` in Parameters control where it goes) |
| Add a shortcut by dragging | Drop a file on empty space, or right-drag it anywhere on the bar |
| Show or hide the bar from anywhere | Set a hotkey in Settings (e.g. `Ctrl+Alt+Space`) |
| See a program's output | *Properties → Keep a log of the program's output*, then right-click → *Show log…* |
| Make the bar translucent | Settings → *Transparency* |
| Remove an item | Right-click → *Remove…*, or `Delete` |
| Multi Launch | Right-click a shortcut → *Add to Multi Launch group*; start them all from the options menu (▸) |
| Move the bar | Drag the dotted grip on the left or any empty background. Double-click the grip to toggle *always on top* |
| Resize the bar | Drag any edge or corner |
| Settings | Options menu (▸) → *Settings…*, or `Ctrl+,` |
| Sync with Google Drive | Options menu (▸) → *Sync with Google Drive…* (see below) |

## Sync with Google Drive

The sync dialog keeps named copies of your settings and icons in a `DesktopIconGrid` folder
in your Google Drive. Save the current bar as "Work", load it on another PC, or keep several
copies and switch between them. A device that saved or loaded a copy is *linked* to it:
changes are uploaded automatically a few seconds after they happen, and when another device
saved a newer version the app offers to load it at startup. Each PC has a device name
(defaults to the computer name) so you can see who saved what.

Google requires every app to use its own OAuth client, so a one-time setup is needed:

1. Open [Google Cloud Console](https://console.cloud.google.com/apis/credentials) and create a
   project (or pick one).
2. *APIs & Services → Library*: enable the **Google Drive API**.
3. *OAuth consent screen*: choose *External*, fill in the app name and your e-mail, then
   **publish** the app. Publishing avoids the 7-day sign-in expiry that "Testing" status
   imposes. Settings sync only needs the `drive.file` scope (files the app itself created),
   which needs no verification. Google Drive Buckets additionally ask for the full `drive`
   scope the first time you use one; it is a restricted scope, so an unverified app shows
   Google's "unverified app" warning that you, as the project owner, can accept.
4. *Credentials → Create credentials → OAuth client ID → Desktop app*. Download the JSON.
5. In Desktop Icon Grid: options menu (▸) → *Sync with Google Drive…* → *Import
   client_secret.json…* (or paste the client ID and secret), then *Connect…* and sign in
   in the browser.

Sign-in tokens are stored encrypted for your Windows user and renewed automatically. If
Google revokes the token (for example after the 7-day Testing expiry), the dialog offers
*Connect…* again. Until you create a Google Drive Bucket, the app can only see files it
created in your Drive.

## Building

Ready-made builds are attached to each [release](https://github.com/bamboo82/DesktopIconGrid/releases):
a small zip that needs the .NET 10 Desktop Runtime and a self-contained one that does not. Every push
also leaves a `DesktopIconGrid-win-x64` artifact on its Actions run. Changes are listed in
[CHANGELOG.md](CHANGELOG.md).

Requirements: Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build
```

```bash
dotnet run --project src/DesktopIconGrid
```

Run the unit tests. The core tests run on any OS; the `DesktopIconGrid.App.Tests` project (WPF-side
logic such as the icon worker and the running badge's path matching) needs Windows and is skipped elsewhere:

```bash
dotnet test
```

Publish a single-file build that runs on any machine with the .NET 10 Desktop Runtime:

```bash
dotnet publish src/DesktopIconGrid -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Add `--self-contained true` to bundle the runtime as well.

To publish a release, add its section to `CHANGELOG.md` and push a tag; the release workflow builds,
tests, publishes both zips and creates the GitHub release with that section as its notes:

```bash
git tag v0.2.0 && git push origin v0.2.0
```

The solution also compiles on Linux and macOS (`EnableWindowsTargeting` is set), which is
how CI builds it, but the app itself only runs on Windows.

## Project layout

```
src/DesktopIconGrid.Core/        models, ordering/grouping logic, JSON storage, launch planning, Google OAuth/Drive, sync (no WPF)
src/DesktopIconGrid/             WPF app: bar window, icon grid control, dialogs, shell interop, view models
tests/DesktopIconGrid.Core.Tests/  xunit tests for the core library (any OS)
tests/DesktopIconGrid.App.Tests/   xunit tests for the WPF-side logic without UI (Windows only)
```

## Local files

| File (in `%LocalAppData%\DesktopIconGrid`) | Contents |
| --- | --- |
| `layout.json` | Settings, window position and items. Written atomically after every change; an unreadable file is renamed to `layout.json.corrupt-<timestamp>` and the bar starts empty. |
| `sync.json` | Device name, Google API client credentials, the linked copy and when it was last synced. |
| `google-token.bin` | The Google sign-in token, encrypted with DPAPI for the current Windows user. |
| `logs\app.log` | The application log shown in the log viewer (rotated to `app.previous.log` at 2 MB). |
| `logs\` | Captured output of shortcuts that have logging turned on (ten newest runs per shortcut). |
| `icons\` | Icons downloaded from web addresses (256 px PNG). |
| `sounds\` | Recordings of Sound Replayer icons (WAV). |
| `drive-cache\` | Files downloaded for the Google Drive Bucket Viewer (purged after a day). |
