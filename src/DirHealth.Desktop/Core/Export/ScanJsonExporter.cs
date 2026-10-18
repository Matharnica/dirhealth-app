using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.Export;

// ── S22-7: machine-readable scan export (ADR 0005) ────────────────────────────
// Explicit DTOs instead of serialising the domain types: the file is a published contract, so a new
// property on AdFinding/AffectedObject must not leak into it unnoticed. No DTO carries raw AD attributes —
// secret-bearing attribute names (userPassword, unicodePwd, LAPS, BitLocker) can never appear (test-guarded).

public sealed record ScanJsonDocument(
    int SchemaVersion, int FormulaVersion, string Tool, DateTime CreatedUtc,
    string Domain, string DomainController, string ScanTime,
    ScanJsonScore? Score, ScanJsonTotals? Totals,
    IReadOnlyList<ScanJsonCheck> Checks, IReadOnlyList<ScanJsonFinding> Findings);

/// <summary>Score per ADR 0002. Value is null when no score could be computed (too many failed checks).</summary>
public sealed record ScanJsonScore(int? Value, bool IsUpperBound, int EvaluatedRules, int TotalRules, double Coverage);

public sealed record ScanJsonTotals(int Users, int Groups, int Computers);

public sealed record ScanJsonCheck(string RuleId, string Category, CheckStatus Status, string Reason, int Count);

public sealed record ScanJsonFinding(
    string RuleId, string Category, string Title, FindingSeverity Severity, int Weight, int Count,
    int PointsIfFixed, string HowToFix, IReadOnlyList<FindingReference> References,
    IReadOnlyList<ScanJsonObject> Objects);

public sealed record ScanJsonObject(
    string Name, string DistinguishedName, string Sid, AffectedObjectType Type, string Detail,
    bool IsDomainController, ScanJsonException Exception);

/// <summary>Exception state of the object (ADR 0001): None/Active/Expired + "until … — reason".</summary>
public sealed record ScanJsonException(ExceptionState State, string Label);

/// <summary>What the dashboard knows after a live scan — pure input for <see cref="ScanJsonExporter.Build"/>.</summary>
public sealed record ScanJsonInput(
    IReadOnlyList<AdFinding> Findings, IReadOnlyList<CheckResult> Checks, ScoreResult? Score, ScanTotals? Totals,
    string Domain, string DomainController, string ScanTime, DateTime CreatedUtc, string Version);

public static class ScanJsonExporter
{
    /// <summary>Version of the file layout (ADR 0005). Bump on a breaking change; adding a field is not breaking.</summary>
    public const int SchemaVersion = 1;

    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters           = { new JsonStringEnumConverter() },   // "High", "Passed" — readable for scripts
    };

    public static ScanJsonDocument Build(ScanJsonInput input) => new(
        SchemaVersion, ScoreModel.FormulaVersion, $"DirHealth {input.Version}", input.CreatedUtc,
        input.Domain, input.DomainController, input.ScanTime,
        input.Score is { } s
            ? new ScanJsonScore(s.Available ? s.Score : null, s.IsUpperBound, s.EvaluatedRules, s.TotalRules, s.Coverage)
            : null,
        input.Totals is { } t ? new ScanJsonTotals(t.Users, t.Groups, t.Computers) : null,
        input.Checks.Select(c => new ScanJsonCheck(c.RuleId, c.Category, c.Status, c.Reason, c.Count)).ToList(),
        WorklistExporter.Ordered(input.Findings).Select(f => new ScanJsonFinding(
            f.RuleId, f.Category, f.Title, f.Severity, f.Weight, f.Count,
            input.Score?.PointsFor(f.Category) ?? 0, f.Remediation, f.References,
            f.Objects.Select(o => new ScanJsonObject(o.Name, o.DistinguishedName, o.Sid, o.Type, o.Detail,
                o.IsDomainController, new ScanJsonException(o.Exception, o.ExceptionLabel))).ToList())).ToList());

    public static string Serialize(ScanJsonDocument doc) => JsonSerializer.Serialize(doc, Options);

    public static ScanJsonDocument? Deserialize(string json) => JsonSerializer.Deserialize<ScanJsonDocument>(json, Options);

    /// <summary>Writes the JSON (UTF-8 without BOM — JSON tools expect none) and its .meta.txt. Returns the finding count.</summary>
    public static int Export(ScanJsonInput input, string path, ExportContext ctx)
    {
        var doc = Build(input);
        File.WriteAllText(path, Serialize(doc), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        ExportMetadata.WriteFor(path, ctx, "Scan (JSON)", $"schemaVersion={SchemaVersion}", doc.Findings.Count);
        return doc.Findings.Count;
    }
}
