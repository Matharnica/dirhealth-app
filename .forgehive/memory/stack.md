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

## AD / LDAP

- **`GetEntryByDn(dn)`**: baut LDAP-Pfad mit optionalem Domain-Prefix — immer diese Methode nutzen statt direkt `new DirectoryEntry($"LDAP://{dn}")`.
- **`GetAdDateTime()`**: AD gibt `whenCreated`/`whenChanged` als `DateTime`-Objekte zurück (nicht als Generalized-Time-String). `val is DateTime dt` reicht.
- **Generalized Time im LDAP-Filter**: Format `yyyyMMddHHmmss.0Z` (Punkt vor Z, nicht Doppelpunkt).
- **`trustAttributes` Bit 8** = Forest Trust; Bit 4 = SID-Filtering; Bit 32 = Cross-Organization.
- **`trustDirection`**: 1=Inbound, 2=Outbound, 3=Bidirectional.
- **`trustType`**: 1=NT4 Downlevel, 2=AD/Kerberos, 3=MIT Kerberos.

## Score-System

Penalties in `ComputeComplianceScoreAsync()`:
- Prozentual (User/Group/Computer-Hygiene): `PctPenalty(count, total, maxPenalty, fullAtPct)`
- Absolut (Security-Findings): `Math.Min(cap, count * perItem)`
- Floor: `Math.Max(10, score)` — Score ist nie 0
- Phase 4 SID History: `-3/Account, Cap 12`

## Neue Converter

`EqualityConverter` (seit v2.5.0) — in `App.xaml` als `{StaticResource EqualityConverter}` verfügbar.
