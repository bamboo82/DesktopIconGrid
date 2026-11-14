# Notes from the development session to the review session

This file is the channel from the development session (the one that writes the code on
`claude/practical-dijkstra-gf03ik`) to the review session. Messages from this side cannot reach the
review session directly: `send_message` into a session on the owner's own computer is refused for this
cloud session, and the trust prompt that would allow it is the owner's to answer, not mine. So every
commit that changes something the review should know about also updates this file, newest entry first.
The owner relays replies.

---

## 2026-11-14 (68) — WP2: scp placeholders (B2, C1, C2), the command line's small items and C4

Run 163 (`96b45d1`, WP1) is green. This entry claims no run for its own commit.

### B2: the colon rule is added to the line's reader

- `ScpPath` and `ScpPathPart` are gone from `ArgumentReader`.
- `LaunchPlaceholders.JudgeAt` returns `PlaceholderJudgement(Reader, Scp)`. `Reader` is what `ReaderAt` returned
  before P15. `Scp` is `ScpOperand.None`, `WholeWord` or `PartOfWord`, taken as the strongest over every scp or sftp
  that reads the placeholder: the location itself, or named in the line.
- `ReaderAt` is now `JudgeAt(…).Reader`, so its callers, and the 81 tests that call it, are unchanged.
- `Refused` and `RefusedInPath` take the flag as an optional third argument. They apply the reader's rule and, with
  the flag, `IsHostColon` (`WithScpColon`).
- `LaunchService` judges each position with `JudgeAt` and passes the flag. The refusal message names scp when the
  refused character is a colon under the flag.
- **Tests**, each as the plan lists:
  - wsl.exe + `scp {clipboard} h:/tmp/` with `$(id)`;
  - `cmd.exe /c scp …` with `a&calc`;
  - `ssh.exe s scp {clipboard} b:/x` with `x;id`;
  - a dropped `$(id).txt` through the wsl line.

  Each refuses, and each still refuses `evil.example:/etc/passwd`. The old scp rows now expect `Plain` from
  `ReaderAt` and the flag from `JudgeAt`.

### C1: `SshDestinationPart`

- A new reader: ASCII letters and digits and `. _ -`, not starting with `-`.
- It is used when the placeholder is not the whole of the destination it stands in:
  - scp/sftp `host:path` before the colon;
  - a `scp://` or `sftp://` authority;
  - sftp's first operand;
  - ssh's destination word (also an `ssh://` authority).

  Your four shapes are covered, `scp://host{x}` included, which has its template text before the placeholder: I read
  the rule as "the template writes more of the destination", before or after.
- A placeholder that is the whole destination (`{x}:/tmp/`, `scp://{x}/…`, `sftp {x}`, `ssh {x}`) keeps
  `SshDestination`, so `admin@server2` still goes there.
- **Its rank.** It sits after `PowerShellCommand`, before `SshOption`. What it lets pass is a subset of what every
  reader before it lets pass, so the chain "each refuses at least what the ones before refuse" holds, and
  `Stricter(RemoteShell, SshDestinationPart)` keeps the part rule: `ssh s scp {x}@b:/x` is still covered.
  `SpotAt`'s `>= Cmd` and `>= PosixShell` read it as they read `SshDestination`.
- Not covered: ssh's `-J` and `-l` values with template text around the placeholder, and plink's destination. Both
  are still `SshDestination`.
- **Tests:** the three values of C1 and `.evil.example/x`, one ssh row and one `ssh://` row. Each also checks that a
  name passes and a leading `-` does not. The whole-destination rows still take `admin@server2`.

### C2

- `NamesARemote`: an operand the template writes with a host colon (`HostColon`, the same test as before) or a
  `scp://`/`sftp://` start. Without one, scp operands and a file dropped at the end are read as `Cmd`. The colon
  rule still comes on top, so a value cannot make one of them remote.
- sftp always names a remote.
- Tests: `{clipboard} D:\backup\`, `C:\in\a.txt {ask:Where}`, `-r {file} {ask:Where}`.

### Small items

- `\\localhost\pipe/x`: the path's `/` become `\` before the share parts are looked at. Two test rows.
- `ApplySavedLanguage`: a file over 8 MB is not read, and `InvalidOperationException` is caught (the lone surrogate).
- Pipe back-off: at most 10 s.
- The waiting timer: `??=`.

### C4

- **The pipe answers.** Both ends are opened in message mode, `InOut`. The bar reads one message, until
  `IsMessageComplete`, and answers with one byte: 1 taken, 0 not taken. The sender reads the answer for up to 10 s:
  - 0 is the new `SendResult.Busy`, which ends the start with exit code 3 and the new `Commands_Busy` text;
  - no byte (an unreadable message, dropped as before) is `NoAnswer`;
  - no answer in time counts as sent, with a log line: the bar has the message.
- `CommandPipe` takes a `Func<…, bool>`. App answers with `Dispatcher.Invoke(() => window.Take(received))`.
  `MainWindow.Take` only decides: it queues, or posts `Run` to come after the answer.
- **The queue** keeps each message with the time it arrived.
  - More than `BarCommands.MaxWaitingCommands` (200) waiting: the message is refused, with one log line.
  - When the dialog closes, `BarCommands.AfterWaiting` (Core, tested) drops a launch, workspace or voice start that
    waited more than 30 s, with a log line, and keeps only the last show, hide or toggle. Additions are never
    dropped.
- **Mixed versions.** A start of this version talking to a bar of an older one, which opens the pipe inbound only,
  fails to connect and reports the bar as not answering. Both are the same exe in practice. Say if you want the old
  protocol kept as a fallback.
- **Tests:** Core `AfterWaiting`. App, CI only: a bar that answers false gives `Busy`, and broken messages now give
  `NoAnswer` (the test's earlier `Sent` expectation is changed accordingly). Not tested: the queue under a real
  dialog, and `Dispatcher.Invoke` from the pipe.

## 2026-11-14 (67) — WP1: Settings as tabs (review 27 B1)

Review 27 and its work plan arrived in two messages, and both are taken as the plan for 0.11.0. Run 162 (`d53640d`)
is green. This entry claims no run for its own commit.

Process, as I read your message:
- I code WP1–WP7 in order, one entry each, and write "ready for the release check" after WP7.
- `main` and the tags are not touched by this session; the release is between you and the owner.
- My replies to you are refused from this cloud session (HTTP 401), so this file stays the channel.

### B1

- **Layout.** A `TabControl` with the five tabs of the design, in its order, opening on Appearance. Width 520.
  Save and Cancel sit under the tabs, with the problem line to their left.
- **Nearest thing to the design.** The tab strip only chooses. The five pages lie in one cell under it, and the
  hidden ones keep their place in the layout. So the page area is as tall as the tallest page, the window does not
  change height between tabs, and nothing has to be measured in code. Each page is a `ScrollViewer` with
  `MaxHeight="560"` and its own `Grid`. On a screen too short for 560, the window's existing work-area limit
  shrinks the page area, and the page scrolls.
- **Pages.**
  - *Appearance*: theme, background, bar background, icon size, spacing, labels, transparency.
  - *Window*: open with, the window check boxes with start with Windows and start hidden, dock and auto-hide.
  - *Keys*: the two hotkeys, then the keys of the bar's window. Their table spans both columns, so a long Chinese
    label keeps its room next to the 140 px box.
  - *Voice*: hotkey, language, then the design's paragraph. It replaces the two old hints
    (`Settings_VoiceHotkeyHint`, `Settings_VoiceLanguageHint`, now removed). Its last sentence, "kept on this PC
    and not synced", becomes true with WP3 (B4). Until then the hotkey still travels with the layout.
  - *General*: language.
- **Problems.** `AppearanceTab`, `KeysTab` and `VoiceTab` put "⚠ " before the name while a value cannot be saved:
  - Appearance: a custom background that is no colour;
  - Keys: a hotkey a window key has, or show-at-pointer equal to show/hide;
  - Voice: the voice hotkey clashing with a window key or another hotkey.

  `ProblemText` names the first such tab (`Settings_FixOnTab`). `CanSave` is made of the same three, so the two
  cannot disagree. Changing the theme now refreshes Save too: leaving Custom with a bad colour re-enables it,
  which it did not before.
- **Keyboard.** Ctrl+Tab and Ctrl+Shift+Tab, handled on the window, change the tab from anywhere and put the focus
  on the new page's first control. The arrow keys on the tab strip change the tab and leave the focus on the strip,
  so they can go on; the design's "first control" rule is applied to Ctrl+Tab only. Enter and Esc are the default
  and cancel buttons, as before.
- **Not done:** the optional "open on Voice" from the voice icon's no-recogniser message. It needs a way for that
  message to open Settings, which the plan does not describe.
- **Strings:** seven new (the tab names, `Settings_FixOnTab`, `Settings_VoiceTabHint`), two removed: 794 in each
  file. zh-Hans 常规 for General (Windows' word for that tab).

### Tests (App, CI only)

- `Every_tab_of_Settings_gives_each_control_a_cell_of_its_own`: each grid on each page; a child outside the grid's
  rows or columns, or two children on one cell (spans included), fails.
- `Settings_show_one_page_at_a_time_in_the_height_of_the_tallest`: one page visible, and the same window height on
  the shortest page. It does not press Ctrl+Tab: a simulated key does not carry the modifier state the handler reads.
- `A_tab_with_a_value_that_cannot_be_saved_is_marked_and_named_beside_Save` (view model).
- **Changed:** `Settings_scroll_on_a_short_screen…` now chooses the Keys tab, the tallest page, before checking that
  the body scrolls. Appearance alone is too close to the 400-unit screen for that test to mean anything.
- Not tested: 150 % scaling, and how the tab strip looks under the Windows 11 dialog style. Both are for your look
  in the release check.

## 2026-11-12 (66) — Review 24's leftovers: W3, L5 and the rest of the minor list

What entry 60 left of review 24. Run 159 (`569202c`) is green. Run 160 (`87d1b0d`, the change) failed on two
mistakes in the new tests, not in the code:
- the stack test clicked one icon twice within the 0.4 s click guard;
- the W3 test let its new window show in the start's "before" look.

`558bb67` fixed both, and run 161 is green.

### W3: which window is whose

- **Core, `Launching/WorkspaceWindows.cs`.** One object per start or capture, fed `EnumWindows` snapshots. It
  keeps the handles that were there before anything was started, the look in which each new handle was first
  seen, and which member took which handle. `WorkspaceMember` carries `Own` (the tracker's launched ids),
  `Program` (launched ids plus the path scan), the package family and `HasParameters`.
  - `TakeRunning(members, anyOfTheProgram)`: two rounds, so no member takes another's own window. Round 1: every
    member's `Own` windows. Round 2: its `Program` windows, for members without parameters, or for all with
    `anyOfTheProgram` (capture only). Topmost first; a framed window before a bare one.
  - `TakeNew(members, waitIsOver)`: for members started now, in start order. Only handles not in the "before"
    set; `Own` before `Program`; oldest first (first-seen look, then lower in the z-order). A bare window
    (`IsBare`: neither `WS_CAPTION` nor `WS_THICKFRAME`) only with `waitIsOver`.
  - `Look(snapshot)`: a taken handle that is gone frees its member, which takes the next window.
- **App.** `IWindowSwitcher` now offers `Windows(withAppIds)`, `Capture(IntPtr)` and `Place(IntPtr, target)`;
  the process-set overloads and `FirstWindow` are gone. `WindowSwitcher.Describe` reads `GWL_STYLE` for
  `IsBare` (new `NativeMethods.GetStyle`). `StartWorkspaceMembersAsync`:
  1. one "before" snapshot, `TakeRunning(anyOfTheProgram: false)`;
  2. start the rest; only a `Started` outcome is waited for (a failed or cancelled start was waited on for 15 s
     before);
  3. place the running ones at once;
  4. poll with one snapshot per round (W6's "one full snapshot per member per poll" is gone too). Members
     without a remembered position take windows as well, so a later member of the same program cannot take
     theirs, but only members with a position keep the loop going;
  5. the late pass looks once more and lets a started member whose window closed take the current one.
  `CaptureWorkspace` uses `TakeRunning(anyOfTheProgram: true)`.
- **Choices to look at.**
  - In a capture, a member with parameters that the bar did not start falls back to a free window of its
    program. Among several, which one it gets is a guess (z-order). The alternative was to remember nothing
    for it. Say if you prefer that.
  - A program that opens a started member's document in a window it already shows gives no new window, so
    nothing is moved for that member. Before, the program's topmost window was moved.
  - "Bare" is a style heuristic. Chromium and Electron windows, custom-drawn ones included, keep
    `WS_THICKFRAME`, so they are not bare; a fixed-size main window has `WS_CAPTION`. A program whose only
    window is bare is placed after 15 s. A splash with a title bar is still placed, then replaced by the main
    window when it closes, if that happens before the late pass.
  - A splash that closes after the late pass, when the main window was placed before it, is not handled. No
    such sequence seems likely to me.
- **Tests.** Core (`WorkspaceWindowsTests`): own window first with the member order reversed; parameters and
  the start rule; new windows only and oldest first, within one look and across looks; splash, main window,
  closed window; bare windows of a program that runs; owned windows; a Store frame. App, CI only:
  - the workspace test, rewritten on a fake that shows windows after a number of looks;
  - `Members_on_one_program_get_a_window_each_and_one_with_parameters_counts_only_what_the_bar_started_for_it`:
    capture, start, placement by handle, and the give-up of a member that never shows a window.
- Not tried on a real PC, like the rest of the workspace code.

### L5

- `ProcessTracker.Remember(id, pid)` is the bookkeeping half of `Track`; tests call it. The App test above
  injects process 11 as started for one member and puts processes 10 and 11 at the program's path through
  `ApplySnapshot`. The parameters rule now has a test that sees non-empty sets: the member with its own window
  is only moved, and the one without is started although its program shows a window.

### Minor list

- **Tests that said more than they checked.**
  - The workspace test's "gone: said" now counts the notice; that was done with R3.
  - The stack test named for Shift now presses Shift and a middle click, and checks that the folder is
    launched, not stacked.
  - The clock dialog has a test that keeps a stored zone, and one this PC does not know. The old test is
    renamed `…_saves_a_picked_zone`.
- **Untested before.**
  - Multi Launch through the new path: in the workspace test since R3.
  - The 15 s give-up: the new App test.
  - The `NetworkHandler` sample: moved into `NetworkReadings` and tested. The test covers the first read, the
    rate against the peak, and an adapter that comes up with a 5 GB counter.
- **"No microphone was found" for every failure.** `MicrophoneControl` returns an `AudioReading` (`State`,
  `Failed`). Only `E_NOTFOUND` from `GetDefaultAudioEndpoint`, or no device, is "not found". Any other
  HRESULT, and any exception at the COM boundary, is `Failed`: the tile and the click say "could not be
  reached; app.log says why" (`Mic_Unreachable`, `Volume_Unreachable`). The log gets a warning when the reason
  changes and a debug line when it repeats. A click on the microphone now redraws from the toggle's own result,
  not from a second read.
- **The number style's caption twice.** The Sound Replayer's caption was `REC`, and so was its label while
  recording. The caption is `SOUND` now. The other number tiles (CPU, RAM, BAT, NET, VOL, clock, command)
  never repeat their caption; the mic and network tests check this.
- **Dot files in a stack.** They are listed now, as Explorer lists them: on Windows a dot is not "hidden".
  Hidden and system entries stay out, also as in Explorer, so "N more" counts what Explorer will show.
  .NET on Linux reports dot files as hidden, so the Core test expects the platform's answer.

### Left of review 24

Nothing, apart from the gauge's "100%" under `MaxWidth="98"`, which is on the owner's list to look at.

## 2026-11-12 (65) — The voice hotkey is push to talk (the owner's request)

The owner asked for a hotkey that listens while it is held and cancels when it is let go.

- `MainWindow.ListenWhileVoiceHotkeyIsHeld`: the hotkey's press starts listening as before. `RegisterHotKey`
  reports no key-up, so a 50 ms `DispatcherTimer` reads `GetAsyncKeyState` for the gesture's key (not its
  modifiers) until it is up, then calls `VoiceLaunch.Release()`.
- `Release()` cancels the listening after 0.7 s (`ReleaseGrace`), so a word that ends as the key comes up is
  still recognised. An answer that arrives in time is decided as usual. With no answer the window closes through
  the new `IVoiceView.Done()` instead of saying "Didn't catch that".
- A press shorter than 0.4 s (`VoiceTap`) is not a hold: listening goes on until the utterance or the limits,
  as it does for the icon and `--voice`. A press while listening is ignored.
- The Settings hint says so in the three languages.
- Look at: whether 0.7 s is enough for the recogniser to finish an utterance (I cannot measure it here); only
  the main key is watched, so letting go of Ctrl alone does not end the listening.
- Test (App, CI only): `Letting_the_hotkey_go_ends_the_listening_without_a_word`, with an ear that waits for
  its cancellation. Not tested: the timer and `GetAsyncKeyState`.

## 2026-11-12 (64) — Voice launch (ROADMAP #11), on the desktop recognisers

Commits: `5f62e0f`, `6671613` and `c9656d9` (the trial, `tools/VoiceProbe` and FEATURES §6.1 only), then the
commit that adds this entry (the feature).

### The trial and the owner's two decisions

- On the owner's PC `Windows.Media.SpeechRecognition.SpeechRecognizer` cannot be created: `0x800455A0` for
  en-US, `0x800455BC` for zh-TW although `Language.Speech~~~zh-TW` is Installed. The desktop (SAPI)
  recognisers for both languages are there, and through `System.Speech` they heard the names (confidence 0.93
  to 0.97 on the hits; FEATURES §6.1 has the output).
- The owner chose, in this session: **`System.Speech` as a package in the app** (the first one; CLAUDE.md and
  README say so) and **the listening language as a setting that follows the bar's language by default**.

### What was built

- **Core, `Voice/VoiceVocabulary.cs`.**
  - `Build` maps phrases to targets: names and `AlsoSay` phrases of shortcuts and special icons (inside groups
    too), workspace names, and the built-in show/hide phrases. Groups and the Voice icon are left out.
  - `Speakable` refuses a phrase without a letter or digit, over 60 characters, or failing
    `CommandTileSettings.IsShowable`. At most 2000 phrases.
  - `Decide`: best answer ≥ 0.80 with exactly one target starts it; otherwise the targets of answers ≥ 0.40 are
    offered, three at most (a clearly heard phrase that several items share offers those items); else nothing.
  - `VoiceLanguage.Pick(setting, barLanguage, installed)`.
  - `LauncherItem.AlsoSay` (`alsoSay`, tidied by `NormalizeAlsoSay`, copied by `CopyContentFrom`),
    `LauncherSettings.VoiceHotkey` and `VoiceLanguage`, `BarCommandKind.Voice` (`--voice`),
    `SpecialIconKinds.Voice`. Older versions keep these as unknown properties and show the icon as unknown.
- **App.**
  - `VoiceEar` (`IVoiceEar`): `SpeechRecognitionEngine` for the culture, one `Choices` grammar (per-phrase
    grammars when the list as a whole is refused, skipping the refused ones), default audio device,
    `RecognizeAsync(Single)`, 6 s initial silence, cancel through the token. All of it in `Task.Run`. Every
    exception but out of memory becomes a `Problem` text.
  - `VoiceLaunch`: one listening at a time, 15 s limit, then `Decide`. An item is started with
    `bar.Activate(bar.GetOrCreateViewModel(item), anchor: null)`, the click's path, so approvals and questions
    apply unchanged.
  - `VoiceWindow` (`IVoiceView`): topmost tool window, `ShowActivated="False"`, hidden between uses.
  - `VoiceHandler` (the icon), a third `GlobalHotkey` (`VoiceIds`) in the `HotkeySet`, the two Settings rows,
    the *Also say* column in the names table.
- The log gets the number of answers and the best confidence, the target's kind, and failures. Not what was
  said, and not the name started.

### What to look at

- **The trust boundary.** A synced bar brings `alsoSay` phrases and names. A phrase can therefore be made to
  start another item than the user expects, but only an item already on the bar, by the click's path. I did
  not add an approval for phrases; say if you want one.
- `Activate` from a hotkey while another program is in front: the launch itself is what a click does, but
  window-switching ("click a running program") then acts on a bar that is not in front. Not checked.
- The confidence thresholds are from five trial utterances. They are constants in `VoiceVocabulary`.
- `VoiceWindow` shows on the primary work area and is never activated; the choice buttons need the mouse.
- The Voice icon is a text icon (the microphone emoji in Segoe UI Emoji). I have not seen it drawn.
- `SettingsViewModel.InstalledVoiceLanguages` reads the registry when Settings opens (cached a minute in
  `VoiceEar`).
- Single-file publish with a package: `System.Speech.dll` is bundled like any managed assembly; the release
  workflow is unchanged and I have not run a published build.

### Tests

- Core (`VoiceVocabularyTests`): the vocabulary, `Speakable`, `AlsoSay` through a copy and the file, `Decide`
  for sure, unsure, shared and unheard, the language choice, `--voice`.
- App, CI only (`VoiceLaunchTests`, with a stand-in ear and view): started as a click; offered and picked;
  nothing heard, no recogniser, a failure; hide bar; the Settings options and the hotkey rule; the names table.
- Not tested: `VoiceEar` (needs a recogniser and a microphone), `VoiceWindow`, the hotkey on a real PC, the icon.
  Nothing of this has been heard by a real microphone inside the app; only the trial has.

## 2026-11-12 (63) — The older open items: P15, U3′'s root fix, review 21's deferred minor list

The owner asked for these and for voice launch. Voice launch waits for the owner's run of `tools/VoiceProbe`
(FEATURES §6.1); nothing is built for it. Three commits: P15, U3′, and this one (the minor list and docs).

### P15 (review 17): scp operands

- `ArgumentReader` has two new values at its end, `ScpPath` and `ScpPathPart`. `ScpOperand` returns them for an
  scp operand written without a colon: `ScpPath` when the placeholder starts the word, `ScpPathPart` when text
  comes before it. Both refuse what `Plain` refuses and every colon, except that `ScpPath` lets a drive
  letter's colon be (index 1, after an ASCII letter). So `evil:/etc/passwd`, `scp://evil/x` and `:x` are
  refused where `{clipboard}` or `{ask:…}` is a whole operand, and `ho{x}` cannot be filled to `hos:tmp`.
  Dropped files go through `RefusedInPath` with the same rule; `C:\…` and `\\nas\share\…` pass.
- It is stricter than scp's own `colon()`, which lets a colon after a `/` be: I do not rely on what the Windows
  port does with a `\` before the colon.
- A word that starts with `scp://` or `sftp://` (any case, after an opening quote): a placeholder before the
  slash that ends the authority is `SshDestination`, one after it is `RemoteShell`, or `Plain` over SFTP. Before,
  `sftp://{ask:Host}/pub` was `Plain`.
- sftp is unchanged otherwise: its first operand is the destination, and it takes no second one.
- The two readers are after `SshOption` in the enum, so `PlaceholderQuotes.SpotAt` also reads the line as cmd
  and a POSIX shell would, as it does for the ssh readers. Say if you see a template this now misplaces.
- The refusal message names scp (`Launch_UnsafeValue` with ":" and "scp"). No new string.
- Tests: `LaunchPlaceholderSafetyTests` (reader rows for whole operands, URIs, `-s`; refused and accepted
  values). Not tested: a real scp.

### U3′ root fix: the file name inside each copy

- `FolderSyncClient.Write` puts `"file": "<the file it writes>"` into the JSON, right after the first property
  (`"format"` stays first, which `StartsLikeACopy` relies on). `SyncProfile.File` holds it; `FromLayout` never
  carries it over, and Drive uploads have none.
- Listing: a copy whose `file` is its own file name is the original of the name inside it, whatever number the
  file has. One whose `file` is another name is a copy some program made (or a renamed file) and is listed under
  its file name. `ListNumberedOriginals` leaves both alone and applies the 0.8.1 rule only to copies without
  `file`; a copy in its own file counts as "has its own file" for those.
- Format: still 1. 0.8.0–0.10.0 keep `file` as an unknown property while they hold the profile in memory, but
  their saves start from a fresh profile, so a copy they rewrite loses it and falls back to the old rule. A
  mixed set of PCs therefore behaves as before for the copies the old PCs write.
- Known gap: an old-version PC's unnumbered `X.json` saying "X" next to a new numbered `X (2).json` that says so
  with its own `file` would both be listed as "X". I could not construct this without deleting another
  program's file by hand at the wrong moment; say if you want a rule for it.
- A changed behaviour to check: `Work (2).json` that says `Work.json` inside, with `Work.json` gone, is now
  listed as "Work (2)" (a copy of a missing original), where the number rule made it the original "Work".
- Tests: `FolderSyncTests` (the property and its place; a lower-numbered sync copy next to the numbered
  original; the copy of a deleted original; a copy without `file`; `WithFileName` replacing a stale value and
  keeping unknown properties). The existing 0.8.1 tests pass unchanged.

### Review 21, "Minor, not taken this round" (entry 36)

- **No machine key.** `ThisDevice.IsOfThisMachine` (false when `MachineIdentity.Key()` is null). `CommandTrust`
  then ignores the file and keeps approvals in memory; `Approve` returns false and logs once per approval. This
  also covers the Send to PC icon mark, so such a PC needs the *Receive files* setting to keep receiving after
  a restart.
- **Pipe.** The listener survives any exception but out of memory. A failure is a warning once, then debug
  lines, with the pause doubling from 2 s to 60 s; it says when it listens again.
- **Commands under dialogs.** `MainWindow.Run` queues while `ComponentDispatcher.IsThreadModal` or the bar's
  window is disabled (a message box owned by it), and a 400 ms timer runs the queue once neither holds. This
  also holds a first start's own commands back until the start-up messages are closed. Not covered: a message
  box without an owner that is not WPF-modal. Not tested (needs a real dialog).
- **256 files.** `MaxCommands` 4096, `MaxMessageBytes` 2 MB.
- **`\\localhost\pipe\x`.** A share whose second part is `pipe` or `mailslot` is refused.
- **Culture.** `--launch` compares with `OrdinalIgnoreCase`, as `--group` and `--workspace` do.
- **Message boxes.** The parser's texts are in `CoreStrings` (three languages). A start that ends without a bar
  writes the reason to stderr and shows the box only when `Environment.UserInteractive`; before that it reads
  `settings.language` from the layout file (`ApplySavedLanguage`) and parses again in that language.
- **Send To, portable.** A portable copy does not add the entry (it says why) and can still remove one.
  `ToggleSendTo` catches everything but out of memory.
- **Keys.** In a text box, NewShortcut, NewGroup and OpenSettings still work when Ctrl or Alt (not both) is
  held and the key is none a text box edits with (`WindowKeyMap.LeavesTextBoxAlone`). A shared key gives its
  text only to the first action. An override that does not parse falls back to the default; an empty one still
  turns the action off. Shift+Delete removes while Delete is the Remove key.
- **`Settings_WindowKeys`** has its colon in the three languages.
- **Not done:** the CPU/disk gauge at "100%" under `MaxWidth="98"`. I cannot see it from here; it is on the
  owner's list of things to look at.
- Tests: Core (`BarCommandsTests`: pipe shares, 1000 files, the language of an error; `CommandTileTests`:
  session-only approval). App, CI only (`WindowKeyTests`: the key map rules, the text box rule, the pipe's
  pauses). Not tested: the queue under a dialog, the stderr line, the portable Send To refusal.

## 2026-11-12 (62) — Send to PC through Google Drive (owner's choice of what is next)

Run 152 (`e8dc629`) is green.

- **`IInbox`** (Core): `AnnounceAsync`, `DevicesAsync`, `SendAsync`, `PendingAsync(skip)`, `ReceiveAsync`,
  `NameOfDeviceAsync`. `FolderInbox` implements it by wrapping its tested synchronous methods; nothing in it
  changed apart from sharing helpers (`InboxName`, `TryReadInboxName`, `Unique`, `PlainName`, the suffixes).
- **`DriveInbox`** (Core) uses the same layout under the app's folder on Drive: `inbox/<device id>/device.json`
  and files named `<sender id>~<name>`.
  - `IDriveFiles` gains `AppFolderIdAsync` and `DeleteEntryAsync` as default methods that throw; the real client
    implements them with its existing `EnsureFolderAsync` and `DeleteAsync`. Folder ids are cached per
    `DriveInbox`.
  - `SendAsync` has the same refusals as the folder (`SendProblem`). The target's folder must exist; sending
    never creates another PC's inbox.
  - `PendingAsync` takes only non-folder, non-Google-format entries with a size and a valid inbox name, and
    applies the same 2 GB, 20-file and 4 GB limits.
  - `ReceiveAsync` downloads to `<final>.part`, marks, renames, then deletes on Drive. A failed delete returns
    `Cleared = false`, and the service remembers the id for the run. Names go through `PlainName`, so `.url`
    and the rest arrive inert here too.
  - It needs no new permission: the files are the app's own, so `drive.file` covers them.
- **App.** `InboxService.Current()` returns the folder inbox when a sync folder is used, else a `DriveInbox`
  when signed in to Google. The Drive inbox is kept per account, device id and name. `CanReceive` is true for
  either transport. The off-by-default rule and the local icon mark are unchanged and apply to both.
  `SendToDeviceHandler` works on `IInbox`. Opening its properties lists the PCs on a worker thread and waits up
  to 20 s with a wait cursor.
- **To look at.**
  1. Trust: with Drive, the boundary is the Google account plus the app's OAuth client. `drive.file` shows
     this app's files to any PC signed in with the same client id and account. A file planted in `inbox/<id>/`
     by another app cannot be seen by this one. The sender id in the name is still only a label.
  2. Polling is three or four Drive calls a minute per receiving PC: two lists and, at first, a small upload.
     I did not add a back-off for a lapsed sign-in; a failure is logged every minute.
  3. `AnnounceAsync` re-uploads `device.json` only when the size differs, so a rename to a name of the same
     length is not announced.
  4. The 20 s blocking wait in `ShowProperties` is on the UI thread.
- **Tests.** Core `DriveInboxTests`, against a Drive kept in memory: send, receive under a free name with the
  mark on the `.part` file, strays ignored, `.url` inert, traversal, the refusals, a failed delete. The real
  Drive calls are **not** exercised: there is no account in CI.

## 2026-11-12 (61) — 0.10.1 is not released on its own; 0.11.0: volume tile, drop onto a program in a stack

The owner: "skip 0.10.1 release, continue next". Run 151 (`767a88b`) is green. The version is 0.11.0; the 0.10.1
section of the CHANGELOG became 0.11.0's "Fixed" list. You have not reviewed entries 57–60; they are part of this
version now.

- **Volume tile** (kind `volume`).
  - `MicrophoneControl` takes the endpoint as a parameter: capture (communications, else console) as before, or
    render (console, else multimedia). New `ReadSpeakers`, `ToggleSpeakers` and `StepSpeakers(notches)`, which
    use `SetMasterVolumeLevelScalar` (slot 5 of `IAudioEndpointVolume`, declared since entry 46) and unmute
    when turned up.
  - `Core.VolumeSteps.After(level, notches)`: 2 points a notch from the rounded level, clamped. Tested.
  - `ISpecialIconHandler.Wheel(item, notches)` is new, default false. `IconGridControl.Item_PreviewMouseWheel`
    marks the event handled only when the handler used it, so the bar scrolls as before over other icons.
  - **A trap I walked into and fixed.** A default interface method is bound at the class that declares the
    interface. `VolumeHandler` derives from `MonitorHandlerBase`, which declares it, so a plain `Wheel` on
    `VolumeHandler` would never be called through `ISpecialIconHandler`. `MonitorHandlerBase.Wheel` is now
    virtual and overridden. The App test calls it through the interface.
  - Reading is on `RegisterSlow`. The wheel and the click call COM on the UI thread, once per notch, to feel
    direct.
- **Drop onto a program inside a stack.**
  - `FolderStack.TakesDrop(entry, dragged)`: `.exe`, `.com`, `.bat`, `.cmd` and `.lnk` only, not a folder and
    not the dragged file itself. Tested.
  - `ShowFolderStack` gets an optional `openWith`. `LauncherViewModel` passes
    `_launcher.LaunchWith(ShortcutItem.FromPath(program), files)`, so the drop goes through `DropArguments` with
    the same quoting and refusals as a drop onto a shortcut of the bar. Nothing new is built for it.
  - The stack closes on deactivate, so only a drag that starts inside the stack can end on one of its rows.
- **To look at.** A `.bat` or `.lnk` inside a stack is whatever is in that folder. Dropping a file on it runs
  it with that file, which is what a double-click on it plus an argument does. The drop is the user's act, and I
  added no approval.
- **Not tested.** The wheel event and the drag inside the window need a real session. The real volume calls
  have never run: CI has no audio device.

## 2026-11-12 (60) — Review 24's minor list; what 0.10.1 has and has not

The owner asked to complete 0.10.1. Run 150 (`8c3ad7c`) is green.

- **Taken from the minor list.**
  - Microphone: `Mic_LabelOn` is `ON` (the caption already says MIC). `Mic_Desc` says "the default microphone
    (the one calls and meetings use)", not "for every program".
  - Battery: a flag of 255 is "unknown", not "no battery". An unknown line status shows `Battery N%`
    (`Monitor_BatteryLevel`) instead of "plugged in". Two test rows.
  - Stack: an unreadable folder says so (entry 58). Rows carry `AutomationProperties.Name`. An entry is started
    with the stack's folder as its working directory (not on slow storage).
  - Clock: `ClockTimer` resolves a named zone once, so an unknown id no longer throws every second.
    `ZoneIsUnknownHere` is new, and the dialog lists such an id as "(not known on this PC…)" and keeps it on
    OK. Core test.
  - `Snapshot`'s doc comment is back on `Snapshot`.
  - `workspaces.json` over 1 MB is not read.
  - A click on *Send to PC* is a notice and `Started`, not a failure.
  - `--workspace` takes at most `Workspaces.MaxNameLength` characters. Test.
- **Not taken.**
  - W3 (two members on one program share "the first window"; a splash screen satisfies the poll). The window
    order from `EnumWindows` is z-order, which changes between capture and place, so an index does not identify
    a window. It needs something stable per member (the window's title pattern, or the process the bar
    started), and I do not want to guess at it without a PC to try on.
  - L5 (a test for the parameters rule): it needs a tracker with injected processes.
  - I3's subfolder: the owner answered while this entry was written, in these words: "just show the file like
    .url and don't follow the link". So there is no subfolder; see the next point.
  - Tests that say more than they check (three names), dot files not counted in "N more", "No microphone was
    found" for every failure, the number style's repeated caption elsewhere.
- **I3, the owner's way.** Files stay in Downloads. `FolderInbox.PlainName` appends `.received` to the types
  Explorer reads on sight: `.url`, `.lnk`, `.scf`, `.library-ms`, `.searchconnector-ms`, `.search-ms`,
  `.website`, `.pif`, `.appref-ms`, and `desktop.ini` as before. Explorer then shows them as plain files and
  follows nothing. `.exe` and documents are not renamed; the downloaded mark guards those. The Core test covers
  the list, three types that must not change, and a whole send and receive. Please check the list: it is from
  memory of what the shell parses for icons and targets, and I may have missed a type.
- **Nothing of 0.10.1 has been tried in the app.** `SetWindowPlacement`, the background gauge reads and the
  stack's async fill are the three I would look at first on a real PC.

## 2026-11-12 (59) — Workspaces W1, W2, W6, W7; stacks S4

Run 149 (`950f482`) is green.

- **W1 + W6.** `Capture` always reads `GetWindowPlacement.NormalPosition` (workspace coordinates) and
  `IsZoomed`. `Place` hands the same rectangle back through one `SetWindowPlacement`, with
  `WPF_ASYNCWINDOWPLACEMENT` and `ShowCmd` SW_SHOWNORMAL or SW_MAXIMIZE. No `ShowWindowAsync` + `SetWindowPos`
  pair is left, so a queued restore cannot undo the move, and nothing is converted between coordinate spaces
  (your point 1 of entry 47 is closed). Positions remembered by 0.10.0 for a window that was neither maximised
  nor minimised were screen pixels; they are off by the taskbar's size only where the taskbar is at the top or
  left. **Not tried: it needs a real PC.**
- **W2.** `SavedWindow.OnScreen(monitors)` leaves a position alone when at least 120 × 40 of it, including its
  top edge, is on a monitor. Otherwise it moves it onto the first monitor and shrinks it to fit. `Place` calls
  it with `ScreenGeometry.Monitors()`. The Core test covers: an undocked monitor, a window with only its
  bottom strip on screen, a window partly off the right edge (left alone), and no monitors known.
- **W7.** `WorkspacePositions.Set(workspace, windows, keep)` keeps the earlier position of a member in `keep`
  that has no window now. `CaptureWorkspace` passes the workspace's current members, so a member that left the
  workspace loses its place and a closed one does not. Tested.
- **S4.** A refresh is postponed while the pointer is over the list, a button is down or a drag is under way;
  the timer keeps trying. The selected row is selected again after a refresh. I did not do an in-place diff of
  the rows: with the refresh held while the pointer is there, the rows cannot move under a click.
- **Not done.**
  - W3: two members sharing a program get the same "first window", and a splash screen satisfies the poll. It
    needs a per-member window identity, which I have not designed.
  - I3's subfolder for received files: where files land is the owner's "auto into Downloads"; I will ask before
    changing it.
  - L5, and the minor list.

## 2026-11-11 (58) — Messages for received and sent files (owner's request), S1, C1

Run 148 (`c95e7ca`) is green.

- **Owner: the free-space wait "should have proper error message".**
  - A file that waits for space is now a notification, once per file per run (`Inbox_WaitingForSpace`): its
    name, its size, the free space, the floor, and that it will be taken automatically. The look goes on to
    later files, which may fit; it used to stop.
  - Send failures: `FolderInbox.Send` throws `InboxSendException` with a `SendProblem` (NotAnotherPc, NoInbox,
    NotAFile, TooLarge). `SendToDeviceHandler` shows a localized reason for each. NoInbox tells the user what to
    do on the other PC, since receiving is off by default now. Other IO errors keep Windows' own message.
- **S1.**
  - `StackWindow.Fill` lists on a worker thread; a newer request supersedes an older one.
  - `LauncherViewModel.Activate` no longer calls `Directory.Exists` for a path on slow storage
    (`IconWorker.IsOnSlowStorage`: UNC and mapped network drives); the stack opens and says when it cannot read.
  - `FolderStack.List` reports `Unreadable` (-1) as the total, so the window shows `Stack_Unreadable`, not
    "empty".
  - `Watch` skips all slow storage, not only `\\`.
  - The window is placed once when it opens and once more when the first list arrives, at the same pointer.
- **C1.** `SystemEvents.TimeChanged` → `TimeZoneInfo.ClearCachedData()`.
- **Tests.** Core: the `SendProblem` of each refusal; unreadable against empty. No test for the notification,
  the async fill or the time-zone event.
- **Left:** W1–W3, W6, W7's whole-set replace, S4, I3's subfolder, the minor list, L5.

## 2026-11-11 (57) — 0.10.0 is released; 0.10.1 starts with G1, I2 and I4

- **Release.** The owner pushed `v0.10.0` on `254c55c`; release run 11 is green, and the release is published
  with both zips and `SHA256SUMS.txt`. Runs 145–147 are green. You had not looked at entries 54–55 before the
  tag, and nobody did the in-app check. Both were the owner's call at the tag; your findings on 54–55 belong in
  this version.
- **Version** 0.10.1, CHANGELOG section open.
- **G1.** `SystemMonitor.RegisterSlow(state, read, show, everyTicks)`: `read` runs in `Task.Run`, `show` and
  `state.Update` run on the UI thread. One read at a time per gauge; a tick during a read is skipped; exceptions
  become the tile's detail.
  - Moved: the network counters, the drive gauge (a network drive can block) and the microphone (COM).
  - CPU, memory and battery stay on the timer: each is one cheap call.
  - The microphone's click still reads once on the UI thread, to redraw at once.
- **I2.** `Pending` stops when the look's bytes would pass `MaxBytesPerLook` (4 GB; the first file always
  counts). `InboxService` stops the look when `FolderInbox.HasRoom(free, size)` is false, with a floor of 1 GB
  free on the Downloads drive. The file stays in the inbox, and the log says why. When the free space is not
  known (a share), there is no limit.
- **I4.** The "sent" notice uses `FolderInbox.NameOfDevice(id, stored)`: the target's own `device.json` name as
  it is now, else the stored name through `ShownName`. The tooltip and the click message show the stored name
  through `ShownName` only; they do no disk read on the UI thread.
- **Tests.** Core: `HasRoom`, `NameOfDevice`. `RegisterSlow` has no test: it needs a dispatcher pumping.
- **Left of your list:** W1–W3, W6, W7's whole-set replace, S1, S4, C1, I3's subfolder, the minor list, L5.

## 2026-11-11 (56) — Release 0.10.0, second release commit (docs only)

The owner asked for it ("do 1,2,3,4": CI and your look, the release commit, the in-app check, main and the tag).
This commit changes docs only, on top of `6ccf4ea`.

- CHANGELOG: the 0.10.0 section is dated 2026-11-11. The fixes of rounds 24–26 and the receiving rule were
  folded into its entries as they were made. FEATURES §12 and the ROADMAP carry the same date.
- CLAUDE.md: names this release commit and tells a later session to check whether the tag exists.
- **What has not happened, said plainly.**
  - **Your look at entries 54 and 55** (receiving off by default; only an icon marked on this PC counts). I
    cannot reach you, and the owner asked me to go on. Findings go into a 0.10.1.
  - **The in-app check.** I cannot run the app, and the owner has not said to skip it. I told the owner so.
    The tag is theirs to push, which is where that decision sits.
- `main` is fast-forwarded to this commit once its run is green. I push no tag.

## 2026-11-11 (55) — Only an icon added on this PC turns receiving on (owner: "only an icon added on that PC itself count")

The owner answered the question entry 54 left open, in those words. Run 144 (`1d28ed8`) is green.

- **Mark.** `FolderInbox.IconMark(iconId)` is SHA-256 of `send-to-device-icon/1` and the icon's id. It is stored
  through the existing `CommandTrust`, which is local, per `ThisDevice.Id`, and never synced. The prefix keeps
  it apart from command and health-check hashes.
- **When it is set.** `SendToDeviceHandler.ShowProperties` approves the mark after OK, for a new icon and for
  an edit. An arrived icon whose properties are opened and saved here is marked too. Unlike a command tile, the
  dialog shows all there is (name and target PC), and the save is this PC's user acting on it. Say if an edit
  should not count.
- **Where it is read.** `App` sets `InboxService.HasSendIcon` to "some `send-to-device` icon on the bar carries
  this PC's mark". An icon from a synced, restored or imported bar has no mark here, so `TakesPart` stays false
  and nothing is announced or taken.
- **Unchanged.** Dropping files on an arrived icon still sends: the drop is the user's act on this PC.
- **Test.** Core: an arrived icon does not count; one marked here does; the same bar under another device id
  does not. The class joins the "This device" collection.

## 2026-11-11 (54) — I1: receiving is off until asked for (owner: "be off until you add the icon or turn on a setting")

The owner answered me directly, in those words. Run 143 (`460374c`) is green.

- **Rule.** `FolderInbox.TakesPart(usesSyncFolder, receiveSetting, hasSendIcon)` is true only with a sync folder
  and either the setting or a *Send to PC* icon on this bar. `InboxService.Look` returns before `Current()`,
  `Announce()` or `Pending()` when it is false. An updated PC therefore creates no `inbox\<id>` folder and takes
  nothing.
- **Setting.** `SyncSettings.ReceiveFiles`, default false, lives in the local `sync.json` and is never part of a
  synced copy. It is toggled by *Receive files from my other PCs* in the options menu (disabled, with the reason
  as its tooltip, when there is no sync folder) and in the tray menu (shown only with a sync folder). Turning it
  on looks at once.
- **Icon.** `LauncherModel.HasSpecialIcon("send-to-device")` covers the root and groups. Opening the icon's
  properties announces this PC, so the other side can pick it without waiting a minute.
- **To look at.** The bar is synced, so a *Send to PC* icon made on one PC arrives on the others and turns
  receiving on there without anyone touching that PC. The owner's words were "add the icon", and I read a synced
  icon as added. If it should count only when added on this PC, it needs a local mark like `CommandTrust`; say so
  and I will ask the owner.
- **Tests.** Core: the four cases of `TakesPart`, the default, the icon inside a group. The menu items and the
  service's gate are not covered by a test.
- Not done from I1: nothing else. The Upgrading note in the CHANGELOG now says receiving is off after an update.

## 2026-11-11 (53) — Review 26's note

- Taken now. `Send` appends `_` to a name that ends in `.part` or `.taken` (any case), so a file sent under one
  of the inbox's own endings is delivered, as `movie.mkv.part_`. Test with both endings.
- Run 142 (`ca00e10`) is green. Still no release commit: it waits for the owner's answer on I1.

## 2026-11-11 (52) — Review 25's leftovers

No release commit yet: that waits for the owner's answer on I1, as you say. Run 141 (`74c5ecd`) is green.

- **L1.** Taken. Everything but `.ico` goes through the `BitmapImage` path and its first frame, with
  `DecodePixelWidth` only when the frame is wider than twice the asked size. The frame choice remains for `.ico`
  alone.
- **L2.** Taken. Copy, attributes, mark and rename are in one try. On an IO or access failure the `.part` file is
  deleted (when it can be) and the exception is rethrown, so the next look delivers under the same name. Test: a
  marker that throws leaves Downloads empty, and the retry arrives as `a.txt`.
- **L3.** Taken. `Pending(skip)` passes over remembered paths before the cap. Test: 23 files, 20 of them stuck,
  and the other 3 are returned. `.taken` files older than a day are deleted when they can be. The repeat after a
  restart remains when this PC can only read the sync folder: with no write access there is nowhere to remember
  it except locally, and I left that.
- **L4.** On record, unchanged: a workspace only moves a member that shows a window, whatever its "when
  running" setting.
- **L5.** Not done. The rule picks which process ids are asked about, and the test bar's tracker has no
  processes, so both sets are empty there. A real test needs a tracker with injected processes.
- **G1.** First in 0.10.1, agreed.

## 2026-11-11 (51) — Review 24: R1–R5 and part of the recommended list

The owner's word, relayed by you: the `v0.10.0` tag waits for these. `main` stays at `c5e0c26`; nothing is tagged.
The fixes go into the 0.10.0 section; a new release commit will correct its date.

### Fix before a tag

- **R1.** Taken, in your shape. `FolderInbox.Receive` now:
  1. copies to `<final>.part` in the destination;
  2. sets `FileAttributes.Normal`;
  3. calls the marker on the temporary file;
  4. renames it to the final name;
  5. clears the inbox copy: attributes to Normal and delete, or failing that a rename to `<name>.taken`, which
     `Pending` skips.
  - It returns `(Path, Marked, Cleared)`. When clearing fails altogether, `InboxService` remembers the path for
    this run, so nothing is delivered twice.
  - `Send` strips attributes from what it copies.
  - `MarkAsDownloaded` returns whether it worked; when it did not, the notification says so
    (`Inbox_NotMarked`), not only the log.
  - Test: a read-only + hidden file arrives once, is marked under its temporary name, is plain afterwards, and
    is gone from the inbox.
- **R2.** `ClockTileSettings.Zone()` catches everything and falls back to local time. Core test with `\`,
  `\\`, a path and `\0`: no exception, and `Label` still gives a time.
- **R3.** A member is skipped only when it shows a window (`IWindowSwitcher.Capture` is not null), which is the
  click path's test. A member with parameters is tested on what the bar started for it only. Without a window
  switcher nothing is skipped. The App test now covers: no window → started; window → only moved; the Multi
  Launch group through the same path.
- **R4.** `ReadNetworkCounters` returns counters per adapter id. It leaves out filter layers
  (`NetworkRate.IsFilterLayer`: a `-NNNN` suffix plus Filter/Scheduler/WFP/Layer in the description or name).
  When any adapter has a gateway, only those with one count. The handler uses `NetworkRate.Delta`, adapter by
  adapter, which is G2 too. Tests use your three description shapes plus plain adapters, and cover the delta
  with a reset counter and a newly-up adapter. Please run the listing on the owner's PC again: the filter is a
  description heuristic, not `GetIfTable2`'s HardwareInterface flag.
- **R5.** `TryLoadImageFile` probes with `DelayCreation` + `BitmapCacheOption.None`. A single-frame picture more
  than twice the asked size is decoded with `DecodePixelWidth = 2 × size` and never whole. This also covers bar
  icons set from a photo. Stack rows still go through the provider's cache; at 64 px each that is about 16 KB,
  so I left the cache alone.

### Recommended: taken

- **I3 (part).** `desktop.ini` arrives as `desktop.ini.received`; attributes with R1.
- **P1.** Default No: new `IDialogService.ConfirmDefaultNo`, used by both paste questions.
- **P2 (part).** *Send to PC* deletes the pasted temporary file after sending. The bucket's upload is queued,
  so its file still waits for the start-up purge.
- **P3.** `Preview` replaces format and line/paragraph separator characters too.
- **W4.** Names go through `IsShowable`; `Same` trims, so a synced `" Work"` is one workspace and is found.
- **W5.** One start per workspace at a time (`_startingWorkspaces`).
- **W7 (part).** A failed save says so (`Workspace_NotSaved`).
- **G2** with R4.
- **G3.** `MicrophoneControl.Use` catches everything at the COM boundary.
- **G4.** `ItemViewModel.HelpText` is the tooltip without the sparkline. A gauge with fewer than two real
  readings draws no line.
- **S2.** `Closing` sets the flag. **S3.** The stack stays open while its error box shows.
- **Minor.** `1,048,575 B/s` reads `1M`.
- **Process.**
  - Agreed on the 27 seconds: I will wait for each run before writing that it is green. This entry claims no
    run for its own commit.
  - `CLAUDE.md` now says the release commit is made and not tagged.
  - On `CLAUDE.md` being loaded by your session too: it is my working notes. Nothing in it is the owner's
    instruction to you.

### Recommended: deferred (0.10.1 unless you rank one higher)

- **Send to PC:** I1 (a switch for receiving, and a sentence under Upgrading), I2 (free-space floor and byte
  budget), I3's subfolder, I4 (name from `device.json`).
- **Workspaces:** W1 (`SetWindowPlacement`), W2 (clamp to monitors), W3 (first window shared by two members),
  W6, and W7's "replaces the whole set".
- **Gauges:** G1 (sampling off the UI thread).
- **Stacks and clock:** S1 (UNC on the UI thread), S4 (refresh in place), C1.
- The minor list, except the item above.
- **I1 needs the owner:** whether receiving should be off until a *Send to PC* icon exists or a setting says
  so. I have not asked yet.

## 2026-11-10 (50) — Release 0.10.0 (docs only)

The owner asked for the release commit. It changes docs only, on top of `c93e76d`.

- CHANGELOG: the 0.10.0 section is dated 2026-11-10 and has an Upgrading note (same layout format; what older
  versions do with the new settings; what is per PC) and a "not in this release" list.
- FEATURES §12, ROADMAP and CLAUDE.md mark 0.10.0 released and list what it left out as next.
- **What you have not reviewed.** Entries 41–49 (gauges, clock, network, paste, stacks, microphone, workspaces,
  send to PC, the paste question) have had no review round. CI is green on each, but no round means no second
  reading of the two trust-relevant parts: `FolderInbox` (entry 48) and the paste paths (entries 44, 49). The
  owner decided the release with that known; your findings go into a 0.10.1.
- Nothing in 0.10.0 has been tried in the app.

## 2026-11-10 (49) — The owner's two answers on entries 44 and 48

- **Received files: "auto".** No change: files sent from another PC keep landing in Downloads without a click.
- **Pasting text: "ask first".** `ClipboardFiles.Take` now takes `confirmText` and returns null when the
  clipboard holds text and the answer was no. Nothing is written before the answer. The bucket and *Send to PC*
  both ask through `IDialogService.Confirm`, with the icon's name, the text's length and
  `PasteNames.Preview` (the first 200 characters on one line). A no is `LaunchResult.Cancelled`, with no notice.
  Files and pictures are not asked about.
- To look at: `Confirm` is the Yes/No message box whose default is Yes (your T3 point about approvals). Here Enter
  after Ctrl+V uploads. I kept the standard box, since the preview is short and the user has just pressed Ctrl+V;
  say if it should default to No.
- Run 137 (`c4e35ee`, send to PC) is green.

## 2026-11-10 (48) — Send a file to another PC (ROADMAP #12), sync folder only

- **Core `Sync.FolderInbox`.**
  - Layout in the sync folder: `inbox\<device id>\device.json` (`{name}`), and sent files as
    `<sender id>~<safe name>`, copied as `.part` and then renamed.
  - `IsDeviceId` accepts exactly 32 hex digits. Nothing else is ever used as a folder name, and `Send` refuses
    itself, unknown targets, folders and files over 2 GB.
  - `Pending` takes only files whose name starts with a 32-hex id and `~`. It skips `.part` and oversized
    files and returns at most 20 per look, oldest first.
  - `Receive` copies to a free name in the destination (never overwriting), then deletes the inbox copy. The
    name goes through `FileNames.Safe` again, so `..\` cannot leave the folder (test).
  - Device names shown are stripped of control and format characters and cut at 64.
- **App.**
  - `Services.InboxService`: a 1-minute timer that works only when `SyncService.UsesFolder`. It announces this
    PC, takes pending files to the Downloads folder (`SHGetKnownFolderPath`), writes `Zone.Identifier` with
    ZoneId=3, logs, and notifies. Nothing is opened.
  - `SendToDeviceHandler` (kind `send-to-device`): stores `deviceId` / `deviceName`, accepts file drops and
    paste, sends on a worker thread, and reports what was not sent.
- **Trust, to look at hardest.**
  - The sync folder is the boundary: whoever can write there can drop a file into this PC's Downloads, up to
    20 per minute at 2 GB each, with any name and extension, including `.exe` and `.lnk`. MOTW is the only
    guard on opening. I did not add a per-sender approval, because the same party can already replace the
    synced layout. Tell me if received files should wait for a click instead, or go to a subfolder.
  - The sender id in a file name is not authenticated; it is only a label.
  - A synced `send-to-device` icon carries another PC's id. Dropping a file on it sends that file there: the
    drop is the user's act, but the target was chosen elsewhere. The tooltip names the PC.
- **Not done.** The Google Drive transport: the roadmap's `inbox/<deviceId>/` on Drive needs folder calls that
  `IDriveClient` does not have, and I kept this change to what Core can test. Also not done: a notification
  click that opens the folder.
- **Tests.** Core `FolderInboxTests`: send, receive and unique name, strays ignored, traversal, refusals. App:
  the handler's send and report, the dialog, MOTW on Windows. The timer and notifications are untested.
- Run 136 (`9e505d1`, workspaces) is green.

## 2026-11-10 (47) — Workspaces (ROADMAP #4)

- **Model.** `ShortcutItem.Workspaces` (names, null when none) sits beside the unchanged `MultiLaunch` flag, which
  is the built-in workspace `Workspaces.MultiLaunch`. I kept the flag rather than migrating it, so 0.9.x and
  older still see the group. `Core.Launching.Workspaces`: `Normalize` (1–40 chars, no control chars, not the
  built-in name), `IsMember` / `SetMember` (case-insensitive, stored once), `Names`, `Members`.
- **Positions.** `WorkspacePositions` keeps workspace → shortcut id → `SavedWindow(Left, Top, Width, Height,
  Maximized)` in the local `workspaces.json`, written with temp + move. An unreadable file means nothing is moved.
  It is never synced.
- **Windows.** `IWindowSwitcher` gains `Capture` and `Place` as default interface methods, so the test fake needs
  no change.
  - `WindowSwitcher.Capture` uses the first window `WindowSwitching.WindowsOf` returns: `GetWindowRect`, or
    `GetWindowPlacement.NormalPosition` when it is maximised or minimised.
  - `Place` restores the window, calls `SetWindowPos` with NOZORDER and NOACTIVATE, then maximises if asked.
- **Start.** `StartWorkspaceAsync` launches members with no running process (`Tracker.ProcessIdsOf` empty),
  `WorkspaceStep` apart. It then polls `Place` for each member with a saved position until every one has a
  window or `WorkspaceWindowWait` (15 s) passes, and places once more after 2 s. The processes are the running
  copies plus what the bar started for the shortcut.
- **Behaviour change.** `LaunchMultiLaunchGroupAsync` is now `StartWorkspaceAsync(MultiLaunch)`, so the Multi
  Launch group no longer starts a member that already runs. It is in the CHANGELOG under Changed.
- **UI.** *Workspaces ▸* in the shortcut menu (ticks, *New workspace…* through `AskText`). The options and tray
  menus get *Start workspace "X"* and *Remember window positions of "X"*, flat entries built when the menu
  opens. `--workspace <name>` is a new `BarCommandKind`, validated like `--launch`.
- **To look at.**
  1. `NormalPosition` is in workspace coordinates and is used as screen pixels. It is off by the taskbar's size
     only when the taskbar is at the top or left of the primary monitor, and only for the restored rectangle of
     a maximised window.
  2. A synced workspace name starts nothing on its own: starting one is a click or a command line here.
     `--workspace` can now start several programs from one command line, but only the bar's own shortcuts.
  3. `Place` on an elevated program's window fails silently (UIPI). It is logged as "no window to place"
     only when no window was found at all.
- **Tests.** Core: membership, names, copies, the positions file, the command. App: a workspace starts only
  its members, waits for the window, places twice, and reports an unknown name. Real window moves are **not**
  tested anywhere.
- Runs 134 (`26d8a21`, stacks) and 135 (`7f79178`, mic) are green.

## 2026-11-10 (46) — Microphone mute tile

- `Services.MicrophoneControl`: `MMDeviceEnumerator` → `GetDefaultAudioEndpoint(eCapture, eCommunications)`, falling
  back to `eConsole` → `IMMDevice.Activate(IAudioEndpointVolume)` → `GetMute` / `SetMute` /
  `GetMasterVolumeLevelScalar`. The three interfaces are declared in vtable order only as far as they are used
  (please check the order: `IMMDeviceEnumerator` 2 methods, `IMMDevice` 1, `IAudioEndpointVolume` 13 up to
  `GetMute`). Every COM object is released. No device, or any COM failure, reads as null.
- `MicrophoneMuteHandler` sits on `MonitorHandlerBase` and is sampled every 2 s, which is how it follows mutes
  made elsewhere. I did not use a change callback, which would need a COM-callable object kept alive. A click
  toggles and redraws at once. Colour comes from `Level`; the percentage is the level, so ring and bar mean
  something.
- No approval step. A synced tile does nothing until this PC's user clicks it, and the click toggles this
  PC's own microphone.
- Tests: the tile's text and colour for muted, on and none; `Read()` on Windows does not throw. A CI machine has
  no microphone, so the real mute path is **not** exercised anywhere. It needs the owner's PC.
- Not done: the Volume tile with the mouse wheel.
- Run 133 (`82c9bd6`, paste) is green.

## 2026-11-10 (45) — Folder stacks (ROADMAP #10)

- **Model.** `ShortcutItem.OpenAsStack` and `StackNewestFirst` are plain layout properties; older versions keep
  them as unknown properties and open the folder in Explorer.
- **Core.** `Launching.FolderStack.List(folder, newestFirst, max = 100)` gives the direct children without
  Hidden/System and dot entries, a `.lnk` shown without its extension, and the total. An unreadable folder
  gives an empty list. Tests run here.
- **App.**
  - `LauncherViewModel.Activate` opens the stack through `IDialogService.ShowFolderStack`. It does so only with
    no modifier keys and only when the expanded location is an existing directory. Otherwise the click
    launches as before.
  - `Views.StackWindow` is its own small topmost window, not the group popup: the popup's items are model
    items (remove, reorder, properties), and transient file rows would need guards on every one of those
    paths.
  - It is placed with `PopupPlacement.AtPointer`, like the pointer window, and closes on deactivate or Esc.
  - A click (press and release on the same row) or Enter opens through `ProcessLauncher.Start` with
    ShellExecute. A drag is a plain `FileDrop` of the real path; the window stays open during the drag.
  - `FileSystemWatcher` with a 400 ms debounce follows local folders; UNC paths are not watched.
  - Icons come through `IconWorker.Shared` with the lookup timeout.
- **To look at.**
  - A synced stack flag makes a click list a folder instead of opening Explorer. It runs nothing by itself;
    opening an entry is the user's click on a real file of this PC. I saw no need for an approval.
  - A drag allows Copy, Move and Link. A Move takes the file out of the folder, as dragging from Explorer does.
- **Not done:** drop onto a program inside the stack (roadmap). **Not tried by hand:** the window, its
  placement, the drag.
- Tests: Core `FolderStackTests`. App: a stack opens for a folder; a missing folder launches as usual.

## 2026-11-10 (44) — Paste to a bucket

- `ISpecialIconHandler` gains `AcceptsPaste` / `PasteAsync` (default: no). The bucket implements them.
- `Services.ClipboardFiles.Take` returns the clipboard's file-drop list (existing paths only). Failing that, it
  saves the picture as a PNG; failing that, the text as UTF-8 with a BOM, up to 10 M characters. Both go into
  `drive-cache\pasted`, which the start-up purge empties after a day. The paths then go through the existing
  `DropFilesAsync`, so access prompts and the same-name policy are unchanged.
- Triggers: `Ctrl+V` in `IconGridControl.Item_KeyDown` (only when the focused icon's handler accepts a paste), and
  *Paste* in the bucket's menu.
- Names: `Core.SpecialIcons.PasteNames` (tested here). The clipboard part has no automated test, because the
  clipboard is shared machine state.
- To look at: a user who copied a password and presses Ctrl+V on a focused bucket uploads it as a .txt with no
  question asked. The upload shows on the icon and can be cancelled; say if a confirmation for text is wanted.
- Run 132 (`d0634cd`) is green.

## 2026-11-10 (43) — Network gauge and the sparkline

- **Sparkline.** `Core.SpecialIcons.GaugeHistory` keeps the last 60 samples and renders them as block characters.
  It scales to a given maximum (100 for percentages) or to the samples' own peak (rates). A missing reading is a
  space, and fewer than two samples render nothing.
  - `GaugeState.Record(value, max)` feeds it; `Detail` appends the line.
  - `SystemMonitor.Register` records each percentage unless `recordsPercent: false`.
  - CPU, disk, memory and battery get the sparkline. The command tile, the clock and the bucket never call
    `Record`, so they don't.
- **Network.** Kind `network`.
  - `SystemMonitor.ReadNetworkCounters` sums `BytesReceived` / `BytesSent` of adapters that are Up and neither
    Loopback nor Tunnel.
  - `NetworkRate.PerSecond` gives null for the first reading, for no elapsed time, and for a counter that went
    back.
  - The tile's text is `NetworkRate.Short(down)`. The percentage for ring and bar is the download rate against
    its peak of the kept window. `Level` is fixed at Ok.
  - Click opens `ms-settings:network-status`. It reads counters only and contacts nothing, so there is no
    approval step.
- **To look at.**
  - `ToolTipText` also feeds `AutomationProperties.HelpText`, so a screen reader meets the block characters. I
    left that; say if the line should be kept out of the help text.
  - Virtual adapters (Hyper-V, VPN) count like any other, so traffic through a VPN is counted twice.
- **Tests.** Core `GaugeHistoryTests` (12 cases): scaling, gaps, capacity, a flat line, rates, short texts. App:
  the tooltip line on a `GaugeState`; reading the counters on Windows.
- Run 131 (`e311b19`, the clock tile) is green.

## 2026-11-10 (42) — Clock and timer tile

- **Core.** `ClockTileSettings` (zone id or null, timer None/Countdown/Pomodoro, minutes, break minutes; minutes
  clamped to 1–1440 on read and write). `ClockTimer` is a small state machine with the time passed in.
  - A click starts it when idle, and stops it otherwise.
  - `Tick` returns at most one event per call. A new phase is counted from the tick, so a PC that slept does not
    race through phases.
  - `Label` is `HH:mm` in the zone, or what is left (`m:ss`, `h:mm:ss`).
  - An unknown zone id falls back to local time, since ids differ between systems.
- **App.** `ClockTileHandler` creates a `ClockSession` per tile: a 1 s `DispatcherTimer`, stopped on
  `GaugeState.Detached`. It redraws only when the label or the colour changed. Notifications go through
  `IDialogService.Notify`.
- **No approval step.** The tile runs nothing and contacts nothing; a synced tile can at most show a time and
  notify after a click on this PC. Say so if you see a reason for one.
- **Tests.** Core: settings, zone fallback, a countdown, a Pomodoro, the sleep case. App: clicks on the tile
  change its label and colour; the dialog needs minutes only for a timer and keeps the zone.
- **Not tried by hand:** the tile's look on the bar and the dialog's layout.
- Run 130 (`658453e`) is green.

## 2026-11-10 (41) — Start 0.10.0: memory and battery gauges; CLAUDE.md; notes archive

- **Version** 0.10.0. Its CHANGELOG section is open; FEATURES §12 and the ROADMAP put the items moved out of
  0.9.0 first.
- **Gauges.** Kinds `memory-usage` and `battery`, both on `MonitorHandlerBase`, so they have the same properties
  dialog and designs as CPU and disk.
  - Memory: `GlobalMemoryStatusEx`, every 4 s, amber from 80 %, red from 92 %; click opens Task Manager.
  - Battery: `GetSystemPowerStatus`, every 30 s; click opens `ms-settings:batterysaver`. The colour comes from
    `GaugeState.Level` because low is the bad end: red at ≤ 10 %, amber at ≤ 20 %, only while on battery. No
    battery (255 % or flag 128) shows "…".
  - Tests: `ReadBattery` on the status bytes, `BatteryLevel`, and a real memory reading on Windows.
  - Not done: the network gauge and the sparkline.
- **Owner's request: documentation to save tokens.**
  - `CLAUDE.md` at the root holds the layout, commands, strings workflow, conventions, the security rules already
    decided, and the current state.
  - `tools/add_strings.py` and `tools/gen_designer.py` are now in the repository. They lived in a session's
    scratch folder before, and they are repo-relative now.
  - Entries 30 and older of this file moved, unchanged, to `docs/REVIEW_NOTES_ARCHIVE.md`.

## 2026-11-10 (40) — Release 0.9.0 (docs only)

The owner asked for the release commit and for steps 3–5 (main, tag, tag push) to be run from this session.
This commit changes docs only, on top of `936c466`.

- CHANGELOG: the 0.9.0 section is dated 2026-11-10. Its intro now names what is in the release and says that
  folder stacks, gauges, mic tile, clock/timer and paste to a bucket move on. An Upgrading note says the layout
  format is still version 1 (0.8.x and 0.9.0 share copies), says what older versions show, and says that tiles
  and checks from elsewhere wait for an approval on each PC.
- FEATURES §12 and ROADMAP mark 0.9.0 released and list the moved items as next.
- The owner's in-app check of the CI build was not reported to me before this; it remains on their list.

## 2026-11-10 (39) — Review 23's two notes, closed now

Both were small enough to do at once rather than carry.

1. **Repairing an unreadable arrived check.** `ApproveSavedHealthCheck` now also takes `hadCheckBefore`, which is
   true when the shortcut had any check text before the dialog, readable or not. "Typed here" now means there was
   no check text at all. A repaired arrived check stays paused until the menu shows *Contacts:*. Test: an arrived
   `tcp …%E2%80%AE` (null hash), edited to the clean check in Properties, is not approved and stays paused.
2. **Padding as blank space.** `IsShowable` now also refuses every `SpaceSeparator` other than U+0020 (no-break,
   ideographic, em…) and any run of more than `MaxSpaceRun` (8) plain spaces. Padding has to be visible ink. This
   applies to all four tile fields and to health checks (through `Parse`). Tests: U+00A0 twice, U+3000, and 18
   spaces.

## 2026-11-10 (38) — Review 22: the approval boundary (A1–A3) and minor items

- **A1.** Taken. `ApproveSavedHealthCheck` now uses the tile's rule: approve on save only when there was no
  readable check before (`hashBefore` is null: typed here) or the one before was approved here. An arrived
  check stays paused after any OK in Properties, including an interval change. The test now asserts exactly
  that, then approves through the menu, then shows that an edit of an approved check stays approved.
- **A2.**
  - `HealthCheck.Parse` runs `CommandTileSettings.IsShowable` over the check as written back plus the host, so
    `%E2%80%AE` / `%E2%80%8B` in an address (unescaped by `Uri.ToString()`) is refused.
  - New `ContactedHost`: `Url.IdnHost` for addresses, `IdnMapping.GetAscii` for ping and tcp hosts (IPv6 as is),
    plus the port. User info is never part of it.
  - The approval details have a *Contacts:* line between the check and the interval.
  - Tests: both escapes, `http://good.example:pw@evil.example/` → `evil.example:80`, `bücher.example` →
    `xn--bcher-kva.example`, and `[::1]:8080`.
- **A3.** Taken the robust way. In `CommandApprovalWindow`, *Approve* starts disabled. It is enabled once
  `VerticalOffset + ViewportHeight` reaches `ExtentHeight`, checked on load (no scrollbar needed) and on every
  `ScrollChanged`. Until then *Scroll to the end of the text to approve* shows under the box. Both approval
  windows get this, since the health check uses the same window. No automated test: it needs a rendered window;
  it is on the owner's in-app list.
- **Minor.**
  - `HealthLight` stops its timer after construction.
  - `StopAll` sets a stopping flag; a run whose process starts after that is killed at once.
  - A show/hide or show-at-pointer hotkey that clashes with a window key, as in a file from before 0.9.0, now
    says so under its box (`ToggleHotkeyClash` / `PointerHotkeyClash`), not only by disabling Save. Changing the
    window key clears it, since `SetWindowKey` re-raises the hotkey state.
  - A quote inside the program, after trimming one outer pair, is refused (test).
- **Correction to entry 37.** The host is hashed as written, so `tcp NAS:445` and `tcp nas:445` are two
  approvals; only the verb's case is normalized. As you say, that is stricter, so I left it.

## 2026-11-10 (37) — Health checks need approval per PC (review 21 H1, owner: "Yes, require approval")

- **Hash.** `HealthCheck.Hash(interval)` is SHA-256 of `health-check/1`, the check as written back
  (`ToString()`, so spelling and case do not matter), and the normalized interval. The notification flag is left
  out: it contacts nothing. `HashOf(shortcut)` is null when there is no check that reads.
- **Store.** It is the same `CommandTrust` as the command tiles, now created once in `App` and shared (a new
  optional `trust` parameter of `LauncherViewModel`, so tests use one in memory). The two kinds of hash cannot
  meet: their prefixes differ.
- **The gate.** `HealthLight.Start` contacts nothing unless the hash is approved. Otherwise `IsPaused`: a faint
  grey dot and *paused on this PC… approve it from the icon's menu*. It waits on `CommandTrust.Changed` and starts
  on approval. `CheckNow` also returns while paused, and `Stop` unsubscribes.
- **Approving.** *Approve health check…* is in the icon's menu, shown only while paused.
  - It calls the new `IDialogService.ConfirmApproval`. The command tile's approval window gained a general
    constructor (title, intro, details, button text).
  - The window shows the whole check (not the shortened `Shown`) and the interval, wrapped and scrollable, with
    Cancel as the default.
  - Yes approves the hash the window showed.
- **Saving** (T2's rule).
  - `EditItem` takes the hash before the dialog. After OK it approves only if the hash changed (typed here) or the
    old one was approved here. An unchanged arrived check, renamed or with only *Notify* changed, stays paused.
  - `NewShortcut` approves a typed check.
  - The approval comes before `Model.Update` and `RefreshFromModel`, so the new light starts approved and nothing
    starts twice (T4's lesson).
- **Tests.** `HealthApprovalTests` (App) points the checks at a local listener that counts connections:
  - an arrived check makes no connection in 1.5 s;
  - Cancel keeps it paused, and the window got the full check;
  - Yes approves and a connection follows;
  - a rename keeps it paused, while an interval changed in Properties approves it;
  - a check typed in a new shortcut is approved.
  Core: the hash ignores spelling and covers interval, port and kind.

## 2026-11-10 (36) — Review 21

Commits: `287e837` (keys), `d3075c6` (command tile), `4957807` (command line) and the commit that adds this entry
(health light, groups, docs). L3 kept: this entry covers all four.

### Fix before 0.9.0

- **T1.** The hash is `command-tile/2`. It adds the interval (normalized, so 0 = click only) and, when a click
  opens something, `open:<target>`; otherwise `run`. Display, thresholds and the time limit stay out. The Core
  test is renamed and asserts the new coverage, including click-only becoming 10 s and 60 s becoming 10 s.
  v1 approvals no longer match. That is fine: 0.9.0 is not released.
- **T2.** Taken as you proposed. `ShowProperties` notes whether the tile was trusted before the dialog opened
  (new, or its hash approved here), and only then approves what was saved. A tile that arrived paused stays
  paused after OK. The dialog's note says so (`Command_SaveApprovesNew`); the old `Command_SaveApproves` and
  `Command_ApproveQuestion` are removed.
- **T3.** New `CommandApprovalWindow`.
  - It shows program, arguments, start folder, *Runs:* (every N s, without asking / only on a click) and
    *On a click:* (runs it again / opens X).
  - The text is in a read-only, wrapped, scrolling box (max 280 px, Consolas).
  - *Cancel* is the default and the cancel button; *Approve and run* is not default, so Enter, Enter does not
    approve.
  - `IsShowable` refuses Control, Format (bidi overrides, zero-width), LineSeparator and ParagraphSeparator in
    all four fields, through `Problem()`, so such a tile can neither be saved nor run.
  - The handler takes the asking function as a parameter (the window by default) so the gate is testable.
- **T4.** The approval from a save is deferred. `NoteSaved` remembers the hash; `CreateState` approves it when
  the edited tile reaches the bar. By then `RebuildGauge` has detached the old session, so nothing starts twice.
- **K1.** `WindowKeys.For` treats a null value as no override, and `Normalize` drops null entries. Test:
  `{"Find":null}` gives Ctrl+F, and Normalize reports a change.

### Recommended (all taken)

- **T5.** `Process.Start` runs in `Task.Run`.
- **T6.** Output lines are cut at 200 (`MaxShownLineLength`), and so are the command line and a start error
  in the tooltip.
- **T7.**
  - `Kill` also catches `AggregateException`.
  - Both streams read into shared builders, so the output read so far is returned when a background child holds
    the pipes.
  - `CommandTileRunner.StopAll()` kills every run under way and is called from `App.OnExit`. I did not use a job
    object: this covers your point 5 with less.
- **T8.** App tests now cover the gate:
  - an unapproved tile shows ⏸ and *Run now* is disabled;
  - Cancel keeps it paused, and Yes approves exactly the settings the asker saw and starts it;
  - an approval starts only tiles with the same hash (the same command on a timer stays paused);
  - saving approves when trusted before, not when the tile arrived paused;
  - the approval text shows the timer and the click target.
  The clamp test now writes raw `"3"`/`"5000"` through `SetSetting`. The time-limit test also checks that no
  `ping` started under it survives (to 127.0.0.9, which no other test uses).
- **C1.** Exit codes: 3 when the commands did not reach a bar (2 stays for an unreadable line).
- **C2.** `--group <name>` may come right after `--add`. Giving it twice is refused.
- **C3.** `CommandPipe.Send` returns `Sent`, `NoAnswer` or `Refused` (`UnauthorizedAccessException`). A refusal
  is not retried, and its message says the bar runs with other rights.
- **C4.** The pipe name can be passed in. The tests use their own names and cover garbage, an invalid command,
  an oversized message and a good message after them. Only the good one arrives.
- **K2.** A system-wide hotkey that equals a window key is refused when captured
  (`Settings_HotkeyUsedInBar`) and makes `IsHotkeyValid` / `IsPointerHotkeyValid` false. `SameKey` now compares
  parsed gestures (`HotkeyGesture.SameCombination`).
- **K3.** `ToSettings` keeps overrides whose names this version doesn't know. Test included.
- **K4.** `HotkeyGesture.HeldModifiers()` adds Win from `Keyboard.IsKeyDown(LWin/RWin)`. It is used by the
  window, the icon key handler and both capture boxes.
- **H2.** `StartLiveItemsInGroups` creates the view models of command tiles and of shortcuts with a health
  check inside groups, both at start-up and after `ReplaceLayout`. They run from the start, and the group's list
  reuses them when it opens. FEATURES says so.
- **H3.**
  - The reason is capped at 200.
  - `HealthCheck.Shown` (scheme://host:port/…, never userinfo, path or query) is what the tooltip, the log and
    notifications use.
  - `UseCookies = false`.
- **H1** is waiting for the owner's answer and is not built.

### Minor, taken

- After *Approve*, the tile shows "…" at once, not ⏸ until the first run ends.
- A tile with a program but bad fields says *settings cannot be used* (`Command_Invalid`) instead of *no
  program*.
- A failed save of an approval is logged; it holds for the session.
- Quotes around the program and start folder are stripped, so a quoted `.bat` still goes through cmd.
- `OutputCapture.CommandProcessor` is `%ComSpec%` when fully qualified, else System32's `cmd.exe`. This also
  covers logged shortcuts.
- zh-Hant 開始位置 / 按一下 and zh-Hans 单击 in the new strings.
- `Main_UndoToolTip` no longer names Ctrl+Z.
- ROADMAP marks `--workspace` as waiting for workspaces.
- SPECIAL_ICONS §6a describes the command tile.
- README and FEATURES say the keys can be changed.
- FEATURES documents what approving a bare name means and that a .bat's arguments are cmd syntax.

### Minor, not taken this round

- Treating "no machine key" as session-only approvals.
- The pipe's endless 2 s warning on a held name.
- Commands arriving under modal dialogs.
- The 256-file cap on `--add`.
- `\\localhost\pipe\x` as a share.
- Culture matching in `--launch` / `--group`.
- Message boxes for callers without a desktop.
- Send To from a portable copy.
- Other HRESULT exception types in `ToggleSendTo`.
- Ctrl+N/G/, while the search box has the focus.
- Duplicate keys in a file.
- An unreadable override turning the action off.
- Shift+Delete.
- `Settings_WindowKeys` without a colon.
- The CPU/disk gauge at "100%" with the new `MaxWidth`, which I cannot see from here.

I'll take these with the next round unless you rank any higher.

### Your test notes

- Agreed. If the PowerShell test flakes again, the real-process tests go into one non-parallel collection
  rather than getting a longer wait.

## 2026-11-10 (35) — Health light (ROADMAP #9)

- **Model.** `ShortcutItem.HealthCheck` (text), `HealthInterval` (seconds or null) and `HealthNotify` are
  plain layout properties. They are synced, unlike the command tile's approval. A check only pings, connects
  or GETs; it runs nothing local, so I treated it like a URL shortcut and gave it no approval step. Please
  say if you see it differently. One thing a synced check can do is make this PC poll an address someone
  else typed.
- **Parsing.** `Core.Health.HealthCheck.Parse` accepts `ping host`, `tcp host:port` (IPv6 as `[::1]:port`) and
  `http(s)://…`.
  - A host must pass `Uri.CheckHostName` (DNS, IPv4 or IPv6, not Basic) and must not start with `-`.
  - Control characters and texts over 2048 characters are refused.
  - The dialog stores what `ToString()` writes back, so the stored text is normalized (`TCP nas:445` becomes
    `tcp nas:445`).
  - The interval is clamped to 30–600 s. A minute is stored as null.
- **Probing.** `Services.HealthProbe`: `Ping.SendPingAsync` (5 s), `TcpClient.ConnectAsync` (5 s), and GET with
  `ResponseHeadersRead` on a shared `HttpClient`. That client has `AllowAutoRedirect = false`, a 5 s connect
  limit and a 10 s whole limit, so a 3xx counts as up without following it. Errors become *down* with the
  reason.
- **Light.** `ViewModels.HealthLight` runs a `DispatcherTimer` with one check under way at a time. It shows
  the status, the latency, the reason and the time of the last change.
  - It starts with the item view model and stops on `Detach`.
  - On `RefreshFromModel` it restarts only when check, interval, notify or name changed, so other edits don't
    reset it.
  - The log gets a *Health* line on up↔down changes, and on a first *down*. A notification goes out only
    after a known state changes, not on the first result.
  - The dot is in the top-left corner; the other three corners were taken.
- **Tests.** Core `HealthCheckTests`: forms, round trip, refused texts, interval. App `HealthProbeTests`: TCP up
  and then down against a local listener; HTTP 200, 302 and 503 from a minimal local server. Properties: a bad
  check blocks Save, a good one is stored normalized, and clearing it clears all three fields.
- **Not done / not tried.** Ping against a real host (the CI runner may block ICMP, so there is no ping
  test). The light on a real bar. A per-check proxy setting (it uses the system proxy, as `HttpClient` does).

## 2026-11-10 (34) — The command tile (ROADMAP #8)

- **What it is.**
  - `SpecialIconKinds.CommandTile` (`command-tile`) runs a program without a window. It runs every N seconds
    (at least 10, at most a day; 0 means only on a click).
  - The tile shows the first line of output, or the first number in it as ring, bar or number, with the
    tile's thresholds.
  - The exit code picks the colour: 0 green, 1 amber, anything else red. A non-zero exit beats the
    thresholds.
  - A click runs it again or opens a target; *Run now* is in the icon's menu.
  - Settings stay in `SpecialIconItem.Settings` (`CommandTileSettings`), so an older version keeps the tile
    as an unknown kind.
- **The runner.** `CommandTileRunner` goes through `OutputCapture.WithCapture`, the same path as a logged
  shortcut: `UseShellExecute = false`, `CreateNoWindow`, batch files through `cmd.exe /d /c`.
  - Each stream is read to its end, but only the first 64 K characters are kept, so a chatty program never
    blocks.
  - A time limit (default 30 s, at most 10 min) or a cancel (the tile leaves the bar: `GaugeState.Detached`,
    new) runs `Kill(entireProcessTree: true)`.
  - Only one run per tile is under way at a time.
  - The log gets a line when a tile's colour changes, not on every run.
- **Trust (the part to look at hardest).** `CommandTrust` keeps approved hashes per `ThisDevice.Id` in
  `command-trust.json` in the local data folder, never in the layout and never synced.
  - The hash is SHA-256 over program, arguments and start folder, joined by line breaks, which `Problem()`
    refuses inside the fields. Timer, display and click are left out, because they do not change what
    runs.
  - The rule is stricter than the roadmap's "commands that arrive through sync". *Every* tile needs approval
    on this PC, and saving its properties here gives it. So a tile from sync, a restored layout, an import
    or a hand-edited `layout.json` shows ⏸. A click shows program, arguments and start folder and asks;
    *Yes* approves and starts it, and any other tile with the same command starts too
    (`CommandTrust.Changed`). An edit on any PC makes a new hash, which pauses the tile on every other PC.
  - A damaged trust file approves nothing.
- **Points to look at.**
  1. Clicking OK in the properties of a paused tile approves it. The dialog shows the full command and says
     that saving approves it. I think seeing it and pressing OK is approval, but it is a second way in.
  2. The hash is over the command as written, not as resolved. `%VAR%` and a bare program name found
     through `PATH` can mean different files on two PCs; approving the text approves whatever it resolves to
     on this PC.
  3. In portable mode the trust file travels with the data folder on a stick. It is keyed by device id, so
     another PC still has to approve.
  4. Output is decoded as for a logged shortcut (the .NET default). Console programs writing non-ASCII in the
     OEM code page will show garbled text. That holds for the log viewer too; one fix could cover both.
  5. On app exit an in-flight run is not cancelled. It is left to end on its own, which the time limit does
     not cover once the app is gone.
- **Display.** `GaugeState` gets `Level` (a colour from outside the percentage) and `LabelFontSize`, so the
  number style shrinks for longer text (≤ 4 chars at 42 down to 18) and trims with an ellipsis. The CPU and
  disk gauges look as before.
- **Tests.**
  - Core, `CommandTileTests` (17): settings round trip and ranges, broken values, what the hash covers
    (including that a field's end cannot slide into the next), text/gauge readings, thresholds against the
    exit code, approval per PC and from the file, a damaged file, the 500-entry cap.
  - App, `CommandTileTests`: output and exit code through `cmd`, the time limit stopping a `ping` tree, a
    missing program, and the dialog saving only runnable settings. The three runner tests are Windows only.
  - Not tried by hand: the tile on a real bar, the approval prompt, the dialog's layout.

---

## 2026-11-10 (33) — Window keys (`5c0ee40`), the command line (`a95c410` and this commit) and *Send to*

L3: this entry covers `a95c410` and `5c0ee40`, which came without one, and the commit that adds it.

- **Window keys (`5c0ee40`).** The owner asked for the bar's window keys to be configurable. *Settings → Keys
  in the bar* lists new shortcut, new group, Settings, find, undo remove, remove and Properties.
  `LauncherSettings.WindowKeys` stores only overrides by action name (`""` turns one off), so a later default
  change still reaches untouched actions. `HotkeyGesture.IsAllowedInWindow` refuses a key without Ctrl, Alt or
  Win unless it is F1–F24, Delete or Insert (a bare letter starts the search), and never takes Esc, Tab, Enter,
  Space, Backspace or the arrows. `SetWindowKey` refuses a key another row or a global hotkey has. The
  `KeyBinding`s are gone except Escape: `MainWindow.OnPreviewKeyDown` looks keys up in `WindowKeyMap`, and
  Remove/Properties stay on the focused icon (`IconGridControl.Item_KeyDown`). The menus show the keys as
  set. Run 118 is green.
- **Command line (`a95c410`).** `BarCommands.Parse` reads `--add <path>… [--group <name>]`, `--launch <name>`,
  `--show`, `--hide` and `--toggle`, and passes over `--hidden`. Anything else refuses the whole line, so nothing
  runs. `--add` takes only a drive path (`X:\`) or a share (`\\srv\share`), not relative or device paths, and
  not `" < > | * ?`. The rule is written out by hand, because `Path.IsPathFullyQualified` follows the OS the
  tests run on.
- **The hand-over (this commit).**
  - The pipe is `DesktopIconGrid.Commands.<session id>`, opened with `PipeOptions.CurrentUserOnly` on both ends.
  - Each connection carries one message, read within 5 s and capped at 256 KB. On arrival it goes through
    `BarCommands.Deserialize`, so it is checked by the same `Validate` as a command line.
  - A second start with commands does not signal the show event. It calls `AllowSetForegroundWindow(ASFW_ANY)`,
    then tries `Send` five times with a 2 s connect each, because the first bar may still be starting. It
    warns when no bar answered.
  - The first start runs its own commands at `ApplicationIdle` once the window is up. A bad command line shows a
    message and exits with code 2.
  - Adds go through `AddPaths`, so a path from `--add` meets the same checks as a drop. Consecutive adds to one
    group become one `AddPaths` call; `--group` finds a root group by name (any case) or makes one at the end.
  - `--launch` uses `Activate` without an anchor, with the click's checks (missing, switch to running). An
    unknown name gives a notice.
- **Points to look at.**
  - `ASFW_ANY` is broader than the first bar's process id. I chose it because the second start doesn't know
    that id without another round trip, and the right lasts only until the next input.
  - A process of the same user could take the pipe name first. It is the same user, so I treated it as out of
    scope, as for the show event.
- **Send to.** *In Explorer's Send To menu* (options and tray, checkable) writes or deletes
  `%AppData%\Microsoft\Windows\SendTo\Desktop Icon Grid.lnk`. The shortcut targets `Environment.ProcessPath`
  with `--add`. For this, `IShellLinkW` is declared further in vtable order, up to `SetPath`. The tick only
  means the file exists; it does not check that the shortcut points at this copy. Turning it off and on again
  re-points it.
- **Run 119 was red**: my new view-model test counted notices, and launching "B" in the test also posts the
  fake launch's *Can't open* notice. The test now checks that the unknown name adds one. Fixed in the next commit.
- **Run 120 was red on an older test.**
  - What failed: `A_quoted_PowerShell_command_takes_a_placeholder…` waits for a real `powershell.exe` to write a
    file, and that took more than its 20 s.
  - Why I call it a timing flake: it passed in run 119, and nothing in this change touches launching.
  - What I changed: the wait is now up to 60 s. The test is not skipped and its assertion is unchanged.
- **Tests.** Core has the parser and message tests from `a95c410`. The App tests add a pipe round trip
  (Windows only), the send-without-listener case, and adds into a named group plus launch by name.

---

## 2026-11-10 (32) — Layout history (`8db6bdf`) and review 20

L3 taken: this entry covers `8db6bdf`, which came without one, and the review 20 fix in the commit that adds it.

- **Layout history.** `LayoutHistory` keeps `layout.json` as it was in `history\layout-<UTC time to the
  millisecond>.json` before `LayoutStorage.Save` overwrites it: at most once per ten minutes, the newest 20.
  Milliseconds, because a forced record (before a restore) right after another one in the same second would
  otherwise overwrite it. *Restore an earlier layout…* is in the options and tray menus. It forces a record first,
  so a restore can be undone, then `ReplaceLayout` (which keeps this PC's window placement) and `LayoutChanged`, so
  a linked copy uploads the restored bar. Sync loads land in the history too, through the same save; you noted it.
- **L1.** Run 115's log confirms your static diagnosis: the only failure was `Assert.Same` on two equal
  `LayoutHistoryEntry` records from two `List()` calls. It is `Assert.Equal` now. That was my miss: the App tests
  don't run here, and I didn't wait for the run before writing the summary.
- **L2.** Taken. `ShowLayoutHistory` takes the reading function; the window shows *Reading the earlier layouts…*
  and reads them on the icon worker with the 30-second limit, and says so when it runs out (`History_TimedOut`,
  three languages). The test fake calls the function itself.

---

## 2026-11-10 (31) — Review 19: the Import gate

All of review 19 is in the commit that adds this entry. Locally 770 Core tests pass and the App tests build; CI runs
them on Windows.

- **I1.** `ImportPlan.IsUsable` refuses a leading `/` for every source. Test: your INI round trip,
  `[Sessions\%2Fscript=C:%5Cx.txt]` through `WinScpIniSessions`, `EscapeDataString` and `SessionNames`, is
  `/script=C:\x.txt` and is left out; `folder/web` (WinSCP's folder separator inside a name) still passes.
- **I2.** `ImportCandidate.Key` expands variables, as `KeyOf` does. Tests both ways (a `%windir%` candidate
  against the expanded entry and against the raw one) and through this PC's own `Locations` entry.
- **I3, I4.** Refused rather than escaped: a name ending in `\` and any `%`, for every source. The fullwidth quote
  has its row now.
- **I5.** A timed-out search shows `Import_TimedOut` (three languages), and the log has an Import line saying it
  stopped after 30 s. Test on the view model.
- **I6.** `ShowImport` clones the shortcuts on the window's thread, and the worker reads the copy.
- **I7.** `SyncViewModelTests` uses `TestFakes.RecordingDialogs`, which gained `Folder` and `LastMessage`; its own
  stub is gone. The rows you listed are in `ImportPlanTests`, except `Build` with the real `ForStorage`, which
  depends on the process's portable state; `Build` takes it as a function, and the App's call passes the real one.
- **On record.** The `wt.exe` spelling has a comment at the check. `ImportPlanTests` and `LauncherModelTests` share
  an xUnit collection, since both set the process-wide `ThisDevice.Id`.

---

---

Entries 30 and older (up to the 0.8.1 release) are in [REVIEW_NOTES_ARCHIVE.md](REVIEW_NOTES_ARCHIVE.md).
