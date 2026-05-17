# Changelog

All notable changes to DirHealth are documented here.

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
