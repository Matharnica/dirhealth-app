namespace DirHealth.Desktop.Core.Export;

internal static class DnHelper
{
    internal static string OuFromDn(string dn)
    {
        var parts = dn.Split(',')
                      .Where(p => p.StartsWith("OU=", StringComparison.OrdinalIgnoreCase))
                      .Select(p => p[3..]);
        return string.Join("/", parts);
    }
}
