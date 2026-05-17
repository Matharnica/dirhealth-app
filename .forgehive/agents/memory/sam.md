# Sam — Agent Memory

Persistenter Kontext für Sam zwischen Sessions.
Wird von Claude aktualisiert wenn Sam Entscheidungen trifft.

## Entscheidungen

[2026-05-16] Security test coverage audit completed (v2.6.0). Critical gaps found in UpdateChecker (zero tests), CredentialStore (zero tests), and CSV injection paths. Tests added for all three, plus CryptoHelper edge cases (truncated blob, empty plaintext, HMAC-region tampering).

## Projekt-Kontext

- **Test files**: CryptoHelperTests.cs (8 tests), CsvExporterTests.cs (7 tests), HwidTests.cs (4 tests), UpdateCheckerTests.cs (6 tests, new), CredentialStoreTests.cs (3 tests, new).
- **Test framework**: xunit + Moq, net8.0-windows. DPAPI tests require Windows — covered by CI (windows-latest).
- **UpdateCheckerTests**: Uses SequencedMockHandler (HttpMessageHandler subclass) to mock GitHub API responses. Tests cover: empty releases, all-draft releases, no exe asset, exe asset parsing, up-to-date check, SHA-256 checksum fetch.
- **CredentialStoreTests**: Uses backup/restore pattern for credentials.dat — preserves existing credentials on developer machines. Tests: round-trip, clear, load-when-absent.
- **SearchBySid**: Already has Regex guard `^S-\d+-\d+(-\d+)*$` before inserting into LDAP filter — not an injection risk. Sam's initial finding was already fixed before the audit.
- **CsvExporter SafeField**: Formula-injection prevention added — ExportPasswordReport and ExportInactiveUsers tests cover =, +, - prefixed field values.
- **Remaining test gap**: AdConnector.IsDomainAdmin unescaped samName — LOW priority, backlogged.
