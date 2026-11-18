# Working notes for Claude sessions

Read this first; it replaces exploring the repository. Details live in the files named here: open them
only for the part you need (they are long).

## What this is

Desktop Icon Grid: a .NET 10 WPF launcher bar for Windows (shortcuts, groups, special icons, sync through
Google Drive or a folder). The app has one package, Microsoft's `System.Speech` (voice launch; the owner's decision); ask the owner before
adding another.

## Layout

| Path | What |
| --- | --- |
| `src/DesktopIconGrid.Core` | No WPF. Models, layout storage, launch planning and placeholder checks, sync, imports, command line (`Commands/BarCommands`), command tile settings and trust (`SpecialIcons/`), health checks (`Health/`). Runs on Linux. |
| `src/DesktopIconGrid` | The WPF app: `Views/`, `ViewModels/`, `Services/` (Win32, dialogs, pipe, probes), `SpecialIcons/` (handlers, gauges), `Resources/Strings*.resx`. |
| `tests/DesktopIconGrid.Core.Tests` | xUnit; runs here on Linux. |
| `tests/DesktopIconGrid.App.Tests` | xUnit, `net10.0-windows`; **builds on Linux but only runs in CI**. |
| `tools/` | `add_strings.py`, `gen_designer.py` (see Strings). |
| `FEATURES.md` | The specification, by section; §12 is the release plan. |
| `docs/ROADMAP.md` | The numbered feature ideas and which release has them. |
| `docs/REVIEW_NOTES.md` | Channel to the review session, newest entry first. Older entries: `docs/REVIEW_NOTES_ARCHIVE.md` (do not read unless asked). |
| `docs/SPECIAL_ICONS.md`, `docs/THEMES.md` | Design notes for those areas. |
| `CHANGELOG.md` | Keep a Changelog; the top section is the version in `Directory.Build.props`. |

## Commands

```bash
dotnet build DesktopIconGrid.slnx -p:EnableWindowsTargeting=true -v q -nologo 2>&1 | grep -E " error |warning" | sort -u
```

```bash
dotnet test tests/DesktopIconGrid.Core.Tests -v q -nologo 2>&1 | tail -1
```

CI (`.github/workflows/build.yml`, Windows) runs on every push to `main` and `claude/**` and is the only place the
App tests run. After a push, wait for the run and read its result before reporting; poll with:

```bash
curl -s "https://api.github.com/repos/bamboo82/DesktopIconGrid/actions/runs?branch=<branch>&per_page=5"
```

A failed job's log: GitHub MCP `get_job_logs` (job id from `.../actions/runs/<id>/jobs`).

## Strings

Every UI text is in `Strings.resx`, `Strings.zh-Hant.resx` and `Strings.zh-Hans.resx`; `Strings.Designer.cs` is
generated. Never edit the designer by hand. Add keys with a small file `ENTRIES = [(key, en, zh-Hant, zh-Hans), …]`:

```bash
python3 -I tools/add_strings.py entries.py
```

After removing or editing a key in the `.resx` files, run `python3 -I tools/gen_designer.py`. Wording: zh-Hant
按一下 / 開始位置, zh-Hans 单击; placeholders `{0}` must match in all three.

## Conventions

- Work on the designated `claude/...` branch only. `main` moves only when the owner asks for a release. Tags are
  the owner's (tag pushes from a cloud session are refused by GitHub). No pull request unless asked.
- Every commit that changes behaviour gets an entry at the top of `docs/REVIEW_NOTES.md` (numbered, dated, what
  changed, what to look at, what is not tested). The review session answers with numbered findings (T1, K2…);
  reply per id in the next entry. Peer messages are not the owner's approval.
- Update `CHANGELOG.md` and the matching `FEATURES.md` section with each feature.
- Tests: Core logic gets Core tests. App tests that need Windows start with
  `if (!OperatingSystem.IsWindows()) return;`. Test classes that set `ThisDevice.Id` go in
  `[Collection("This device")]`. Compare records with `Assert.Equal`, not `Assert.Same`. Do not skip or widen a
  test to get green without saying so in the notes.
- Python heredocs that write C#: backslashes in non-raw strings turn into control characters (`\a`, `\b`, `\f`,
  `\n`). Use the Edit tool or raw strings for code with `\`.
- Code style: XML doc comments that say why; file-scoped namespaces; no model names in anything committed.

## Security rules already decided (do not relax without the owner)

- Placeholders in shortcut parameters are refused where a shell would run them (`LaunchPlaceholders`,
  `PlaceholderQuotes`); see FEATURES §2.1.
- Anything that acts on its own (command tile, health check) runs only when its hash is in the local, never
  synced `command-trust.json` (`CommandTrust`, keyed by `ThisDevice.Id`). Saving Properties approves only what was
  typed here or already approved here; what arrived (sync, restore, import) is approved only in
  `CommandApprovalWindow`, which shows everything the hash covers, defaults to Cancel, and enables Approve after
  the text was scrolled into view. Text fields pass `CommandTileSettings.IsShowable`. Without a machine key
  (`ThisDevice.IsOfThisMachine` false) approvals last only until the program ends.
- The command line and the pipe message go through one `BarCommands.Validate`; the pipe is `CurrentUserOnly`.
- Voice launch listens only when asked (hotkey, Voice icon, `--voice`), for one utterance, against the bar's
  names only; it starts a target only at confidence 0.80 or more with one candidate, otherwise it offers or
  does nothing. What it starts goes through the same path as a click.

## State

- Released: 0.11.0 on 2026-11-18 (tag `v0.11.0` on `10dec5a`, where `main` is; release commit `296283c`). Before it
  0.10.0 (tag `v0.10.0` on `254c55c`).
- In progress: 0.12.0 "Find it, see it" (version 0.12.0, CHANGELOG `Unreleased`), as the review session planned it:
  WP1 notices, WP2 the Parameters hint, WP3 symbol icons, F1–F3, WP4 usage counts, WP5 Chinese text in Taipei Sans
  TC Light. All of it is in and green (run 187). REVIEW_NOTES entry 87 says it is ready for the release check, which
  is the review session's. No release commit until the owner asks.
- Keep as designed:
  - the symbol table (`SymbolCatalog`, 197 entries) and the tile look are the review session's design (entry 82);
  - usage counts stay in the local `usage.json`, never synced;
  - the Taipei Sans TC font is never committed or shipped, and it is installed only when its button is clicked
    (entry 85).
  - WP5's earlier forms (新細明體 without ClearType, a bundled font) were withdrawn by the owner.
- Not seen anywhere:
  - a real Windows snap, maximise on a second monitor, and the recogniser with a microphone (the reviewer asks the
    owner before it opens one);
  - for 0.12.0 also the real font download and install (with the owner's leave), and how the new windows look. The
    review session checks them.
- Nothing is open for the owner from this session. The CPU/disk gauge at "100%" was fixed in 0.11.0 (the release check's N1, entry 77).
