# Changelog

All notable changes to DirHealth are documented here.

---

## [Unreleased]

### Fixed — Follow-up review of Sprints 13–20
- **A check that could not read its data no longer reads as "Passed"** — the krbtgt, Guest, Pre-Windows 2000, operator-group, Recycle Bin and dSHeuristics checks used to report "passed" when the object they look at could not be read; they now report **Failed** with the reason. Domain-basics and LAPS/machine-account checks now run one query per rule, so one unreadable object fails only its own rule and the other rules keep their findings.
- **User detail: "Last Logon (this DC)"** shows "Could not be read: …" when the query fails, instead of "No logon on this DC".
- Dashboard hint text now says "add exceptions" instead of the retired "acknowledge".

### Added — Sprint 20 (Column chooser, remaining checks)
- **Column chooser** (S20-1) — right-click a column header to show or hide columns; the menu shows how many are hidden and offers "Reset to default". The choice is remembered per view. The Export menu has a "Visible columns only" switch (default: all columns).
- **7 more read-only checks** (S20-2) — LAPS not deployed / computers without a LAPS-managed password (only expiry attributes are read — never a LAPS password), machine or DC passwords that stopped rotating, replication-conflict and duplicate objects, dSHeuristics allowing anonymous LDAP or excluding operator groups from AdminSDHolder, smart-card accounts with an old password hash. Score model version 4.
- Internal: proof of concept for evaluating DCSync rights from a security descriptor (not shown in the app yet); the read-only guard now also blocks ACL-writing APIs.

### Added — Sprint 19 (New checks) — ⚠ your score will drop without any change in AD
23 new read-only LDAP checks, each with rule id, weight, explanation, copyable fix and undo:
- **Tier 0 and hidden memberships** (S19-1) — Tier 0 now follows the BloodHound "high value" model (by SID, incl. Key Admins, Enterprise Key Admins, Cert Publishers; DnsAdmins by name). New: accounts with a non-standard primary group (a membership that does not show up in the group's member list).
- **Admin account hardening** (S19-2) — Tier 0 accounts that are not "sensitive and cannot be delegated" nor in Protected Users, that have an SPN (Kerberoastable), no Kerberos pre-authentication, an old or never-expiring password, or a mailbox; members in Account/Server/Print/Backup Operators.
- **Domain basics** (S19-3) — krbtgt password older than 180 days, Guest enabled, ms-DS-MachineAccountQuota above 0, Pre-Windows 2000 Compatible Access with Authenticated Users/Anonymous/Everyone, AD Recycle Bin disabled, domain functional level below 2016, fewer than two DCs.
- **Kerberos encryption** (S19-4) — DES-only accounts, service accounts without AES, reversible password encryption, userPassword/unixUserPassword set (DirHealth only checks that a value exists — it never reads it).
- **Delegation and trusts** (S19-5) — delegation to domain controllers or krbtgt (constrained and resource-based), delegation to services that no longer exist, trusts without SID filtering, with TGT delegation, or inactive.
- **Critical severity** (S19-6) — the severity filter offers "Critical" and the dashboard shows Critical in red.
- The score model version changes (3): more checks mean a lower score for the same AD. The dashboard shows a one-time notice, and the trend/report do not compare across the change.
- A check that cannot read a required domain attribute now reports "Failed" instead of producing a wrong finding.

### Added — Sprint 18 (Reports and Excel)
- **Two report levels** (S18-1) — "Export report" asks for **Summary** (1–2 pages for management: score with "higher is better", coverage, fix-first list by score gain, overview by category, changes since the last scan) or **Full** (summary plus every finding card with what/why/how, fix and undo commands, affected objects, and appendix tables).
- **Print discipline** (S18-2) — sections flow instead of starting a new page each; object lists stop at 25 with "N more — see the CSV export"; every page has a footer with domain, creation time and "Page x of y"; column headers repeat after a page break; wide tables (6+ columns) print in landscape.
- **Exceptions and failed checks in the report** (S18-3) — own sections for active exceptions (object, reason, expiry, creator) and for checks that could not run (with reason).
- **Redact user names** (S18-4) — option in the export dialog: user names, sAMAccountNames, e-mail addresses and DNs become stable aliases (user-001 …); counts, scores and computer names stay.
- **Excel export** (S18-5) — every table's Export menu offers "Excel (filtered)" / "Excel (all)": frozen header row, autofilter, text cells for text (formula payloads stay inert text), typed numbers and dates so sorting and number filters work, and an "Info" sheet with the export metadata. The `.meta.txt` with the SHA-256 is written as for CSV. New dependency: MiniExcel 1.46.0 (Apache-2.0, no further packages).

### Changed — Sprint 17 (Exceptions and score) — ⚠ the score changes without any change in your AD
- **Exceptions per object** (S17-1/S17-2) — instead of acknowledging a whole category, add an exception for one object of one rule, with a required reason and an expiry date (default 90 days, max. 365). Excepted objects stay visible (greyed), a filter can hide them, and expired exceptions count again. Exceptions are stored locally in `%APPDATA%\DirHealth\exceptions.json`; nothing is written to AD.
- **Old acknowledgements no longer hide findings.** Findings of categories you acknowledged in earlier versions show up again; your old note is displayed on the finding card. Add exceptions per object where they still apply. `acknowledged.json` is left untouched (downgrading stays safe).
- **New score model** (S17-3/S17-4) — score = 100 minus 2.5 × rule weight × how widespread a finding is. Exploitable findings (unconstrained delegation, AS-REP, Kerberoast, …) now weigh much more than hygiene: one computer with unconstrained delegation costs 25 points (was 6). Each finding card shows "+X points if fixed", and the dashboard lists where the points go. Checks that could not run never raise the score: it is then shown as an upper bound ("≤ 74"), or "n/a" when too much is missing. The trend marks the formula change instead of reporting a score drop.
- Fixed: the "score dropped" notice compared against the scan before the previous one.
- Deferred: "Fix first" list (S17-5) — needs an effort estimate per rule first.

### Added — Sprint 16 (Finding card and fix block)
- **Rule catalog** (S16-1) — every finding has a stable rule id (e.g. `DH-KRB-002`), a weight (1–10; exploitable issues weigh more than hygiene) and references (Microsoft Learn, MITRE ATT&CK). Affected objects are stored with name, DN, SID and type. Scan caches from earlier versions still load.
- **Finding card** (S16-2) — fixed layout: title with count, rule id · severity · weight, "What is it · Why it matters · How to fix", references, and the affected objects as a sortable, exportable table. Double-click an object to open its detail.
- **Fix block** (S16-3) — per rule a PowerShell command to copy, the operational risk in one sentence, and an undo command. The command uses **exactly the LDAP filter of the check** (same source in code), writes a backup file first and starts with `-WhatIf`. DirHealth only shows and copies it — it never runs anything.
- **Check status** (S16-4) — every check reports Found, Passed, Failed (with reason) or Not applicable. A failing query no longer aborts the whole scan, and checks that used to swallow errors (Stale Domain Admins, password policy, fine-grained policies) now report "Failed" instead of a silent pass. The dashboard shows the summary and lists failed checks; the score note says when it may be too high.

### Added — Sprint 15 (Search and user dossier)
- **Search results as a table** (S15-1) — sortable, exportable, with a type icon (tooltip shows the type). Searches stop at 500 rows and say so: "Showing 500 of more than 500 — narrow your search". The LDAP-filter warning is unchanged.
- **Detail next to the results** (S15-2) — double-click or Enter opens the user, computer or group detail on the right; the list and selection stay. Esc or ✕ closes it.
- **Search prefixes** (S15-3) — `user:`, `group:`, `computer:`, `ou:` search one object type by name (e.g. `group:admins`). Works from any mode except the raw LDAP filter.
- **New user detail** (S15-4) — every attribute in one name/value list (UAC flags, SPNs, delegation, SID, creation date, account expiry, adminCount, …), both logon values labelled honestly ("replicated, ±14 d" and "this DC"), collapsible "Member Of (n)" with direct/nested/primary and Tier 0 per group, "Affected by Findings (n)", and a "Tier 0 member" badge.
- **User security dossier** (S15-5) — "Export dossier…" writes a PDF (attributes, every group with its path such as `jdoe → IT-Admins → Domain Admins`, affected findings with fixes, metadata) plus a groups CSV and a findings CSV, each with a `.meta.txt` holding its SHA-256.
- **Quick filters** (S15-6) — chips in the user browser (Disabled, Password never expires, Admin (adminCount), Inactive > 90 d) and the computer browser (Disabled, Inactive > 90 d). They narrow the loaded list; the counter follows.

### Removed — Sprint 15
- The old single-user PDF ("User Profile") — replaced by the dossier. It highlighted privileged groups by English name only.

### Added — Sprint 14 (Tables and user export)
- **Shared data table** (S14-1) — every list is now a sortable table (click a header; arrow shows the direction), the header stays visible while scrolling, a counter shows "37 of 1,204" for the local filter, and an **Export** menu writes "CSV (37 filtered)" in the displayed order or "CSV (all 1,204)". Right-click a row: Copy DN / Copy SID / Copy sAMAccountName.
- **Rolled out to 11 views** (S14-1/2/3) — Users, Computers, Password Report, Domain Admins, Privileged Groups (one row per membership; empty or missing groups keep a row with a status), Group Manager (groups and members), DC Inventory, Domain Trusts, GPOs, OUs, Recent Changes. Selection and detail panels work as before; Esc closes the detail.
- **ADRecon-style user export** (S14-4) — "Export users…" writes SamAccountName, DisplayName, Enabled, OU, LastLogonApprox (±14 d), NeverLoggedOn, DaysSinceLogon, PwdLastSet, PasswordAgeDays, PasswordNeverExpires, PwdNotRequired, PreAuthNotRequired, Delegation, AdminCount, HasSPN, WhenCreated, AccountExpires, Department, Title, SID, DN and one TRUE/FALSE column per stored userAccountControl flag. Mail, Manager and Description are opt-in (data minimisation — descriptions often contain passwords).
- **Audit metadata for every CSV** (S14-5) — a `<name>.meta.txt` next to each export: domain, queried domain controller, user, UTC time, view, filter, row count, DirHealth version and the SHA-256 of the CSV. The CSV itself stays clean (no comment lines).

### Fixed — Sprint 14
- **Findings badge** (A6) — the sidebar badge counts open findings only, as intended since v2.8.0.
- **Acknowledge note** (A8) — selecting a finding in the list now loads its saved note (the list bypassed the command that loaded it).
- **Severity colours** (A9) — app and PDF use the same colours from one source (Critical #DC2626, High #D97706, Medium #CA8A04, Low #6B7280). High findings are now orange in the app, as in the PDF.
- **Honest logon labels** (A10) — "Last Logon (replicated, ±14 d)": `lastLogonTimestamp` can lag up to 14 days. CSV date columns are empty instead of "Never".

### Fixed — Sprint 13 (Correct results)
- **Admin groups found by SID, not by name** (S13-1) — Domain Admins (RID 512), Schema Admins (518), Enterprise Admins (519, forest root), Key Admins (526), Enterprise Key Admins (527) and the BUILTIN groups (S-1-5-32-544/548/549/550/551/555) are now resolved by SID. On domains installed in another language (e.g. German "Domänen-Admins") the login check, the Domain Admins view, the Privileged Groups view and the Stale Domain Admins finding no longer come back empty. The UI shows the group's real name from AD. DnsAdmins has no fixed RID and is still looked up by name — if it is missing, the view now says "Not found" instead of dropping it.
- **Login: "could not check" ≠ "not an admin"** (S13-1) — a failed LDAP query during the Domain Admins check used to read as "not a member". The login dialog now shows the real error. Accounts whose *primary group* is Domain Admins are recognised. The Domain-Admin requirement itself is unchanged.
- **User groups include nested and primary groups** (S13-2) — the user detail and the user PDF list direct, nested (`LDAP_MATCHING_RULE_IN_CHAIN`) and primary-group memberships, each marked `direct` / `nested` / `primary`. A failed lookup shows an error instead of an empty list.
- **End-of-life list** (S13-3) — Windows 10 (2025-10-14), Windows 2000 (2010-07-13) and Windows NT (2004-12-31) are flagged. Windows 10 LTSC/LTSB editions are deliberately not flagged (later support end, not distinguishable from `operatingSystem` alone). The remediation text mentions paid Extended Security Updates, which AD cannot show.
- **Search errors are visible** (S13-4) — a failed LDAP search shows "Search failed: …" instead of "No results found."
- **CSV export** (S13-5) — files are UTF-8 with BOM (umlauts display correctly in Excel), use the Windows region's list separator (`,` on English, `;` on German systems), and formula protection now also catches line feeds, full-width `＝＋－＠` and leading blanks before a formula character. Finding titles are escaped too.

### Added — Sprint 13
- **Read-only guard test** (S13-6) — a unit test scans the application source for AD write APIs (`CommitChanges`, `DeleteTree`, `SetPassword`, `Properties[...] = …`, …) and fails with file:line on any hit.

### Note
- After this update, domains with localized group names may show admin-related findings that were missing before. This is the scan becoming correct, not a change in your AD.
- The Privileged Groups view now lists 12 groups (added Administrators, Key Admins, Enterprise Key Admins).

---

## [2.9.0] — 2026-07-04

Phase 5 was taken off hold on 2026-07-04; this release bundles Sprints 10–12.

### Added — Sprint 10 (Phase 5)
- **GPO Browser** (S10-1) — new view listing every Group Policy Object with creation/modification dates, link count, and a status badge (Active / Orphaned / Disabled). Two-stage LDAP: enumerate `groupPolicyContainer` objects under `CN=Policies,CN=System,…`, then count `gPLink` references across the domain and join by GUID. Orphaned (unlinked) GPOs are surfaced first. Link/status logic extracted to the pure, unit-tested `GpoLogic` helper.
- **AD Data Quality Report** (S10-2) — new view measuring completeness of user master data (`mail`, `telephoneNumber`, `department`, `title`, `manager`, `physicalDeliveryOfficeName`). Horizontal completeness bars sorted worst-first, with CSV export (`CsvExporter.ExportDataQuality`). Not score-relevant — intended for MSP reporting.

### Added — Sprint 11 (Quality, Tests & Report Depth)
- **Per-finding remediation** (S11-1) — new `AdFinding.Remediation` property gives a concrete, plain-text fix for every finding category; shown in the Findings detail panel and in both PDF reports.
- **Testable scan core** (S11-2) — pure score/EOL/UAC logic extracted from `AdScanner` into `ScoreCalculator`, `EolMatcher` and `UacFlags`. New `AdScannerLogicTests` covers score clamp/minimum/proportional penalties, the EOL ordering rule (`2012 R2` before `2012`), and UAC bit masks — all without an AD or Windows dependency.
- **Richer full report** (S11-3) — the full PDF now shows the score delta vs the previous scan on the cover, a "Changes Since Last Scan" section (new / resolved / changed findings from `ScanDiffCalculator`), and each finding's remediation under its description. `PdfPageBuilder` anatomy and per-page column headers unchanged.
- **Findings detail panel** (S11-4) — findings list sorted by severity (Critical → Low) with a colour-coded severity badge per row; the detail panel now shows the recommended fix plus the Microsoft Learn link. Existing acknowledge flow unchanged.
- **Structured scan log** (S11-5) — `RunCompleteScanAsync` writes one line per sub-query (name, duration, result count, any error) to `%APPDATA%\DirHealth\scan.log` with rolling truncation. Purely additive; scan behaviour unchanged.

### Added — Sprint 12 (UX Consistency & Maintainability)
- **Unified empty / error states** (S12-1) — new `ListBrowserViewModel` base plus a reusable `ListStateOverlay` control give every list view (Users, Computers, OUs, Groups, DC Inventory, Domain Trust, Timeline) the same three distinguishable states: loading, empty-after-load, and error. No list view is left blank without feedback.
- **Accessibility pass** (S12-2) — `AutomationProperties.Name` on all icon-only controls (title-bar buttons, banner dismiss buttons, the Findings nav button, GPO/Data-Quality actions). Login/Settings tab order verified as document-order-logical; dark/light text contrast spot-checked against WCAG AA.

### Changed
- `AdScanner` is now a `partial` class, split by theme: `AdScanner.Phase5.cs` (GPO + data quality), `AdScanner.Kerberos.cs` (delegation / Kerberos queries), `AdScanner.Trust.cs` (trust / SID history / timeline), `AdScanner.Groups.cs` (OU + group queries). Core dropped from ~1400 to ~1030 lines. `RunCompleteScanAsync` remains the single scan entry point; behaviour unchanged (S12-3).

---

## [2.8.3] — 2026-06-14

### Fixed
- **Build error CS0165** — `RelativeScanTimeConverter` used unassigned local variable `s` in the `is not string` branch; replaced `return s ?? "Never"` with `return "Never"`.

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
