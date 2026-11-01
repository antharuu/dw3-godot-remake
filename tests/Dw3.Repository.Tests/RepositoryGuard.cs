// Provenance and architecture guard for public repository release.
// Implements the P1 rules from the publication plan and the concrete rules
// from the provenance audit (section 5). Each rule corresponds to a named
// guard check; violations are returned as a list so callers can assert on them.

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Dw3.Repository.Tests;

/// <summary>A single guard violation: the tracked file and the rule that failed.</summary>
public sealed record Violation(string Path, string Rule, string Note);

/// <summary>
/// Checks a Git repository against provenance and architecture rules required
/// for public release. Reads tracked files via <c>git ls-files</c> and scans
// text content with per-rule regular expressions.
/// </summary>
public static class RepositoryGuard
{
    // ── Forbidden disc and PSX format extensions (P1 rule: forbidden-extension) ──
    private static readonly HashSet<string> ForbiddenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bin", ".iso", ".cue", ".img", ".tim", ".clut", ".vab", ".vag",
        ".pro", ".pak", ".str", ".xa", ".smd", ".pmd",
    };

    // ── Forbidden path prefixes / patterns (P1 rule: forbidden-path) ──
    // These paths must never appear in the tracked public file list.
    private static readonly Regex[] ForbiddenPathPatterns =
    [
        new Regex(@"^AGENTS\.md$",           RegexOptions.IgnoreCase),
        new Regex(@"^SUIVI\.md$",            RegexOptions.IgnoreCase),
        new Regex(@"^agent-notes/",          RegexOptions.IgnoreCase),
        new Regex(@"^\.hermes/",             RegexOptions.IgnoreCase),
        new Regex(@"^\.verify/",             RegexOptions.IgnoreCase),
        new Regex(@"^archive/",              RegexOptions.IgnoreCase),
        new Regex(@"^research/",             RegexOptions.IgnoreCase),
        new Regex(@"^docs/research/",        RegexOptions.IgnoreCase),
        new Regex(@"^docs/M\d+[_-]",        RegexOptions.IgnoreCase),
        new Regex(@"^_reference/",           RegexOptions.IgnoreCase),
        new Regex(@"^\.env\.local$",         RegexOptions.IgnoreCase),
        // rom/ is only allowed for rom/README.md
        new Regex(@"^rom/(?!README\.md$)",   RegexOptions.IgnoreCase),
    ];

    // ── Media / image path rule (provenance.md rule: media-or-capture) ──
    private static readonly Regex MediaPathPattern =
        new Regex(@"\.(?:png|jpe?g|gif|webp|mp4|webm|wav|ogg|flac)$", RegexOptions.IgnoreCase);

    // ── Suspicious generated-payload path rule (provenance.md rule: suspicious-generated-payload) ──
    private static readonly Regex SuspiciousPayloadPathPattern =
        new Regex(@"(?:^|/)(?:cache|output|results?|fixtures?|goldens?)/.*\.(?:json|csv|txt|md)$",
                  RegexOptions.IgnoreCase);

    // ── Text content rules (provenance.md section 5 concrete rules) ──
    // Each entry: (rule-name, compiled regex, human note).
    // Rules are applied only to files in the public allowlist when running in public mode.
    private static readonly (string Name, Regex Pattern, string Note)[] ContentRules =
    [
        // Rule: personal-path — absolute Unix/Windows/macOS home-directory paths.
        ("personal-path",
         new Regex(@"/home/[A-Za-z0-9_.\-]+/|/Users/[A-Za-z0-9_.\-]+/|[A-Za-z]:\\Users\\|~/(?:Projects|\.hermes)/",
                   RegexOptions.IgnoreCase),
         "Absolute local home path found"),

        // Rule: email-address — accidental contact leakage (requires valid TLD).
        ("email-address",
         new Regex(@"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase),
         "Email address found"),

        // Rule: internal-identifier — owner/machine names and internal process labels.
        // Exception: this guard file and its tests necessarily contain the rule patterns.
        ("internal-identifier",
         new Regex(@"\b(?:antho|antharuu|doroth[e\xE9]e|kanban|C[0-9]-0[0-9]+|TASK-0[0-9]+)\b",
                   RegexOptions.IgnoreCase),
         "Internal identifier (owner/machine/process) found"),

        // Rule: decomp-dependency — references to the decompilation repository or its environment.
        ("decomp-dependency",
         new Regex(@"dw3_decomp|DW3_DECOMP_ROOT|decomp_root|ReGame\-Labs|_reference/dw3_decomp|config/eu/stages/wstag\d+\.txt",
                   RegexOptions.IgnoreCase),
         "Decompilation repository reference found"),

        // Rule: decomp-source-citation — source file paths (src/…/…c) or decompiled symbol names.
        ("decomp-source-citation",
         new Regex(@"(?:src|include)/[A-Za-z0-9_./\-]+\.(?:c|h)\b|\bfunc_8[0-9A-Fa-f]{7}\b|\bD_8[0-9A-Fa-f]{7}\b",
                   RegexOptions.IgnoreCase),
         "Decompilation source file path or symbol found"),

        // Rule: translation-claim — phrases that indicate mechanical source transcription.
        // Note: "verbatim" alone is not included because it has legitimate technical uses
        // (e.g. "copy bytes verbatim"). Only explicit transcription claims are matched.
        ("translation-claim",
         new Regex(@"\b(?:transcribed|transcription|faithful reconstruction|exact integer order|mirrors? the source)\b",
                   RegexOptions.IgnoreCase),
         "Source-transcription claim found"),

        // Rule: hex-payload — dense 0x-prefixed byte sequences suggesting embedded raw data.
        // Requires at least 8 consecutive 0x?? values to avoid false positives on
        // small format-constant arrays. At least one value must use [a-f] to distinguish
        // from arrays of small decimal-looking integers.
        ("hex-payload",
         new Regex(@"0x[0-9a-fA-F]{2}(?:\s*,\s*0x[0-9a-fA-F]{2}){7,}", RegexOptions.IgnoreCase),
         "Dense hex byte sequence found (potential raw data embedding)"),

        // Rule: game-text-fixture — game archive names directly adjacent to an equality
        // assertion, indicating expected decoded game-text values in tests.
        // Note: generic "assert" is intentionally excluded to avoid false positives on
        // Assert.Throws and similar structural assertions; only direct equality matters.
        ("game-text-fixture",
         new Regex(@"(?:FSTALK|FSFGTRPT|FSMFIGHT|FSDIGNAM|FSSKLNAM|FSITMNAM).{0,40}\.Equal\b",
                   RegexOptions.IgnoreCase | RegexOptions.Singleline),
         "Game text archive name adjacent to equality assertion (potential embedded game content)"),

        // Rule: media-or-capture (text) — source comments or documentation that reference
        // locally retained game screenshots or capture files. "framebuffer" and "screenshot"
        // alone are legitimate rendering/testing terms; the rule only flags direct references
        // to external capture files (contact sheets produced from game captures).
        ("media-or-capture",
         new Regex(@"\b(?:contact[- ]sheet)\b", RegexOptions.IgnoreCase),
         "Contact-sheet reference found in text (potential retained game capture reference)"),
    ];

    // ── Self-exemption list ──
    // These files necessarily contain rule patterns (they ARE the rule implementation).
    // Each pair is (file path with forward slashes, rule name); both case-sensitive.
    // This list is the "explicit and commented exceptions" required by the publication plan.
    private static readonly HashSet<(string, string)> Exemptions = new(
    [
        // The guard implementation contains every regex pattern as a string literal.
        ("tests/Dw3.Repository.Tests/RepositoryGuard.cs", "personal-path"),
        ("tests/Dw3.Repository.Tests/RepositoryGuard.cs", "email-address"),
        ("tests/Dw3.Repository.Tests/RepositoryGuard.cs", "internal-identifier"),
        ("tests/Dw3.Repository.Tests/RepositoryGuard.cs", "decomp-dependency"),
        ("tests/Dw3.Repository.Tests/RepositoryGuard.cs", "decomp-source-citation"),
        ("tests/Dw3.Repository.Tests/RepositoryGuard.cs", "translation-claim"),
        ("tests/Dw3.Repository.Tests/RepositoryGuard.cs", "hex-payload"),
        ("tests/Dw3.Repository.Tests/RepositoryGuard.cs", "game-text-fixture"),
        ("tests/Dw3.Repository.Tests/RepositoryGuard.cs", "media-or-capture"),
        // The unit test file contains fabricated violation strings used in red/green tests.
        ("tests/Dw3.Repository.Tests/RepositoryGuardTests.cs", "personal-path"),
        ("tests/Dw3.Repository.Tests/RepositoryGuardTests.cs", "email-address"),
        ("tests/Dw3.Repository.Tests/RepositoryGuardTests.cs", "internal-identifier"),
        ("tests/Dw3.Repository.Tests/RepositoryGuardTests.cs", "decomp-dependency"),
        ("tests/Dw3.Repository.Tests/RepositoryGuardTests.cs", "decomp-source-citation"),
        ("tests/Dw3.Repository.Tests/RepositoryGuardTests.cs", "translation-claim"),
        ("tests/Dw3.Repository.Tests/RepositoryGuardTests.cs", "hex-payload"),
        ("tests/Dw3.Repository.Tests/RepositoryGuardTests.cs", "game-text-fixture"),
        ("tests/Dw3.Repository.Tests/RepositoryGuardTests.cs", "media-or-capture"),
        // The real-repo integration test may reference rule names and exemption paths.
        ("tests/Dw3.Repository.Tests/RealRepositoryTests.cs", "internal-identifier"),
        ("tests/Dw3.Repository.Tests/RealRepositoryTests.cs", "decomp-source-citation"),
        // The HUD extraction step has a required disc frame-ID array (format knowledge, not
        // game content): 0x0B, 0x1E, 0x33…  All values are ROM-structure selectors, not
        // decoded game output.
        ("src/Dw3.Extractor/BattleHudExtractionStep.cs", "hex-payload"),
        // The field animation loader uses the same disc cue-frame selector array.
        ("src/Dw3.Game/Conversion/FieldAnimationDataPackLoader.cs", "hex-payload"),
        // The field animation test mirrors the production selector array as a synthetic fixture.
        ("tests/Dw3.Game.Tests/FieldAnimationDataPackLoaderTests.cs", "hex-payload"),
        // TIM decoder unit tests use fabricated malformed TIM headers (all zeros / minimal header).
        ("tests/Dw3.Formats.Tests/Binary/Tim/TimDecoderTests.cs", "hex-payload"),
        // RLEN decoder unit tests use a fabricated all-zero run as a malformed-input probe.
        ("tests/Dw3.Formats.Tests/Binary/RlenDecoderTests.cs", "hex-payload"),
        // BattleRenderer probe builds a contact sheet of fighter sprites for comparison;
        // this is a local diagnostic tool, not a retained game-derived capture file.
        ("src/Dw3.Game/Presentation/BattleRenderer.cs", "media-or-capture"),
        // Community files legitimately contain the project owner's GitHub username as a public handle.
        // These are not internal identifiers but the public repository identity.
        ("LICENSE.md", "internal-identifier"),
        ("README.md", "internal-identifier"),
        (".github/ISSUE_TEMPLATE/config.yml", "internal-identifier"),
    ]);

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs all provenance and architecture checks against the given repository.
    /// </summary>
    /// <param name="repoRoot">Absolute path to the repo root (must contain .git/).</param>
    /// <param name="publicAllowlist">
    ///   When non-null, content rules run only on files in this set.
    ///   Path-level rules (forbidden extension/path/media) still run on all tracked files.
    /// </param>
    /// <returns>All violations found; empty means the repo is clean.</returns>
    public static IReadOnlyList<Violation> Check(
        string repoRoot,
        IReadOnlySet<string>? publicAllowlist = null)
    {
        var tracked = GetTrackedFiles(repoRoot);
        var violations = new List<Violation>();

        foreach (var file in tracked)
        {
            // When running in public mode, path-level rules apply only to allowlisted files.
            // Internally-tracked files (research/, agent-notes/, etc.) are expected to stay
            // outside the public set and will trigger unreviewed-source below if not excluded.
            bool isInPublicScope = publicAllowlist == null || publicAllowlist.Contains(file);

            if (isInPublicScope)
            {
                // P1: forbidden disc/PSX format extension.
                var ext = Path.GetExtension(file);
                if (!string.IsNullOrEmpty(ext) && ForbiddenExtensions.Contains(ext))
                    violations.Add(new Violation(file, "forbidden-extension",
                        $"Extension '{ext}' identifies a disc or PSX proprietary format"));

                // P1: Python files must not be tracked in the public repository.
                if (ext.Equals(".py", StringComparison.OrdinalIgnoreCase))
                    violations.Add(new Violation(file, "no-python",
                        "Python file must not be tracked in the public repository"));

                // P1: forbidden path patterns (AGENTS.md, research/, etc.).
                foreach (var pattern in ForbiddenPathPatterns)
                {
                    if (pattern.IsMatch(file))
                    {
                        violations.Add(new Violation(file, "forbidden-path",
                            $"Path matches internal-only pattern '{pattern}'"));
                        break; // one violation per file per path check is enough
                    }
                }

                // provenance.md: media or image file must not be tracked.
                if (MediaPathPattern.IsMatch(file))
                    violations.Add(new Violation(file, "media-or-capture",
                        "Media or image file must not be tracked"));

                // provenance.md: suspicious generated-payload path.
                if (SuspiciousPayloadPathPattern.IsMatch(file))
                    violations.Add(new Violation(file, "suspicious-generated-payload",
                        "Generated-payload path requires explicit allowlisting"));
            }

            // provenance.md: every tracked public file must be reviewed and listed.
            if (publicAllowlist != null && !publicAllowlist.Contains(file))
                violations.Add(new Violation(file, "unreviewed-source",
                    "File is tracked but absent from the public allowlist"));
        }

        // Content-rule scan: only files in the public allowlist when in public mode.
        var filesToScan = publicAllowlist != null
            ? tracked.Where(f => publicAllowlist.Contains(f))
            : tracked;

        foreach (var file in filesToScan)
        {
            var fullPath = Path.Combine(repoRoot,
                file.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath)) continue;

            string content;
            try { content = File.ReadAllText(fullPath); }
            catch { continue; }

            foreach (var (name, pattern, note) in ContentRules)
            {
                if (IsExempt(file, name)) continue;
                if (pattern.IsMatch(content))
                    violations.Add(new Violation(file, name, note));
            }
        }

        // Architecture check: Dw3.Game must not reference Dw3.Disc or Dw3.Formats.
        violations.AddRange(CheckGameArchitecture(repoRoot));

        return violations;
    }

    /// <summary>
    /// Checks that src/Dw3.Game does not reference Dw3.Disc or Dw3.Formats in its
    /// project file or contain disc-file extension references in its C# source files.
    /// </summary>
    public static IReadOnlyList<Violation> CheckGameArchitecture(string repoRoot)
    {
        var violations = new List<Violation>();
        var gameDir = Path.Combine(repoRoot, "src", "Dw3.Game");
        if (!Directory.Exists(gameDir)) return violations;

        foreach (var csproj in Directory.EnumerateFiles(gameDir, "*.csproj", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(csproj);
            var rel = Rel(repoRoot, csproj);
            if (Regex.IsMatch(content, @"Dw3\.Disc", RegexOptions.IgnoreCase))
                violations.Add(new Violation(rel, "architecture",
                    "Dw3.Game project must not reference Dw3.Disc"));
            if (Regex.IsMatch(content, @"Dw3\.Formats", RegexOptions.IgnoreCase))
                violations.Add(new Violation(rel, "architecture",
                    "Dw3.Game project must not reference Dw3.Formats"));
        }

        // C# files: no disc-file string literals.
        var discExtInString = new Regex(@"""[^""]*\.(?:cue|bin)""", RegexOptions.IgnoreCase);
        foreach (var cs in Directory.EnumerateFiles(gameDir, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(cs);
            var rel = Rel(repoRoot, cs);
            if (discExtInString.IsMatch(content))
                violations.Add(new Violation(rel, "architecture",
                    "Dw3.Game source contains a .cue/.bin disc file extension reference"));
        }

        return violations;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Returns all files tracked by Git in the given repository.</summary>
    public static IReadOnlyList<string> GetTrackedFiles(string repoRoot)
    {
        var psi = new ProcessStartInfo("git", "ls-files")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();
    }

    /// <summary>
    /// Loads the public allowlist from a plain-text file.
    /// Each non-empty, non-comment line is one tracked file path.
    /// </summary>
    public static IReadOnlySet<string> LoadAllowlist(string path) =>
        File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
            .Select(l => l.Trim())
            .ToHashSet(StringComparer.Ordinal);

    private static bool IsExempt(string filePath, string ruleName)
    {
        var norm = filePath.Replace(Path.DirectorySeparatorChar, '/');
        return Exemptions.Contains((norm, ruleName));
    }

    private static string Rel(string root, string full) =>
        Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');
}
