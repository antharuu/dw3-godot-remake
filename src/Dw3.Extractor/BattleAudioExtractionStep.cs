using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Battle;

namespace Dw3.Extractor;

/// <summary>Extracts the VAB header archives and paired SPU bodies required by battle sound banks.</summary>
internal static class BattleAudioExtractionStep
{
    private const string Domain = "battle-audio";
    private const string OutputSubdirectory = "battle/audio";
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    private static readonly SoundBankSource[] Sources =
    [
        new("common", "AAA/DAT/SOUND/COMMON/MPCOMMON.BIN", "AAA/DAT/SOUND/COMMON/MVCOMMON.BIN"),
        new("battle", "AAA/DAT/SOUND/BATL00/MPBATL00.BIN", "AAA/DAT/SOUND/BATL00/MVBATL00.BIN"),
        new("boss", "AAA/DAT/SOUND/BOSS00/MPBOSS00.BIN", "AAA/DAT/SOUND/BOSS00/MVBOSS00.BIN"),
    ];

    /// <summary>Extracts the three combat-relevant bank pairs and records their decoded VAB headers.</summary>
    /// <param name="openDisc">Opens the supported disc.</param>
    /// <param name="outputDirectory">Root directory for the data pack.</param>
    /// <param name="output">Receives the completion event.</param>
    /// <param name="error">Receives extraction errors.</param>
    /// <returns>Zero on success, otherwise one.</returns>
    internal static int Extract(Func<IDiscSource> openDisc, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = openDisc();
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            new DataPackWriter(outputDirectory).Write(
                new DataPackMetadata(typeof(BattleAudioExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0", edition.BinSha1, edition.Label, DateTimeOffset.UtcNow),
                context => WriteBanks(context, disc, entries));
            output.WriteLine("{\"event\":\"domain-complete\",\"domain\":\"battle-audio\"}");
            return 0;
        }
        catch (Exception ex) when (ex is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error [{Domain}]: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Reads each supported pair, decodes its header, and writes its private bytes and manifest.</summary>
    /// <param name="context">The data-pack write context.</param>
    /// <param name="disc">The opened source disc.</param>
    /// <param name="entries">All source-disc entries.</param>
    private static void WriteBanks(DataPackWriteContext context, IDiscSource disc, IReadOnlyList<DiscFileEntry> entries)
    {
        var banks = new List<ExtractedSoundBank>();
        foreach (SoundBankSource source in Sources)
        {
            byte[] headerArchive = disc.ReadFile(Require(entries, source.HeaderPath));
            byte[] bodyArchive = disc.ReadFile(Require(entries, source.BodyPath));
            SoundBankHeader header = SoundBankHeaderDecoder.Decode(headerArchive);
            WriteFile(context, $"{OutputSubdirectory}/{source.Name}.mp", headerArchive);
            WriteFile(context, $"{OutputSubdirectory}/{source.Name}.mv", bodyArchive);
            banks.Add(new ExtractedSoundBank(source, header, headerArchive, bodyArchive));
        }

        WriteManifest(context, banks);
    }

    /// <summary>Writes one extracted proprietary bank member below the data-pack image root.</summary>
    /// <param name="context">The data-pack write context.</param>
    /// <param name="relativePath">Relative destination path.</param>
    /// <param name="bytes">Source bytes to preserve locally.</param>
    private static void WriteFile(DataPackWriteContext context, string relativePath, byte[] bytes)
    {
        string path = context.GetImagePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Writes the source paths, hashes, and decoded VAB metadata for every bank pair.</summary>
    /// <param name="context">The data-pack write context.</param>
    /// <param name="banks">Decoded source pairs.</param>
    private static void WriteManifest(DataPackWriteContext context, IReadOnlyList<ExtractedSoundBank> banks)
    {
        string path = context.GetImagePath($"{OutputSubdirectory}/manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new { schema = "battle-audio-vab-v1", banks = banks.Select(BankManifest) }, IndentedJson) + "\n", new UTF8Encoding(false));
    }

    /// <summary>Builds one serializable manifest row without retaining a direct disc handle.</summary>
    /// <param name="bank">The decoded bank.</param>
    /// <returns>A JSON-serializable projection.</returns>
    private static object BankManifest(ExtractedSoundBank bank) => new
    {
        name = bank.Source.Name,
        headerPath = bank.Source.HeaderPath,
        bodyPath = bank.Source.BodyPath,
        headerBytes = bank.HeaderArchive.Length,
        bodyBytes = bank.BodyArchive.Length,
        headerSha256 = Convert.ToHexStringLower(SHA256.HashData(bank.HeaderArchive)),
        bodySha256 = Convert.ToHexStringLower(SHA256.HashData(bank.BodyArchive)),
        vab = bank.Header,
    };

    /// <summary>Finds a required ISO member by its canonical path.</summary>
    /// <param name="entries">All available disc entries.</param>
    /// <param name="path">The required canonical ISO path.</param>
    /// <returns>The matching disc entry.</returns>
    private static DiscFileEntry Require(IReadOnlyList<DiscFileEntry> entries, string path) =>
        entries.SingleOrDefault(entry => entry.IsoPath == path) ?? throw new InvalidDataException($"Required disc member missing: {path}");

    /// <summary>Canonical ISO members forming one VAB header and SPU body pair.</summary>
    private sealed record SoundBankSource(string Name, string HeaderPath, string BodyPath);

    /// <summary>Decoded and retained bytes for one source pair while the data pack is written.</summary>
    private sealed record ExtractedSoundBank(SoundBankSource Source, SoundBankHeader Header, byte[] HeaderArchive, byte[] BodyArchive);
}
