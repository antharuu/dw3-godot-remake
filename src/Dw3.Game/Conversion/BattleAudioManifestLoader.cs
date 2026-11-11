using System.Text.Json;
using Dw3.Data;

namespace Dw3Production.Conversion;

/// <summary>Loads the battle audio manifest and builds program-to-sample mappings from the v3 manifest.
/// No ISO access and no direct format-layer dependency; all decoding was performed by the extractor.</summary>
public static class BattleAudioManifestLoader
{
    private const string ManifestRelativePath = "battle/audio/manifest.json";
    private const string ExpectedSchema = "battle-audio-vab-v3";
    private const string CommonBankName = "common";

    /// <summary>Loads program-to-sample entries for the common sound bank from the given pack.
    /// Programs with no active tone or a zero sample reference are omitted from the result.</summary>
    /// <param name="pack">Reader for the extracted data pack.</param>
    /// <returns>Map of program index (SoundId) to its resolved audio entry.</returns>
    /// <exception cref="CacheResourceException">The manifest is absent, its schema mismatches, or a WAV file is missing.</exception>
    public static IReadOnlyDictionary<int, AudioProgramEntry> Load(DataPackReader pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        string manifestPath = RequireFile(pack, ManifestRelativePath, "audio manifest");
        return LoadFromManifest(pack, manifestPath);
    }

    /// <summary>Parses the manifest and resolves entries for the common bank.</summary>
    /// <param name="pack">Reader for the extracted data pack.</param>
    /// <param name="manifestPath">Absolute path to the manifest JSON.</param>
    /// <returns>Map of program index to audio entry.</returns>
    private static Dictionary<int, AudioProgramEntry> LoadFromManifest(DataPackReader pack, string manifestPath)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            JsonElement root = document.RootElement;
            if (root.GetProperty("schema").GetString() != ExpectedSchema)
            {
                throw Invalid($"audio manifest schema mismatch: expected {ExpectedSchema}");
            }

            foreach (JsonElement bank in root.GetProperty("banks").EnumerateArray())
            {
                string name = bank.GetProperty("name").GetString() ?? string.Empty;
                if (!string.Equals(name, CommonBankName, StringComparison.Ordinal))
                {
                    continue;
                }

                return LoadCommonBank(pack, bank);
            }

            throw Invalid("audio manifest has no common bank");
        }
        catch (CacheResourceException)
        {
            throw;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or FormatException)
        {
            throw Invalid($"audio manifest is unreadable: {error.Message}");
        }
    }

    /// <summary>Reads the common bank's programs array and builds SoundId-to-entry mappings.</summary>
    /// <param name="pack">Reader for the extracted data pack.</param>
    /// <param name="bank">JSON element for the common bank.</param>
    /// <returns>Map of program index to audio entry for every resolved program.</returns>
    private static Dictionary<int, AudioProgramEntry> LoadCommonBank(DataPackReader pack, JsonElement bank)
    {
        string[] samplePaths = BuildSamplePaths(pack, bank);
        var entries = new Dictionary<int, AudioProgramEntry>();
        foreach (JsonElement program in bank.GetProperty("programs").EnumerateArray())
        {
            int programIndex = program.GetProperty("index").GetInt32();
            int sampleIndex = program.GetProperty("sampleIndex").GetInt32();
            if (sampleIndex < 0 || sampleIndex >= samplePaths.Length)
            {
                continue;
            }

            entries[programIndex] = new AudioProgramEntry(
                SampleIndex: sampleIndex,
                WavPath: samplePaths[sampleIndex],
                Volume: (byte)program.GetProperty("volume").GetInt32(),
                Pan: (byte)program.GetProperty("pan").GetInt32(),
                CenterNote: (byte)program.GetProperty("centerNote").GetInt32(),
                PitchShift: (sbyte)program.GetProperty("pitchShift").GetInt32());
        }

        return entries;
    }

    /// <summary>Reads every sample's WAV path from the manifest, resolved to absolute paths.</summary>
    /// <param name="pack">Reader for the extracted data pack.</param>
    /// <param name="bank">JSON element describing the bank.</param>
    /// <returns>Array of absolute WAV paths indexed by sample index.</returns>
    private static string[] BuildSamplePaths(DataPackReader pack, JsonElement bank)
    {
        JsonElement samplesElement = bank.GetProperty("samples");
        int sampleCount = samplesElement.GetArrayLength();
        var paths = new string[sampleCount];
        int sampleIndex = 0;
        foreach (JsonElement sample in samplesElement.EnumerateArray())
        {
            string relPath = sample.GetProperty("wavPath").GetString()
                ?? throw Invalid($"sample entry {sampleIndex} has no wavPath");
            paths[sampleIndex++] = RequireFile(pack, relPath, $"sample WAV {sampleIndex - 1}");
        }

        return paths;
    }

    /// <summary>Resolves a pack-relative path, requiring the file to exist on disk.</summary>
    /// <param name="pack">Pack reader.</param>
    /// <param name="relativePath">Pack-relative path.</param>
    /// <param name="subject">Human-readable resource name for error messages.</param>
    /// <returns>Absolute path to the existing file.</returns>
    private static string RequireFile(DataPackReader pack, string relativePath, string subject)
    {
        string path;
        try
        {
            path = pack.ResolvePath(relativePath);
        }
        catch (ArgumentException error)
        {
            throw new CacheResourceException("CACHE_INVALID", $"{subject} path is invalid: {error.Message}");
        }

        if (!File.Exists(path))
        {
            throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"{subject} is missing: {path}");
        }

        return path;
    }

    /// <summary>Builds a cache-invalid exception for manifest content defects.</summary>
    /// <param name="message">Description of the defect.</param>
    /// <returns>The exception, not yet thrown.</returns>
    private static CacheResourceException Invalid(string message) => new("CACHE_INVALID", message);
}
