# Desktop Icon Grid

A compact launcher bar for Windows. Put programs, folders and web links on it as
icons, arrange them in a grid, and open any of them with one click.

Built with .NET 10 and WPF. The only package is Microsoft's DPAPI wrapper, used to encrypt the
Google sign-in token.

## Features

- **Launcher bar** — a borderless, resizable window with a grip on the left to move it,
  a close button and an options menu. Icons flow into as many columns as fit.
- **Shortcuts** — any file, folder or URL, with optional parameters and a custom icon.
  Icons are extracted the same way File Explorer shows them.
- **Groups** — a group is a folder on the bar. Click it to open a popup with its own
  grid of shortcuts; drag shortcuts in and out.
- **Windows items** — one-click entries for This PC, Control Panel, Recycle Bin,
  Show Desktop, Notepad, Command Prompt, Windows Terminal and more.
- **Multi Launch group** — flag any shortcuts and start them all together.
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
| Open an item | Click it (or double-click, see Settings). `Enter` when focused |
| Rearrange | Drag an icon to a new position; the blue line shows where it lands |
| Put a shortcut in a group | Drag it onto the group icon |
| Edit an item | Right-click → *Properties*, or `F2` |
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
   **publish** the app. Publishing needs no verification for the scope this app uses
   (`drive.file`, files the app itself created) and avoids the 7-day sign-in expiry that
   "Testing" status imposes.
4. *Credentials → Create credentials → OAuth client ID → Desktop app*. Download the JSON.
5. In Desktop Icon Grid: options menu (▸) → *Sync with Google Drive…* → *Import
   client_secret.json…* (or paste the client ID and secret), then *Connect…* and sign in
   in the browser.

Sign-in tokens are stored encrypted for your Windows user and renewed automatically. The app
can only see files it created in your Drive.

## Building

Requirements: Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build
```

```bash
dotnet run --project src/DesktopIconGrid
```

Run the unit tests (they cover the platform-neutral core and run on any OS):

```bash
dotnet test
```

Publish a single-file build that runs on any machine with the .NET 10 Desktop Runtime:

```bash
dotnet publish src/DesktopIconGrid -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Add `--self-contained true` to bundle the runtime as well.

The solution also compiles on Linux and macOS (`EnableWindowsTargeting` is set), which is
how CI builds it, but the app itself only runs on Windows.

## Project layout

```
src/DesktopIconGrid.Core/        models, ordering/grouping logic, JSON storage, launch planning, Google OAuth/Drive, sync (no WPF)
src/DesktopIconGrid/             WPF app: bar window, icon grid control, dialogs, shell interop, view models
tests/DesktopIconGrid.Core.Tests/  xunit tests for the core library
```

## Local files

| File (in `%LocalAppData%\DesktopIconGrid`) | Contents |
| --- | --- |
| `layout.json` | Settings, window position and items. Written atomically after every change; an unreadable file is renamed to `layout.json.corrupt-<timestamp>` and the bar starts empty. |
| `sync.json` | Device name, Google API client credentials, the linked copy and when it was last synced. |
| `google-token.bin` | The Google sign-in token, encrypted with DPAPI for the current Windows user. |
