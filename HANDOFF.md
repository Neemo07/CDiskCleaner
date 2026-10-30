# HANDOFF — CDiskCleaner (WPF / .NET Framework 4.8 disk cleaner for C:)

Last updated: 2026-10-20 (session 2, after agent interruption)

## 1. Goal
Build a WPF app (.NET Framework 4.8) that implements the Arabic spec in `C:\Users\ImPro\Desktop\Clean C.txt`:
scan drive C: → classify candidates A/B/C/D → show path/size/type/mtime/reason/safety/recoverability/space →
**never delete without explicit per-item approval** → per-item choice (Recycle Bin / permanent / skip) →
final report (space before, after, recovered, deleted count, skipped count, items still needing review).

User decisions (from the in-app question round):
- Scope: **full application** (scan + classify + per-item review + report).
- Deletion: **per-item choice** between Recycle Bin, permanent delete, and skip.
- UI: **Arabic + English**, instant switch, RTL/LTR.

## 2. Rules (source: `C:\Users\ImPro\Desktop\Clean C.txt`)
- "ممنوع منعاً باتاً: حذف أي ملف تلقائياً … نقل أو إعادة تسمية أي ملف … تعديل Registry … إلغاء تثبيت البرامج … تنفيذ أمر حذف جماعي بدون موافقتي."
- "لا تعتبر مجرد 'عدم استخدام الملف حالياً' سبباً كافياً للحذف" — must relate each file to its function/programs/projects.
- For every candidate show: full path, size, type, last modified, reason, safety, recoverability, space reclaimed.
- Classification: **A** safe-to-delete, **B** needs review, **C** must not delete, **D** unknown → user decides.
- Folders with many candidates: show folder + total size + reason; do not delete contents before approval.
- After approval delete **only** the approved item, then report result + space.
- Final re-scan report; "الأولوية القصوى هي سلامة النظام والبيانات".

Global rules also in effect: `C:\Users\ImPro\.config\opencode\AGENTS.md` (smallest change, verify before claiming,
VERIFIED vs ASSUMED, never fabricate numbers, no unrelated refactors).

## 3. Done (verified)
- Solution `C:\Users\ImPro\Desktop\TEST\CDiskCleaner\` — src `CDiskCleaner` (SDK-style, `net48`, `UseWPF`),
  `App.config` long-path switches, `app.manifest` (asInvoker, per-monitor v2, longPathAware).
- `CDiskCleaner.sln` regenerated with `dotnet new sln --format sln` + `dotnet sln add` (SDK 10 defaults to `.slnx`).
- Services: `FileSystemScanner.cs` + `.Cleanup.cs` (read-only scan: temp/browser caches/update cache/thumbnails/
  INetCache/crash dumps/WER/prefetch/Windows logs, generic walk, build outputs, node_modules, installers, old
  logs, large files, duplicate hashing, program leftovers, protected C-list), `DeletionService` (SHFileOperation
  recycle + permanent delete), `PathSafety` (conservative allow/deny guard), `InstalledPrograms` (registry **read**),
  `ReportBuilder`, `Loc` (atomic string cache, safe from background threads), `Format`.
- ViewModels: `MainViewModel` (scan, filter, per-item approve/skip, session counters, language toggle, elevation,
  report), `CategoryFilterOption`, `RelayCommand`, `ObservableObject`.
- Views: `MainWindow` (header/sidebar/list/details/footer), `Views/ConfirmDeleteWindow`, `Views/ReportWindow`,
  `Views/ConfirmBulkDeleteWindow` (one confirmation for a whole selection).
- Resources: `Theme.xaml` + `Strings.ar.xaml` / `Strings.en.xaml` (291 keys each, key parity proven by XML parse).
- **Smoke tests**: `tests/CDiskCleaner.SmokeTests` (net48 console, no NuGet) → **35/35 checks PASS**
  (Format, PathSafety guard, scanner classification of build-output/node_modules/installer/log/large/duplicate,
  permanent delete, group delete, recycle, protected-path refusal, ReportBuilder).
- `dotnet build CDiskCleaner.sln` → 0 warnings / 0 errors (both projects).
- Window now fits the screen: 1217×684 on a 1366×768 screen, 16:9, centred, side columns shrink below 1280 px
  (`MainWindow.ApplyInitialSize`).
- Sidebar (categories + scan options) is wide enough that **no text is clipped and no scrollbar is needed**:
  column 272 px (compact) / 300 px, check-box labels wrap, the primary "بدء الفحص" button sits at the top of the
  sidebar, the green "no automatic deletion" note is one line, and all five categories are visible.
  Verified on screen: `docs/screenshot-main-ar.png`.
- Fixed: `ProgressBar Value` TwoWay on read-only `UsedPercent` → `Mode=OneWay`.
- Fixed: `ScanOptions.IsSystemDriveScan` compared the **drive letter** (any C: path = system scan) → now compares
  the scan root with the drive root, so a folder scan uses the user-data rules (this was found by the smoke tests).
- **Verified on the real machine (quick scan of C:)**: 98 candidates / 25.1 GB / 63,902 files / 19,939 folders /
  15 inaccessible folders. Candidates include protected C: items (pagefile.sys, hiberfil.sys, WinSxS, Program Files
  leftovers as D), old WER reports (A), old logs (B) and unknown large folders (D). The app selects the first
  *actionable* item after the scan, the details pane shows the full path (wrapped), and the confirm dialog was
  opened and cancelled during verification.
- Arabic/RTL **and** English/LTR both verified on screen (`docs/screenshot-*.png`).
- **Select all (2026-10-30)**: the candidate list has a selection column (first column, so right-most
  in RTL) with per-row checkboxes plus a master checkbox in that column's header. The master box ticks
  **only what is visible right now** (category filter + search + "hide skipped") and only rows where
  `ScanItem.CanBeDeleted` is true, so protected C: items can never be ticked. Ticking anything reveals a
  bar at the bottom of the list card: "تم تحديد N • SIZE" + "حذف المحدد..." + "إلغاء التحديد".
  `حذف المحدد...` opens ONE `Views/ConfirmBulkDeleteWindow` that lists every selected item with its size,
  offers "نقل إلى سلة المحذوفات" (default) and "حذف نهائياً" (disabled until the acknowledgement box is
  ticked), and only then `MainViewModel.DeleteManyAsync` deletes each item separately through the existing
  `DeletionService` + `PathSafety.IsDeletable` guard, recording every outcome in the session.
  Verified on the real machine (quick scan of C:, 93 candidates): header box Off → On, first row box On,
  bar "80 selected • 16.8 GB" (13 C: rows correctly disabled, 93 − 13 = 80), delete button enabled, bar
  hidden again after "Clear selection"; same after switching to English/LTR; the bulk dialog rendered
  (640×595) and was cancelled with ESC. Nothing was deleted during verification.
- **Row-level selection fix (2026-10-30)** — the reported bug: ticking a *single* row updated only the
  checkbox visual, `ScanItem.IsSelected` never changed, so the selection bar never appeared and "حذف المحدد"
  stayed out of reach for a partial pick (select-all worked because it writes the model directly).
  Root cause: on the `CheckBox` inside the `DataGridTemplateColumn.CellTemplate`, the `IsChecked`
  **`Mode=TwoWay` binding did not write back to the source** (instrumented `PropertyChanged` trace: 0 events
  for a row click, while select-all produced the expected burst). Fix: the row box now handles the routed
  `Checked`/`Unchecked` events (`MainWindow.SelectionCheck_OnToggle`) and pushes `box.IsChecked` into
  `ScanItem.IsSelected`; the binding stays for the model → visual direction (select-all). `Checked`/
  `Unchecked` (not `Click`) is deliberate: they also fire for keyboard Space and for UI Automation's
  `TogglePattern`, which bypasses `Click`.
  Verified on the real machine (quick scan of C:, 30 checks, **0 failures**, nothing deleted):
  1 row ticked by real mouse → bar appears with the delete button enabled, header stays Off; 2 rows → bar
  stays; untick both → bar hidden; select-all → all rendered rows On, header On; untick ONE row inside the
  batch → that row Off, neighbours On, header Off, bar and delete button still active; the partial batch
  (80 items / 11.4 GB after a select-all minus one row, plus 1 / 2 / 4 / 7 row picks) opens the same
  `ConfirmBulkDeleteWindow`, and ESC cancels it with the main window alive and no item deleted.
  The same build also re-passed the select-all + English/LTR check and the smoke tests (35/35).
- **App icon (2026-10-30)**: `tools/make-icon.ps1` draws the icon with `System.Drawing` (no image
  generator, no NuGet) and packs a real multi-size `.ico` (16/24/32/48/64/128/256 — DIB frames up to 128,
  PNG for 256, correct AND masks) → `src/CDiskCleaner/Assets/app-icon.ico`. Design: Windows 11 rounded
  tile in the app accent (`#0F766E` → `#115E59`), a flat drive case with the Explorer-style seam + LED,
  plus two white sparkles for "clean". Wired as `<ApplicationIcon>` (exe/shell/taskbar) and as a WPF
  `<Resource>` referenced by `Icon="/Assets/app-icon.ico"` on `MainWindow`, `ConfirmDeleteWindow`,
  `ConfirmBulkDeleteWindow` and `ReportWindow`. Verified: the Debug and Release exes carry it
  (`Icon.ExtractAssociatedIcon` → 32×32, teal corner, light drive body), the RTL title bar shows it,
  and `docs/icon-preview-256.png` / `docs/icon-preview-sizes.png` are the rendered proofs.
  Re-run `powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 -PreviewSheet` after any tweak.
- New tooling: `tools/ui-verify.ps1` (drives the app through UI Automation, captures the real states, always
  cancels the confirm dialog), `tools/ui-diagnose.ps1` (live state dump while scanning), `tools/ui-read-text.ps1`
  (reads the real text of the window), `tools/ui-sidebar-check.ps1` (before/during/after captures),
  `tools/ui-measure-sidebar.ps1` (prints the exact geometry of the side panel in every state),
  `tools/publish-single.ps1` (builds the one-file distribution and proves it starts on its own). Main controls
  carry `x:Name` values so they can be addressed by AutomationId (StartScanButton, FullScanCheck, RecycleButton,
  SkipButton, LanguageButton, ReportButton...).

## 4a. Single file distribution (VERIFIED)
- `tools\publish-single.ps1` → **`dist\CDiskCleaner.exe` (198,144 bytes, one file)**, Release build, no PDB.
  The script builds, copies, asserts the folder holds exactly one file, launches it and checks it responds.
- The app has **no NuGet dependencies** and all XAML/BAML is compiled into the assembly, so the exe is
  self-contained.
- **Hard constraint on .NET Framework**: the long-path switches
  (`Switch.System.IO.UseLegacyPathHandling`, `Switch.System.IO.BlockLongPaths`) are read **before `Main` runs**,
  so they can only come from `<app>.exe.config`. Both `AppContext.SetSwitch` and
  `AppDomain.CurrentDomain.SetData("APP_CONFIG_FILE", …)` were tested and are too late.
  → `dist-portable\` contains the exe **plus** the 327-byte config (full long-path support).
  → `dist\` contains the exe alone; the app then shows a footer notice
  ("بدون ملف الإعداد بجوار البرنامج لن يُفحص ما هو أطول من 260 حرفاً") and skips those paths.
- A/B measured (same machine, same minute, `tests/CDiskCleaner.SmokeTests` real quick scan of C:):
  with switches → 100 candidates / 25.9 GB / A=59 B=18 C=14 D=9 / 64,165 files / 15 inaccessible;
  without → 100 candidates / 25.9 GB / A=59 B=18 C=14 D=9 / 64,033 files / 15 inaccessible.
  **Identical results**, only 0.2 % of the touched files differ (the ones under >260 char paths).
- The welcome screen no longer has a big "بدء الفحص" button; it points to the sidebar button instead.
- **The side panel no longer changes width when the scan starts** (the complaint that triggered this): the footer used
  to grow during the scan (a second status line + the progress row), which shrank the content area, which made the
  side panel's ScrollViewer show a vertical scrollbar and lose 17 px of content width. Fixes: the footer is now one
  line tall in every state, the quick-mode hint moved to the centre panel, the category rows are single-line, and the
  category ListBox has no horizontal scrollbar. Measured with `tools/ui-measure-sidebar.ps1`:
  `x=1005 w=246` in all five states (initial / quick mode / scanning / later / finished).

## 4. In progress / pending
- P1: a **full** C: scan (not just the quick scan) has never been run; the quick scan finds ~100 candidates /
  25.9 GB / 64k files, so a full scan will take a while and is worth running once by hand.
- P1: the report window has never been opened on screen (only the builder is unit-checked).
- P2: `dotnet build CDiskCleaner.sln` works, but nothing verifies the XAML at runtime (only manual screenshots).
- P2: the bulk dialog is reachable only through **Win32 window enumeration**
  (`EnumWindows` for the pid + `AutomationElement.FromHandle`): it is not listed as a UI Automation
  *desktop child* for the process (only the main window is), so `AutomationElement.RootElement.FindAll`
  never finds it. Enumerated this way on 2026-10-30, the dialog **did** open for a 1-row, 2-row, 4-row,
  7-row pick and for a select-all-minus-one batch (80 items / 11.4 GB), and ESC cancelled it every time.
  The "acknowledgement enables حذف نهائياً" toggle is still only checked by reading the code (the button
  correctly starts disabled); failure mode is safe.
- P2: `dist-portable\CDiskCleaner.exe` refreshed on 2026-10-30 from the Release build (442,368 bytes —
  the embedded icon accounts for the growth) with the existing `CDiskCleaner.exe.config` kept next to it;
  the exe starts, responds and carries the icon (checked). `dist\` (the single-file variant) was **not**
  rebuilt — run `tools\publish-single.ps1` for that (it deletes `dist\`).
- P2: numbers render in Arabic-Indic digits (٥٣) because the thread UI culture is ar-EG. Consistent inside the app
  but the exported report text keeps Latin digits. Change the thread culture if Latin digits are preferred.
- P2: a truly self-contained single file (no .NET Framework on the target PC) is only possible by retargeting to
  .NET 8 + `PublishSingleFile` — not done, because the project must stay on .NET Framework 4.8.

## 5. Open issues / risks
- Recycle Bin: moving a file to the bin does **not** free space; the report must say so (already implemented:
  `Msg.RecycleDone`, `Report.NoteRecycle`, plus `LikelyPermanentlyDeleted` detection via free-space delta).
- Program Files leftovers are the only `Program Files` paths the guard allows (tag `ProgramLeftover`, top level only).
- `Loc` must stay cache-based: `Application.Resources` is not safe to read from the deletion thread.

## 6. Files (45 source files, ~366 KB)
```
CDiskCleaner.sln
src/CDiskCleaner/CDiskCleaner.csproj, App.xaml(.cs), App.config, app.manifest
src/CDiskCleaner/MainWindow.xaml(.cs)
src/CDiskCleaner/Models/{Enums,ScanOptions,ScanItem,ScanResult,ScanProgress,DriveSnapshot,
                          SessionState,OperationRecord,DeletionOutcome,ReportData}.cs
src/CDiskCleaner/Services/{FileSystemScanner,FileSystemScanner.Cleanup,DeletionService,PathSafety,
                            InstalledPrograms,ReportBuilder,Loc,Format}.cs
src/CDiskCleaner/ViewModels/{MainViewModel,CategoryFilterOption,RelayCommand,ObservableObject}.cs
src/CDiskCleaner/Views/{ConfirmDeleteWindow,ConfirmBulkDeleteWindow,ReportWindow}.xaml(.cs)
src/CDiskCleaner/Converters/*.cs (6)
src/CDiskCleaner/Resources/{Theme.xaml,Strings.ar.xaml,Strings.en.xaml}
src/CDiskCleaner/Assets/app-icon.ico (from tools/make-icon.ps1)
tools/make-icon.ps1
```

## 7. Environment
- Windows 10 19045, .NET Framework 4.8 installed, VS 2022 Professional, dotnet SDK 10.0.401 (also 5/6/7/8).
- Build: `dotnet build src\CDiskCleaner\CDiskCleaner.csproj -v:m` (net48 WPF builds fine with the SDK).
- Run: `src\CDiskCleaner\bin\Debug\net48\CDiskCleaner.exe`.
- Temp dir to use: `C:\Users\ImPro\AppData\Local\Temp\opencode`.
- Not a git repository (no commits, no VCS) — keep a clean file tree and this handoff instead.

## 8. Do not repeat
- Don't hand-write .sln GUIDs; regenerate with `dotnet new sln --format sln` + `dotnet sln add` (SDK 10 writes
  `.slnx` by default, which VS 2022 cannot open).
- Don't bind read-only VM properties to TwoWay targets (ProgressBar/TextBox) — use `Mode=OneWay`.
- Don't call `Application.TryFindResource` from background threads — use the `Loc` cache.
- **Never trust a `PrintWindow` screenshot of a right-to-left window: it returns a horizontally MIRRORED bitmap**
  (a mirrored "d" looks like "b", which is how this fake "bidi bug" was chased for a while). Use a screen grab
  (`Graphics.CopyFromScreen` of the window rect, with the window on top) — see `tools/ui-verify.ps1`.
- `Process.MainWindowHandle` can be cached to a transient window: call `Process.Refresh()` and re-read it, or
  enumerate the visible top-level windows of the pid.
- UI Automation `PropertyCondition` needs the exact value type (`NativeWindowHandleProperty` → Int32), and
  `New-Object` picks the wrong overload — use `[Type]::new($prop, $value)`.
- `DataGridColumn.Width` is a `DataGridLength` (enum `DataGridLengthUnitType`, in `System.Windows.Controls`),
  not a `GridLength`.
- **Any panel that must not change width has to keep a constant height everywhere**: a hidden row/element that
  appears later shrinks the viewport, and the vertical scrollbar that then appears eats ~17 px of content width.
- On .NET Framework the app config file is read before `Main`: runtime `AppContext.SetSwitch` and
  `APP_CONFIG_FILE` are too late for the long-path switches (tested).
- **A `CheckBox` inside a `DataGridTemplateColumn.CellTemplate` can silently drop the target → source write
  of a `Mode=TwoWay` `IsChecked` binding** (mouse click changes the visual, the source property stays
  `false`; select-all works because it writes the source). Don't rely on that binding alone: handle the
  routed `Checked`/`Unchecked` events and set the model property in code. `Click` is not enough either —
  UI Automation's `TogglePattern` bypasses it, while `Checked`/`Unchecked` fire for mouse, keyboard and
  automation alike. Instrument `PropertyChanged` before blaming the UI.
- The modal confirm dialog of this app is **not** a UI Automation desktop child: enumerate the pid's
  top-level windows with `EnumWindows` (+ `IsWindowVisible`) and use `AutomationElement.FromHandle`.
  `Process.MainWindowHandle` vs `AutomationElement.Current.NativeWindowHandle` must be compared as
  `[IntPtr]` on both sides, otherwise every window looks like "not the main window".
- PowerShell's `[int]` cast **rounds** (banker's rounding), it does not truncate: `[int](127/8)` is `16`.
  That silently breaks byte/bit layout code. Use `-shr` / `-band` for masks, or `[Math]::Floor`.
- This app's window is **RTL**, so Windows puts the caption buttons on the left and the title-bar icon on
  the right: a title-bar screenshot of `(0,0,w,36)` shows the icon at the right end, not the left.
