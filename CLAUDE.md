# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**DirHealth** is a free, read-only Windows desktop tool (C# WPF, .NET 8, single .exe) that scans Active Directory for security risks and gives IT admins a hygiene score. No licensing. No server-side dependencies for core features.

**Constraint:** `dotnet` is not installed on the dev machine. Build happens exclusively via GitHub Actions. To trigger a release, push a version tag:
```
git tag v1.x.x && git push origin v1.x.x
```
The GitHub Actions workflow builds a self-contained single-file exe and creates a GitHub Release.

## Active Plugin Context

### Aktive Plugins
- **Workflow**: superpowers (writing-plans, executing-plans)
- **Dev**: systems-programming (Go), context7
- **Security**: insecure-defaults, supply-chain-risk-auditor, security-guidance
- **Code Quality**: comprehensive-review, sonarqube
- **Git**: commit-commands, git-cleanup, gh-cli
- **Testing**: tdd-workflows, property-based-testing
- **Overnight**: claude-session-driver, double-shot-latte

### Nicht relevant — nicht laden
- playwright, frontend-mobile-security, frontend-mobile-development
- DB Plugins, c4-architecture, full-stack-orchestration
- blockchain, gaming, marketing, ml-ops, kubernetes
## Build & Test

```bash
# Run all tests (requires Windows + dotnet SDK)
dotnet test src/DirHealth.Tests/DirHealth.Tests.csproj

# Build release exe (do not use — handled by GitHub Actions)
dotnet publish src/DirHealth.Desktop/DirHealth.Desktop.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true -o ./publish
```

Test framework: **xunit** + **Moq** (`net8.0-windows`; DPAPI tests require Windows, covered by CI on `windows-latest`). Test files: `CryptoHelperTests.cs`, `CsvExporterTests.cs`, `HwidTests.cs`, `CredentialStoreTests.cs`, `UpdateCheckerTests.cs`, `AdConnectorEscapeTests.cs`, `AdSearcherValidationTests.cs`, `AdWmiClientAllowlistTests.cs`. `UpdateCheckerTests` mocks the GitHub API via a `SequencedMockHandler` (`HttpMessageHandler` subclass); `CredentialStoreTests` uses a backup/restore pattern on `credentials.dat` to preserve real dev-machine credentials.

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
│   │   ├── CredentialStore.cs     ← DPAPI-encrypted credentials.dat (CurrentUser scope); migration from legacy HWID format on first load
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
- **WMI connectivity:** Requires port 135 (DCE/RPC) + dynamic RPC ports + Windows firewall rule "WMI-In" enabled on target machines. Out of scope: write operations, patch management, RDP control.
- **Terminal Server HWID fallback:** On RDS/Terminal Server environments, append UserName to prevent all sessions sharing the same HWID: SHA256(CPU+MB+Disk+UserName). `AdWmiClient` covers: disks, local admins, active sessions, event log.
- **`AdSearcher` search modes:** 5 modes — Users, Computers, Groups, OUs, Any. Free-text across `sAMAccountName`, `displayName`, `cn`, `description`.
- **`AdSearcher` — LDAP Filter mode is intentional raw passthrough** (power-user feature). `AdSearchView` shows a persistent amber warning banner whenever this mode is active — never remove the `IsLdapMode` binding or the warning `Border`.
- **`AdWmiClient.GetEventLogAsync` — `logName` allowlist:** Validate against `{"System", "Security", "Application"}` before WQL interpolation. Any other value returns an empty list. Never interpolate a free-form `logName` into a WQL query string.

### Security

- **LDAP filter value escaping:** Use `AdConnector.EscapeFilterValue(value)` for any user-supplied string that goes into an LDAP filter *attribute value* position (e.g. `(sAMAccountName={value})`). It escapes `\`, `*`, `(`, `)`, NUL per RFC 4515. Distinguished from `EscapeDn()` which only escapes `(`, `)`, `\` for use in DN position within a filter.
- **Credential storage — DPAPI:** `CredentialStore` uses `ProtectedData.Protect/Unprotect(null, DataProtectionScope.CurrentUser)`. Keys are bound to the Windows user's login secret and machine TPM — not derivable offline, even from a copy of the file. NuGet: `System.Security.Cryptography.ProtectedData 8.0.0`. `Load()` includes a transparent migration path: if DPAPI fails, tries the legacy HWID-based format and re-saves with DPAPI on success. Never revert to HWID-based key derivation — on VMs all three WMI values return `"UNKNOWN"`, making the key globally known.
- **SID format validation:** `AdSearcher.SearchBySid` validates the query against `^S-\d+-\d+(-\d+)*$` before passing it to the LDAP filter. Reject anything that doesn't match.
- **OU path validation:** `AdSearcher.SearchByOu` validates the query starts with `CN=`, `OU=`, or `DC=` (case-insensitive) before passing to `GetEntry`. Rejects free-form input that could redirect to a rogue LDAP server.

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
- **Download URL host pinning:** Before downloading the installer, validate that the URL host is `github.com` or `objects.githubusercontent.com` and scheme is `https`. Reject anything else with a user-visible error. Use `Guid.NewGuid():N` in the temp filename — never a predictable fixed name.

### Installer (Inno Setup / ISPP)

- **No backup block in `CurStepChanged(ssInstall)`:** the installer never writes to `%APPDATA%`, so renaming user data there was pointless and made it invisible after upgrade. Removed.
- **No standalone `#13#10` lines:** ISPP reads a line starting with `#` as a preprocessor directive. Always append `#13#10` to the previous line, never on its own line.
- **No `on E: Exception do`:** Inno Setup Pascal has no typed exception handler. Use bare `except ... end` only.
- **No `VersionInfoVersion` with a suffix:** `0.0.0-dev` is not a valid Windows version string. Omit `VersionInfoVersion` entirely on dev builds.

### Export

- Wrap all exporter calls in try/catch with `StatusMessage = $"Export failed: {ex.Message}"` — file locks and network-share failures must not reach the global crash handler.
- **CSV formula injection:** `CsvExporter.SafeField()` prefixes any field value starting with `=`, `+`, `-`, `@`, tab, or CR with a leading apostrophe so spreadsheet apps don't execute it as a formula. Route every exported field through it.
- **No CSV export for single detail rows:** a one-row CSV (e.g. a single user detail) isn't useful — only offer list exports.
- PdfSharp: reprint column headers after every `NewPage()` call.
- **Export CanExecute guard:** All export command buttons must have `CanExecute = false` when their data list is empty. Wire via `[RelayCommand(CanExecute = nameof(HasData))]` or equivalent.
- **`FullReportData` record:** `(string Domain, int Score, List<AdFinding> Findings, List<AdUser> InactiveUsers, List<AdUser> ExpiringPasswords, List<string> DomainAdmins)` — passed to `PdfExporter.ExportFullReport()`.
- **`PdfPageBuilder`:** Private helper class inside `PdfExporter` that encapsulates shared page anatomy: dark header bar (`#0f172a`, 28pt), white content area (40pt margins all sides), light footer strip (`#f8fafc`, 20pt). Reused across all 4 export methods.
- **PDF score colors:** >=80 → green `#7DFFB3`, >=60 → yellow `#FCD34D`, <60 → red `#FCA5A5`.
- **PDF table row spacing:** 6pt top/bottom padding, 10pt right, 13pt left (accounts for 3pt left accent border). Dates and SAM account names rendered in monospace. 3pt gap between rows.
- **PDF logo:** Loaded via `pack://application:,,,/Resources/icon_128.png`.
- **`DaysToExpiryColorConverter`:** red = <14 days, orange = <30 days, yellow = <60 days, green = >=60 days. Used in Password Report user list.
- **`ThemeManager`:** Persists selected theme (Dark/Light) to `HKCU` registry key so the preference survives restarts. Swaps theme via merged `ResourceDictionary`.

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
| Security | LDAP injection fixes, DPAPI credentials, WMI allowlist, update URL pinning, LDAP filter warning | ✅ Done |
| **5 — Next** | GPO Browser, AD Data Quality Report | ⏸️ On hold (no start date) |

**Current focus (since v2.7.0): stability, performance, and code quality — no new features.** Do not propose Phase 5, new views, or new findings. Scope is limited to bugfixes, performance work, test coverage, refactoring, and security hardening. Phase 5 stays on hold until the user initiates it. Latest release: **v2.8.3** (2026-06-14).
