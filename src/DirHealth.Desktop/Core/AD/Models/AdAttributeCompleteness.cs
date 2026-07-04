namespace DirHealth.Desktop.Core.AD.Models;

public class AdAttributeCompleteness
{
    public string AttributeName { get; set; } = "";  // display label, e.g. "Email"
    public string LdapName      { get; set; } = "";  // e.g. "mail"
    public int    FilledCount   { get; set; }
    public int    TotalCount    { get; set; }

    public double Percent      => TotalCount == 0 ? 0 : Math.Round((double)FilledCount / TotalCount * 100, 1);
    public string PercentLabel => $"{Percent:0.#}%";
    public string CountLabel   => $"{FilledCount} / {TotalCount}";
}
