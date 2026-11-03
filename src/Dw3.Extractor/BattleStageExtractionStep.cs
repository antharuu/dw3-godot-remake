using System.Security.Cryptography;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary;
using Dw3.Formats.Binary.Png;

namespace Dw3.Extractor;

/// <summary>
/// Extracts the 27 fight-stage (battle background) models from the disc.
/// Walks FILE_FIGHT_STAGES (engine file 0x1CB) to read FightStageInfo records,
/// then decodes each stage model the same way as fighter models (bones, meshes, textures, motions).
/// Writes stage-N-model.json + stage-N-texture-K.png under battle/stages/ in the data pack.
/// </summary>
internal static class BattleStageExtractionStep
{
    private const string Domain = "battle-stages";
    private const string ExecutablePath = "SLES_039.36";
    private const string EditionLabel = "SLES-03936";
    private const string OutputSubdir = "battle/stages";

    /// <summary>
    /// Iterates the 27 entries of FILE_FIGHT_STAGES (engine file 0x1CB), decodes each stage model
    /// with bones, meshes, and textures using <see cref="BattleModelDecoder.DecodeStage"/>, and writes
    /// PNG textures and model JSON under <c>battle/stages/</c> in the data pack.
    /// </summary>
    /// <param name="openDisc">Opens the disc whose executable file table locates the battle stage files; disposed before returning.</param>
    /// <param name="outputDirectory">Root of the data pack to write into.</param>
    /// <param name="output">Receives progress and completion JSON event lines.</param>
    /// <param name="error">Receives per-stage decode errors and one fatal error line on failure.</param>
    /// <returns>0 on success, 1 on fatal extraction error.</returns>
    internal static int Extract(Func<IDiscSource> openDisc, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = openDisc();
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            DiscFileEntry exeEntry = entries.SingleOrDefault(e => e.IsoPath == ExecutablePath)
                ?? throw new InvalidDataException($"Required disc member missing: {ExecutablePath}");
            var executable = PsxExecutable.Parse(disc.ReadFile(exeEntry));
            var files = new EngineFileReader(disc, entries, executable);

            // Read the FightStageInfo file (FILE_FIGHT_STAGES = 0x1CB).
            (_, byte[] stagesData) = files.Read(BattleStageDecoder.FightStageFileId);

            new DataPackWriter(outputDirectory).Write(new DataPackMetadata(
                typeof(BattleStageExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1, edition.Label, DateTimeOffset.UtcNow),
                context =>
                {
                    string imagesDir = context.GetImagePath(OutputSubdir);
                    Directory.CreateDirectory(imagesDir);
                    int decoded = 0, failed = 0;
                    for (int stageId = 0; stageId < BattleStageDecoder.StageCount; stageId++)
                    {
                        try
                        {
                            BattleStageInfo info = BattleStageDecoder.ParseRecord(stagesData, stageId);
                            (_, byte[] modelData) = files.Read(info.ModelFile);
                            byte[] modelSha256 = SHA256.HashData(modelData);
                            BattleModelPayload payload = BattleModelDecoder.DecodeStage(
                                modelData, info.ModelEntry, info.MotionEntry);
                            WriteStageAssets(context, stageId, info, payload, modelSha256);
                            decoded++;
                        }
                        catch (FormatException ex)
                        {
                            error.WriteLine($"ERROR battle-stage-failed stage={stageId} reason={ex.Message}");
                            failed++;
                        }
                    }
                    output.WriteLine($"{{\"event\":\"domain-progress\",\"domain\":\"{Domain}\",\"decoded\":{decoded},\"failed\":{failed}}}");
                });

            output.WriteLine($"{{\"event\":\"domain-complete\",\"domain\":\"{Domain}\"}}");
            return 0;
        }
        catch (Exception ex) when (ex is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error [{Domain}]: {ex.Message}");
            return 1;
        }
    }

    /// <summary>Writes the texture PNGs and model JSON for one decoded battle stage.</summary>
    /// <param name="context">The data pack write context for image paths and registration.</param>
    /// <param name="stageId">The zero-based stage index used as the output filename prefix.</param>
    /// <param name="info">The stage metadata read from FILE_FIGHT_STAGES.</param>
    /// <param name="payload">The decoded stage model with bones, meshes and textures.</param>
    /// <param name="modelSha256">The raw SHA-256 hash of the model file bytes, used as provenance.</param>
    private static void WriteStageAssets(
        DataPackWriteContext context,
        int stageId,
        BattleStageInfo info,
        BattleModelPayload payload,
        byte[] modelSha256)
    {
        string prefix = $"stage-{stageId}";

        // Write texture PNGs.
        foreach (BattleTexture texture in payload.Textures.Textures)
        {
            string rel = $"{OutputSubdir}/{prefix}-texture-{texture.Key}.png";
            File.WriteAllBytes(context.GetImagePath(rel), PngEncoder.Encode(texture.Width, texture.Height, texture.Rgba));
            context.RegisterImage(rel);
        }

        // Write model JSON.
        string jsonRel = $"{OutputSubdir}/{prefix}-model.json";
        string modelSha256Hex = Convert.ToHexStringLower(modelSha256);
        File.WriteAllText(context.GetImagePath(jsonRel), BattleStageJsonWriter.WriteStageModel(info, payload, EditionLabel, prefix, modelSha256Hex));
    }
}
