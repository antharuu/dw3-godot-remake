// Fabricated-repository tests for each provenance and architecture rule.
// Each rule gets a RED test (violation is detected) and a GREEN test
// (clean repo produces no violation for that rule).
// Repositories are assembled in temporary directories; no real disc or ROM data
// is used — all content is invented.

using System.Diagnostics;
using Xunit;

namespace Dw3.Repository.Tests;

public sealed class RepositoryGuardTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal Git repo in a temp dir with the given tracked files.
    /// All files are added and committed; no real ROM data is used.
    /// </summary>
    private string MakeRepo(params (string path, string content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dw3-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);

        Git(dir, "init -b main");
        Git(dir, "config user.email \"guard-test@example.invalid\"");
        Git(dir, "config user.name \"Guard Test\"");

        foreach (var (path, content) in files)
        {
            var full = Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            Git(dir, $"add \"{path}\"");
        }
        Git(dir, "commit -m init --allow-empty");
        return dir;
    }

    private static void Git(string workdir, string args)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory = workdir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
    }

    private static void AssertViolation(IReadOnlyList<Violation> v, string rule) =>
        Assert.Contains(v, x => x.Rule == rule);

    private static void AssertNoViolation(IReadOnlyList<Violation> v, string rule) =>
        Assert.DoesNotContain(v, x => x.Rule == rule);

    private static void AssertClean(IReadOnlyList<Violation> v) =>
        Assert.Empty(v);

    // ─────────────────────────────────────────────────────────────────────
    // P1: forbidden-extension
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ForbiddenExtension_DetectsDiscFile()
    {
        var repo = MakeRepo(("rom/disc.bin", "placeholder"), ("README.md", "ok"));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "forbidden-extension");
    }

    [Fact]
    public void ForbiddenExtension_PassesOnCleanRepo()
    {
        var repo = MakeRepo(("README.md", "ok"), ("src/Module.cs", "// ok"));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "forbidden-extension");
    }

    // ─────────────────────────────────────────────────────────────────────
    // P1: no-python
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoPython_DetectsPyFile()
    {
        var repo = MakeRepo(("tools/script.py", "# placeholder"), ("README.md", "ok"));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "no-python");
    }

    [Fact]
    public void NoPython_PassesWithNoPyFiles()
    {
        var repo = MakeRepo(("README.md", "ok"), ("src/Module.cs", "// ok"));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "no-python");
    }

    // ─────────────────────────────────────────────────────────────────────
    // P1: forbidden-path
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ForbiddenPath_DetectsAgentsMd()
    {
        var repo = MakeRepo(("AGENTS.md", "internal"), ("README.md", "ok"));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "forbidden-path");
    }

    [Fact]
    public void ForbiddenPath_DetectsResearchDir()
    {
        var repo = MakeRepo(("research/notes.md", "internal"), ("README.md", "ok"));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "forbidden-path");
    }

    [Fact]
    public void ForbiddenPath_DetectsRomDirExceptReadme()
    {
        var repo = MakeRepo(("rom/disc.cue", "placeholder"), ("README.md", "ok"));
        // .cue triggers forbidden-extension and forbidden-path
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "forbidden-path");
    }

    [Fact]
    public void ForbiddenPath_AllowsRomReadme()
    {
        var repo = MakeRepo(("rom/README.md", "Put your disc here."), ("README.md", "ok"));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "forbidden-path");
    }

    [Fact]
    public void ForbiddenPath_PassesOnCleanRepo()
    {
        var repo = MakeRepo(("README.md", "ok"), ("src/Game.cs", "// ok"));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "forbidden-path");
    }

    // ─────────────────────────────────────────────────────────────────────
    // provenance.md: unreviewed-source
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void UnreviewedSource_DetectsFileAbsentFromAllowlist()
    {
        var repo = MakeRepo(("README.md", "ok"), ("src/Module.cs", "// ok"));
        // Allowlist only contains README.md — Module.cs is unreviewed.
        var allowlist = new HashSet<string> { "README.md" };
        var v = RepositoryGuard.Check(repo, allowlist);
        AssertViolation(v, "unreviewed-source");
    }

    [Fact]
    public void UnreviewedSource_PassesWhenAllFilesListed()
    {
        var repo = MakeRepo(("README.md", "ok"), ("src/Module.cs", "// ok"));
        var allowlist = new HashSet<string> { "README.md", "src/Module.cs" };
        var v = RepositoryGuard.Check(repo, allowlist);
        AssertNoViolation(v, "unreviewed-source");
    }

    // ─────────────────────────────────────────────────────────────────────
    // provenance.md: personal-path
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void PersonalPath_DetectsUnixHomePath()
    {
        var repo = MakeRepo(("README.md", "See /home/developer/projects for details."));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "personal-path");
    }

    [Fact]
    public void PersonalPath_PassesWithNoHomePaths()
    {
        var repo = MakeRepo(("README.md", "No personal paths here."), ("src/M.cs", "// ok"));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "personal-path");
    }

    // ─────────────────────────────────────────────────────────────────────
    // provenance.md: email-address
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void EmailAddress_DetectsEmail()
    {
        var repo = MakeRepo(("README.md", "Contact: developer@example.com for help."));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "email-address");
    }

    [Fact]
    public void EmailAddress_PassesWithNoEmail()
    {
        var repo = MakeRepo(("README.md", "No contact info."), ("src/M.cs", "// ok"));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "email-address");
    }

    // ─────────────────────────────────────────────────────────────────────
    // provenance.md: internal-identifier
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void InternalIdentifier_DetectsOwnerName()
    {
        // Fabricated owner name used only in this test — NOT a real person's name.
        // Uses a source file (not a community doc) so no community-file exemption applies.
        var repo = MakeRepo(("src/InternalRef.cs", "// Created by ProjectOwner and antharuu."));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "internal-identifier");
    }

    [Fact]
    public void InternalIdentifier_DetectsKanbanId()
    {
        var repo = MakeRepo(("src/M.cs", "// C5-001 workaround for task TASK-007."));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "internal-identifier");
    }

    [Fact]
    public void InternalIdentifier_PassesWithNoIdentifiers()
    {
        var repo = MakeRepo(("README.md", "Unofficial reimplementation."), ("src/M.cs", "// ok"));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "internal-identifier");
    }

    // ─────────────────────────────────────────────────────────────────────
    // provenance.md: decomp-dependency
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void DecompDependency_DetectsDecompRoot()
    {
        var repo = MakeRepo(("src/Loader.cs", "// Requires dw3_decomp to be present."));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "decomp-dependency");
    }

    [Fact]
    public void DecompDependency_PassesWithNoDecompRef()
    {
        var repo = MakeRepo(("README.md", "Disc-only project."), ("src/M.cs", "// ok"));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "decomp-dependency");
    }

    // ─────────────────────────────────────────────────────────────────────
    // provenance.md: decomp-source-citation
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void DecompSourceCitation_DetectsSourcePath()
    {
        var repo = MakeRepo(("src/Calc.cs",
            "// Ported from src/battle/battle_mode/damage.c lines 16–190."));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "decomp-source-citation");
    }

    [Fact]
    public void DecompSourceCitation_PassesWithNoSourcePaths()
    {
        var repo = MakeRepo(("src/Calc.cs", "// Damage formula based on project spec."));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "decomp-source-citation");
    }

    // ─────────────────────────────────────────────────────────────────────
    // provenance.md: translation-claim
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void TranslationClaim_DetectsTranscriptionWord()
    {
        var repo = MakeRepo(("src/Growth.cs",
            "// This is a faithful reconstruction of the growth function."));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "translation-claim");
    }

    [Fact]
    public void TranslationClaim_PassesWithNoTranscriptionClaim()
    {
        var repo = MakeRepo(("src/Growth.cs",
            "// Reward calculation based on project-defined growth spec."));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "translation-claim");
    }

    // ─────────────────────────────────────────────────────────────────────
    // provenance.md: hex-payload
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void HexPayload_DetectsDenseByteSequence()
    {
        // Fabricated byte sequence — 8 consecutive 0x-prefixed values with hex digits.
        var repo = MakeRepo(("docs/Format.md",
            "Header: 0x1A, 0x2B, 0x3C, 0x4D, 0x5E, 0x6F, 0x7A, 0x8B raw bytes."));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "hex-payload");
    }

    [Fact]
    public void HexPayload_PassesOnFormatConstantArray()
    {
        // A short format-constant array (< 8 values) should not trigger.
        var repo = MakeRepo(("src/Decoder.cs",
            "private static readonly int[] Ids = [0x01, 0x02, 0x03];"));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "hex-payload");
    }

    // ─────────────────────────────────────────────────────────────────────
    // provenance.md: game-text-fixture
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void GameTextFixture_DetectsArchiveNameNearAssertion()
    {
        // Coupling a game text archive name to an Assert.Equal call is suspicious.
        var repo = MakeRepo(("tests/TextTests.cs",
            "// from FSTALK00 Assert.Equal(expected, result);"));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "game-text-fixture");
    }

    [Fact]
    public void GameTextFixture_PassesOnFormatDocumentation()
    {
        // Mentioning the archive name in format documentation (no assertion nearby) is fine.
        var repo = MakeRepo(("src/Decoder.cs",
            "/// Reads FSTALK00.BIN from the disc and decodes field text entries."));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "game-text-fixture");
    }

    // ─────────────────────────────────────────────────────────────────────
    // provenance.md: suspicious-generated-payload
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void SuspiciousGeneratedPayload_DetectsOutputJson()
    {
        var repo = MakeRepo(("output/zones.json", "{\"zones\":[]}"), ("README.md", "ok"));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "suspicious-generated-payload");
    }

    [Fact]
    public void SuspiciousGeneratedPayload_PassesOnRegularSourceFile()
    {
        var repo = MakeRepo(("src/Module.cs", "// ok"), ("README.md", "ok"));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "suspicious-generated-payload");
    }

    // ─────────────────────────────────────────────────────────────────────
    // provenance.md: media-or-capture (path rule)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void MediaOrCapture_DetectsTrackedImageFile()
    {
        var repo = MakeRepo(("docs/screenshot.png", "PNG"), ("README.md", "ok"));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "media-or-capture");
    }

    [Fact]
    public void MediaOrCapture_PassesWithNoMediaFiles()
    {
        var repo = MakeRepo(("README.md", "ok"), ("src/M.cs", "// ok"));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "media-or-capture");
    }

    // ─────────────────────────────────────────────────────────────────────
    // provenance.md: media-or-capture (text rule)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void MediaOrCaptureText_DetectsContactSheetReference()
    {
        var repo = MakeRepo(("docs/Notes.md",
            "Probe builds a contact-sheet from decoded fighter sprites."));
        var v = RepositoryGuard.Check(repo);
        AssertViolation(v, "media-or-capture");
    }

    [Fact]
    public void MediaOrCaptureText_PassesWithNoCaptureMention()
    {
        var repo = MakeRepo(("docs/Notes.md", "Disc format description only."));
        var v = RepositoryGuard.Check(repo);
        AssertNoViolation(v, "media-or-capture");
    }

    // ─────────────────────────────────────────────────────────────────────
    // P1: architecture — Dw3.Game must not reference Dw3.Disc or Dw3.Formats
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Architecture_DetectsDiscReferenceInCsproj()
    {
        var repo = MakeRepo(
            ("src/Dw3.Game/Dw3.Game.csproj",
             "<Project><ItemGroup><ProjectReference Include=\"../Dw3.Disc/Dw3.Disc.csproj\" /></ItemGroup></Project>"),
            ("README.md", "ok"));
        var v = RepositoryGuard.CheckGameArchitecture(repo);
        AssertViolation(v, "architecture");
    }

    [Fact]
    public void Architecture_DetectsFormatsReferenceInCsproj()
    {
        var repo = MakeRepo(
            ("src/Dw3.Game/Dw3.Game.csproj",
             "<Project><ItemGroup><ProjectReference Include=\"../Dw3.Formats/Dw3.Formats.csproj\" /></ItemGroup></Project>"),
            ("README.md", "ok"));
        var v = RepositoryGuard.CheckGameArchitecture(repo);
        AssertViolation(v, "architecture");
    }

    [Fact]
    public void Architecture_PassesWhenGameReferencesOnlyData()
    {
        var repo = MakeRepo(
            ("src/Dw3.Game/Dw3.Game.csproj",
             "<Project><ItemGroup><ProjectReference Include=\"../Dw3.Data/Dw3.Data.csproj\" /></ItemGroup></Project>"),
            ("README.md", "ok"));
        var v = RepositoryGuard.CheckGameArchitecture(repo);
        AssertNoViolation(v, "architecture");
    }

    // ─────────────────────────────────────────────────────────────────────
    // IDisposable: clean up temp directories
    // ─────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}
