---
type: feedback
last_updated: 2026-05-16
confidence: high
---

# Feedback & Korrekturen

## Installer / ISPP
- **Kein Backup-Block in `CurStepChanged(ssInstall)`** — Installer schreibt nie nach `%APPDATA%`, rename war sinnlos und machte User-Daten nach Upgrade unsichtbar. Entfernt.
- **Keine standalone `#13#10`-Zeilen** — ISPP liest diese als Präprozessor-Direktive. Immer in vorherige Zeile einbauen.
- **Kein `on E: Exception do`** — Inno Setup Pascal kennt kein typed exception handler. Nur `except ... end`.
- **Kein `VersionInfoVersion` mit Suffix** — `0.0.0-dev` ist kein gültiger Windows-Versionsstring. Direkt weglassen auf dev builds.

## AD-Queries
- **Nested group membership**: immer `memberOf:1.2.840.113556.1.4.1941:=` verwenden, nicht plain `memberOf=`. Sonst werden nur direkte Mitglieder gefunden.
- **PSO-Attribute**: direkte `(int)`-Casts crashen wenn AD `long` liefert. `GetPsoInt()` mit Pattern Matching und -1-Sentinel verwenden.
- **Disabled accounts ausschließen**: Security-Findings immer mit `!(userAccountControl:1.2.840.113556.1.4.803:=2)` filtern.

## Lizenz
- Projekt läuft unter **Elastic License 2.0**, nicht MIT.
- SignPath Foundation erfordert OSI-Lizenz — deshalb SignPath entfernt. ELv2 bleibt.
- Konsistent mit Vatk und ForgeHiveAI (beide ELv2).

## Commits & Release-Prozess
- Kein `dotnet` lokal — Build nur via GitHub Actions.
- Release: `git tag vX.Y.Z && git push origin vX.Y.Z`
- Dev-Branch heißt `dev`, nicht `main`. `dev` ist gleichzeitig der Main-Branch.
- Tags direkt auf `dev` setzen — kein separater Merge-Schritt.

## Export-Muster
- **Export-Buttons immer mit CanExecute-Guard**: alle Export-Commands müssen `CanExecute = false` zurückgeben wenn die Daten-Liste leer ist — kein Export auf leere Seiten.
- **Kein CSV für einzelne User-Detail-Zeilen**: 1 Zeile als CSV-Download ist nicht nützlich — nur Listen-Exports anbieten.
- **Exporter-Aufrufe immer in try/catch**: File-Locks und Netzlaufwerk-Fehler dürfen nie den globalen Crash-Handler erreichen. StatusMessage setzen.

## WMI
- **WMI-Verbindung kann scheitern ohne Exception zu werfen** wenn die Firewall-Regel fehlt — immer im catch auf `StatusMessage` setzen und WMI-Fehler als nicht-kritisch behandeln (AD-Daten bleiben verfügbar).

## Performance-Muster (v2.6.0)
- **N+1 LDAP**: immer prüfen ob eine Loop über `GetEntry(dn)` durch einen OR-Filter-Batch ersetzt werden kann. Schwellwert: ab ca. 10 Einträgen lohnt sich das Batching.
- **`Task.WhenAll` statt sequentiellem await**: bei unabhängigen Queries strukturell bevorzugen. Aber: `DirectoryEntry` niemals teilen — jede Task braucht eigene Instanz via `_connector.GetRootEntry()`.
- **Scan-Ergebnis als Record zurückgeben**: statt mehrere public Methoden für verbundene Daten, einen `record` mit allen Ergebnissen zurückgeben und die caller-seitigen Wrapper dünn halten.
- **Navigation-Cache invalidieren bei Settings-Save**: nach jedem Credential-Wechsel müssen Browser-VMs neu laden. Muster: `Action? OnCredentialsSaved` im SettingsViewModel, in MainViewModel verdrahten.
