# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**DirHealth** is a free, read-only Windows desktop tool (C# WPF, .NET 8, single .exe) that scans Active Directory for security risks and gives IT admins a hygiene score. No licensing. No server-side dependencies for core features.

**Constraint:** `dotnet` is not installed on the dev machine. Build happens exclusively via GitHub Actions. To trigger a release, push a version tag:
```
git tag v1.x.x && git push origin v1.x.x
```
The GitHub Actions workflow builds a self-contained single-file exe and creates a GitHub Release.

## Build & Test

```bash
# Run all tests (requires Windows + dotnet SDK)
dotnet test src/DirHealth.Tests/DirHealth.Tests.csproj

# Build release exe (do not use — handled by GitHub Actions)
dotnet publish src/DirHealth.Desktop/DirHealth.Desktop.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o ./publish
```

Test framework: **xunit** + **Moq**. Test files: `CryptoHelperTests.cs`, `CsvExporterTests.cs`, `HwidTests.cs`.

## Architecture

```
src/DirHealth.Desktop/
├── App.xaml.cs              ← Startup: theme → login → main window; global crash handler
├── MainWindow.xaml.cs       ← Shell: auto-runs scan on load, persists window state
├── Core/
│   ├── AD/
│   │   ├── AdConnector.cs   ← Holds LDAP connection + credentials (in-memory)
│   │   ├── AdScanner.cs     ← All read-only AD queries (LDAP via DirectorySearcher)
│   │   ├── AdSearcher.cs    ← Free-text AD search across users/computers/groups
│   │   ├── AdWmiClient.cs   ← WMI queries for per-computer detail (disks, admins, sessions)
│   │   └── Models/          ← AdUser, AdComputer, AdGroup, AdOU, AdFinding, WmiDisk, etc.
│   ├── Services/
│   │   ├── ScanDiffCalculator.cs  ← Computes delta between scan runs
│   │   ├── ScanScheduler.cs       ← DispatcherTimer for scheduled auto-scans
│   │   └── UpdateChecker.cs       ← GitHub releases API; startup (5s delay) + manual check
│   ├── Storage/             ← All persistence to %APPDATA%\DirHealth\
│   │   ├── CredentialStore.cs     ← AES-256 encrypted credentials.dat
│   │   ├── ScanCacheStore.cs      ← Cached scan results (JSON)
│   │   ├── ScoreHistoryStore.cs   ← Score trend data
│   │   ├── AcknowledgeStore.cs    ← acknowledged.json for dismissed findings
│   │   └── WindowStateStore.cs    ← Window position/size
│   ├── Crypto/CryptoHelper.cs     ← AES-256 + HMAC-SHA256 helpers
│   ├── Export/
│   │   ├── CsvExporter.cs   ← CsvHelper-based export
│   │   └── PdfExporter.cs   ← PdfSharp; reprint column headers after every NewPage()
│   ├── HWID/HwidManager.cs  ← CPU+MB+Disk → SHA256 (WMI-based)
│   └── Theme/ThemeManager.cs← Dark/Light theme swap via merged ResourceDictionary
├── ViewModels/              ← CommunityToolkit.Mvvm; one VM per view
│   ├── MainViewModel.cs     ← Navigation state; owns all sub-VMs
│   ├── DashboardViewModel.cs← Score, findings summary, score history chart
│   ├── FindingsViewModel.cs ← Filterable/acknowledgeable findings list
│   ├── UserBrowserViewModel / UserDetailViewModel
│   ├── ComputerBrowserViewModel / ComputerDetailViewModel
│   ├── DcInventoryViewModel ← Domain controllers + FSMO roles
│   ├── OuBrowserViewModel
│   ├── GroupManagerViewModel
│   ├── PasswordReportViewModel
│   ├── DomainAdminsViewModel
│   ├── DcInventoryViewModel ← Domain controllers + FSMO roles
│   ├── PrivilegedGroupsViewModel ← 9 hardcoded privileged groups overview
│   ├── DomainTrustViewModel ← Inter-domain trust relationships
│   ├── TimelineViewModel    ← Recent changes (7/30/90d), EqualityConverter for period selector
│   ├── AdSearchViewModel
│   ├── LoginViewModel / SettingsViewModel
│   └── BaseViewModel.cs     ← IsBusy, StatusMessage
├── Views/                   ← One folder per view, XAML + code-behind
└── Resources/
    ├── Styles.xaml          ← Global styles incl. GridViewColumnHeader dark theme
    ├── Icons.xaml
    ├── Strings.xaml
    └── Themes/Dark.xaml + Light.xaml
```

### Key MVVM patterns

- `[RelayCommand]` on `Task FooAsync()` generates `FooCommand` (strips "Async") — bind as `{Binding FooCommand}` in XAML.
- `IsBusy` CanExecute from a subclass: add `partial void OnIsBusyChanged(bool value) => MyCommand.NotifyCanExecuteChanged();` in the subclass (cannot use `[NotifyCanExecuteChangedFor]` across base/subclass).
- CanExecute guards for stale cache: use a private `bool _liveScanCompleted` flag set only after a real scan completes — `LastScanTime != "Never"` is insufficient because a persisted cache sets `LastScanTime` without populating in-memory lists.
- **Navigation cache (5-min TTL):** Browser VMs hold `private DateTime _lastLoaded = DateTime.MinValue` and `private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5)`. Guard at start of `LoadAsync`: `if (_allItems.Count > 0 && DateTime.Now - _lastLoaded < CacheTtl) { ApplyFilter(); return; }`. Add `internal void InvalidateCache() { _lastLoaded = DateTime.MinValue; _allItems.Clear(); }` for credential-change invalidation.
- **Credentials changed → invalidate browser caches:** `SettingsViewModel` exposes `Action? OnCredentialsSaved`. `MainViewModel` wires it in the constructor to call `InvalidateCache()` on all three browser VMs. `SettingsViewModel.Save()` invokes it after persisting credentials.

### AD / WMI

- All AD queries go through `AdConnector` (LDAP via `DirectorySearcher`). Credentials are held in memory; any code path that updates credentials must call both `AdConnector` and `CredentialStore.Save()`.
- `member` attribute has an AD MaxValRange limit (often 15–1500). Groups with more members than the limit require range retrieval (`member;range=0-*`) or only the first N members are returned.
- **Batch LDAP for group members:** `BatchResolveGroupMembers(root, memberDns)` batches DNs in OR-filter groups of 50: `(|(distinguishedName=dn1)...(dn50))`. Reduces N+1 individual `GetEntry` calls to `ceil(N/50)` queries. Uses `Dictionary<string, AdGroupMember>(StringComparer.OrdinalIgnoreCase)` for lookup; missing DNs fall back to `new AdGroupMember { Name = dn, DistinguishedName = dn }`.
- **Parallel multi-query pattern:** `Task.WhenAll` over `Task.Run` lambdas, each opening its own `_connector.GetRootEntry()`. `DirectoryEntry` is not thread-safe — each parallel task needs its own instance. Collect results with `[.. await Task.WhenAll(tasks)]`.
- **`RunCompleteScanAsync`:** Single method that runs all 23 sub-queries concurrently and returns `CompleteScanResult(Findings, Score, InactiveUsers, ExpiringPasswords, DomainAdmins)`. `DashboardViewModel` calls it once per scan — no separate `GetInactiveUsersAsync` / `GetDomainAdminsAsync` calls needed. Old `RunFullScanAsync` and `ComputeComplianceScoreAsync` are thin wrappers around it.
- Every `LoadAsync` / `SelectAsync` method that touches AD must have `catch (Exception ex) { StatusMessage = $"...: {ex.Message}"; }` — unhandled LDAP exceptions cascade to `DispatcherUnhandledException`.
- **Nested group membership:** Use `memberOf:1.2.840.113556.1.4.1941:=` (LDAP_MATCHING_RULE_IN_CHAIN), not plain `memberOf=`. Plain filter only finds direct members.
- **Security filters:** Always exclude disabled accounts with `!(userAccountControl:1.2.840.113556.1.4.803:=2)`.
- **PSO attributes:** Direct `(int)` cast crashes when AD returns `long`. Use `GetPsoInt(props, name)` which returns `-1` when the attribute is absent (sentinel to avoid false positives) and handles `int`/`long` via pattern matching.
- **`whenChanged`/`whenCreated`:** DirectorySearcher returns these as `DateTime` objects, not strings. Use `val is DateTime dt`. LDAP filter format: `yyyyMMddHHmmss.0Z`.
- **Domain trusts:** Query `(objectClass=trustedDomain)` under `CN=System,<domainDn>`. `trustDirection` 1=Inbound, 2=Outbound, 3=Bidirectional. Bit 8 of `trustAttributes` = Forest Trust.

### WPF quirks

- **PasswordBox pre-population:** Data binding cannot pre-fill a PasswordBox. Handle `DataContextChanged` and set `PasswordBox.Password = vm.Password` if non-empty. See `LoginWindow.xaml.cs` and `SettingsView.xaml.cs`.
- **`Run.Text` binding:** Always specify `Mode=OneWay` on `Run` elements and `Count`-bindings in DataTriggers — the TwoWay default crashes with `XamlParseException` on readonly properties.
- **ListBox in StackPanel:** StackPanel gives ListBox infinite height; it never scrolls. Use Grid with `<RowDefinition Height="*" />` instead.
- **`DynamicResource` in ControlTemplate Triggers:** Use verbose form: `<Setter Property="X"><Setter.Value><DynamicResource ResourceKey="Foo" /></Setter.Value></Setter>`.
- **`Border` has no `Command`.** Use `<Button BorderThickness="0" Padding="0" FocusVisualStyle="{x:Null}" Cursor="Hand" Command="...">` with `<Border>` as content.
- **WPF Transparent Splash:** `WindowStyle=None` + `AllowsTransparency=True` also requires `Background=Transparent`, otherwise a white border remains.
- **`DispatcherTimer` tick handler:** Register only once in the constructor — not in `Start()` — or the handler accumulates on each call.
- **`DispatcherUnhandledException`:** Wrap file ops in try-catch. `MessageBox.Show` inside causes nested dispatcher loops → cascading dialogs. Crash log: `%APPDATA%\DirHealth\dirhealth.log`.
- **RadioButton + int property:** Use `EqualityConverter` with `ConverterParameter=30` etc. ConvertBack returns `Convert.ChangeType(parameter, typeof(int))`. Registered in `App.xaml` as `{StaticResource EqualityConverter}`.
- **`PropertyChanged` subscription leak:** `DataContextChanged` fires on every navigation. Unsubscribe the old handler before subscribing a new one. See `DashboardView.xaml.cs`.
- **OU load performance:** Never query OU counts during initial load (N×3 LDAP queries). Load counts on selection via `GetOUCountsAsync(dn)`.
- **WPF ListBox virtualization:** Requires bounded height — put ListBox in a Grid row with `Height="*"`, not a StackPanel. Enable with `VirtualizingStackPanel.IsVirtualizing="True" VirtualizingStackPanel.VirtualizationMode="Recycling" ScrollViewer.IsDeferredScrollingEnabled="True"`. `ItemsControl` has no built-in virtualization; replace with `ListBox` + `ItemContainerStyle` using `<ContentPresenter />` to suppress selection highlight.
- **Frozen brushes in code-behind:** Declare `private static readonly SolidColorBrush` fields, initialize inline, and call `.Freeze()` in a `static` constructor. Frozen brushes are immutable and thread-safe; WPF skips change-notification overhead on every render call.
- **Chart skip-unchanged guard:** Track `_lastHistoryCount` (int), `_lastLastScore` (int, value of `history[^1].Score`), `_lastCanvasWidth` (double), `_lastCanvasHeight` (double). Skip redraw only when all four match. Reset all to 0 in `DataContextChanged`. Tracking just count is insufficient: at the 90-entry history cap, `TakeLast(90)` keeps the count at 90 while the data changes.

### Auto-update

- Startup check: `StartupUpdateCheckAsync` (5 s delay, fire-and-forget). Manual check: `ManualUpdateCheckAsync` (no delay, returns diagnostic string shown in Settings).
- GitHub `/releases/latest` returns 404 for pre-releases. Use `/releases` and take the first non-draft entry.
- GitHub CDN omits `Content-Length`. Read file size from `assets[].size` in the API response and store in `UpdateInfo.FileSize`.
- Inno Setup restart: add a second `[Run]` entry with `Check: WizardSilent` for silent-mode restarts. `/RESTARTAPP` is not a valid Inno Setup switch.
- File lock: dispose the `FileStream` before calling `Process.Start` on the downloaded installer.

### Export

- Wrap all exporter calls in try/catch with `StatusMessage = $"Export failed: {ex.Message}"` — file locks and network-share failures must not reach the global crash handler.
- PdfSharp: reprint column headers after every `NewPage()` call.

## Plans & Roadmap

Implementation plans: `docs/superpowers/plans/` — each plan has checkbox steps for agentic execution.

Feature roadmap with LDAP filters and implementation notes: [`docs/feature-roadmap.md`](docs/feature-roadmap.md).

| Phase | Features | Status |
|-------|----------|--------|
| 1 | EOL OS finding + DC Inventory View | ✅ Done |
| 2 | AS-REP Roasting, Unconstrained Delegation, PASSWD_NOTREQD | ✅ Done |
| 3 | Stale Domain Admins, Fine-Grained Password Policies, Privileged Groups Overview | ✅ Done |
| 4 | Domain Trust View, SID History, Timeline / Recent Changes | ✅ Done |
| Perf (v2.6.0) | ListBox virtualization, batch LDAP, parallel scans, navigation cache, frozen brushes | ✅ Done |
| **5 — Next** | GPO Browser, AD Data Quality Report | 🔜 Next |

<!-- forgehive:start -->
## forgehive

This project uses **forgehive** for structured AI-assisted development.

### Session Start (Required)

1. Read `.forgehive/capabilities.yaml`
   - If `status: draft` → tell the user: "Run `fh confirm` to activate capabilities."
   - If `status: confirmed` → load silently and apply throughout the session
2. Read `.forgehive/memory/MEMORY.md` — follow the index links to load project context
3. Run `fh scan --check` to verify the stack snapshot is current

### During the Session

- Only suggest tools and libraries listed in `capabilities.yaml`
- If a capability has a `check` field: verify it before use
- If a capability has a `fulfill` field and the check fails: fulfill it
- At session end: append brief notes to `.forgehive/state/YYYY-MM-DD.md`
- If you learn something non-obvious about the project, offer to persist it to memory

### Skills — Progressive Loading

Before starting a technical task, read `.forgehive/skills/INDEX.yaml` to find relevant skills.
Load only the skills matching your current task — not all skills at once.

Examples:
- Working with TypeScript types → load `expert/typescript-patterns.md`
- Database migration → load `expert/database-patterns.md`
- Reviewing a PR → load `expert/code-review.md`
- Security review → load `expert/security-checklist.md`
- Performance issue → load `expert/performance-patterns.md`

### Workflow Commands

ForgeHive installs slash commands in `.claude/commands/`:
- `/fh-start-task` — start a new feature branch with full context loaded
- `/fh-ship` — pre-ship checklist: tests, diff review, PR draft
- `/fh-review` — structured code review using the review skill
- `/fh-hotfix` — minimal hotfix protocol (< 50 lines rule)

### Agent Memory

Each agent has persistent memory in `.forgehive/agents/memory/<name>.md`.
Before activating a party set, read the relevant agent memory files.
Update memory files when agents make significant decisions.
Format: `[YYYY-MM-DD] <decision or learned context>`

### Party Mode

Slash commands auto-configured by forgehive:
- `/party` — activate build agents (Viktor + Kai + Sam)
- `/design-party` — activate design agents (Suki + Viktor)
- `/review-party` — activate review agents (Kai + Sam + Eli)
- `/full-party` — activate all agents

### Prohibited

- Writing to paths outside the project root
- Modifying `.forgehive/scan-result.yaml` manually
- Skipping the session-start capability and memory check
- Suggesting tools not in `capabilities.yaml` without explicitly noting the deviation
- Activating party agents without reading their memory files first

<!-- forgehive:end -->
