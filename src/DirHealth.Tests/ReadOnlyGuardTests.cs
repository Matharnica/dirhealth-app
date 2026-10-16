using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace DirHealth.Tests;

/// <summary>
/// S13-6 — Gate "DirHealth only reads AD". Scans every *.cs file under src/DirHealth.Desktop
/// for directory write APIs and fails with file:line for each hit.
///
/// What it checks:
///   everywhere            CommitChanges, DeleteTree, SetPassword, InvokeSet,
///                         Add/Modify/ModifyDN/DeleteRequest (System.DirectoryServices.Protocols)
///   in files that mention MoveTo(, Rename(, Children.Add/Remove, Properties[...] = …,
///   System.DirectoryServices  Properties[...].Add/AddRange/Clear/Remove/Insert/Value =,
///                         ObjectSecurity =, *AccessRule*( , SetOwner( (ACL writes, S20-3)
///   (scoped there because WPF panels have Children.Add and System.IO has MoveTo)
///
/// What it does NOT find:
///   - calls built at runtime: reflection, `dynamic`, Invoke("…") with a computed name
///   - ADSI through COM interop (IADs/IADsUser) or a cast to an interop interface
///   - writes in other assemblies or in a NuGet package
///   - PowerShell/ldifde/dsmod started via Process.Start
///   - aliases/extension methods that wrap a write API under another name
///   - line comments are skipped; text inside string literals is checked (false positive
///     possible — then rename the text, never weaken the pattern)
/// Known limit: CI runs on push to dev and on tags, not on PRs, so this blocks the
/// release build, not the merge.
/// </summary>
public class ReadOnlyGuardTests
{
    private readonly ITestOutputHelper _out;
    public ReadOnlyGuardTests(ITestOutputHelper output) => _out = output;

    private static readonly Regex[] GlobalPatterns =
    [
        new(@"\bCommitChanges\s*\("),
        new(@"\bDeleteTree\s*\("),
        new(@"\bSetPassword\b"),
        new(@"\bInvokeSet\s*\("),
        new(@"\b(Add|Modify|ModifyDN|Delete)Request\b"),
    ];

    private static readonly Regex[] DirectoryFilePatterns =
    [
        new(@"\.MoveTo\s*\("),
        new(@"\.Rename\s*\("),
        new(@"\bChildren\s*\.\s*(Add|Remove)\s*\("),
        new(@"\bProperties\s*\[[^\]]+\]\s*(\.Value\s*)?=(?!=)"),
        new(@"\bProperties\s*\[[^\]]+\]\s*\.\s*(Add|AddRange|Clear|Remove|RemoveAt|Insert)\s*\("),
        // ACL write paths (S20-3 gate): reading nTSecurityDescriptor is fine, changing it is not
        new(@"\.ObjectSecurity\s*=(?!=)"),
        new(@"\b(Add|Set|Remove|Purge|Modify|Reset)AccessRule\w*\s*\("),
        new(@"\bSetOwner\s*\("),
    ];

    public record Hit(string File, int Line, string Text);

    public static List<Hit> Scan(IEnumerable<(string Path, string Text)> files)
    {
        var hits = new List<Hit>();
        foreach (var (path, text) in files)
        {
            bool touchesDirectory = text.Contains("System.DirectoryServices", StringComparison.Ordinal);
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var code = StripLineComment(lines[i]);
                bool hit = GlobalPatterns.Any(p => p.IsMatch(code))
                           || (touchesDirectory && DirectoryFilePatterns.Any(p => p.IsMatch(code)));
                if (hit) hits.Add(new Hit(path, i + 1, lines[i].Trim()));
            }
        }
        return hits;
    }

    // Drops "// …" outside of string literals. Good enough for this codebase; a "//" inside
    // a verbatim/interpolated string with odd quoting just means the rest of that line is kept.
    private static string StripLineComment(string line)
    {
        bool inString = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            if (line[i] == '"' && (i == 0 || line[i - 1] != '\\')) inString = !inString;
            if (!inString && line[i] == '/' && line[i + 1] == '/') return line[..i];
        }
        return line;
    }

    private static string FindDesktopSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "DirHealth.Desktop");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("src/DirHealth.Desktop not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void DesktopSource_ContainsNoDirectoryWriteApi()
    {
        var root  = FindDesktopSourceDir();
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => (Path.GetRelativePath(root, f), File.ReadAllText(f)))
            .ToList();

        var hits = Scan(files);
        // Denominator (PROCESS.md P9): what the gate actually looked at
        _out.WriteLine($"ReadOnlyGuard — checked: {files.Count} files · skipped: bin/obj · hits: {hits.Count}");
        foreach (var h in hits) _out.WriteLine($"  {h.File}:{h.Line}  {h.Text}");

        Assert.True(files.Count > 50, $"Only {files.Count} source files found — wrong directory?");
        Assert.True(hits.Count == 0,
            "AD write API found — DirHealth is read-only:\n" +
            string.Join("\n", hits.Select(h => $"{h.File}:{h.Line}  {h.Text}")));
    }

    // ── Self-test (PROCESS.md P9): the gate must go red on a real regression ────

    [Theory]
    [InlineData("entry.CommitChanges();")]
    [InlineData("entry.DeleteTree();")]
    [InlineData("user.Invoke(\"SetPassword\", new object[] { pwd });")]
    [InlineData("entry.InvokeSet(\"description\", \"x\");")]
    [InlineData("var req = new ModifyRequest(dn, op, attr, val);")]
    [InlineData("conn.SendRequest(new DeleteRequest(dn));")]
    public void Scan_FlagsWriteApi_InAnyFile(string line)
    {
        var hits = Scan([("Injected.cs", "class X {\n" + line + "\n}")]);
        var hit  = Assert.Single(hits);
        Assert.Equal(2, hit.Line);
    }

    [Theory]
    [InlineData("entry.Properties[\"description\"].Value = \"x\";")]
    [InlineData("entry.Properties[\"member\"].Add(dn);")]
    [InlineData("entry.Properties[\"member\"].Clear();")]
    [InlineData("entry.Children.Add(\"CN=x\", \"user\");")]
    [InlineData("entry.MoveTo(newParent);")]
    [InlineData("entry.Rename(\"CN=y\");")]
    [InlineData("entry.ObjectSecurity = sd;")]
    [InlineData("entry.ObjectSecurity.AddAccessRule(rule);")]
    [InlineData("sd.SetAccessRuleProtection(true, false);")]
    [InlineData("sd.SetOwner(sid);")]
    public void Scan_FlagsDirectoryMutation_InDirectoryFile(string line)
    {
        var src = "using System.DirectoryServices;\nclass X {\n" + line + "\n}";
        Assert.Single(Scan([("Injected.cs", src)]));
    }

    [Theory]
    [InlineData("canvas.Children.Add(dot);")]
    [InlineData("new FileInfo(a).MoveTo(b);")]
    public void Scan_IgnoresSameNames_OutsideDirectoryFiles(string line)
    {
        Assert.Empty(Scan([("View.xaml.cs", "class X {\n" + line + "\n}")]));
    }

    [Theory]
    [InlineData("var v = props[\"cn\"][0];")]
    [InlineData("if (r.Properties[\"x\"].Count == 0) return;")]
    [InlineData("searcher.PropertiesToLoad.Add(\"cn\");")]
    [InlineData("// entry.CommitChanges() would write — never do this")]
    public void Scan_IgnoresReadsAndComments(string line)
    {
        var src = "using System.DirectoryServices;\nclass X {\n" + line + "\n}";
        Assert.Empty(Scan([("Reader.cs", src)]));
    }
}
