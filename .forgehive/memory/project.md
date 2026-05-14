---
type: project
last_updated: 2026-05-14
confidence: high
---

# Projektkontext

## Status

DirHealth v2.5.0 ist released. Phasen 1–4 sind vollständig implementiert.

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

## Nächste Phase

### Phase 5 (geplant)
- GPO Browser — `(objectClass=groupPolicyContainer)` + gPLink-Auswertung auf OUs; Orphaned GPOs
- AD Data Quality Report — Vollständigkeit von mail, telephoneNumber, department, title, manager, physicalDeliveryOfficeName

## Architektur-Entscheidungen

- **Lizenz**: Elastic License 2.0 — konsistent mit Vatk und ForgeHiveAI; nicht MIT
- **Build**: Ausschließlich via GitHub Actions; kein lokales dotnet installiert
- **SignPath**: entfernt (nicht eingerichtet, blockierte Builds); unsigned artifact für Releases
- **Score-Penalties**: Sicherheits-Findings absolut (nicht prozentual); Floor bei 10
- **AD-Queries**: LDAP_MATCHING_RULE_IN_CHAIN (OID `1.2.840.113556.1.4.1941`) für nested group membership server-side
- **PSO-Parsing**: `GetPsoInt()` mit -1-Sentinel unterscheidet "Attribut nicht vorhanden" von "Wert ist 0"
- **EqualityConverter**: neu in Converters.cs + App.xaml für RadioButton two-way binding auf int-Properties
