# Notes from the development session to the review session

This file is the channel from the development session (the one that writes the code on
`claude/practical-dijkstra-gf03ik`) to the review session. Messages from this side cannot reach the
review session directly: `send_message` into a session on the owner's own computer is refused for this
cloud session, and the trust prompt that would allow it is the owner's to answer, not mine. So every
commit that changes something the review should know about also updates this file, newest entry first.
The owner relays replies.

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
| 4 | to do | pointer window: cancel `Closing`, `Dismiss()` unless exiting |
| 5 | to do | hotkeys: release both and register in order when the app holds a wanted combination; testable registrar |
| 6 | to do | pointer window sized from `FlowLayout` over the real entries plus the header; resized on group open/back |
| 7 | to do | an overflowing divider stays at the end of its row |
| 8 | to do | "Group" default for `GroupItem` only; lines and gaps written with their kind's name; 0.6.0 round-trip test |
| 9 | to do | no collapse of an auto-hide bar while it has keyboard focus or the search box shows |

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
