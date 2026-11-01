// Integration tests against the actual repository.
// These tests run the guard in "public files mode", meaning only files present
// in tools/public-allowlist.txt are content-scanned. All tracked files are still
// checked for forbidden extensions, Python files, and forbidden paths.
//
// For the real repository to pass these tests, the public allowlist must cover
// every non-internal tracked file, and those files must be clean of all
// provenance violations.

using Xunit;

namespace Dw3.Repository.Tests;

public sealed class RealRepositoryTests
{
    // Walk up from the test output directory to find the repo root (.git/).
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string AllowlistPath =
        Path.Combine(RepoRoot, "tools", "public-allowlist.txt");

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // .git is a directory in a normal clone and a file in a worktree.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git)) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        throw new InvalidOperationException("Cannot locate the repository root (.git/).");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Allowlist integrity
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void PublicAllowlist_Exists()
    {
        Assert.True(File.Exists(AllowlistPath),
            $"tools/public-allowlist.txt not found at {AllowlistPath}");
    }

    [Fact]
    public void PublicAllowlist_IsNotEmpty()
    {
        var allowlist = RepositoryGuard.LoadAllowlist(AllowlistPath);
        Assert.NotEmpty(allowlist);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Full guard in public-files mode
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RealRepo_PublicFilesMode_HasNoViolations()
    {
        var allowlist = RepositoryGuard.LoadAllowlist(AllowlistPath);

        // Run the guard in public mode. Only files listed in the allowlist
        // are content-scanned; the allowlist-coverage check applies to all
        // tracked files that are not matched by an internal exclusion pattern.
        var allViolations = RepositoryGuard.Check(RepoRoot, allowlist);

        // Separate allowlist violations (expected = zero non-internal unreviewed files)
        // from content violations.
        var contentViolations = allViolations
            .Where(v => v.Rule != "unreviewed-source")
            .ToList();

        var unreviewedViolations = allViolations
            .Where(v => v.Rule == "unreviewed-source")
            .ToList();

        // Internal paths that are intentionally tracked but not in the public allowlist.
        // These are excluded from the "unreviewed-source" check.
        var internalPrefixes = new[]
        {
            "research/", ".hermes/", "agent-notes/", "archive/", ".verify/",
            "docs/research/", "docs/M", "AGENTS.md", "SUIVI.md",
        };

        var unexpectedUnreviewed = unreviewedViolations
            .Where(v => !internalPrefixes.Any(p =>
                v.Path.StartsWith(p, StringComparison.OrdinalIgnoreCase) ||
                v.Path.Equals(p.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (contentViolations.Count > 0 || unexpectedUnreviewed.Count > 0)
        {
            var all = contentViolations.Concat(unexpectedUnreviewed).ToList();
            var report = string.Join("\n",
                all.Select(v => $"  [{v.Rule}] {v.Path}: {v.Note}"));
            Assert.Fail(
                $"{all.Count} provenance violation(s) found in public files:\n{report}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Architecture: Dw3.Game isolation
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void GameProject_DoesNotReferenceDiscOrFormats()
    {
        var violations = RepositoryGuard.CheckGameArchitecture(RepoRoot);
        if (violations.Count > 0)
        {
            var report = string.Join("\n",
                violations.Select(v => $"  [{v.Rule}] {v.Path}: {v.Note}"));
            Assert.Fail($"Dw3.Game architecture violation(s):\n{report}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // No Python files in public scope
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoPythonFilesTracked_InPublicScope()
    {
        var allowlist = RepositoryGuard.LoadAllowlist(AllowlistPath);
        var tracked = RepositoryGuard.GetTrackedFiles(RepoRoot);

        // Only allowlist files are in scope for the no-python check.
        var pythonInPublic = tracked
            .Where(f => allowlist.Contains(f) &&
                        f.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(pythonInPublic);
    }
}
