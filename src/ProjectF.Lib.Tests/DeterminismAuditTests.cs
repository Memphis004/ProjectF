// Stage-4 determinism audit, enforced by the test suite itself (mirrors
// tools/audit-determinism.ps1 — keep the two pattern lists in sync).
//
// knowledge.md rule 2: ProjectF.Lib is on-chain code that Libplanet
// re-executes on every full node, so the whole assembly must be free of
// wall-clock reads, process-seeded RNGs, GUIDs, file I/O and environment
// reads. If this test fails, fix the code — do not weaken the pattern list
// without a written justification in the audit script too.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ProjectF.Lib.Tests;

public class DeterminismAuditTests
{
    // (regex, why it is forbidden) — case-sensitive, applied per line.
    // Mirrors $patterns in tools/audit-determinism.ps1.
    private static readonly (string Pattern, string Reason)[] ForbiddenPatterns =
    {
        (@"DateTime\.Now", "wall clock"),
        (@"DateTime\.UtcNow", "wall clock"),
        (@"DateTimeOffset\.Now", "wall clock"),
        (@"DateTimeOffset\.UtcNow", "wall clock"),
        (@"new\s+Random\s*\(", "process-seeded RNG"),
        (@"System\.Random", "process-seeded RNG"),
        (@"Guid\.NewGuid", "random GUID"),
        (@"\bFile\.\w+", "file I/O"),
        (@"\bDirectory\.\w+", "filesystem I/O"),
        (@"\bEnvironment\.\w+", "machine/environment reads"),
        (@"Process\.Start", "process spawn"),
        (@"Stopwatch\.", "wall-clock timing"),
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ProjectF.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent!;
        }

        throw new InvalidOperationException("repo root (ProjectF.sln) not found");
    }

    public static IEnumerable<object[]> SourceFiles()
    {
        string sourceRoot = Path.Combine(RepoRoot(), "src", "ProjectF.Lib");
        foreach (string file in Directory.EnumerateFiles(
            sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f =>
            {
                string normalized = f.Replace('\\', '/');
                return !normalized.Contains("/bin/") && !normalized.Contains("/obj/");
            })
            .OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return new object[] { file };
        }
    }

    [Fact]
    public void Audit_scope_contains_source_files()
    {
        // Guards against the audit silently passing on an empty file set
        // (e.g. if the repo layout ever moves).
        Assert.NotEmpty(SourceFiles().ToList());
    }

    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void Source_file_is_free_of_non_deterministic_APIs(string path)
    {
        string[] lines = File.ReadAllLines(path);
        var violations = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            foreach ((string pattern, string reason) in ForbiddenPatterns)
            {
                if (Regex.IsMatch(lines[i], pattern))
                {
                    string relative = Path.GetRelativePath(RepoRoot(), path);
                    violations.Add(
                        $"{relative}:{i + 1}: [{reason}] {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "knowledge.md rule 2 violations (non-deterministic APIs in " +
            "on-chain code):\n" + string.Join("\n", violations));
    }
}
