# Changelog

All notable changes to DirHealth are documented here.

---

## [2.8.2] — 2026-05-19

### Fixed
- **Findings acknowledge binding** — `SelectedFinding.IsAcknowledged` chained XAML path replaced with `SelectedFindingIsAcknowledged` computed property on `FindingsViewModel`; eliminates implicit null-propagation dependency in `DataTrigger` bindings and adds explicit `OnPropertyChanged` after each Acknowledge/Unacknowledge call.

---

## [2.8.1] — 2026-05-18

### Fixed
- **NavButton active indicator** — Active nav item now shows a 3 px accent-blue left stripe and accent foreground via `Tag` + `EqualityConverter` binding; no more "all buttons look the same" state.
- **ProgressBar global style** — Implicit `TargetType="ProgressBar"` style sets `Background=Transparent`, `Foreground=AccentBrush`, `BorderThickness=0`; eliminates system-accent bleed in Light theme.
- **OU Browser card shadow clipping** — Stat cards in OU detail now have bottom/right margin so the 12 px drop shadow is no longer cut off.
- **Privileged Groups empty state** — When all 9 groups have zero members after loading, a "No elevated groups have members — your AD is clean. ✓" message is shown via `MultiDataTrigger (IsLoading=False AND Groups.Count=0)`.

---

## [2.8.0] — 2026-05-18

### Visual
- **Card depth** — All card surfaces now have a subtle drop shadow (BlurRadius=12, Opacity=0.12) for better visual hierarchy.
- **Smooth button hover** — Primary button background fades between accent colours via 120 ms `ColorAnimation`; nav button uses a 120 ms opacity overlay (fully theme-aware).
- **Thin scrollbar** — Global 6 px scrollbar with rounded thumb; turns accent-blue on hover.
- **Sidebar gradient** — Sidebar fades from surface colour at the top to a slightly darker tone at the bottom in both Dark and Light themes.

### UX
- **"HEALTH SCORE"** — Renamed from "Compliance Score" throughout the UI; the internal property name is unchanged.
- **Remediation links** — Each finding now exposes a "→ Remediation guidance" button in the detail panel that opens the relevant Microsoft Learn documentation.
- **Score trend** — Health score shows a green ▲ or red ▼ delta (e.g. `▲ +5`) directly below the number after each scan.
- **Findings badge** — The Findings navigation button now shows a red count bubble with the total number of open findings.
- **First-run welcome card** — Dashboard shows a 3-step onboarding guide on the right panel when no scan has been run yet.

---

## [2.7.6] — 2026-05-18

### Security
- **SHA-256 mandatory on auto-update** — Installer download is aborted if the release has no `.sha256` asset; user is directed to download manually from GitHub Releases.
- **Checksum URL host pinning** — SHA-256 asset URL must be `https://objects.githubusercontent.com/…`; any other host or HTTP scheme is silently ignored and treated as if no checksum existed.
- **WMI hostname validation** — `AdWmiClient` validates all hostname parameters against `^[a-zA-Z0-9][a-zA-Z0-9\-\.]{0,253}$` before any WMI or ping call; invalid hostnames return empty / false.
- **WQL logName allowlist** — `GetEventLogAsync` only accepts `"System"`, `"Security"`, or `"Application"` as the log name (case-sensitive); any other value returns an empty list without touching WMI.
- **LDAP filter escaping** — `AdConnector.EscapeFilterValue` is now `public static`; all user-supplied strings that enter LDAP filter attribute-value positions go through RFC 4515 escaping.
- **SID format validation** — `AdSearcher.SearchBySid` validates the query against `^S-\d+-\d+(-\d+)*$` before LDAP; rejects anything that doesn't match.
- **OU path validation** — `AdSearcher.SearchByOu` rejects paths that don't start with `CN=`, `OU=`, or `DC=` to prevent LDAP server redirection.

### Tests
- `AdConnectorEscapeTests` — covers backslash, wildcard `*`, parentheses, NUL, and filter break-out payload escaping.
- `AdSearcherValidationTests` — invalid SID formats, invalid/valid OU paths; confirms validation gates fire before any LDAP call.
- `AdWmiClientAllowlistTests` — logName allowlist (valid + invalid, case-sensitive), hostname validation in `GetDisksAsync` and `PingAsync`.
- `UpdateCheckerTests` — two new cases: rogue-host checksum URL and HTTP checksum URL both result in `ExpectedSha256 == null`.

---

## [2.7.5] — 2026-05-17

### Fixed
- **Email copy-to-clipboard** — Email field in User Browser detail panel now uses the copyable text style; click to select and copy.
- **SAM account copy in Group Manager** — Member list SAM account names are now selectable/copyable, consistent with the Name field.
- **Timeline zero-result state** — After a load that returns no changes, the right panel now shows "No changes in this period. ✓" instead of a blank list.
- **Privileged Groups empty expander** — Groups with no members now show "No members." inside the expander instead of an empty content area.
- **Settings confirmation auto-clears** — "Settings saved." status message disappears automatically after 3 seconds.

---

## [2.7.4] — 2026-05-17

### Fixed
- **`Run.Text` binding crash** — Added `Mode=OneWay` to `MemberCount` binding on a `Run` element in Group Manager; WPF's default TwoWay mode on a read-only property could cause `XamlParseException`.
- **Distinguished Name tooltip** — Truncated DN column in the Domain Admins list now shows a tooltip with the full value on hover.
- **Placeholder flicker during load** — "Click Refresh" placeholders in DC Inventory and Domain Admins now use `MultiDataTrigger` (IsLoading=False AND data=null) so they don't appear simultaneously with the loading spinner.
- **AD Search zero-result state** — Right panel now shows "No results found." after a search with no matches instead of a blank `ListView`.

---

## [2.7.3] — 2026-05-17

### Performance
- **Frozen color brushes** — `ScoreColorConverter` and `DaysToExpiryColorConverter` now allocate static frozen `SolidColorBrush` instances once at class load instead of `new SolidColorBrush(...)` on every render call.
- **Relative scan time auto-refresh** — Dashboard last-scan timestamp ("5 min ago") now updates every 60 seconds via `DispatcherTimer`; previously stayed at "just now" indefinitely.

---

## [2.7.2] — 2026-05-17

### UX
- **Score color** — Hygiene score on the dashboard now renders green (≥80), amber (≥60), or red (<60) instead of always green.
- **Severity stripe on findings** — Each finding card has a 3 px left-border accent: red for High, amber for Medium, gray for Low.
- **Severity badge in findings** — Colored dot (●) next to severity label in the findings detail panel.
- **Copyable fields in AD Search** — SAM account and OU columns in search results use the copyable text style.
- **Copyable hostname in Computer Browser** — Hostname in the detail panel is now selectable and copyable.
- **Copyable username in Password Report** — SAM account column uses the copyable text style.
- **Stat tile tooltips** — Dashboard stat tiles (findings, inactive users, password issues, group issues) show descriptive tooltips on hover.
- **Escape to clear filter in Group Manager** — Pressing Escape clears the group filter text box.
- **OU Browser empty state** — Shows "No organizational units found." when the filtered list is empty.
- **Status bar hidden on first run** — Bottom status bar is hidden until the first scan completes.

---

## [2.7.1] — 2026-05-17

### UX
- **Domain name in title bar** — Connected domain is displayed next to the app name in the window title bar.
- **Relative scan time** — Last scan time shows as "just now", "5 min ago", "3 h ago" etc. instead of a raw timestamp.
- **Global status bar** — Persistent status bar at the bottom of the main window shows scan state across all views.
- **F5 to refresh** — Global `F5` key binding triggers a new scan from any view.
- **Escape to dismiss** — Escape key clears filter inputs in Findings, User Browser, and Computer Browser.
- **Copy-to-clipboard** — SAM accounts, distinguished names, and hostnames are now selectable read-only text fields throughout the app (click to select, Ctrl+C to copy).
- **Tooltips** — Added tooltips to icon-only controls and abbreviated labels throughout.
- **Loading spinners and empty states** — All browser views now show a progress indicator while loading and a descriptive message when the list is empty.
- **Acknowledge feedback** — Acknowledging a finding briefly shows a confirmation label before the item is removed from the list.
- **Computer detail empty states** — WMI-backed panels (Disks, Local Admins, Sessions) show "No data" messages when unavailable instead of blank areas.

---

## [2.7.0] — 2026-05-17

### Security
- **Auto-update integrity check** — SHA-256 checksum is now generated for every release and verified before the downloaded installer is executed. The `.sha256` file is published alongside the `.exe` on every GitHub Release.
- **LDAP path injection guard** — OU search mode now validates that the query matches a Distinguished Name pattern (`CN=`, `OU=`, or `DC=`) before constructing the `LDAP://` path.
- **CSV injection prevention** — Exported CSV files now prefix formula-starting characters (`=`, `+`, `-`, `@`) with `'` to prevent formula execution when opened in Excel or LibreOffice Calc.
- **WQL hostname escaping** — Single quotes in computer hostnames are escaped before being interpolated into WMI queries.
- **Error dialog hardening** — Fatal error dialogs now show only the exception type and message; the full stack trace is written to the log file only (`%APPDATA%\DirHealth\dirhealth.log`).
- **Credential save logging** — Failed credential saves are now logged instead of silently discarded.

### Tests
- Added `UpdateCheckerTests` — covers empty releases, draft filtering, asset parsing, SHA-256 checksum fetch.
- Added `CredentialStoreTests` — covers DPAPI round-trip, clear, and load-when-absent.
- Extended `CryptoHelperTests` — truncated blob, empty plaintext round-trip, HMAC-region tampering.
- Extended `CsvExporterTests` — formula injection prefix-escaping for all user-controlled AD fields.

---

## [2.6.0] — 2026-05

### Performance
- **Single concurrent scan pass** — all 23 AD sub-queries now run in one `Task.WhenAll` call (`RunCompleteScanAsync`), eliminating duplicate LDAP round-trips on every dashboard refresh.
- **Batch LDAP group member resolution** — `N` individual `GetEntry` calls reduced to `ceil(N/50)` queries via OR-filter batching.
- **Parallel privileged group queries** — 9 hardcoded group lookups now run concurrently via `Task.WhenAll`.
- **ListBox virtualization** — User, Computer, and Group Browser sidebars now use recycling `VirtualizingStackPanel`; smooth scrolling at 10 000+ objects.
- **5-minute navigation cache** — Browser ViewModels cache LDAP results for 5 minutes; cache is invalidated automatically when credentials change in Settings.
- **Frozen brushes in chart** — Dashboard score chart uses static frozen `SolidColorBrush` fields; redraw is skipped when score, history count, and canvas dimensions are unchanged.

---

## [2.5.0] — 2026-04

### New Features
- **Domain Trust View** — lists all trust relationships to other domains/forests with direction (Inbound/Outbound/Bidirectional) and type (NT4/AD/MIT/Forest).
- **SID History Finding** — detects enabled accounts retaining historical SIDs from AD migrations; silent privilege escalation risk. Severity Medium from 1 account, High from 6.
- **Timeline / Recent Changes** — shows all AD objects created or modified in the last 7, 30, or 90 days, with Created/Modified distinction.

---

## [2.4.0] — 2026-03

### New Features
- **Stale Domain Admins Finding** — domain admin accounts (direct and nested) with no login in 30+ days.
- **Fine-Grained Password Policies** — PSO objects that weaken the domain-wide password policy for specific groups.
- **Privileged Groups Overview** — dedicated view for 9 high-risk groups: Domain Admins, Enterprise Admins, Schema Admins, Backup Operators, Account Operators, Server Operators, Print Operators, DnsAdmins, Remote Desktop Users.

---

## [2.3.0] — 2026-02

### New Features
- **AS-REP Roasting Finding** — accounts without Kerberos pre-authentication (`DONT_REQUIRE_PREAUTH`); password hash crackable offline without prior authentication.
- **Unconstrained Delegation Finding** — computers and users flagged as trusted for delegation (`TRUSTED_FOR_DELEGATION`), excluding domain controllers.
- **Password Not Required Finding** — accounts with `PASSWD_NOTREQD` flag set; empty password permitted.

---

## [2.0.0] — 2026-01

### New Features
- **EOL OS Finding** — computers running end-of-life Windows versions (XP through Server 2012 R2). DCs on EOL OS flagged as High severity.
- **DC Inventory View** — all domain controllers with OS, FSMO roles, Global Catalog status, and EOL flag.
- **CSV + PDF Export** — full hygiene report exportable as CSV (per category) or PDF (single full report with score, findings, password expiry, inactive users, domain admins).
- **Auto-update** — startup and manual check against GitHub Releases; in-app download with progress indicator.
- Removed licensing requirement — DirHealth is now fully free with no activation gate.
