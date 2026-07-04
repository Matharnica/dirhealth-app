namespace DirHealth.Desktop.Core.AD;

// All hygiene-score arithmetic in one pure, DI-free place so it is unit-testable and the
// scanner's RunCompleteScanAsync stays a thin orchestrator (see S11-2 / S12-3).
public static class ScoreCalculator
{
    public const int Max = 100;
    public const int Min = 10;

    // Penalty proportional to (count/total), reaching maxPenalty at fullAtPct% saturation.
    public static int PctPenalty(int count, int total, int maxPenalty, double fullAtPct)
    {
        if (total == 0 || count == 0) return 0;
        var pct = (double)count / total * 100.0;
        return (int)Math.Min(maxPenalty, Math.Round(maxPenalty * pct / fullAtPct));
    }

    public static int Compute(ScoreInputs i)
    {
        int score = Max;
        score -= PctPenalty(i.InactiveUsers,     i.TotalUsers,     20, 30);
        score -= PctPenalty(i.NeverExpires,      i.TotalUsers,     15, 50);
        score -= PctPenalty(i.ExpiredPwd,        i.TotalUsers,     18, 30);
        score -= PctPenalty(i.EmptyGroups,       i.TotalGroups,     8, 35);
        score -= PctPenalty(i.SingleMember,      i.TotalGroups,     6, 35);
        score -= PctPenalty(i.InactiveComputers, i.TotalComputers, 10, 40);
        score -= Math.Min(5, i.NoOs);
        score -= Math.Min(12, i.Kerberoastable * 4);
        score -= i.PolicyHighCount * 8 + i.PolicyOtherCount * 4;
        score -= Math.Min(15, i.EolDcCount * 8 + i.EolPcCount * 3);
        score -= Math.Min(15, i.AsRepRoastable * 3);
        score -= Math.Min(20, i.UnconstrainedDelegation * 6);
        score -= Math.Min(10, i.PasswordNotRequired * 2);
        score -= Math.Min(20, i.StaleDomainAdmins * 5);
        score -= i.FgppHighCount * 8 + i.FgppOtherCount * 4;
        score -= Math.Min(12, i.SidHistory * 3);
        return Math.Max(Min, score);
    }
}

// Aggregated counts feeding the score; all default to 0 so a clean AD scores 100.
public record ScoreInputs
{
    public int TotalUsers              { get; init; }
    public int TotalGroups             { get; init; }
    public int TotalComputers          { get; init; }
    public int InactiveUsers           { get; init; }
    public int NeverExpires            { get; init; }
    public int ExpiredPwd              { get; init; }
    public int EmptyGroups             { get; init; }
    public int SingleMember            { get; init; }
    public int InactiveComputers       { get; init; }
    public int NoOs                    { get; init; }
    public int Kerberoastable          { get; init; }
    public int PolicyHighCount         { get; init; }
    public int PolicyOtherCount        { get; init; }
    public int EolDcCount              { get; init; }
    public int EolPcCount              { get; init; }
    public int AsRepRoastable          { get; init; }
    public int UnconstrainedDelegation { get; init; }
    public int PasswordNotRequired     { get; init; }
    public int StaleDomainAdmins       { get; init; }
    public int FgppHighCount           { get; init; }
    public int FgppOtherCount          { get; init; }
    public int SidHistory              { get; init; }
}
