# Desktop Icon Grid

A compact launcher bar for Windows. Put programs, folders and web links on it as
icons, arrange them in a grid, and open any of them with one click.

Built with .NET 10 and WPF. No third-party packages.

## Features

- **Launcher bar** — a borderless, resizable window with a grip on the left to move it,
  a close button and an options menu. Icons flow into as many columns as fit.
- **Shortcuts** — any file, folder or URL, with optional parameters and a custom icon.
  Icons are extracted the same way File Explorer shows them.
- **Groups** — a group is a folder on the bar. Click it to open a popup with its own
  grid of shortcuts; drag shortcuts in and out.
- **Special items** — one-click entries for This PC, Control Panel, Recycle Bin,
  Show Desktop, Notepad, Command Prompt, Windows Terminal and more.
- **Multi Launch group** — flag any shortcuts and start them all together.
- **Drag and drop** — reorder icons, drop files or folders from Explorer, drop links
  from a browser.
- **Settings** — icon size, spacing, labels, single or double click, always on top,
  background colour.
- Everything is saved automatically to `%LocalAppData%\DesktopIconGrid\layout.json`.

See [FEATURES.md](FEATURES.md) for the full feature plan and delivery phases.

## Using it

| Action | How |
| --- | --- |
| Add a shortcut | Right-click the bar → *New shortcut…*, drop a file onto the bar, or `Ctrl+N` |
| Add a group | Right-click → *New group…* or `Ctrl+G` |
| Add a Windows location or tool | Right-click → *Add special item ▸* |
| Open an item | Click it (or double-click, see Settings). `Enter` when focused |
| Rearrange | Drag an icon to a new position; the blue line shows where it lands |
| Put a shortcut in a group | Drag it onto the group icon |
| Edit an item | Right-click → *Properties*, or `F2` |
| Remove an item | Right-click → *Remove…*, or `Delete` |
| Multi Launch | Right-click a shortcut → *Add to Multi Launch group*; start them all from the options menu (▸) |
| Move the bar | Drag the dotted grip on the left. Double-click it to toggle *always on top* |
| Resize the bar | Drag any edge or corner |
| Settings | Options menu (▸) → *Settings…*, or `Ctrl+,` |

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
src/DesktopIconGrid.Core/        models, ordering/grouping logic, JSON storage, launch planning (no WPF)
src/DesktopIconGrid/             WPF app: bar window, icon grid control, dialogs, shell interop, view models
tests/DesktopIconGrid.Core.Tests/  xunit tests for the core library
```

## Layout file

`%LocalAppData%\DesktopIconGrid\layout.json` holds the settings, the window position and
the items. It is written atomically after every change. If it ever becomes unreadable the
app renames it to `layout.json.corrupt-<timestamp>` and starts with an empty bar.
