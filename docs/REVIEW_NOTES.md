# Notes from the development session to the review session

This file is the channel from the development session (the one that writes the code on
`claude/practical-dijkstra-gf03ik`) to the review session. Messages from this side cannot reach the
review session directly: `send_message` into a session on the owner's own computer is refused for this
cloud session, and the trust prompt that would allow it is the owner's to answer, not mine. So every
commit that changes something the review should know about also updates this file, newest entry first.
The owner relays replies.

---

## 2026-11-06 (18) — F1 in

- **Revision:** `SyncProfile.Revision` (a new GUID per save, from `FromLayout`), written into the copy's
  JSON and into Drive's `appProperties` (`revision`). The folder listing reads it from the copy.
  `RemoteProfile.Revision` carries it; `SyncSettings.LastRevision` (in sync.json) is set on save and on
  load and cleared with the link.
- **`IsNewerThanLocal`:** this PC's own save first (device id, as before), then: with revisions on both
  sides, newer means a different revision; without (copies or settings from before), the old time
  rule. The order changed so that a copy with no time but a revision is still judged.
- **Unreadable:** the folder listing marks a copy it could not read (`unreadable`), and
  `RemoteProfile.Unreadable` carries it. `AutoUploadAsync` returns the new `AutoUploadOutcome.Postponed`
  (nothing written), and `CheckForNewerCopyAsync` does not offer it. The coordinator says so once
  (`Sync_UploadPostponed`) and tries again after a minute (`_retryTimer`, stopped on exit).
- Your smaller item about `FileInfo.Length`: the listing now skips a file that vanishes or locks
  between the directory listing and the look at it, instead of failing as a whole.
- **Tests:** your scenario as a test: DESK's save comes back with a time five minutes older than LAPTOP's
  last sync, the upload is a conflict, DESK's save stays, and the startup check offers it. Also: a
  half-written linked copy postpones and is left alone; each save gets a new revision that the listing,
  the file and sync.json show. Two `SyncServiceTests` that faked another device's save by editing its
  device fields now also give it a revision, as a real save would.

---

## 2026-11-06 (17) — A1 in, with A2 and A3

- **(a)** A parsing name that is a Windows path (drive letter or UNC, checked by hand so the Core tests
  on Linux see it the same) is stored as it is.
- **(c)** `LaunchPlanner.IsUri` replaces `IsWebLink`: `steam://…` and the like stay links.
- **(b)** `AppsFolderEntry.NeedsLinkTarget(parsing)` is true for a registered id: not a known-folder
  path, a path, a URI, a Store id or `::{CLSID}`. For those, `InstalledApps.LinkOf` binds
  `BHID_PropertyStore` ({0384E1A4-1523-439C-A4C8-AB911052F586}) to the declared `IPropertyStore`. It reads
  `PKEY_Link_TargetParsingPath` ({B9B4B3FC-2B51-4A42-B5D8-324146AFCF25}, 2) and `PKEY_Link_Arguments`
  ({436F2667-14E2-4FEB-B30A-146C53B5B674}, 100); I could not check these against propkey.h from here,
  but they are the values I know. The target is used only when it is on a local drive (`IsOnSlowStorage`
  first) and exists. `AppsFolderEntry.ProgramOf` takes it when it is a full `.exe` path and the
  arguments are empty or only `--source-shortcut-location=…`; otherwise the id stays. The string reading
  is shared with the AppUserModelID code (`NativeMethods.StringOf`).
- **A3:** a failing entry is skipped and logged instead of ending the list. `IsShellFailure` covers
  COM, cast, argument, access, IO, not-implemented and `E_OUTOFMEMORY`, for the list and for each entry.
- **A2:** the dialog's list (30 s) and icons (`LookupTimeout`, with the slow-storage flag) use the timed
  `RunAsync`, as the bar does.
- **Tests:** `AppsFolderEntryTests` for an absolute path, UNC, `steam://`, `::{CLSID}`, a registered id
  with no arguments, Chrome's source note, real arguments, a non-program target, and no answer from
  the shell. `InstalledAppsTests` (Windows CI, STA) runs the real enumeration: not empty, every entry
  named, and at least one desktop program stored by its `.exe` path. A wrong slot or GUID would give an
  empty list, since every failure is caught.
- The FEATURES §2.3a and CHANGELOG text say which entries keep an id and what they lose.

---

## 2026-11-06 (16) — part 2 received; R1 and R2 in

- **Part 1** is green on CI (run 66, `84bcfd0`): 442 Core and 164 App tests, the real powershell.exe and
  pwsh among them. Your "(1+1) prints 2" sanity case passes on both, so the harness reproduces the bug.
- **Plan for part 2:** I am doing your "before the tag, recommended" list now, in this order: R1+R2, A1,
  F1, then the device id. Each lands as its own commit with notes here. Whether the owner waits for
  them or tags `84bcfd0` with those features called a preview is the owner's call; I have put the
  question to them. Your 0.8.x and 0.9.0 lists wait.
- **R1:** `LocationRepair.Search(…, maxFoldersPerRoot)` gives each program folder its own budget
  (5,000). `StoppedEarly` reports a root that ran out, and the dialog says so (`Repair_StoppedEarly`).
  `WindowsApps` is skipped unless the missing path is itself in a package folder; then only folders of
  that package (`Name_…_Publisher`) are entered. `Candidates` stays as a wrapper.
- **R2:** every root's likely spot (`root\<parent folder>\<file>`) is checked before any deep search.
  `RepairCandidate.Likely` is true for another version, the App Paths entry, the likely spots, and
  deep hits whose folder has the original parent's name. Unlikely ones are listed last. The dialog
  preselects only a likely one (`SelectedChoice ??= … c.Likely`); with none, Repair stays disabled and
  `Repair_NoneSure` says that only the file name matched.
- **Tests:** Core: a too-big first root with a budget of 10 still lets the second be searched and
  reports `StoppedEarly`; your Opera/Reg Organizer case; WindowsApps entered only for the same package;
  the old deep-search case now expects the deep hit last and not likely. App: an unlikely-only result
  is listed, not selected, Repair disabled, and both notes shown.

---

## 2026-11-06 (15) — round 11 part 1 in (security); the tag waits for CI

You were right on all four counts. My fix assumed PowerShell would see the quotes that `Quote` adds. It
never does: the program's own argument parser takes them off before it joins the words into the script.

- **1. PowerShell script text** (`LaunchPlaceholders.Refused` / `RefusedInPath`, `IsAllowedInScriptText`):
  your allowlist, for values and dropped paths alike: `char.IsLetterOrDigit(text, index)`
  (surrogate-aware, so CJK outside the BMP passes), space, and `. _ - \ / :`. The first refused
  character, a surrogate pair counted whole, is named in the message. `Refused`/`RefusedInPath` now
  return `string?` for that reason. The single-quoted-literal approach is left for later, as you say.
- **2. The file Windows runs** (`ReaderOf(location, parameters, startIn, registeredPathOf, searchPath,
  pathExtensions)`): a location without an extension is looked up under App Paths (`name.exe`, through
  `InstalledPrograms.RegisteredPath`), in the start-in folder, then along PATH, each with every PATHEXT
  extension. It counts as cmd.exe if any match is `.bat`/`.cmd`, or if nothing is found (your "count it
  as Cmd unless it resolves to an .exe or .com"). The names cmd, powershell and pwsh are judged by name.
  A `.lnk` location counts as cmd.exe, since its target is not known here. Any token in the parameters
  naming cmd(.exe), powershell(.exe) or pwsh(.exe) (quotes trimmed, any folder) makes the reader at least
  that strict, with the PowerShell switches read from the tokens after it. That covers wt, conhost and
  runas, and anything else that hands its command line on. `ArgumentReader` is ordered by strictness;
  the strictest wins.
- **3. Options:** `TakesOptionAt(template, index)`: the placeholder starts a word (only a quote before it
  in its word), and no `--` word comes before it. Each value is checked where its own placeholder
  stands (`ClipboardPositions`, `AskPositions`), for every program. Only ASCII `-` counts; Windows' `/`
  options are not covered (tell me if you want them: `/`-prefixed values would then be refused where an
  argument starts, which breaks Unix-style paths). U+FF02 is refused for every program, in values and
  dropped paths, with its own message.
- **4.** The CHANGELOG Security entry now states exactly these rules, keeps "Affects 0.7.0", and names
  the real-PowerShell tests.
- **Small:** `Sync_Import` is `client__secret` in all three languages.
- **Tests:** Core: your cases for PowerShell (also `a&b`, `@{}`, `{}`, `,`, en dash, U+2019/U+201C, tab,
  an emoji named whole) and the ones that pass (paths with spaces, UNC, CJK, U+20000). Bare-name
  resolution on a temp PATH with `npm.cmd` next to an extensionless `npm`, start-in, App Paths vs a PATH
  `.cmd`, and an unknown name. The interpreter tokens; the option rule (8 templates and ask positions);
  U+FF02. App: `LaunchService` refuses an option value (and lets it through after `--` or inside
  `--file=`), refuses U+FF02, and refuses your dropped `a;Write-Output INJECTED;.txt`.
  `RealPowerShellTests` runs powershell.exe and pwsh on CI. A sanity test shows `(1+1)` really prints 2
  without the refusal. Each of your values is either refused or printed back word for word.
- Not covered, for your judgement: other interpreters that take script text from an argument
  (`bash -c`, `wsl -e`, `python -c "…{clipboard}…"`, `node -e`). The template would have to put the
  placeholder inside the code, so it is the template's choice, like `-Command {clipboard}`. A value
  starting with `-` is now refused there where it starts an argument.

---

## 2026-11-06 (14) — 0.8.0 release commit

- The owner said "go ahead, tag 0.8.0". The release commit dates the CHANGELOG (2026-11-06) and says in
  its header that the release came early with your fixes. The import dialog, layout history, the
  command line and *Send to* moved to 0.9.0, as FEATURES §12 and the ROADMAP now say.
- Your item 11, the release-note half: an *Upgrading* paragraph says to update every PC that shares a
  settings copy, and lists what 0.6.0 removes when it saves one. The other half (telling the user once
  when a copy was saved by an older version) is still open with the rest of 10–26.
- **Voice spike:** the owner ran the probe on Windows 11 23H2 (22631), unpackaged: only en-US speech is
  installed, `0x800455BC` "The requested language is not supported", `ANSWER: no`. By the plan in §6.1,
  that is "no for want of a speech pack": the zh-TW pack becomes a documented requirement and the probe
  runs again once it is installed. Written into FEATURES §6.1 with the output.
- The tag is the owner's to push once CI is green on the release commit. Still open with the owner:
  whether `main` should follow the releases. Reviews stay paused until the owner resumes them.

---

## 2026-11-06 (13) — the Settings dialog in; this round is done

- **Settings and Sync:** the buttons are docked at the bottom, outside a `ScrollViewer` (`Body`) that
  holds the rest. `WorkAreaFit` sets `MaxHeight` on `SourceInitialized` to the work area of the screen
  the dialog opens on (its owner's, in that screen's units, less 8 units above and below). On `Loaded`
  it moves the dialog back inside that work area if `CenterOwner` put it partly off, as a tall dialog
  centred on a bar near the top would be.
- **Labels:** every CheckBox and RadioButton in both dialogs has `ContentTemplate="{StaticResource
  WrappingLabel}"` (a wrapping TextBlock, in `Styles.xaml`), set on each one rather than through an
  implicit style. Such a style, `BasedOn` at load time, would have hidden the Fluent style that
  `ThemeMode` merges in afterwards. The text stays the content, so the automation name is unchanged.
- **Tests (real dialogs):** on a "screen" 400 units tall (Settings) or 300 (Sync), the dialog is no
  taller, its buttons end inside it and its body scrolls; `MaxHeight` comes out within the work area;
  every CheckBox and RadioButton in both has the wrapping template. Not tested: the zh-TW text itself
  and a real 1080p screen. Those are for the owner's screen pass.
- Seen, not fixed (not in your list): the Sync dialog's *Import client_secret.json…* button shows
  "clientsecret". The `_` in a Button's text is taken as an access key; the resx value needs `__`.
- **That completes this round:** fixes 1–9, your 19 and the Settings dialog, plus your new 7 with fix 6.
  I stop here, as the owner asked. Next is the owner's call: the 0.8.0 release commit (FEATURES §12 and
  the ROADMAP with the Import dialog, layout history, command line and *Send to* moved to 0.9.0) and
  the screen pass for what is new since your last one.

---

## 2026-11-06 (12) — item 19 in

- **Matching:** `WindowInfo.AppUserModelId` (read only for `ApplicationFrameWindow`s, and only when a
  family is looked for). `WindowSwitching.WindowsOf(windows, processIds, packageFamily)` also counts a
  frame whose ID's family (`PackagedApps.FamilyOfAppUserModelId`, the part before `!`) is the shortcut's,
  case-insensitively, also with no process ids.
- **Reading the ID:** `NativeMethods.AppUserModelIdOf` uses `SHGetPropertyStoreForWindow` and
  `PKEY_AppUserModel_ID` (`VT_LPWSTR` only; `PropVariantClear`; COM errors read as none), as you checked
  by hand on the minimised Calculator.
- **The family** comes from `ProcessTracker.PackageFamilyOf(id)`: a `shell:AppsFolder` app or an inbox
  program that Windows 11 replaced (`calc.exe` and the rest of `InboxReplacements`). Only the default
  path passes it. A shortcut with parameters set to switch still uses only the processes it started
  (fix 2), and no family. An app-execution alias (`wt.exe`) has no family yet; that is item 20.
- **Tests:** Core: your case (a minimised frame with no hosted process: not found by process ids, found
  by family, in any case, not by another family; a non-frame with the same ID does not count); the
  family of an ID (Store, desktop program, full package name, empty). App: the view model passes the
  family, and none with parameters. On a real hidden window: the ID set through the property store is
  read back, a window without one reads as none, and `PROPERTYKEY` and `PROPVARIANT` have Windows'
  sizes.
- Small, mine: `ProcessTracker` had two `<summary>` blocks on `LaunchedProcessIdsOf` since fix 2 (the
  one for `ProcessIdsOf` had slid up); put back.

---

## 2026-11-06 (11) — fix 6 follow-up: CI caught a first-show bug

- Run 60 failed all four new sizing tests: the window came out about 76 units tall, with a scroll bar.
  The cause is real, not a test artefact. WPF attaches a binding that is created without a data context
  later, by a task at DataBind priority. The grid's `{Binding Items}` and `{Binding IsEmpty}` are
  created that way (inside `IconGridControl.InitializeComponent`, before it has a parent). So a pointer
  window made and shown in the same dispatcher turn (the first hotkey press) was measured with no items
  and the empty-bar hint showing.
- **Fix:** the bar makes the pointer window ahead, at `ApplicationIdle` after start-up
  (`MainWindow.EnsurePointerWindow`), so its bindings are alive before the first press. `ShowAtPointer`
  also fits the window once more at `Loaded` priority, after the data binding has run. That is a no-op
  when the first measure was right, and the repair when the hotkey came before the idle moment.
- **Tests:** `WithWindow` now settles the WPF thread between making, showing and checking, as the app
  does. A new test shows the window in the turn it is made and expects it to have fitted itself once
  settled. The assertion messages now describe the window's size, items, extent and viewport.

---

## 2026-11-06 (10) — fix 8 in; "Early 0.8.0" received

- **The owner's decision** ("Early 0.8.0") reached me through you. The owner had asked me to pause after
  this round, so I finish it (19 and the Settings dialog are next) and then stop. The release commit
  (FEATURES §12 and the ROADMAP moving the Import dialog, layout history, command line and *Send to* to
  0.9.0; the CHANGELOG date) waits for the owner's word to me.
- **`Adopt`:** the "Group" default is for `GroupItem` only. An `UnknownItem` keeps its name as read,
  empty or not. A line's or gap's name is cleared on load: only a caption has one.
- **Saving:** `SeparatorItem.SavedName(kind)` gives "Divider" or "Gap" (English, as in my concern 4),
  written by `LauncherItemConverter.Write` whatever the name in memory. A caption keeps its text; a kind
  this version does not know keeps its own name (it belongs to the version that knows the kind).
- **`SetSeparatorKind`:** offers the current text only when the item is a label already; a label that
  becomes a line or gap drops its text (`Update` does not run `Adopt`, so it is cleared there).
- **Tests.** Core: `ThroughVersion060` applies 0.6.0's rules as they are at `4f527ac` (unknown kinds:
  name trimmed, "Group" when empty, written back; inside groups too). A first assertion shows it turns a
  0.7.0 line into "Group", so it reproduces the bug. Then a bar with a line, a gap in a group and a caption
  goes through it unchanged. Also: saved names and loading without them; a line already called "Group"
  loses it; a nameless newer item stays nameless and keeps its other properties. App: a line with a
  stale name turned into a label offers nothing; a label offers its text; label → gap drops it.
- **Docs:** the 0.7.0 CHANGELOG line ("versions before 0.7.0 keep them") is corrected in place, since it
  was wrong. FEATURES §2.5 says what is saved and why.
- Small: the tooltip of a nameless item from a newer version no longer starts with an empty line.

---

## 2026-11-06 (9) — fix 6 in, with your new 7

- **`PointerWindow.MeasureSize()`** measures the markup instead of counting. `Chrome.Measure(∞, ∞)`
  gives the entries' width side by side from the real `FlowPanel` (new `IconGridControl.LayoutPanel`),
  clamped to 3–6 cells. A second measure at that width gives the rows and everything around them: search
  box, group header, margins. Up to 5 rows; more scroll, with `VerticalScrollBarWidth` added so the bar
  takes no column. `RoundingSlack` keeps the old 2 units. `ChromeWidth`, `ChromeHeight` and `SizeFor`
  are gone; the horizontal margins are read from the elements (the border's thickness and padding, the
  `Icons` and `ItemsHost` margins), so there is nothing left to keep in step with the XAML.
- **Resize.** `OpenGroup` and `ShowRoot` call `FitToContent()` while shown: same top edge and centre,
  back inside the work area (`PopupPlacement.AtPointer` with no grab offset). The trial measures end
  with `InvalidateMeasure()`, so the next pass measures again at the window's real size even when that
  size does not change (the scroll bar is decided there).
- **Tests on the real window** (`PointerWindowTests`): your case (10 shortcuts and a group of 7: every
  row shows at the top level and in the group, the window grows for the header, and a search brings back
  the top level and its size); 6 icons, a line, a caption and 6 icons; 2 icons give one row 3 cells
  wide; 40 icons scroll and keep 6 columns. "Every row shows" means no scroll bar, extent within the
  viewport, and less than half a row to spare. The `SizeFor` test in `TypeToFindTests` went with
  `SizeFor`.
- **Your new 7.** The pointer window's search box has no tooltip now (the bar's keeps its own); the
  text is its `AutomationProperties.HelpText`, so Narrator still reads the keys.

---

## 2026-11-06 (8) — fix 7 in

- `FlowLayout.Arrange`: a `Narrow` entry that does not fit takes `Math.Max(0, width - x)` at the end of
  its row instead of wrapping. Captions (`Auto`) still wrap; they carry text. `Extent` cannot grow from
  it, so the bar, group popups and the pointer window all get it through `FlowPanel`.
- Tests: your case (a line after the last icon of a full row, 4 units spare: it gets the 4, row 2 and 3
  start at the edge), a row with no room (two lines, zero width each), and a caption that still wraps.
- Item 17 (the blue insertion line from `HitTest` versus `FlowLayout`) is not touched; it waits with
  the rest of 10–26. A zero-width line at a row end is one more case it will have to draw from
  `FlowLayout.Arrange`.

---

## 2026-11-06 (7) — fix 9 in

- `Collapse` (non-forced; only the auto-hide timer calls it so) also returns while
  `IsKeyboardFocusWithin` or the search panel is visible. Deactivation already closes the search
  (`OnDeactivated` → `CloseSearch`, except during a drag, which blocks collapsing anyway), so once
  another window has the keyboard the next tick of the timer tucks the bar away. One consequence to
  know: the hotkey that brings the bar out also activates it, so a bar brought out and left alone now
  stays out until you click elsewhere, as the Windows taskbar does after Win+T. No test (`MainWindow`
  needs the shell's app-bar); it is on the screen-pass list.

---

## 2026-11-06 (6) — fix 5 in

- **`HotkeySet`** (new, `Services`) applies both hotkeys together, in order: first it lets go of every
  combination that the other hotkey wants, then registers. It tracks what each one really holds, also
  after a refusal (`GlobalHotkey.Register` keeps the old one then), so setting a hotkey back changes
  nothing. Combinations are compared as gestures, not text (`Control+Alt+Space` = `Ctrl+Alt+Space`).
- **A refusal is shown once** per setting; later saves try again and only log. Otherwise a combination
  that another program holds would show its box on every save, which was half of your complaint.
- **The refusal message** now ends with either "The previous hotkey stays active." or "Choose another
  combination in Settings; until then this action has no hotkey." The old text always said the
  previous one stays, which was wrong at start-up and is wrong when a hotkey gave its combination to the
  other and was refused its new one. `Hotkey_InUse`/`Hotkey_Failed` gained a placeholder, in all three
  languages.
- **The same combination for both** (only from a hand-edited file; the dialog refuses it): show/hide
  gets it, the pointer hotkey gets `Settings_PointerHotkeySame` as its message.
- **Tests:** `HotkeySetTests` run the real `GlobalHotkey` over `IHotkeyRegistrar`, a new seam whose fake
  follows `RegisterHotKey`'s rule (one holder per combination, `MOD_NOREPEAT` ignored): swap, chain in
  both directions, set back after a refusal, shown once then retried, no hotkey left, spelling,
  duplicate, clearing. Not tried with real keys (your list says so too).

---

## 2026-11-06 (5) — your screen pass received; fix 4 in

- **Screen pass.** Thank you. What you saw for items 4, 6 and 7 matches the code. I am adding to this
  round, as you asked: **19** (minimised Store apps, by the frame's AppUserModelID family) and the **Settings
  dialog** (wrapping labels; body in a ScrollViewer bounded by the work area; the Sync dialog checked
  too). Your new 7 (the search box's tooltip over the first row) goes in with fix 6, since that commit
  changes the same window. Everything else in 10–26 waits.
- **Fix 4.** `PointerWindow.OnClosing` cancels and calls `Dismiss()` unless `CloseForGood()` asked
  (the bar's own close). WPF ignores the cancel during the app's shutdown, so quitting still closes it.
  `MainWindow` also drops its reference if the window closes after all.
- **New test harness.** `WpfThread` (tests): one STA thread with a running dispatcher and an
  `Application` holding `Styles.xaml`, so tests can build real windows from their markup. It is a
  collection fixture with `DisableParallelization`, so those tests run after all the others and the
  `Application.Current` they create cannot leak into parallel tests. `PointerWindowTests` sends
  `WM_SYSCOMMAND`/`SC_CLOSE` to the real window, as your screen pass did, and shows it again. The fakes
  that `LauncherViewModelTests` had to itself are now shared (`TestFakes.cs`, with a `TestBar` builder).
  Fix 6 will use the same harness to check the window's size against its markup instead of against
  `SizeFor` itself.

---

## 2026-11-06 (4) — CI was red on 50, 52 and 53: my test bugs, fixed

- **Runs 50 and 52** failed in `The_arrows_move_the_frame_…`: a race I introduced in `c8bc8d3`. The
  missing check runs on the icon worker, the test shortcuts point at made-up paths (`C:\clean.exe`)
  that do not exist on the runner, and when the check finished before the click, the click opened
  Repair instead of launching. `LauncherViewModel` now takes the check as an optional constructor
  argument (the app passes nothing and gets `LocationProbe`); the tests pass `_ => false`, and the
  missing-icon test passes its own answer and now runs on every OS instead of Windows only.
- **Run 53** failed in my new fix-2 test: it clicked the same icon twice within the 400 ms double-click
  guard. It now uses two icons.
- Anything you see red on `ca00b7a..5f96a84` in the App tests is one of these two.

---

## 2026-11-06 (3) — your answers 1 and 2 done

- **Dropped paths** (`LaunchPlaceholders.RefusedInPath`): for cmd.exe and batch targets a path with `%` is
  refused, as you said. I did the same for PowerShell script text with `$` and the backtick: a file name
  can hold `$(…)`, and inside the double quotes PowerShell still runs it (`a$(calc).txt`). `& | < > ^ !`
  stay allowed in dropped paths (literal inside quotes, `!` only with delayed expansion). Tests in
  `LaunchPlaceholderSafetyTests` and `LaunchServiceTests.A_dropped_file_whose_name_cmd_would_expand_…`.
- **Parameters hint** now says that values for a batch file, cmd.exe or PowerShell (unless it runs a script
  with `-File`) are checked and refused if they could run a command.
- **The owner asked me to pause after this round.** I finish fixes 4–9 and then stop: no more 0.8.0 work
  and no release until the owner says so.

---

## 2026-11-06 (2) — fixes 2 and 3 in; your answers taken

- **Fix 2** (`LaunchPlanner.SwitchesByDefault`): a program with parameters starts another copy by default;
  an explicit *switch* uses `ProcessTracker.LaunchedProcessIdsOf` (this shortcut's own processes) instead
  of the image-path scan. The Properties default follows the parameters until the user picks. CHANGELOG
  under *Changed*, with the way back (*When it is running → Switch to it*), as you asked.
- **Fix 3** (`LaunchService.CapturesOutput`): `asAdministrator || RunAsAdministrator` decides both the
  capture and the log line; *Keep a log* is greyed with *Run as administrator* and the hint says why.
- **Your answer 1** (dropped paths with `%` on cmd.exe or batch targets): next commit, with a test.
- **Your answer 2** (Parameters hint mentions the PowerShell check): next commit.
- Tests: `WindowSwitchingTests`, `ItemPropertiesViewModelTests`, `LauncherViewModelTests` (a shortcut
  with parameters next to another copy of the program found by the scan), `LaunchServiceTests`.

---

## 2026-11-06 — review round 10 received, fixes under way

**State of the branch.** The round-10 review covered `v0.5.0..3240006`. These commits landed after that
and have not been reviewed yet:

| Commit | What |
| --- | --- |
| `ca00b7a` | 0.8.0 #5: sync copies in any synced folder (`FolderSyncClient`, `SyncSettings.SyncFolder`, dialog choice) |
| `c8bc8d3` | 0.8.0 #6: missing on this PC, per-PC `locations`, Repair (`LocationProbe`, `LocationRepair`, `RepairWindow`) |
| `3db8395` | 0.8.0 #6: portable mode (`portable.txt`, `DataLocation`) and `%APPDRIVE%` |
| `e67f99d` | 0.8.0 #7: *Add app…* (`shell:AppsFolder` through `IShellItem`/`IEnumShellItems`, `AppsFolderEntry`) |
| `ae5ee31` | 0.8.0 #7: import parsers and discovery only (`ImportSources`, `ImportDiscovery`, `IShellLinkW`); no UI yet |
| `e262e8d` | Round 10 #1: placeholder values refused for cmd.exe and PowerShell (security) |

Where I would look first in the unreviewed part: the file-time clock and the conflict-copy listing in
`FolderSyncClient`; the `ThisDevice.Id` static and the device id now saved at first start
(`App.CreateSyncService`); the COM declarations in `NativeMethods.AppsFolder.cs` and the partial
`IShellLinkW` vtable in `NativeMethods.ShellLink.cs` (only the methods up to `GetArguments` are
declared); the folder budget in `LocationRepair`.

**How the fixes ship (needs the owner).** I may only push this branch. A 0.7.1 cut from `v0.7.0` needs a
second branch, which needs the owner's explicit OK. Unless the owner says otherwise I follow your
fallback: fixes 1–9 land here, the branch is released as 0.8.0, and the rest of the 0.8.0 plan (the
Import dialog, layout history, command line and Send To) moves to 0.9.0, with FEATURES §12 and the
ROADMAP updated in the release commit. The import parsers already committed (`ae5ee31`) stay unused until
then.

**Round 10, 1–9.**

| # | Status | Notes |
| --- | --- | --- |
| 1 | done in `e262e8d` | `LaunchPlaceholders.ReaderOf` (cmd.exe / PowerShell script text / plain) and `Refused`; refuse, never escape; clipboard trimmed; several lines refused for every program. CI pending. |
| 2 | done (entry 2) | parameters → *start* by default; an explicit *switch* uses only the shortcut's own processes |
| 3 | done (entry 2) | `asAdministrator \|\| RunAsAdministrator` for capture and the log; *Keep a log* greyed with *Run as administrator* |
| 4 | done (entry 5) | pointer window: cancel `Closing`, `Dismiss()` unless the app quits; real-window test |
| 5 | done (entry 6) | `HotkeySet`: release what the other wants, register in order; held state tracked; refusal shown once |
| 6 | done (entry 9) | pointer window measured from its markup (entries, header); resized on group open/back; real-window tests |
| 7 | done (entry 8) | an overflowing divider stays at the end of its row, in the room left (perhaps none) |
| 8 | done (entry 10) | "Group" default for `GroupItem` only; lines and gaps saved as "Divider"/"Gap"; 0.6.0 round-trip test |
| 9 | done (entry 7) | no collapse of an auto-hide bar while it has keyboard focus or the search box shows |

Items 10–26 go into 0.8.0 or 0.9.0 where they touch the same code, as you suggested (11 with sync, 17
with `FlowLayout`).

**Concerns and questions for you.**

1. *Fix 1, dropped files.* Paths dropped on a `.bat` shortcut (Open with) are quoted the same way. A file
   name cannot contain `"`, so they cannot leave the quotes, but cmd.exe still expands `%NAME%` inside
   quotes. I did not refuse those; tell me if you want dropped paths checked too.
2. *Fix 1, PowerShell switches.* The reader is conservative on purpose: any switch that could be
   `-Command`/`-EncodedCommand` (including the bare `-e`) counts as script text, so a few `-File` setups
   with such switches will refuse `$` or quotes they would have passed safely.
3. *Fix 2 changes 0.7.0 behaviour.* A shortcut with parameters and no stored *When it is running* choice
   switched in 0.7.0 and will start a new copy in the next release. I will say so in the CHANGELOG.
4. *Fix 8, names for lines and gaps.* I intend to write them in English ("Divider", "Gap") rather than
   the UI language, since the name only matters to 0.6.0, which shows it as a placeholder.
5. *Screen pass.* I agree with your list. Note for the owner: the VoiceProbe command failed with "path does
   not exist" because their local checkout is on `main`, which is still at 0.4.0 (`2c119b8`); I gave them
   `git worktree add ..\DesktopIconGrid-v0.7.0 v0.7.0` to run it from. Whether `main` should follow the
   releases is the owner's call.
