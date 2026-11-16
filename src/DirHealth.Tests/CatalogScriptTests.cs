using DirHealth.Desktop.Core.AD;
using Xunit;

namespace DirHealth.Tests;

/// <summary>
/// Structural checks on the catalog's Fix/Revert scripts (text shown to the admin — DirHealth never runs them).
/// Regression for the review of 2026-11-16: <c>ObjectRevert</c> wrapped the per-row command in
/// <c>{ … }</c> on one line, so a trailing <c># comment</c> (DH-USR-003, DH-GRP-001) swallowed the closing
/// brace and the undo script did not parse.
/// </summary>
public class CatalogScriptTests
{
    public static IEnumerable<object[]> Scripts() =>
        FindingCatalog.All.SelectMany(r => new[]
        {
            new object[] { r.RuleId, "Fix", r.Fix },
            new object[] { r.RuleId, "Revert", r.Revert },
        });

    [Theory]
    [MemberData(nameof(Scripts))]
    public void Braces_AreBalanced_WithCommentsStripped(string ruleId, string kind, string script)
    {
        int depth = 0;
        var lines = script.Split('\n');
        for (int n = 0; n < lines.Length; n++)
        {
            foreach (char c in StripComment(lines[n]))
            {
                if (c == '{') depth++;
                else if (c == '}') depth--;
                Assert.True(depth >= 0, $"{ruleId} {kind}: '}}' without '{{' in line {n + 1}: {lines[n]}");
            }
        }
        Assert.True(depth == 0, $"{ruleId} {kind}: {depth} unclosed '{{' — a comment may hide the closing brace:\n{script}");
    }

    [Theory]
    [InlineData("ExpiredPasswords")]   // DH-USR-003
    [InlineData("EmptyGroups")]        // DH-GRP-001
    public void NamedRegressions_KeepTheirComment_AndCloseTheLoop(string category)
    {
        var revert = FindingCatalog.Get(category)!.Revert;
        Assert.Contains("#", revert);                         // the hint for the admin is still there
        Assert.Equal("}", revert.Split('\n')[^1].Trim());     // and the loop is closed on its own line
    }

    [Theory]
    [InlineData("a { b }   # c }", "a { b }   ")]
    [InlineData("# all comment {", "")]
    [InlineData("x \"#n{\" # c", "x \"   \" ")]     // # and { inside a string are text
    [InlineData("x '#{' # c", "x '  ' ")]
    [InlineData("$a#b {", "$a#b {")]                 // # glued to a token starts no comment
    public void StripComment_FollowsPowerShellRules(string line, string expectedCode) =>
        Assert.Equal(expectedCode, StripComment(line));

    /// <summary>
    /// The code part of one PowerShell line: everything before a <c>#</c> that starts a comment
    /// (outside quotes, at line start or after a blank), with string contents blanked out so braces
    /// inside strings are not counted. Single quotes escape by doubling, double quotes with a backtick.
    /// </summary>
    internal static string StripComment(string line)
    {
        var code = new System.Text.StringBuilder();
        char quote = '\0';
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quote == '\0')
            {
                if (c == '#' && (i == 0 || line[i - 1] is ' ' or '\t')) break;
                if (c is '\'' or '"') quote = c;
                code.Append(c);
            }
            else
            {
                if (quote == '"' && c == '`' && i + 1 < line.Length) { code.Append(' ').Append(' '); i++; continue; }
                if (c == quote) quote = '\0';
                code.Append(c == quote || quote == '\0' ? c : ' ');
            }
        }
        return code.ToString();
    }
}
