---
type: project
last_updated: 2026-05-16
confidence: high
---

# Projektkontext

## Status

DirHealth v2.6.0 ist released. Phasen 1–4 + Performance-Release sind vollständig implementiert.

## Abgeschlossene Phasen

### Phase 1 ✅ (v2.x)
- EOL-Betriebssysteme Finding
- DC Inventory View (FSMO-Rollen, Global Catalog, EOL-Flag)

### Phase 2 ✅ (v2.3.0)
- AS-REP Roasting (`DONT_REQUIRE_PREAUTH`, Bit 4194304)
- Unconstrained Delegation — Computer (non-DC) und User (`TRUSTED_FOR_DELEGATION`, Bit 524288)
- PASSWD_NOTREQD (Bit 32)

### Phase 3 ✅ (v2.4.0 → v2.4.1)
- Stale Domain Admins (30-Tage-Threshold, LDAP_MATCHING_RULE_IN_CHAIN für nested groups, disabled accounts ausgeschlossen)
- Fine-Grained Password Policies — PSOs aus `CN=Password Settings Container,CN=System`; `GetPsoInt()` Helper mit -1-Sentinel für fehlende Attribute
- Privileged Groups Overview — 9 hardgecoded Gruppen (Domain Admins, Enterprise Admins, Schema Admins, Backup Operators, Account Operators, Server Operators, Print Operators, DNSAdmins, Remote Desktop Users)

### Phase 4 ✅ (v2.5.0)
- Domain Trust View — `(objectClass=trustedDomain)` unter `CN=System,<domainDn>`; TrustType (NT4/AD/MIT), Direction (In/Out/Bidirectional), IsForestTrust (Bit 8 von trustAttributes)
- SID History Finding — `(sIDHistory=*)` auf enabled users; -3/Account, Cap 12; Medium ab 1, High ab 6
- Timeline / Recent Changes — `whenChanged >= {generalizedTime}` LDAP-Filter; Created/Modified unterschieden über whenCreated/whenChanged Delta < 5 min; 7/30/90d wählbar via RadioButtons + EqualityConverter

### Performance-Release ✅ (v2.6.0)
- ListBox Recycling-Virtualisierung in User/Computer/Group Browser Sidebars
- Group Member List: `ItemsControl` → virtualisiertes `ListBox` in Grid-Row `Height="*"`
- Batch LDAP: `BatchResolveGroupMembers()` — N+1 → `ceil(N/50)` Queries via OR-Filter
- `Task.WhenAll` für 9 Privileged Group Queries (je eigene `DirectoryEntry`-Instanz)
- `RunCompleteScanAsync()` + `CompleteScanResult` — 23 parallele Subtasks ersetzen 5 Einzelscans
- 5-Minuten Navigations-Cache in User/Computer/Group Browser VMs mit `InvalidateCache()`
- Cache-Invalidierung bei Settings-Save via `SettingsViewModel.OnCredentialsSaved`
- Dashboard Chart: Static Frozen Brushes + Skip-Guard auf Count + LastScore + Canvasgröße

## Nächste Phase

### Phase 5 (on hold — kein aktives Startdatum)
- GPO Browser — `(objectClass=groupPolicyContainer)` + gPLink-Auswertung auf OUs; Orphaned GPOs
- AD Data Quality Report — Vollständigkeit von mail, telephoneNumber, department, title, manager, physicalDeliveryOfficeName

**Why:** Bewusste Entscheidung nach v2.7.0-Release — Phase 5 wird zurückgestellt, kein Zeitplan.
**How to apply:** Phase 5 nicht proaktiv vorschlagen. Erst wieder aufgreifen wenn der User es initiiert.

## Abgeschlossene Zusatz-Features (außerhalb Phasen 1–4)

### Export (CSV + PDF)
- `CsvExporter` (CsvHelper-basiert) + `PdfExporter` (PdfSharp) in `Core/Export/`
- `FullReportData` Record: `(string Domain, int Score, List<AdFinding>, List<AdUser> InactiveUsers, List<AdUser> ExpiringPasswords, List<string> DomainAdmins)`
- `PdfPageBuilder`: privater Helper in PdfExporter mit 3-Zonen-Layout (Header `#0f172a` / Content / Footer `#f8fafc`)
- Score-Farben im PDF: >=80 grün `#7DFFB3`, >=60 gelb `#FCD34D`, <60 rot `#FCA5A5`
- Export-Buttons immer `CanExecute = false` wenn Daten-Liste leer

### Lizenz entfernt (v2.x nach Phase 4)
- Gelöscht: `Core/License/` (5 Dateien), `Views/Activation/` (4 Dateien), `ActivationViewModel.cs`
- Behalten: `HwidManager`, `CryptoHelper`, `HwidTests` — werden von `CredentialStore` (AES-256) benötigt
- Neuer Startup-Flow: Splash → Login → Main Window (kein License-Gate mehr)

## Architektur-Entscheidungen

- **Lizenz**: Elastic License 2.0 — konsistent mit Vatk und ForgeHiveAI; nicht MIT
- **Build**: Ausschließlich via GitHub Actions; kein lokales dotnet installiert
- **SignPath**: entfernt (nicht eingerichtet, blockierte Builds); unsigned artifact für Releases
- **Score-Penalties**: Sicherheits-Findings absolut (nicht prozentual); Floor bei 10
- **AD-Queries**: LDAP_MATCHING_RULE_IN_CHAIN (OID `1.2.840.113556.1.4.1941`) für nested group membership server-side
- **PSO-Parsing**: `GetPsoInt()` mit -1-Sentinel unterscheidet "Attribut nicht vorhanden" von "Wert ist 0"
- **EqualityConverter**: neu in Converters.cs + App.xaml für RadioButton two-way binding auf int-Properties
- **RunCompleteScanAsync**: alle Scan-Subtasks via `Task.WhenAll` in einer Methode; gibt `CompleteScanResult` Record zurück; `RunFullScanAsync` + `ComputeComplianceScoreAsync` sind jetzt thin wrappers
- **Navigation-Cache Pattern**: `_lastLoaded = DateTime.MinValue` + `CacheTtl = TimeSpan.FromMinutes(5)` in Browser-VMs; `InvalidateCache()` wird von `SettingsViewModel.OnCredentialsSaved` ausgelöst
- **DirectoryEntry Thread-Safety**: bei `Task.WhenAll` muss jeder Task seine eigene `GetRootEntry()`-Instanz öffnen — `DirectoryEntry` ist nicht thread-safe
- **PDF-Design**: `PdfPageBuilder` Hilfsklasse kapselt Header/Footer-Layout; Score-Farben, Logo via `pack://application:,,,/Resources/icon_128.png`
- **ThemeManager**: persistiert Dark/Light-Wahl in HKCU Registry; kein App-Restart nötig
