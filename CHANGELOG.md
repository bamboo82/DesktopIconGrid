# Changelog

All notable changes to Desktop Icon Grid. The format follows [Keep a Changelog](https://keepachangelog.com/);
version numbers follow [Semantic Versioning](https://semver.org/).

## [0.9.0] – Unreleased

"The bar tells you things": first what moved from 0.8.0 (the import dialog, layout history, a command line
and *Send to*), then the command tile, a health light on a shortcut, folder stacks, more gauges, a
microphone tile, a clock and timer, and paste to a bucket.

### Added

- *Import…* in the icon and empty-space menus: the taskbar's pinned shortcuts, the desktop's shortcuts and
  links, Windows Terminal profiles, WinSCP and PuTTY sessions, and the hosts of your SSH config, grouped by
  source, with what is on the bar already marked. Ticked items go after the clicked icon or at the end, or on
  the bar itself into a group per source. A name that could not stay one argument on the command line (a
  quote, a line break, a leading `-` or `/`, a `%`, a `;` for Windows Terminal) is left out.
- Layout history: before a save overwrites the layout, the bar as it was is kept in `history\`, at most once
  every ten minutes, the newest 20. *Restore an earlier layout…* (options and tray menus) brings one back,
  keeping the current bar in the history first, so a restore can be undone too. It stays on this PC.

## [0.8.1] – 2026-11-06

The fixes the review of 0.8.0 placed after the release: folder sync with conflict copies and storage
switches, portable mode and *Repair…* on more kinds of PC, the placeholder checks, and the smaller items.

Upgrading: 0.8.1 reads and writes the same layout format as 0.8.0 (version 1), so PCs on either can share a
settings copy. Update every PC all the same: 0.8.0 lacks the placeholder fixes listed under Security.

### Security

- ssh's destination, login name (`-l`) and jump host (`-J`) are now checked too: only what `user@host`,
  `[v6]:port` and an `ssh://` address need passes (letters, digits and `. - _ @ : [ ] /`, no `-` to start the
  name or one of its parts, right after `@`, `/`, `[` or `:`). OpenSSH before 9.6 could pass other characters in a host or user name on to a
  `ProxyCommand` (CVE-2023-51385), and a name that starts with `-` reaches ssh or the proxy program as an
  option.
- ssh's `-o` takes a line of its settings, with a space or `=` after the setting's name, and some settings run
  a program on this PC (`ProxyCommand calc`). A placeholder that starts `-o`'s value, or stands in the
  setting's name (quotes in the name are taken off, as ssh does), is therefore refused, and so is one after a
  setting that runs or loads something (`ProxyCommand`, `LocalCommand`, `PermitLocalCommand`,
  `KnownHostsCommand`, `PKCS11Provider`, `SmartcardDevice`, `SecurityKeyProvider`, `Include`, `XAuthLocation`,
  `Match`), an empty value included. So is the value of ssh's `-I` (a library to load) and `-F` (a config file).
  After another name written in the template, the value is checked as the command ssh sends
  (`-o ConnectTimeout={ask:Seconds}`), or as a host (`HostName`, `User`, `ProxyJump`). A placeholder among an
  option's letters (`-v{clipboard}`) is refused too: its value would add options. OpenSSH reads options after
  the host as well, unless `--` came first, and so does the check; a `--` in a prompt does not count. scp and
  sftp are read the same way, and their `-S` (a program run instead of ssh), sftp's `-D` and `-b` (a batch
  whose `!` lines run here) are refused. In a word written `host:path`, a placeholder before the colon is
  checked as a host, and one after it as the remote shell reads it, unless `-s` asks scp for SFTP (scp's old
  protocol, the default before OpenSSH 9.0, which Windows 10 has, hands the path to the remote shell); sftp's
  `-s` may be the command the server runs. A placeholder that makes up a whole operand is checked as a local
  path, not as a host: write `host:` in the template for the host to be checked.
- A value that held `{file}` got a dropped file's path put inside it, after the value had been checked: with
  `{clipboard}` for a batch file and `x{file}` on the clipboard, a file called `Invoice&calc&.pdf` reached
  cmd.exe with its `&` outside every quote. An answer that held `{clipboard}` got the clipboard the same way.
  All placeholders now go in with one pass over the template, and nothing put in is read again. Dropped files
  are judged where the template puts them, before a value with spaces can move the words they follow
  (`wsl -u {clipboard} cat {file}` with `root --exec`); `{files}` is judged as two `{file}`, its second path a
  word of its own (`scp {files} host:/dir/` takes local paths), and files added at the end are judged for
  quotes too. Older than 0.8.1.
- A placeholder inside quotes with other text (`"--in={file}"`, `git commit -m "WIP {clipboard}"`) had its
  value's own quotes end the template's, so the value stood outside every quote: a dropped `a&calc&.txt` ran
  calc through a batch file. A placeholder is now filled only where every program that reads the line sees it
  alike: the C runtime (the UCRT, for which `""` inside quotes is a quote character), CommandLineToArgvW
  (Chromium, Electron and Windows Terminal, for which that `""` ends the quotes), cmd.exe for a value it reads
  (a backslash does not escape a quote there), and WSL's shell, whose `'…'` quotes too. A placeholder whose
  value would touch a quote (`"https://www.bing.com/search?q="{clipboard}`, `{ask:A}{clipboard}`) counts as
  inside quotes. Such a placement is refused before anything starts, with a note under the parameters in
  *Properties*, which judges them as a launch does. A placeholder written alone in quotes (`"{file}"`) is fine,
  and so is one inside a quoted command for ssh's remote shell or PowerShell (`ssh host "tail -n 50
  {ask:File}"`, `powershell -Command "Get-Content {file}"`), which check a value as bare words anyway: it goes
  in as it is. A backslash written right before a placeholder (`%USERPROFILE%\{ask:File}`) is doubled, so the
  value's quote stays a quote. Older than 0.8.1.
- `cmd.exe /c {file}`: when the command after `/c`, `/k` or `/r` starts with a quote, cmd takes off its first
  and last quote, so a dropped `a&calc&.txt` lost its quotes and ran calc. The command now gets one more pair,
  as Windows gives a batch file's, and a pair written around the whole command (`/c ""…" "{file}""`) keeps
  working. cmd's switches written together (`/d/c`, `/V:ON/C`) are read as cmd reads them, and a value before
  the command switch is refused, since cmd looks for its switches in it. Older than 0.8.1.
- Windows Terminal, conhost, Windows' sudo and gsudo hand their arguments on quoted only when they hold a
  space, and escape nothing. After them, a placeholder that cmd.exe or WSL's shell reads is refused
  (`wt cmd /c type {file}`, a batch file one of them starts, anything gsudo starts, which it hands to cmd.exe
  unless it is a program). Elsewhere a value with a quote or a tab, or a backslash at the end of one with a
  space, is refused, since it would split into other arguments (`wt git clone {clipboard}` with
  `x" --upload-pack=calc "y`), and what only the next program reads keeps working (`wt -d {file}` with
  `C:\Program Files (x86)`). A batch file that a relay (`wt`, conhost, runas, sudo, gsudo) starts, named so or
  found on PATH, is read as cmd.exe reads it. The PATH lookup skips shares and slow drives, which could hold up
  the launch, and a name found nowhere else counts as a batch file. `WindowsTerminal.exe` counts as `wt`. Older
  than 0.8.1.
- A value that starts with `-` where an argument starts is refused unless a `--` before it ends the options of
  the program that reads it (0.8.0). A wrapper's own `--` counted for the program after it: `wsl.exe -- python3
  {clipboard}` let `-c…` through to python3, and so did `wt -- python …`, `npx -- tool …` and `dotnet run --`.
  A `--` now belongs to the nearest program named before it, or the shortcut's program; a wrapper's (wsl,
  Windows Terminal, conhost, sudo, gsudo, runas, npx, pnpx, bunx) counts only when nothing stands between it and the
  placeholder, and dotnet's never, since what follows it is the app's. A value that starts with `/` is refused
  where one that starts with `-` would be, except for a shell: Chromium and Electron programs (Edge, Chrome,
  VS Code, Teams) read `/name` as a switch, and Windows Script Host reads `//` options after the script.
- A dropped file's name holding `!` is refused where cmd.exe reads it, as `%` was: with delayed expansion on
  (`cmd /V:ON`, a batch file's `setlocal EnableDelayedExpansion`), cmd expands `!NAME!` inside quotes too.
- A drop wrote the filled parameters to `app.log`, and *Keep a log* writes them at the top of the shortcut's
  log: a password typed for `pscp -pw {ask:Password} {file}` landed in both. Both now write the parameters as
  written. Older than 0.8.1.
- plink, PuTTY's command-line ssh, is checked the same way; its `-proxycmd` (a command run here) and `-m` (a file
  sent as the command) are refused. PuTTY reads plink's options anywhere and has no remote command, so after
  its host the options are read as plink's and a bare word is the port. After an option of plink's that this
  version does not know, a value is checked as the command. Covered by tests of the rules.

### Fixed

- Starting a portable copy with *Start with Windows* off removed the installed bar's autostart: every copy
  shares one Run entry, and it was removed whatever program it named. A copy now removes or rewrites the
  entry only when it names that copy. An entry for another copy that is still there is left alone, and
  turning the option on says so; one for a program that is gone, a drive letter this PC does not have
  included (a stick that was pulled out), is taken over, as before. A portable copy adds or takes over an
  entry on a PC only after asking (*No* turns the option off again), so a stick no longer leaves entries
  behind on every PC it visits, and it counts its own entry made under another drive letter as its own.
  *Settings* also says that an entry turned off under *Startup apps* keeps the bar from starting. Covered by
  tests against a stand-in for the registry, one of them on a real unused drive letter.
- Portable mode wrote some paths with their drive letter: a program chosen in *Repair…*, an icon picked or
  downloaded in *Properties*, which lands in the portable folder itself, and a location or *Start in* typed or
  pasted there. They broke when the drive got another letter. They are now written with `%APPDRIVE%`, as other
  paths are.
- A portable copy run from the system drive (`C:\Tools`) wrote every C: path with `%APPDRIVE%`,
  `C:\Windows\System32\cmd.exe` too, so a copy on a stick that loaded them looked for them on the stick.
  Paths on Windows' own drive are now written as they are. For such a path that 0.8.0 wrote in Windows' own
  folders (Windows, Program Files, ProgramData, Users), a PC where it is missing on the drive `%APPDRIVE%` points
  to, and there on Windows' drive, gets a location of its own on Windows' drive. Each PC decides for itself, at
  every load, the one at start-up included: the shared location, *Start in* and the icon stay as they are, so a
  copy's own programs on a second disk (`D:\Program Files (x86)\Steam`) keep working on the PC that has them, a
  location the PC already has of its own is replaced only when it is not there either, and the layout's format
  stays the one 0.8.0 reads. A layout of version 2, which only test builds of 0.8.1 wrote, reads as version 1.
- A copy run from a share kept the `%APPDRIVE%` it inherited when a stick's bar started it, and expanded paths
  as the stick. It now clears it.
- *Repair…* could not fix a shortcut to a missing folder: it looked for files only, and *Browse* picked a file.
  It now looks for a file or a folder of that name, whichever is there, other versions of a folder included
  (`node-v20.11.0-win-x64` finds `node-v22.1.0-win-x64`), and *Browse* offers *File…* and *Folder…*.
- `portable.txt` next to a program whose folder cannot be written (under Program Files without elevation, on a
  read-only drive) stopped the app with "Startup failed". It now keeps its files in `%LocalAppData%`, as without
  `portable.txt`, and says so once at start.
- A repaired shortcut could still fail to start on its old *Start in* folder. A *Start in* that was only the
  program's folder (as Windows shortcuts set it) now moves with the program, and a *Start in* that is not on
  this PC (another PC's project folder, on a drive letter this PC may not have) gives way to the program's own
  folder instead of stopping the start.
- *Repair… → On every PC that shares this bar* said it applied everywhere, but a PC with a location of its
  own kept it. That is the right thing (its path differs), so the choice now says so.
- *Edit names…* checked every shortcut's target on the window's thread when it opened. It now uses the bar's
  own check on the icon worker, as the icons do.
- Google in portable mode: the client secret and the sign-in are encrypted for one PC and user, and the copy
  on the drive held only one of each. Another PC could not read them, so its next save erased the first PC's
  secret, and each sign-in replaced the other PC's, so two PCs kept signing each other out. Each PC now keeps
  its own: the secret in `sync.json` under the PC's key (a secret saved by 0.8.0 is kept for its PC), the
  sign-in in a file of its own, which the shared sign-in of 0.8.0 becomes only on the PC that wrote it, so a
  *Disconnect* stays done. A PC new to the drive asks once. Covered by tests with a protector per PC.
- A divider at the end of a full row had no width, so it could not be seen, right-clicked or dragged, though
  Tab still landed on it. It now takes a sliver of room at the row's end, over the last icon's padding, inside
  the view. Covered by a test that clicks it on the real control.
- A click on a shortcut whose parameters take dropped files (`notepad++.exe {files}`) passed `{files}` to the
  program as a word, and such a shortcut started another copy instead of switching to the running one. A
  click now leaves `{file}` and `{files}` out, and they no longer count as parameters for switching.
- *Properties* could grow taller than the screen, with *Advanced* open in Chinese most of all. It now scrolls
  with Save and Cancel always in sight, like *Settings*, and moves back up when opening *Advanced* makes it
  reach past the bottom of the screen. Covered by tests on the real dialog.
- Each placeholder is now checked where it stands. In `ssh {ask:Host} tail {clipboard}`, the host takes
  `admin@server2`, and the words after it are still checked as the remote shell reads them; 0.8.0 checked
  both values as the command, so `admin@server2` was refused. The values of wsl's own options (`-d`, `-u`,
  `--cd`) are wsl's, and `wsl --shell-type none` runs the program without a shell, as `-e` does.

- Loading a copy that a sync program kept after a conflict (OneDrive's `Work-LAPTOP.json`, Dropbox's
  conflicted copy) linked the PC to that file while the other PCs stayed on `Work.json`. Each then uploaded
  to its own file, and both said "up to date". The copy is now loaded, the PC stays linked to `Work`, and
  the next upload asks before it replaces another PC's save there. The start-up question and the "not
  uploaded" message name such copies, which nothing did before. *Overwrite* on such a copy says that the PC
  then stays with it and leaves `Work`.
- Moving the copies to a folder or back to Google Drive, or unlinking, while an automatic upload ran could
  write that upload into the new place, over a copy there and without the conflict check, and then link
  the PC again. These now wait for the upload, and each sync operation uses the place it started with.
- With the copies on a network share that was off, choosing a folder froze the bar for up to a minute.
  Such a share is no longer looked at on the window's thread, the folder picker opens on a local folder
  rather than on that share, and the folder is made on a worker.
- A copy whose name Windows does not allow in a file name, numbered because another copy's name became the
  same file name ("Home? desk" in `Home_ desk (2).json`), was listed under the file's name, and an upload
  renamed it to that. So did a copy whose file name another file held (another program's `config.json`):
  listed as a sync program's copy, it got a third file when another PC loaded it. Saves now keep the copy's
  own name, such a copy is listed under its own name (it is the original while no copy of that name holds its
  file), a PC that loads a sync program's copy of it links to it, and a save from the dialog refuses a copy that
  cannot be read just then; an upload waits and tries again. *Saved* and *Uploaded* name it by its own name.
- While a copy's own file could not be read (a new PC still downloading it), a sync program's numbered copy of
  it (`Work (2).json`) was listed as the copy itself, and saves went into it; two numbered files of one name were
  both listed as it, and the PCs on them split. Now the copy is its own file, readable or not, and only without
  one the lowest-numbered file of its name; any other is listed as a sync program's copy, which the start-up
  check names. A PC that loads a copy from a listing made while it could not be read is linked under the name
  in the copy, and a sync program's copy is no longer linked to an unreadable file that may be another name's.
- Another program's `.json` in the folder that does not parse (settings with comments, an empty file) was
  listed as a copy that could not be read, and a save under its name said to delete it. It is no longer
  listed, and that message no longer says to delete anything.
- An upload that found the copy held open by a sync program just as it replaced it failed with no retry. It now
  waits and tries again, as for a copy that cannot be read, and an upload still waiting gets one more try when
  the app closes.
- A load whose `sync.json` could not be saved (held by a scan, a full or pulled stick) left the PC linked to the
  loaded copy in memory with the old bar, which the next upload then wrote into it. The link now moves only
  once it is saved.
- An automatic upload that came due while a copy loaded (its timer runs on under the sync dialog) wrote the
  bar the load replaced into the copy just loaded, and every other PC on it was offered that bar. Loading the
  linked copy again to undo a change went the same way. Such an upload now writes nothing; a change made
  after the load uploads as usual.
- A shortcut on a drive letter this PC does not have at all (another PC's `D:`, a stick that is not plugged
  in) counted as on slow storage and was never marked missing. Windows says such a letter is not there
  without touching any drive, so it is marked now.

## [0.8.0] – 2026-11-06

"One bar, every PC", released early because it carries the fixes from the review of 0.6.0 and 0.7.0,
among them a security fix: sync through any folder, shortcuts that survive other PCs, and *Add app…*.
The rest of the plan for this release (the import dialog, layout history, a command line and *Send to*)
moves to 0.9.0.

Upgrading: update every PC that shares a settings copy. When 0.6.0 saves a linked copy it removes what
0.7.0 and 0.8.0 added (the launch options, colour tags, *When it is running*, the *Show at pointer*
hotkey, the per-PC locations); 0.7.0 and later keep it. Meanwhile a PC still on 0.7.0 can share a copy on
Google Drive with 0.8.0: its saves are told by Drive's time, as before, so they are offered, never
overwritten.

Note for whoever tests this build: the owner's screen pass of 0.7.0 confirmed four of the bugs fixed here.
What is new in this release has been seen on no screen yet, nor have the fixes that need real input: the
hotkey swap, auto-hide while typing, Settings at 1080p in Chinese, a minimised Calculator. The voice
spike has its first answer: no, for want of the zh-TW speech pack (FEATURES §6.1).

### Security

- `{clipboard}` and `{ask:…}` values could run commands when the shortcut's program reads its command line
  through cmd.exe, as PowerShell script text, through a Linux shell in WSL, or as the command ssh sends.
  The quoting that keeps a value one argument for other programs does not hold there. Copied text such as `x" & calc & "` could start a program through cmd.exe,
  elevated with *Run as administrator*, and PowerShell takes the quotes off before it reads the words, so
  `(calc)` ran too. Such values are now refused before anything starts, with a notification naming the
  character:
  - cmd.exe reads the command line of a batch file, of `cmd.exe` itself, and of a command without an
    extension that Windows finds as a batch file (npm, npx, code and many more) or that is not found
    here. For these, `" % ! ^ & | < >` are refused.
  - PowerShell reads it as script text for `powershell.exe` without `-File` and for `pwsh -Command`.
    For these, only letters, digits, spaces and `. _ - \ / :` pass, and no word may start with `-`,
    which PowerShell would take as a parameter (`x -Recurse`).
  - A Linux shell reads it for `wsl.exe`, unless `-e`/`--exec` runs the program itself, and for the old WSL
    launcher, `bash.exe` in System32. It keeps the quotes, but expands `$(…)` and `` `…` `` inside them
    (`wsl echo "$(…)"` ran the command), so `$` and `` ` `` are refused as well as what cmd.exe refuses.
  - ssh sends the words after its destination to the other computer's shell as a command, without their
    quotes. A value there is checked as PowerShell script text is; the destination and ssh's own options
    are not.
  - A program that hands its command line on to cmd.exe, PowerShell, WSL or ssh (`wt cmd /c …`, `runas`,
    `conhost`) is treated like them. Windows Terminal itself starts another command at every `;`, even
    inside quotes, so a `;` is refused wherever `wt` reads the command line.
  - For every program, a value of more than one line is refused, and so is the fullwidth quotation mark
    `＂`, which Windows can turn into `"` for older programs. The clipboard text is trimmed first.
  - For every program, a value that begins with `-` where an argument starts is refused unless `--`
    comes before it, because most programs would take it as an option (`python "-c…"` runs code).

  A file dropped on such a shortcut is refused when its name holds `%` (cmd.exe expands `%NAME%` even
  inside quotes), for WSL also `$` or `` ` ``, for Windows Terminal `;`, and for PowerShell script text or
  an ssh command anything but the characters above. A placeholder written inside code for another
  program (`python -c "…{clipboard}…"`, `node -e`, `bash -c`) puts the value into that code: that is the
  shortcut's choice, and the Parameters hint says to avoid it. Affects 0.7.0. Covered by tests for each
  refused character and each kind of program, and on Windows by running the real powershell.exe and pwsh
  with the command lines the app builds; WSL, ssh and Windows Terminal by tests of the rules only.

### Added

- Sync through any folder: the sync dialog (now *Sync across PCs…*) can keep the settings copies in a
  folder that OneDrive, Dropbox, Google Drive for desktop or a network share keeps in step, instead of on
  Google Drive. No Google project, no sign-in. Device names, the linked copy, automatic upload and the
  rule that never overwrites a newer copy from another PC work as before. Which copy is newer is told by a
  random revision in each save, not by the clocks: a sync program carries the writer's own file time, so
  a save made offline, or on a PC whose clock is fast, could otherwise overwrite another PC's newer one. On
  Google Drive, whose times are Drive's own, a later time counts as well. A
  linked copy that cannot be read yet postpones the upload instead of being overwritten. Copies the sync programs make after a conflict (`Work (1).json`, `Work-LAPTOP.json`) appear
  under their own names, with their device and time, so they can be loaded or deleted. The Google account
  stays for Google Drive Buckets. The folder backend is covered by tests on real files, including two PCs
  sharing one folder; it has not yet been tried with OneDrive or Dropbox themselves.
- Shortcuts that survive other PCs: a shortcut whose program is not on this PC is drawn faded with a "?".
  A click on it, or right-click → *Repair…*, looks for the program — a folder that differs only by its
  version (`app-1.0.9172` → `app-1.0.9203`), its registration with Windows (App Paths), the folders for
  installed programs — or lets you browse for it. The fix is for *this PC only* by default: it is stored
  in the shortcut per PC, so it travels with sync copies and every PC finds its own path. "This PC" means
  the PC and Windows user, not the data folder, so it holds for a portable copy on a stick and for data
  copied to another PC; settings copied to another PC get a sync identity of their own there. *Properties*
  has the same *Only on this PC* choice, and *Edit names…* can show the shortcuts missing on this PC.
  Only local fixed drives are checked, never network or removable ones. Each folder for installed
  programs is searched with a limit of its own, a folder of the same name first; a match on the file name
  alone is offered but never chosen for you. The search is covered by tests on real folders and the
  per-PC location by model and view-model tests; not yet seen on a screen.
- *Add app…* in the icon and empty-space menus: every installed app the Start menu lists, Microsoft Store
  apps included, with icons, filtered as you type; tick several and they are added at once. Store apps
  no longer need a desktop shortcut first. Desktop programs are stored by their real path, so the
  running badge and switching work for them; that includes the ones the Start menu lists under an id of
  their own (Chrome, Edge, Firefox, Office), when their shortcut starts the program without arguments.
  The mapping from the Start menu's entries is covered by tests, and reading the list and Edge's entry by
  tests on Windows; the dialog itself has not been seen on a screen.
- Portable mode: put an empty `portable.txt` next to `DesktopIconGrid.exe` and the bar keeps its files in
  that folder instead of `%LocalAppData%`. `%APPDRIVE%` stands for the program's drive, and new shortcuts
  to files on that drive use it, so a USB drive that gets another letter keeps working. Covered by
  tests of the rules; not yet run from a USB drive.

### Changed

- A program shortcut with parameters (`mstsc /v:server2`, `putty -load prod`, `code C:\proj2`) now starts
  another copy when clicked, instead of bringing forward a copy of the same program that may have been
  started with other parameters, as 0.7.0 did. If you want the 0.7.0 behaviour for one shortcut, set
  *Properties → Advanced → When it is running → Switch to it*; it then switches only to copies that this
  shortcut itself started.

### Fixed

- When another program had `sync.json` open at start (a virus scan at logon, a sync program), the sync
  settings started blank for that session, and saving any change in the sync dialog replaced the real
  ones: the link, the sync folder, the Google client. They are now kept in memory only and never written
  over the file, the sync dialog says so, and the next start reads the file again. Covered by tests that
  hold the file open.
- *Run as administrator* together with *Keep a log* started the program without elevation and without a
  word (the log needs the program started directly, which cannot ask for elevation). It now starts
  elevated and unlogged, and *Keep a log* is greyed out while *Run as administrator* is ticked, with the
  reason underneath.
- Alt+F4 on the *Show at pointer* window closed it for good: every later press of its hotkey showed an
  "unexpected error" until the app was restarted. Alt+F4 now hides it, like Esc. Covered by a test that
  builds the real window, sends it what Alt+F4 sends and shows it again.
- Swapping the show/hide and *Show at pointer* hotkeys in one save failed for both, with two "already used
  by another program" messages about the app's own keys; the keys kept their old jobs while Settings
  showed the new ones, and every later save showed both messages again. Setting a hotkey back after a
  refused change failed the same way. Both work now. A refused combination is reported once (it is tried
  again on later saves), and the message says whether the previous hotkey still works or the action has
  none, which the old message got wrong at start-up. Covered by tests against a stand-in for Windows.
- A bar docked with *Auto-hide* shrank back to its strip a second after the hotkey brought it out, even
  while you typed in its search box; Enter then opened a match nobody could see. It now stays out while
  it has the keyboard or its search box is open, and tucks away once you work in another window.
  Checked in code only.
- A line after the last icon of a full row moved to the start of the next row, where it divided nothing
  and pushed that row nine units in, so its last icon wrapped and every later row was out of line (on the
  bar, in group popups and in *Show at pointer*). It now stays at the end of its row in whatever room is
  left there, perhaps none. Covered by layout tests.
- The *Show at pointer* window was sized once, by counting the top-level items. A group opened in it lost
  its last row to the group's header, behind a scroll bar that also took an icon from every row; lines
  counted as whole cells, and captions wider than a cell needed rows nobody had made room for. The window
  is now measured from what it shows, header, lines and captions included, and resized when a group
  opens or a search goes back to the top level. Its search box no longer shows a tooltip over the first
  row each time it opens (the key help is still there for screen readers). Covered by tests on the real
  window.
- Lines and gaps that went through a 0.6.0 PC (by sync) came back named "Group": 0.6.0 gives that name to
  every nameless item from a newer version and saves it, and *Line → Label…* then offered "Group" as the
  text. Lines and gaps are now saved with a name of their own ("Divider", "Gap") that 0.6.0 leaves alone;
  this version ignores a line's or gap's name when loading, and turning one into a label offers no text.
  Items from a newer version now keep their name as it was (0.7.0 also called nameless ones "Group").
  Covered by tests that put a saved bar through 0.6.0's rules.
- A click on a Store app that had been minimised for a few seconds (Calculator, Notepad, Paint on
  Windows 11, or an app added with *Add app…*) started a second copy instead of bringing it back: Windows
  suspends the app, and its frame then holds no window of the app's process to recognise it by. The frame
  is now also recognised by the app ID it keeps. The matching is covered by tests and reading the ID by a
  test on a real window; the owner's screen pass found the bug and checked that the ID is still there.
- The Settings dialog could be taller than the screen. In Chinese it was 1,089 pixels high, so on a 1080p
  screen, or a laptop at 125 %, Save and Cancel were below the edge; some options were also cut off at
  the right. The settings now scroll with Save and Cancel always in sight, the dialog is never taller
  than the screen's work area and moves back onto it, and long options wrap. The Sync dialog does the
  same. Covered by tests on the real dialogs.

## [0.7.0] – 2026-11-05

"Get there faster", the first release of the plan in [docs/ROADMAP.md](docs/ROADMAP.md): a click on a
running program goes to its window, the options of a Windows shortcut, type to find and a hotkey that
opens the icons at the mouse, launch feedback, separators and colour tags.

Note for whoever tests this build: all of it is new code covered by tests (the Windows-only parts on
Windows CI), and none of it has been seen on a screen yet. To make room for separators the grid's layout
panel was replaced; a bar without separators should look and wrap exactly as before, so that is the first
thing to look at. The voice spike could not be run here (no Windows, no microphone): its probe is in
`tools/VoiceProbe`, and FEATURES §6.1 says how to run it and what is known so far. Nothing is built for
voice until it answers.

### Added

- Clicking a program that is already running switches to it, as the taskbar does, instead of starting a
  second copy: its window comes forward (restored if minimised), a second click while it is in front
  minimises it, and with several windows each click goes to the next one. Store apps are found through
  the frame Windows draws them in, and the inbox programs that Windows 11 replaced with Store apps
  (Calculator, Notepad, Paint) are recognised by their package. Shift+click or a middle click starts
  another instance; Ctrl+Shift+click starts one as administrator. *Properties → Advanced → When it is
  running* changes this per shortcut; documents, folders and links start again by default. Checked in
  code and with tests against simulated window lists; not yet seen on a screen.
- The options every Windows `.lnk` has, under *Properties → Advanced*: *Start in* (the working folder),
  *Run* normal, minimised or maximised, *Run as administrator* (an ordinary UAC prompt), and for web links
  a browser profile of Edge or Chrome, optionally as an app window without tabs or address bar.
- Two placeholders next to `{file}`/`{files}` in *Parameters*: `{clipboard}` passes the clipboard text and
  `{ask:Question}` asks for a value when the shortcut starts; both are quoted as one argument, and Cancel
  cancels the launch. Checked in code and with tests; not yet seen on a screen.
- Type to find: while the bar has the focus, typing opens a search box at its bottom (`Ctrl+F` does too).
  The icons that match, inside groups as well, stay lit, together with the groups that hold them; the
  rest fade. A match is found by its name on the bar, the name it was created with, its file name, or
  its initials (`vsc` finds Visual Studio Code); case and full-width letters do not matter, and Chinese
  is typed straight into the box through the IME. Enter opens the framed match (its name shows beside
  the box), the arrow keys move the frame, Esc clears the text and then closes the box.
- *Show at pointer*, a second hotkey in Settings (off until a combination is set): the bar's icons open in
  a small window under the mouse with the search box ready, on whichever monitor the pointer is, also
  while the bar is hidden in the tray or tucked away at a screen edge. Groups open in place (Backspace or
  ← goes back), and the window goes away after a launch, on Esc and when another window is clicked.
  Search and the window's size and placement are covered by tests; the window, the keys and typing
  through a Chinese IME have not been seen on a screen. With an IME in Chinese mode, the first key that
  opens the bar's box may not reach the IME; the Show-at-pointer box is focused from the start.
- Launch feedback: the icon just started (or the group it sits in) is framed and pulses until the bar
  loses the foreground, normally when the program's window appears, or for five seconds at most. With
  Windows animations turned off it is only framed.
- Separators: *Add separator ▸ Line / Gap / Label…* on an icon or on empty space. A line is a thin divider
  between icons, a gap an empty cell, a label a small caption as wide as its text; in a bar one icon wide
  they lie across it. They can be dragged, switched to another kind and removed from their own menu, and
  sit in groups too. Versions before 0.7.0 keep them as items from a newer version, but 0.6.0 names lines
  and gaps "Group" (fixed in 0.8.0).
- Colour tags: right-click → *Colour tag ▸* (seven colours or none), drawn as a corner on the icon. The
  names table has a *Colour tag* column and *Show:* narrows it to one colour or to the untagged items.
  Launch feedback, separators and tags are covered by tests of the layout and the view models; none of
  them has been seen on a screen yet.

### Fixed

- Settings and icon properties written by a newer version are kept when this version saves, at every
  level of `layout.json` and of the sync copies (the document, the settings, every kind of item, items
  inside groups, text icons). Before, an older PC that loaded a linked copy and saved it again dropped
  them, and the copy carried the loss to every other PC: the newer PC's language, dock edge or log
  setting was reset without a word. From this version on, every newer field passes through untouched.
- The Settings dialog changes only what it shows; before, saving it reset settings it does not show.

## [0.6.0] – 2026-11-05

Drive copies that stay current, the bar back on its own monitor, the running badge for Store apps, and
a tray icon without Windows Forms. Note for whoever tests this build: the tray icon is new code. How it
is drawn and how the shell's callbacks are decoded are tested on Windows CI, but the icon in the
notification area, its menu and its balloons have not been seen on a screen yet; neither has the bar
returning to a second monitor at another display scale.

### Changed

- The Bucket Viewer's local cache reuses a downloaded copy only while it is still the file on Drive:
  the same MD5 checksum, or the same size and modification time, recorded in a `.stamp` file next to
  the copy. Before, a copy of the same size was reused even after the file had changed.
- Names from Drive that Windows reserves for devices (`CON`, `NUL`, `COM1`, also with an extension) get
  a trailing underscore when they are downloaded, and the characters Windows forbids are replaced the
  same way wherever the code runs.
- The bar's position is saved in device pixels together with the device name of the monitor it is on,
  and restored there in pixels when that monitor is present, so display scales no longer come into it
  (a bar on a 150 % monitor next to a 100 % one came back in the wrong place or size). The WPF-unit
  values are still written for older versions and used when the monitor is gone, with the check against
  the real monitors as before. A bar that would stick out after a change of resolution is pulled inside.
- Shortcuts to Store apps and other programs reached through an app-execution alias (`wt`, `winget`,
  Python from the Store) show the running badge: the alias's reparse data names the executable in the
  package, and any process from that package folder counts, so Windows Terminal (`wt.exe` starts
  `WindowsTerminal.exe`) is covered. Before, only a program started from the bar itself was tracked.
- The tray icon talks to the shell directly (`Shell_NotifyIcon` with version-4 callbacks on a hidden
  window of its own) instead of through Windows Forms' `NotifyIcon`, so the app no longer loads Windows
  Forms or System.Drawing: a smaller self-contained build and one DPI story (the WPF manifest). Behaviour
  is unchanged: left click shows or hides the bar, right click or Shift+F10 opens the menu, a balloon
  click opens the log viewer, and the icon comes back when Explorer restarts.

### Fixed

- A docked bar that is hidden to the tray (with the hotkey, the tray menu or the close button) or started
  hidden gives its screen strip back, so maximised windows use the whole screen again; it is claimed again
  when the bar is shown. Before, windows stopped at an empty strip. A full-screen program still hides the
  bar without releasing the edge.

## [0.5.0] – 2026-11-05

The user interface speaks Traditional and Simplified Chinese as well as English. The language
follows the Windows display language unless *Settings → Language* picks one; a change applies at the
next start. Note for whoever tests this build: the translations were written without a screen, so
long Chinese labels in the fixed-width dialogs (Settings, Sync) are the first thing to check.

### Added

- *Settings → Language*: *Same as Windows*, English, 繁體中文 or 简体中文. The text lives in
  `Resources/Strings.resx` (English) with `zh-Hant` and `zh-Hans` translations, so Hong Kong and
  Macao get Traditional Chinese and Singapore gets Simplified; every other Windows language gets
  English.
- The ready-made Windows items (*This PC*, *Control Panel*, …), the special icons, the tray and
  context menus, every dialog, tooltip, status line and notification are translated. New items are
  named in the language in use when they are created; existing names are kept as they are.
- Text icons without a font of their own use 新細明體 (PMingLiU) first under a Traditional Chinese
  UI and 宋體 (SimSun) otherwise, as before. Pick a font in the *Text icon* dialog to pin one.

### Changed

- Sync status lines carry their own severity instead of being classified by their English wording,
  so warnings and failures reach the tray in every language.

### Fixed

- The tray icon's right-click menu did not open (since 0.3.0): its WPF menu was anchored in a window
  that was never shown, and WPF waits for such an anchor forever. The anchor is now a zero-size window
  moved under the pointer, which also gives the menu that monitor's DPI. If it still fails, `app.log`
  says why.
- The show/hide hotkey and a click on the tray icon bring a bar that is visible but covered by other
  windows, or collapsed as an auto-hide strip, to the front instead of hiding it; only a bar that is in
  front is hidden. Before, the first press seemed to do nothing and the second brought the bar back.
  The tray menu's *Hide bar* / *Show bar* entry does exactly what it says.
- Bringing the bar forward from a second instance no longer detaches *Always on top* from its setting.

### Not translated

- The application log (`logs\app.log` and the log viewer's entries) stays in English, as do the
  error texts Google and Windows return, so a log reads the same whatever the UI language.

## [0.4.0] – 2026-11-04

The bar can start with Windows and live at a screen edge, and removing an icon no longer asks.
Note for whoever tests this build: docking, auto-hide and the hand-off to full-screen programs talk
to the shell through `SHAppBarMessage` and have been checked in code and unit tests only, not yet on
a screen. The defaults (free window, message boxes for questions) are unchanged.

### Added

- *Settings → Window → Start with Windows* registers the app in the current user's Run key, and
  *Start hidden* keeps the bar in the tray until it is shown from the tray icon or with the hotkey.
  The entry is repaired at startup when the program was moved and removed when the setting is
  turned off. Starting the executable with `--hidden` has the same effect once.
- Failures that need no decision (a shortcut that does not start, a file that cannot be opened with
  a program, a bucket or sync upload that fails, a layout that cannot be saved) appear as tray
  notifications instead of message boxes; clicking one opens the log viewer. Questions (removing a
  group, loading a newer copy, an unreadable layout) still use a message box.
- *Remove* no longer asks. A banner under the icons offers *Undo* for ten seconds (also `Ctrl+Z`)
  and puts the icon back where it was. Removing a group still asks and cannot be undone.
- *Settings → Dock to edge* turns the bar into a desktop toolbar at the top, bottom, left or right
  edge of its screen, like the taskbar: maximised windows stop at it, it stays on top, the grip is
  hidden, and its thickness is the current height or width (drag the inner edge to change it). A
  full-screen program hides it until it is gone. *Auto-hide* collapses the bar to a two-pixel strip a
  second after the pointer leaves and brings it back when the pointer touches the edge, the hotkey is
  pressed or the bar is shown from the tray. Undocking returns the bar to where it was.

## [0.3.0] – 2026-11-04

The first release that changes what you see. Each change is a setting, and the defaults keep the
bar itself looking as it did in 0.2.0. Note for whoever tests this build: the dialogs have fixed
widths and the Fluent controls are taller than the classic ones; their layouts have been checked in
code only, not yet on a screen in light and dark mode.

### Added

- Dialogs use the Windows 11 (Fluent) look and follow the system light/dark mode; *Settings → Window*
  turns this off and brings the classic dialogs back. Hint and warning text recolour for dark surfaces.
- *Settings → Bar background*: **Mica** or **Acrylic** (Windows 11 22H2 or later) lets the desktop
  show through the bar with a blur, tinted with the theme colour; the transparency slider then sets how
  much of the blur shows while the icons stay sharp. The group popup gets the same treatment. Older
  Windows keeps the solid colour with whole-window transparency.
- Windows High Contrast: the bar switches to the system colours (and a solid background) while it is
  on, and back to the theme when it is turned off.
- Screen readers get a name for every icon (its caption, with the location and state as help text)
  and for the grip, close and options buttons; Tab now leaves the grid and reaches the close and
  options buttons.
- The Google client secret is stored encrypted for the Windows user (DPAPI) in `sync.json`; a value
  saved in clear by an earlier version is encrypted the first time 0.3.0 runs. The Sync dialog shows
  it as a password field.

### Changed

- The tray icon is rendered at the notification area's size for the screen's DPI instead of being
  scaled down from 32 px.
- Keyboard and mouse activation are told apart by the input device, not by a 150 ms timing window.
- The tray menu is a WPF menu that takes the dialogs' look (light or dark) and the screen's DPI; the
  tray icon is re-rendered when the bar moves to a screen with another scale.
- Whether a saved bar position is still on a screen is checked against the real monitors instead of
  the virtual screen's bounding box, so a bar lost in the dead corner of an L-shaped layout comes back.
  On layouts that mix display scales the check errs on the side of leaving the bar where it is.
- Errors from the sync feature that are programming mistakes are no longer hidden behind a status line.

## [0.2.0] – 2026-11-04

Reliability release after three independent code-review rounds. No new icons or dialogs; everything
that could lose data, hang the bar or mislead the user was fixed. Two things to know when upgrading
from 0.1.0:

- `layout.json` now has a sibling `layout.json.bak` (the previous save) and, when the file was
  written by a newer version, a one-time copy `layout.json.v<N>.json`.
- Items a version does not understand are kept as placeholders instead of being dropped, and the
  file keeps the newer version number, so you can go back and forth between versions safely.
- If 0.1.0 created two `DesktopIconGrid` folders in your Google Drive (it could, when two devices
  connected for the first time at the same moment), copies from both are still listed; new saves go to
  the folder that was created first, and the log notes that two folders exist.

### Changed

- Layout saves are durable: written through a temporary file with write-through and `File.Replace`,
  an unreadable file is quarantined as `layout.json.corrupt-<timestamp>` and the backup is loaded.
- Sync conflicts are decided by Google Drive's modification time plus a per-install device id, and
  copies are replaced by file id, so two copies with the same name can never be swapped.
- The running badge matches the program's full path instead of its name; File Explorer and
  Command Prompt no longer light up because the shell is running.
- Special icons always react to a single click, even when shortcuts are set to double-click.
- Hotkey registration keeps the previous key when the new one is taken, and reports the Windows error.
- Dropped file paths are quoted by Windows rules and environment variables are expanded only once.

### Added

- Single instance: starting the app again brings the running bar to the front.
- Timeouts on every network phase: sign-in, metadata, uploads, download headers, stalled bodies,
  icon downloads from the web (with a Cancel button).
- Resumable uploads follow Google's protocol: the `Range` on a 308 decides what is re-sent, a failed
  chunk probes the session before retrying, and retries back off with jitter and honour `Retry-After`.
- The pending sync upload, including one already running, is finished before the app exits.
- Mark-of-the-Web on files downloaded through the Bucket Viewer (SmartScreen and Protected View treat
  them as downloads, with Google Drive named as the source); executables are revealed in Explorer
  instead of being started.
- A revoked sign-in clears the stored token so *Connect…* reappears in the sync dialog.
- Icon lookups run on one dedicated message-pumping STA worker, so a slow or sleeping drive never
  stalls the bar; stale results are dropped and misses are retried later.
- The log records stack traces, the process scan and icon resolution run off the UI thread.
- Windows CI on every push, a release workflow on `v*` tags, this changelog, and a Windows-only test
  project for the WPF-side logic.

### Fixed

- A startup error no longer leaves a window-less process behind.
- A second *Exit* while the pending upload is being flushed is ignored; the bar is disabled meanwhile.
- Press-and-hold (Sound Replayer) cannot get stuck after a dialog interrupts it.
- Drop handling runs outside the OLE callback, so dialogs opened by a drop no longer hang Explorer.
- Run logs are complete: output is drained before the exit line is written.
- The log viewer's keyboard shortcuts work; the close button's tooltip says it hides to the tray;
  the bar no longer appears in the taskbar or Alt+Tab.
- Two devices connecting to Google Drive at the same moment no longer end up with two
  `DesktopIconGrid` folders.

## [0.1.0] – 2026-11-04

First version: launcher bar with grip, close and options buttons; shortcuts, groups (folders),
Windows items, Multi Launch; drag to reorder, drop from Explorer and browsers, right-drag to place,
left-drag to open files with a program (`{file}`/`{files}`); themes; text icons (宋體 default);
hover names; show/hide hotkey; transparency; run logs and running badge; CPU and disk gauges;
Google Drive sync with named copies and device names; Google Drive Bucket with uploads and the
Bucket Viewer; tray icon; name table editor; Sound Replayer; icons from web addresses; log viewer.
