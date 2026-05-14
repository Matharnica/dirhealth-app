---
type: feedback
last_updated: 2026-05-14
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
- Dev-Branch heißt `dev`, nicht `main`.
- Nach Feature-Commit: merge `dev` → `main`, dann Tag setzen.
