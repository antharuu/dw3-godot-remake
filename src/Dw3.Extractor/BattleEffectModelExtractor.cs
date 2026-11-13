using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary;

namespace Dw3.Extractor;

/// <summary>Extracts the shared three-dimensional battle effect models named by the battle-overlay registry.</summary>
internal static class BattleEffectModelExtractor
{
    private const string OverlayPath = "AAA/PRO/FIGHTSTG.PRO";
    private const string EditionLabel = "SLES-03936";

    /// <summary>Decodes the overlay registry, resolves every model and motion archive, and persists their portable payloads.</summary>
    /// <param name="context">Current data-pack write transaction and image root.</param>
    /// <param name="disc">Open source disc.</param>
    /// <param name="entries">Full source-disc file listing.</param>
    /// <param name="executable">Executable used to resolve engine file identifiers.</param>
    internal static void Write(DataPackWriteContext context, IDiscSource disc, IReadOnlyList<DiscFileEntry> entries, PsxExecutable executable)
    {
        byte[] overlay = disc.ReadFile(Require(entries, OverlayPath));
        IReadOnlyList<EffectModelReference> references = EffectModelTableDecoder.Decode(overlay);
        var files = new EngineFileReader(disc, entries, executable);
        var models = new List<EffectModelExtraction>(references.Count);
        foreach (EffectModelReference reference in references)
        {
            models.Add(Decode(reference, files));
        }

        BattleEffectModelDomainWriter.Write(context, models, EditionLabel);
    }

    /// <summary>Decodes one shared effect model with its model materials and its independent motion archive.</summary>
    /// <param name="reference">Script-visible resource reference.</param>
    /// <param name="files">Engine-file resolver backed by the open disc.</param>
    /// <returns>One fully decoded resource plus source provenance.</returns>
    private static EffectModelExtraction Decode(EffectModelReference reference, EngineFileReader files)
    {
        (string path, byte[] modelBytes) = files.Read(reference.ModelFile);
        byte[] motionBytes = reference.MotionFile == reference.ModelFile ? modelBytes : files.Read(reference.MotionFile).Bytes;
        BattleModelPayload payload = BattleModelDecoder.DecodeComplete(modelBytes, reference.ModelEntry, motionBytes, reference.MotionEntry, EmptyMetadata());
        return new EffectModelExtraction(reference, payload, path, DiscIdentity.Sha256Bytes(modelBytes));
    }

    /// <summary>Builds the explicit empty fighter-only metadata that shared effect models do not carry.</summary>
    /// <returns>Empty metadata isolated from the model, texture and animation payload.</returns>
    private static BattleFighterMetadata EmptyMetadata() =>
        new([], new BattleGeometry(0, 0), new BattleFace(new BattleFaceSource(0, 0, 0, 0, string.Empty), []));

    /// <summary>Finds a required ISO member by its canonical path.</summary>
    /// <param name="entries">All available source-disc entries.</param>
    /// <param name="path">Canonical ISO path to require.</param>
    /// <returns>The matching disc entry.</returns>
    /// <exception cref="InvalidDataException">The required member is absent.</exception>
    private static DiscFileEntry Require(IReadOnlyList<DiscFileEntry> entries, string path) =>
        entries.SingleOrDefault(entry => entry.IsoPath == path) ?? throw new InvalidDataException($"Required disc member missing: {path}");
}
