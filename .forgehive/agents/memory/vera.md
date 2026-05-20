# Vera — Agent Memory

Persistenter Kontext für Vera zwischen Sessions.
Wird von Claude aktualisiert wenn Vera Entscheidungen trifft.

## Entscheidungen

[2026-05-17] Full codebase security audit (Security Party). 3 HIGH, 2 MEDIUM gefunden und alle gefixt:
- HIGH: LDAP Injection in `AdConnector.IsDomainAdmin()` — `samName` ohne Escaping. Fix: `EscapeFilterValue()`.
- HIGH: Update-Binary ohne Integritätsprüfung. Fix: URL-Host-Pinning auf `github.com`/`objects.githubusercontent.com` + HTTPS + GUID-Temp-Pfad.
- HIGH: Raw LDAP Filter Mode — intentionelles Feature, volle DA-Read-Breite. Fix: Persistentes Amber Warning Banner in AdSearchView.
- MEDIUM: HWID-Credential-Key kollabiert auf VMs. Fix: DPAPI (`ProtectedData.Protect(CurrentUser)`).
- MEDIUM: WMI `logName` roh in WQL. Fix: Allowlist-Guard.
Als clean befunden: CryptoHelper (HMAC-then-Decrypt korrekt), AdScanner (keine User-Inputs in Filtern), Deserialisierung (nur System.Text.Json mit konkreten Typen), Pfad-Handling (nur SpecialFolder-Pfade + fixe Namen).

[2026-05-16] Full codebase security audit completed (v2.6.0). No CRITICAL findings. Three HIGH: raw LDAP filter passthrough (intentional feature), unvalidated LDAP path in SearchMode.Ou, no binary integrity check on auto-update. All three fixed. Update binary is unsigned (SignPath pending) — SHA-256 checksum approach implemented instead of Authenticode.

## Projekt-Kontext

- **Credential storage**: DPAPI (CurrentUser scope) for current saves. Legacy AES-256 + PBKDF2 path is migration-only, auto-upgrades on load. No hardcoded secrets anywhere.
- **LDAP escaping**: EscapeFilterValue + EscapeDn helpers exist in AdScanner.cs and AdSearcher.cs; consistently applied except SearchByLdap (intentional raw passthrough, now commented).
- **Update mechanism**: Host-pinned to github.com / objects.githubusercontent.com, HTTPS enforced. SHA-256 checksum now fetched alongside installer and verified before Process.Start. Binary remains unsigned (SignPath Foundation account pending).
- **SearchMode.Ldap**: Intentional power-user raw LDAP filter feature. No sanitisation by design — documented with comment.
- **SearchMode.Ou**: Fixed — DN pattern validation added before LDAP:// path construction.
- **CSV injection**: SafeField() helper added to CsvExporter — prefixes formula-starting chars (=, +, -, @, \t, \r) with apostrophe.
- **AppDomain.UnhandledException**: Fixed to show only type+message in dialog; full stack trace goes to log file only.
- **Dead code**: LicenseApi / LoadSettings removed from App.xaml.cs (was read but never consumed).
