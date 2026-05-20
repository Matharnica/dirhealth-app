---
type: reference
last_updated: 2026-05-16
confidence: high
---

# Stack-Eigenheiten

## WPF-Konventionen in DirHealth

- **RadioButton + int-Property**: `EqualityConverter` mit `ConverterParameter=7` etc. — ConvertBack gibt `Convert.ChangeType(parameter, typeof(int))` zurück.
- **ListBox statt ItemsControl für scrollbare Listen**: ListBox mit `ItemContainerStyle` der den Template auf `ContentPresenter` reduziert — gibt volle Kontrolle ohne das WPF-ListBoxItem-Highlighting.
- **`[RelayCommand]` auf `Task FooAsync()`** erzeugt `FooCommand` (Async wird abgeschnitten).
- **Computed Properties nach ObservableProperty-Änderung**: `OnPropertyChanged(nameof(TotalMembers))` manuell aufrufen — `[NotifyPropertyChangedFor]` funktioniert nicht über base/subclass-Grenze.
- **ListBox Virtualisierung**: braucht bounded height — Grid-Row mit `Height="*"`, nicht StackPanel. Attribute: `VirtualizingStackPanel.IsVirtualizing="True"`, `VirtualizationMode="Recycling"`, `ScrollViewer.IsDeferredScrollingEnabled="True"`.
- **Frozen Brushes in Code-Behind**: `private static readonly SolidColorBrush Foo = new(...); static Ctor() { Foo.Freeze(); }` — immutable, thread-safe, WPF überspringt Change-Notification-Overhead bei jedem Render.
- **Chart Skip-Guard**: zählt nicht nur `history.Count`, sondern auch `history[^1].Score` + Canvasgröße — bei 90-Entry-Cap bleibt Count konstant 90, nur der Score ändert sich.

## AD / LDAP

- **`GetEntryByDn(dn)`**: baut LDAP-Pfad mit optionalem Domain-Prefix — immer diese Methode nutzen statt direkt `new DirectoryEntry($"LDAP://{dn}")`.
- **`GetAdDateTime()`**: AD gibt `whenCreated`/`whenChanged` als `DateTime`-Objekte zurück (nicht als Generalized-Time-String). `val is DateTime dt` reicht.
- **Generalized Time im LDAP-Filter**: Format `yyyyMMddHHmmss.0Z` (Punkt vor Z, nicht Doppelpunkt).
- **`trustAttributes` Bit 8** = Forest Trust; Bit 4 = SID-Filtering; Bit 32 = Cross-Organization.
- **`trustDirection`**: 1=Inbound, 2=Outbound, 3=Bidirectional.
- **`trustType`**: 1=NT4 Downlevel, 2=AD/Kerberos, 3=MIT Kerberos.
- **Batch LDAP — N+1 vermeiden**: DNs in 50er-Gruppen mit OR-Filter batchen: `(|(distinguishedName=dn1)...(dn50))`. Ergebnis in `Dictionary<string, T>(StringComparer.OrdinalIgnoreCase)` für O(1)-Lookup. Limit 50 bleibt sicher unter dem Windows-DC-Filterlimit von 10.240 Zeichen bei normalen DNs.
- **`DirectoryEntry` ist nicht thread-safe**: bei `Task.WhenAll`/`Task.Run` muss jede parallele Task ihre eigene `_connector.GetRootEntry()`-Instanz öffnen. Niemals einen gemeinsamen `DirectoryEntry` über Thread-Grenzen hinweg teilen.

## Security-Patterns

- **LDAP Filter Value Escaping**: `AdConnector.EscapeFilterValue(value)` — RFC 4515, escapes `\`, `*`, `(`, `)`, NUL. Für Attribut-Wert-Position im Filter (z.B. `(sAMAccountName={value})`). Unterschied zu `EscapeDn()`: letzteres escapet nur `(`, `)`, `\` für DN-Position.
- **DPAPI Credential Storage**: `ProtectedData.Protect/Unprotect(null, DataProtectionScope.CurrentUser)`. Key gebunden an Windows-Login-Secret + Maschinen-TPM — nicht offline ableitbar. NuGet: `System.Security.Cryptography.ProtectedData 8.0.0`. Nie zu HWID-basierter Ableitung zurückkehren (auf VMs ist HWID = `SHA256("UNKNOWN-UNKNOWN-UNKNOWN")` = öffentlich bekannt).
- **WMI logName Allowlist**: `if (logName is not ("System" or "Security" or "Application")) return [];` — immer vor WQL-Interpolation. Latente Injection-Fläche bei freiem String.
- **Update-URL Host-Pinning**: Vor Download validieren: `uri.Host` muss `github.com` oder `objects.githubusercontent.com` sein, `uri.Scheme` muss `https` sein. Temp-Dateiname: `Guid.NewGuid():N` — kein vorhersehbarer Pfad.
- **AdSearch LDAP Filter Mode**: Intentionaler Raw-Passthrough — Power-User-Feature. `AdSearchView` zeigt permanentes Amber-Banner wenn `IsLdapMode == true`. Binding nie entfernen.

## Score-System

Penalties in `ComputeComplianceScoreAsync()`:
- Prozentual (User/Group/Computer-Hygiene): `PctPenalty(count, total, maxPenalty, fullAtPct)`
- Absolut (Security-Findings): `Math.Min(cap, count * perItem)`
- Floor: `Math.Max(10, score)` — Score ist nie 0
- Phase 4 SID History: `-3/Account, Cap 12`

## Converter

`EqualityConverter` (seit v2.5.0) — in `App.xaml` als `{StaticResource EqualityConverter}` verfügbar.

`DaysToExpiryColorConverter` — Passwort-Ablauf-Farben: rot <14d, orange <30d, gelb <60d, grün >=60d. Verwendet in `PasswordReportView`.

## PDF-Design-Konstanten (PdfExporter)

`PdfPageBuilder` kapselt das 3-Zonen-Layout:
- Header: `#0f172a` (dark), 28pt Höhe
- Content: weiß, 40pt Margin alle Seiten
- Footer: `#f8fafc` (light), 20pt Höhe

Tabellenzeilen: 6pt oben/unten, 10pt rechts, 13pt links (3pt Abstand für linke Akzent-Border). Datum und SAM-Namen in Monospace. 3pt Abstand zwischen Zeilen. Spaltenheader nach jedem `NewPage()` wiederholen.

Score-Farben: >=80 `#7DFFB3` (grün), >=60 `#FCD34D` (gelb), <60 `#FCA5A5` (rot).

User-Detail-Layout: Label-Spalte 140pt breit, `#6b7280` 9pt Uppercase; Wert-Spalte 10pt `#111827`; rot fett für abgelaufene/Warnung-Werte.

Logo: `pack://application:,,,/Resources/icon_128.png`

## WMI / Verbindung

`AdWmiClient` deckt ab: Disks, lokale Admins, aktive Sessions, Event Log.

WMI-Voraussetzungen auf dem Zielrechner: Port 135 (DCE/RPC) + dynamische RPC-Ports offen + Windows-Firewall-Regel "WMI-In" aktiviert.

Terminal-Server-HWID-Fallback: `SHA256(CPU+MB+Disk+UserName)` — verhindert dass alle RDS-Sessions dieselbe HWID teilen.

## ThemeManager

Persistiert Dark/Light-Auswahl in HKCU Registry — Präferenz überlebt App-Neustarts. Theme-Wechsel via `merged ResourceDictionary` ohne Restart.
