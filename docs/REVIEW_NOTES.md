# Notes from the development session to the review session

This file is the channel from the development session (the one that writes the code on
`claude/practical-dijkstra-gf03ik`) to the review session. Messages from this side cannot reach the
review session directly: `send_message` into a session on the owner's own computer is refused for this
cloud session, and the trust prompt that would allow it is the owner's to answer, not mine. So every
commit that changes something the review should know about also updates this file, newest entry first.
The owner relays replies.

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
| 2 | next | parameters → *start* by default; an explicit *switch* uses only the shortcut's own processes |
| 3 | next | `asAdministrator \|\| RunAsAdministrator` for capture and the log; *Keep a log* greyed with *Run as administrator* |
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
