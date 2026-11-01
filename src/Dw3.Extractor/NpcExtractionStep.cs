using System.Security.Cryptography;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Binary.Tim;
using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>
/// Extracts NPC character sprite archives from the supported disc.
///
/// The extraction resolves NPC actor entries from each stage's field overlay (.PRO file)
/// and decodes the referenced character sprite archives. Actor-to-archive resolution
/// is delegated to <see cref="INpcActorTableSource"/>, which reads FIELDSTG_fileEntries
/// and related tables from supported disc members.
/// </summary>
internal static class NpcExtractionStep
{
    private const string Domain = "npc";
    private const string ManifestSchema = "m7a-npc-v1";
    // Stage overlay load base (EU disc, constant across all WSTAG*.PRO overlays).
    private const uint StageVram = 0x800A5DE0u;

    internal static int Extract(
        string cuePath,
        string outputDirectory,
        INpcActorTableSource tableSource,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(tableSource);
        try
        {
            using var disc = DiscSource.Open(cuePath);
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();

            IReadOnlyList<int> stageIds = tableSource.GetRequiredStageIds();
            int totalCharacters = 0;
            int totalVisible = 0;
            int totalZones = 0;
            var extractedZones = new List<NpcExtractedZone>();

            DataPackWriter writer = new(outputDirectory);
            writer.Write(
                new DataPackMetadata(
                    typeof(NpcExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                    edition.BinSha1,
                    edition.Label,
                    DateTimeOffset.UtcNow),
                context =>
                {
                    foreach (int stageId in stageIds)
                    {
                        string proPath = $"AAA/PRO/WSTAG{stageId}.PRO";
                        DiscFileEntry? proEntry = entries.SingleOrDefault(e =>
                            string.Equals(e.IsoPath, proPath, StringComparison.Ordinal));
                        if (proEntry is null)
                            throw new InvalidDataException($"Required NPC stage overlay is absent: {proPath}");
                        byte[] overlay = disc.ReadFile(proEntry);

                        int zoneCharacters = 0;
                        var extractedActors = new List<NpcExtractedActor>();
                        IReadOnlyList<NpcActorReference> actors = tableSource.GetStageActors(stageId);
                        foreach (NpcActorReference actorReference in actors)
                        {
                            NpcActorEntry actor = NpcOverlayReader.ReadActorEntry(overlay, actorReference.Address, StageVram);
                            IReadOnlyList<NpcCondition> conditions = NpcOverlayReader.ReadConditionList(
                                overlay, actor.ConditionsPtr, StageVram);
                            bool visible = tableSource.IsVisible(conditions);
                            bool isPlayerLead = tableSource.IsPlayerLead(actor.ActorId);
                            int? width = tableSource.GetActorWidth(actor.ActorId);
                            (int fileNumber, int archiveEntry)? packedEntry = tableSource.GetActorSpriteEntry(actor.ActorId);
                            string? archivePath = packedEntry is null ? null : tableSource.GetDiscFilePath(packedEntry.Value.fileNumber);
                            extractedActors.Add(new NpcExtractedActor(
                                actorReference.Index, actor.ActorId, actor.Image, actor.X, actor.Y, actor.Direction,
                                width, isPlayerLead, visible, conditions,
                                packedEntry is null ? null : new NpcFileEntry(packedEntry.Value.fileNumber, packedEntry.Value.archiveEntry),
                                archivePath));

                            if (!visible || isPlayerLead)
                                continue;
                            totalVisible++;
                            if (packedEntry is null || archivePath is null)
                                continue;
                            DiscFileEntry? archiveEntry2 = entries.SingleOrDefault(e =>
                                string.Equals(e.IsoPath, archivePath, StringComparison.Ordinal));
                            if (archiveEntry2 is null)
                                throw new InvalidDataException($"NPC archive is absent from disc: {archivePath}");

                            byte[] archiveBytes = disc.ReadFile(archiveEntry2);
                            NpcExtractedCharacter character = DecodeCharacter(
                                archiveBytes, archivePath, packedEntry.Value.archiveEntry,
                                actor.ActorId, stageId, actorReference.Index);
                            NpcDomainWriter.Write(context, character);
                            zoneCharacters++;
                        }
                        extractedZones.Add(new NpcExtractedZone(
                            stageId, proPath,
                            Convert.ToHexStringLower(SHA256.HashData(overlay)),
                            extractedActors));
                        totalCharacters += zoneCharacters;
                        totalZones++;
                    }
                    NpcDomainWriter.WritePlacements(context, extractedZones, snapshotProgress: 4);
                });

            output.WriteLine($"{{\"event\":\"domain-complete\",\"domain\":\"{Domain}\",\"zones\":{totalZones},\"visible_npcs\":{totalVisible},\"decoded\":{totalCharacters}}}");
            return 0;
        }
        catch (Exception exception) when (exception is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error: {exception.Message}");
            return 1;
        }
    }

    private static NpcExtractedCharacter DecodeCharacter(
        byte[] archiveBytes, string discPath, int archiveEntry, int actorId, int stageId, int actorIndex)
    {
        // NPC character archives use the same format as action archives:
        // entries 0..archiveEntry-1 are TIM4 frames; entry archiveEntry is the anim-set table.
        int[] offsets = ActionArchiveDecoder.DecodeOffsetTable(archiveBytes, archiveEntry);
        IReadOnlyList<ActionAnimationSet> rawSets = ActionArchiveDecoder.DecodeSetTable(archiveBytes, offsets, archiveEntry);

        // Collect all unique sequence indices and decode each sequence.
        var sequenceIndices = new SortedSet<int>();
        foreach (ActionAnimationSet set in rawSets)
            foreach (int seqIndex in set.SequenceIndices)
                sequenceIndices.Add(seqIndex);

        var sequences = new List<NpcAnimSequence>();
        int maxFrame = -1;
        foreach (int seqIndex in sequenceIndices)
        {
            ActionAnimationSequence raw = ActionArchiveDecoder.DecodeSequence(archiveBytes, offsets, seqIndex, archiveEntry);
            foreach (ActionAnimationRecord record in raw.Records)
                if (record.Frame > maxFrame)
                    maxFrame = record.Frame;
            sequences.Add(new NpcAnimSequence(
                seqIndex,
                raw.Terminator == ActionSequenceTerminator.Loop ? NpcSequenceTerminator.Loop : NpcSequenceTerminator.End,
                [.. raw.Records.Select(r => new NpcAnimRecord(r.Duration, r.Frame, r.OffsetX, r.OffsetY))]));
        }

        // Decode all TIM4 frames from 0 to the highest frame referenced in any sequence.
        var frames = new List<NpcExtractedFrame>();
        int frameCount = maxFrame + 1;
        for (int frameId = 0; frameId < frameCount; frameId++)
        {
            int start = offsets[frameId];
            int end = offsets[frameId + 1];
            TimImage image = TimDecoder.Decode(archiveBytes.AsSpan(start, end - start));
            frames.Add(new NpcExtractedFrame(frameId, image.Width, image.Height, image.Pixels));
        }

        var sets = rawSets.Select(s => new NpcAnimSet(s.SetId, [.. s.SequenceIndices])).ToArray();
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(archiveBytes));

        return new NpcExtractedCharacter(
            actorId, stageId, actorIndex,
            ManifestSchema, discPath, sha256, archiveBytes.Length, archiveEntry,
            frames, sets, sequences);
    }

    internal static int Extract(string cuePath, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = DiscSource.Open(cuePath);
            disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            DiscNpcActorTableSource source = DiscNpcActorTableSource.Create(disc, entries);
            return Extract(cuePath, outputDirectory, source, output, error);
        }
        catch (Exception exception) when (exception is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error: {exception.Message}");
            return 1;
        }
    }
}
