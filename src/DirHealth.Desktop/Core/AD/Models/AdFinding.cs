using DirHealth.Desktop.Core.AD;

namespace DirHealth.Desktop.Core.AD.Models;

public enum FindingSeverity { Low, Medium, High, Critical }

public class AdFinding
{
    public string Category    { get; set; } = "";
    public string Title       { get; set; } = "";
    public string Description { get; set; } = "";
    public FindingSeverity Severity { get; set; }
    public int Count          { get; set; }
    /// <summary>Structured affected objects (S16-1). Declared before <see cref="AffectedObjects"/>
    /// so JSON reads it first.</summary>
    public List<AffectedObject> Objects { get; set; } = [];

    /// <summary>
    /// Display names of the affected objects. Kept for exports and for caches written before
    /// S16: reading an old cache fills <see cref="Objects"/> from these strings (Name only).
    /// </summary>
    public List<string> AffectedObjects
    {
        get => Objects.Select(o => o.Detail.Length > 0 ? $"{o.Name} — {o.Detail}" : o.Name).ToList();
        set
        {
            if (Objects.Count == 0 && value is not null)
                Objects = value.Select(v => new AffectedObject { Name = v }).ToList();
        }
    }
    public bool IsAcknowledged { get; set; }
    public string AcknowledgeNote { get; set; } = "";

    // ── S16-1: rule metadata from the catalog (single source: Core/AD/FindingCatalog.cs) ──
    [System.Text.Json.Serialization.JsonIgnore] public FindingRule? Rule => FindingCatalog.Get(Category);
    [System.Text.Json.Serialization.JsonIgnore] public string RuleId => Rule?.RuleId ?? "";
    [System.Text.Json.Serialization.JsonIgnore] public int    Weight => Rule?.Weight ?? 0;
    [System.Text.Json.Serialization.JsonIgnore] public IReadOnlyList<FindingReference> References => Rule?.References ?? [];

    /// <summary>One concrete, plain-text remediation step (reused verbatim in UI and PDF).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string Remediation => Rule?.HowToFix ?? "";

    /// <summary>First reference link (kept for the existing "Microsoft Learn guidance" button).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string RemediationUrl => References.Count > 0 ? References[0].Url : "";
}
