---
type: reference
last_updated: 2026-05-14
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

## Score-System

Penalties in `ComputeComplianceScoreAsync()`:
- Prozentual (User/Group/Computer-Hygiene): `PctPenalty(count, total, maxPenalty, fullAtPct)`
- Absolut (Security-Findings): `Math.Min(cap, count * perItem)`
- Floor: `Math.Max(10, score)` — Score ist nie 0
- Phase 4 SID History: `-3/Account, Cap 12`

## Neue Converter

`EqualityConverter` (seit v2.5.0) — in `App.xaml` als `{StaticResource EqualityConverter}` verfügbar.
