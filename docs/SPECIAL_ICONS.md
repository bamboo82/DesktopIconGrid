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

## 4. Google Drive Bucket

### 4.1 What it does

| Action | Behaviour |
| --- | --- |
| Single click (or Enter) | Opens `https://drive.google.com/drive/folders/<folderId>` in the default browser. No API call, so it also works offline. |
| Drop files from Explorer | Uploads each file into the folder. Progress shows on the icon; a summary dialog appears only when something failed. |
| Drop folders | Recreates the folder tree inside the bucket folder and uploads the files (Phase B). |
| Drop a link or text | Not accepted (the drop cursor shows "not allowed"). |
| Context menu | *Open in browser*, *Open folder picker…*, *Cancel uploads* (while uploading), *Properties*. |

### 4.2 Properties dialog ("Google Drive Bucket Properties")

| Field | Notes |
| --- | --- |
| Name | Caption / tooltip. Defaults to the folder name when a folder is chosen. |
| Folder | The Drive folder. Accepts a pasted folder link or ID; *Browse…* opens the folder picker (§4.4). Shows the resolved folder name and the account it belongs to. |
| When a file with the same name exists | *Keep both* (default; Drive allows duplicates), *Replace the existing file*, *Skip*. |
| Icon | Default is a Drive-folder glyph; *Change icon…* / *Use default* as for other items. |
| Requires | A connected Google account with full Drive access (§4.3). The dialog offers to connect or to grant the extra access inline. |

### 4.3 Permissions

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

### 4.4 Folder picker

A modal dialog that lists folders from *My Drive* using the Drive API
(`mimeType = 'application/vnd.google-apps.folder' and '<parent>' in parents and trashed = false`),
with breadcrumb navigation, *New folder…*, and *Shared with me* as a second root. Selecting a
folder fills in its id and name. Pasting a link such as
`https://drive.google.com/drive/folders/1AbC…` or `https://drive.google.com/drive/u/0/folders/1AbC…`
is parsed to the id and resolved with `files.get` to confirm access and fetch the name.

### 4.5 Uploads

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

### 4.6 Icon

A vector Drive-folder glyph (triangle logo on a folder) in the app's resources, recoloured
when the bucket is in an error state. The common *Icon* override applies.

### 4.7 Security and privacy

- Nothing is uploaded without an explicit drop onto a bucket.
- Tokens stay DPAPI-encrypted in `%LocalAppData%\DesktopIconGrid`; folder ids are stored in
  the layout file and therefore also travel with synced settings copies, which is intended:
  a bucket works on every device signed in to the same account.
- The app never deletes or moves files in Drive from a bucket; *Replace* only rewrites content
  of a file with the same name in that folder.

## 5. Testing

- Core: `SpecialIconItem` JSON round trip including unknown kinds; settings wrapper defaults;
  folder-link parsing; conflict-policy decision table; resumable-upload chunking and
  `Content-Range` headers against a fake HTTP handler; retry of an interrupted chunk;
  folder listing and creation; scope check logic.
- App (manual, Windows): drop one file, many files, a folder tree, a 200 MB file; cancel mid
  upload; offline drop; folder deleted on Drive; account without the `drive` scope; unknown kind
  in the layout file.

## 6. Delivery

| Phase | Scope |
| --- | --- |
| A | Item model, handler registry, menu rename, progress overlay; Google Drive Bucket with click-to-open, file drops with *keep both*, resumable uploads, link/ID entry in properties, incremental consent. |
| B | Folder picker, recursive folder drops, *Replace* and *Skip* policies, cancel, "Open folder picker…" menu entry. |
| C | Further kinds, each a separate design note: e.g. *Clipboard note* (drop text to keep, click to copy), *Screenshot bucket* (click captures the screen to a folder), *Recycle Bin* with fill state. |
