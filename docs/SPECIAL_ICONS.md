# Special Icons — Design

A *special icon* is an item on the bar that does more than open a location. It has its
own click action, can accept files dropped onto it, can show progress, and has its own
properties dialog. The first special icon is the **Google Drive Bucket**: drop files on it
to upload them to a Drive folder; click it to open that folder in the browser.

This document defines the concept, the extension model, and the Google Drive Bucket in
detail. It complements [FEATURES.md](../FEATURES.md).

---

## 1. Naming

Two menu entries would otherwise collide:

| Menu entry | Meaning |
| --- | --- |
| **Add Windows item ▸** | The ready-made shortcuts from the reference app (This PC, Control Panel, Notepad…). They are ordinary shortcuts once added. *Renamed from "Add special item".* |
| **Add special icon ▸** | Active icons with behaviour of their own, defined here. First entry: *Google Drive Bucket*. |

In code, the catalog keeps the name `SpecialItemCatalog`; the new concept is `SpecialIconItem`.

## 2. Item model

A third item kind next to shortcuts and groups:

```json
{ "$type": "special", "id": "…", "name": "Receipts", "iconPath": null,
  "kind": "google-drive-bucket",
  "settings": { "folderId": "1AbC…", "folderName": "Receipts", "onNameConflict": "keep-both" } }
```

- `kind` selects the handler. Unknown kinds (file written by a newer version) are kept as-is
  and shown with a placeholder icon and a tooltip saying the icon needs a newer app version;
  they can still be moved and removed.
- `settings` is a flat string map so the core library and the layout file stay independent
  of individual kinds. Each kind reads and writes its own keys through a small typed wrapper.
- Special icons can live at the root or inside a group, can be reordered and removed like
  any item, and accept a custom icon through the common *Icon* field.
- They are not Multi Launch members (activating a bucket opens a browser tab, which is not
  what "start everything" should do).

## 3. Extension model (app layer)

```
ISpecialIconHandler
  Kind, DisplayName, Description
  SpecialIconItem CreateItem()                       // defaults for a new icon of this kind
  ImageSource? GetIcon(item, pixelSize)              // default icon (vector or shell)
  string GetToolTip(item)
  Task ActivateAsync(item, SpecialIconContext)       // single click / Enter
  bool CanAcceptDrop(item, IDataObject)              // called during DragOver
  Task DropAsync(item, IDataObject, IProgress<SpecialIconProgress>, CancellationToken)
  bool ShowProperties(item)                          // kind-specific dialog; returns true when saved
  IReadOnlyList<SpecialIconMenuEntry> GetMenuEntries(item)   // extra context-menu entries
```

- `SpecialIconRegistry` holds the handlers (`google-drive-bucket` today) and returns a
  fallback handler for unknown kinds.
- `SpecialIconContext` gives handlers what they need without coupling them to views:
  the dialog service, the sync/Drive services, and a way to update the icon's status.
- `ItemViewModel` gains `IsSpecial`, `Progress` (0–1 or none), `StatusText` and `IsBusy`,
  which the icon template renders as a progress ring over the icon and a line in the tooltip.
- `IconGridControl`:
  - during a drag, a special icon whose handler accepts the data highlights like a group
    does today, and the drop goes to `DropAsync` instead of inserting new shortcuts;
  - a click routes to `ActivateAsync`;
  - *Properties* opens the handler's dialog; the generic dialog is not used.
- Context menu for a special icon: `New shortcut…`, `New group…`, `Add Windows item ▸`,
  `Add special icon ▸`, separator, handler entries, separator, `Remove…`, separator, `Properties`.

## 4. Monitor icons (implemented)

The first two kinds show a live percentage and were the pilot for the framework above.

| Kind | Value | Refresh | Click | Properties |
| --- | --- | --- | --- | --- |
| **CPU usage** (`cpu-usage`) | Busy share of all logical processors, from the kernel's idle/kernel/user time counters (`GetSystemTimes`), no performance-counter dependency. | 2 s | Opens Task Manager. | Name, design. |
| **Disk usage** (`disk-usage`) | Used share of a drive's capacity (`DriveInfo`); tooltip shows free and total space and the volume label. | 10 s | Opens the drive in File Explorer. | Name, drive (fixed, removable and network drives), design. |

Designs, chosen per icon:

- **Ring** — a track with an arc that fills clockwise, the number in the middle.
- **Bar** — the caption above a horizontal bar, the number beneath.
- **Number** — a large number coloured by level, the caption beneath.

The level colour is green, then amber (CPU ≥ 70 %, disk ≥ 80 %) and red (≥ 90 %); an unreadable
value shows "…" in grey. A custom image or text icon set in the common fields replaces the gauge.
Gauges live in a `GaugeState` that a `SystemMonitor` timer updates; states are detached when the
icon is removed so sampling stops. Settings keys: `style` (`ring`/`bar`/`number`) and `drive` (`C:\`).

## 5. Google Drive Bucket (implemented: Phase A and the viewer)

### 5.1 What it does

| Action | Behaviour |
| --- | --- |
| Single click (or Enter) | Opens `https://drive.google.com/drive/folders/<folderId>` in the default browser. No API call, so it also works offline. |
| Drop files from Explorer | Uploads each file into the folder. Progress shows on the icon; a summary dialog appears only when something failed. |
| Drop folders | Recreates the folder tree inside the bucket folder and uploads the files (Phase B). |
| Drop a link or text | Not accepted (the drop cursor shows "not allowed"). |
| Ctrl + click | Opens the Bucket Viewer (§5.7) to browse the folder and copy files to the local disk. |
| Context menu | *Open in browser*, *Browse…* (the viewer), *Open folder picker…*, *Cancel uploads* (while uploading), *Properties*. |

### 5.2 Properties dialog ("Google Drive Bucket Properties")

| Field | Notes |
| --- | --- |
| Name | Caption / tooltip. Defaults to the folder name when a folder is chosen. |
| Folder | The Drive folder. Accepts a pasted folder link or ID; *Browse…* opens the folder picker (§4.4). Shows the resolved folder name and the account it belongs to. |
| When a file with the same name exists | *Keep both* (default; Drive allows duplicates), *Replace the existing file*, *Skip*. |
| Icon | Default is a Drive-folder glyph; *Change icon…* / *Use default* as for other items. |
| Requires | A connected Google account with full Drive access (§4.3). The dialog offers to connect or to grant the extra access inline. |

### 5.3 Permissions

The sync feature only needs `drive.file` (files the app created). Uploading into an
arbitrary folder, and browsing folders, needs the full `https://www.googleapis.com/auth/drive`
scope. The app asks for it **incrementally**:

1. The user creates a bucket (or opens its properties) while the connected account only has
   `drive.file`.
2. The app explains why and runs the sign-in again with the extra scope and
   `include_granted_scopes=true`. The stored token records the granted scopes.
3. Every bucket operation checks the scope first and shows the same prompt if it is missing
   (for example after reconnecting with fewer permissions).

Consequences the setup guide must mention: `drive` is a *restricted* scope, so an unverified
OAuth client shows Google's "unverified app" warning that the account owner can accept;
publishing the consent screen to "In production" keeps refresh tokens from expiring after
seven days (Testing status). Token renewal is automatic in all cases: access tokens are
refreshed silently a minute before they expire and on any 401.

### 5.4 Folder picker

A modal dialog that lists folders from *My Drive* using the Drive API
(`mimeType = 'application/vnd.google-apps.folder' and '<parent>' in parents and trashed = false`),
with breadcrumb navigation, *New folder…*, and *Shared with me* as a second root. Selecting a
folder fills in its id and name. Pasting a link such as
`https://drive.google.com/drive/folders/1AbC…` or `https://drive.google.com/drive/u/0/folders/1AbC…`
is parsed to the id and resolved with `files.get` to confirm access and fetch the name.

### 5.5 Uploads

- Uses Drive's **resumable upload** (`uploadType=resumable`): one POST to open a session, then
  PUT requests in 8 MiB chunks with `Content-Range`. This gives per-file progress, survives
  short network hiccups (the session can be resumed from the last acknowledged byte), and
  handles files of any size. Files under 5 MB use a single multipart request.
- Uploads run sequentially per bucket on a background task; dropping more files while an
  upload runs queues them. Dropping onto a different bucket runs in parallel.
- Name conflicts are resolved per file according to the bucket setting; *Replace* finds the
  existing file by name in the folder and updates its content (`files.update` with media),
  keeping the Drive file id and sharing.
- The icon shows a progress ring (completed bytes over total bytes) and the tooltip reads
  "Uploading 3 of 7 — report.pdf (62%)". Finished uploads show a checkmark for a few seconds.
- Failures are collected and shown once at the end: "2 of 7 files were not uploaded" with
  the reasons. A 404 for the folder offers to pick a new folder; a 403 for scope offers to
  grant access.
- Cancelling (context menu) aborts the current chunk and skips the rest of the queue.

### 5.6 Icon

A vector Drive-folder glyph (triangle logo on a folder) in the app's resources, recoloured
when the bucket is in an error state. The common *Icon* override applies.

### 5.7 Bucket Viewer (Ctrl + click)

Holding **Ctrl** while clicking a bucket (or choosing *Browse…* from its menu) opens the
**Google Drive Bucket Viewer**: a window that looks like File Explorer and browses the bucket's
folder on Drive.

| Area | Behaviour |
| --- | --- |
| Toolbar | *Back*, *Up*, a breadcrumb (`My Drive › Projects › Receipts`), *Refresh*, *Download to…*, *Open in browser*. |
| List | Columns *Name*, *Type*, *Size*, *Modified*; folders first, then files, natural sort. Icons come from the shell icon for the file's extension, a folder icon for folders and a document icon for Google Docs formats. |
| Status bar | Item count, selection size, and the progress of the current transfer. |
| Navigation | Double-click a folder to enter it; *Back*/*Up* and breadcrumb segments move around; `Backspace` goes up. |
| Open a file | Double-click downloads the file to the cache and opens it with its default app; Google Docs, Sheets and Slides open in the browser instead. |
| Download | *Download to…* asks for a destination folder and downloads the selection (folders recursively), naming duplicates `name (2).ext`. |
| Drag out | Dragging selected items onto the desktop, Explorer or any program that accepts files **copies them to the local disk**: the items are first downloaded into `%LocalAppData%\DesktopIconGrid\drive-cache\<fileId>\<name>` (progress in the status bar), then an ordinary file drag starts, so the receiver copies real local files. Google Docs formats are exported (`files.export`) as `.docx`, `.xlsx`, `.pptx` or `.pdf`. The cache is purged of entries older than a day at startup. |
| Drop in | Dropping files from Explorer into the list uploads them into the folder currently shown (same code path as dropping on the bucket icon). |
| Not in v1 | Renaming, deleting, moving or sharing Drive files; search; thumbnails. |

API calls: `files.list` with `'<folder>' in parents and trashed = false`, fields
`id,name,mimeType,size,modifiedTime`, `orderBy=folder,name_natural`, 200 per page with paging;
`files.get?alt=media` streamed to disk with `Content-Length` for progress; `files.export` for
Google formats; folder downloads walk the tree with the same listing call. A virtual-file drag
(`CFSTR_FILEDESCRIPTOR` / `CFSTR_FILECONTENTS`, which lets Explorer pull the bytes on drop without a
cache) is a possible later refinement.

### 5.8 Security and privacy

- Nothing is uploaded without an explicit drop onto a bucket.
- Tokens stay DPAPI-encrypted in `%LocalAppData%\DesktopIconGrid`; folder ids are stored in
  the layout file and therefore also travel with synced settings copies, which is intended:
  a bucket works on every device signed in to the same account.
- The app never deletes or moves files in Drive from a bucket; *Replace* only rewrites content
  of a file with the same name in that folder.

## 6. Sound Replayer (implemented)

| Interaction | Behaviour |
| --- | --- |
| Press and hold (≥ 300 ms) while stopped | Starts recording from the default microphone (MCI wave audio, 16-bit mono 22.05 kHz). The icon shows **REC** in red and the seconds elapsed. |
| Release | Stops recording, saves `%LocalAppData%\DesktopIconGrid\sounds\<itemId>.wav` and starts looping it. The icon shows **LOOP** in green. |
| Single click while looping | Stops playback; the icon shows the microphone again. |
| Single click while stopped | Replays the last recording in a loop (nothing happens without one). |
| Context menu | *Stop*, *Play recording*, *Delete recording*, *Open sounds folder*, plus the usual entries. |
| Properties | Name only. |

A hold is detected by the grid: a press on a press-and-hold icon starts a 300 ms timer; moving
the mouse before it fires still begins an ordinary drag, after it fires the release ends the hold
instead of counting as a click. Playback uses `System.Media.SoundPlayer.PlayLooping`. If
Windows blocks microphone access for desktop apps, the MCI error is shown and nothing is recorded.

## 6a. Command tile (implemented, 0.9.0)

Kind `command-tile`; settings in `SpecialIconItem.Settings` (`CommandTileSettings`: program, arguments,
start folder, interval, timeout, display, thresholds, click, open target). `CommandTileHandler` creates a
`CommandTileSession` per tile on the bar, which runs `CommandTileRunner` (redirected output, no window,
batch files through `%ComSpec% /d /c`, the process tree stopped on a time-out, on detach and on exit) and
writes the reading (`CommandTileOutput`) into the tile's `GaugeState` (`Level` for the exit-code colour,
`LabelText` for the text). A tile runs only when `CommandTrust` (local `command-trust.json`, per device id)
holds its `Hash()`, which covers program, arguments, start folder, interval and the click's target; see
FEATURES §2.4 for when saving approves and for the approval window.

## 7. Testing

- Core: `SpecialIconItem` JSON round trip including unknown kinds; settings wrapper defaults;
  folder-link parsing; conflict-policy decision table; resumable-upload chunking and
  `Content-Range` headers against a fake HTTP handler; retry of an interrupted chunk;
  folder listing and creation; scope check logic.
- App (manual, Windows): drop one file, many files, a folder tree, a 200 MB file; cancel mid
  upload; offline drop; folder deleted on Drive; account without the `drive` scope; unknown kind
  in the layout file.

## 8. Delivery

| Phase | Scope |
| --- | --- |
| 0 *(done)* | Item model (`SpecialIconItem`, `$type: "special"`), handler interface and registry, *Add special icon ▸* menu, CPU and disk gauges with three designs. |
| A *(done)* | Progress ring on the icon; Google Drive Bucket with click-to-open, file and folder drops, resumable uploads, link/ID entry with *Check* in properties, incremental consent; Bucket Viewer with browsing, open, *Download to…*, drag-out of files and folders (downloaded to the cache first), drop-in upload, exports of Google formats, *Replace* and *Skip* policies, cancel uploads. |
| B | Folder picker dialog, virtual-file drag without the cache, thumbnails. |
| C | Further kinds, each a separate design note: e.g. *Clipboard note* (drop text to keep, click to copy), *Screenshot bucket* (click captures the screen to a folder), *Recycle Bin* with fill state. |
