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

    /// <summary>
    /// Extracts NPC character sprites and animation data for every stage provided by
    /// <paramref name="tableSource"/>. Writes PNG frames, sequence rows, set rows, and manifest
    /// JSON to <paramref name="outputDirectory"/>; characters that fail to decode are skipped.
    /// </summary>
    /// <param name="openDisc">Opens the disc that holds the stage overlays and character archives; disposed before returning.</param>
    /// <param name="outputDirectory">Root of the data pack to write into.</param>
    /// <param name="tableSource">Source providing actor tables, file paths, and stage id lists.</param>
    /// <param name="output">Receives progress and completion JSON event lines.</param>
    /// <param name="error">Receives per-character decode error lines and a final error line on failure.</param>
    /// <returns>0 on success, 1 on fatal extraction error.</returns>
    internal static int Extract(
        Func<IDiscSource> openDisc,
        string outputDirectory,
        INpcActorTableSource tableSource,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(tableSource);
        try
        {
            using var disc = openDisc();
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
                        (NpcExtractedZone zone, int chars, int vis) =
                            ExtractStage(disc, entries, stageId, tableSource, context);
                        extractedZones.Add(zone);
                        totalCharacters += chars;
                        totalVisible += vis;
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

    /// <summary>
    /// Convenience overload that builds the actor table source from the disc and delegates to
    /// <see cref="Extract(Func{IDiscSource}, string, INpcActorTableSource, TextWriter, TextWriter)"/>.
    /// </summary>
    /// <param name="openDisc">Opens the disc twice: once to build the actor table source, once for the extraction itself.</param>
    /// <param name="outputDirectory">Root of the data pack to write into.</param>
    /// <param name="output">Receives progress and completion JSON event lines.</param>
    /// <param name="error">Receives one plain-text error line on failure.</param>
    /// <returns>0 on success, 1 on fatal extraction error.</returns>
    internal static int Extract(Func<IDiscSource> openDisc, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = openDisc();
            disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            DiscNpcActorTableSource source = DiscNpcActorTableSource.Create(disc, entries);
            return Extract(openDisc, outputDirectory, source, output, error);
        }
        catch (Exception exception) when (exception is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error: {exception.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Finds the single disc file whose ISO path matches exactly (ordinal, case-sensitive, as the
    /// file table spells it, for example <c>AAA/PRO/WSTAG200.PRO</c>).
    /// </summary>
    /// <param name="entries">The full disc file listing.</param>
    /// <param name="isoPath">The ISO 9660 path to look up, without leading slash or version suffix.</param>
    /// <param name="missingMessage">The error text reported, followed by the path, when the file is absent.</param>
    /// <returns>The matching file entry.</returns>
    /// <exception cref="InvalidDataException">No entry has this path, which means the disc is not the supported one.</exception>
    private static DiscFileEntry RequireEntry(IReadOnlyList<DiscFileEntry> entries, string isoPath, string missingMessage)
    {
        return entries.SingleOrDefault(e => string.Equals(e.IsoPath, isoPath, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"{missingMessage}: {isoPath}");
    }

    /// <summary>Extracts all actors from one stage's field overlay and returns the zone with decoded character counts.</summary>
    /// <param name="disc">Open disc source for reading the stage overlay bytes.</param>
    /// <param name="entries">Full disc file listing for resolving overlay and archive paths.</param>
    /// <param name="stageId">Numeric stage ID whose overlay file is WSTAG{stageId:D3}.PRO.</param>
    /// <param name="tableSource">Resolves actor IDs to file entries and sprite archives.</param>
    /// <param name="context">Active write context used to persist decoded character archives.</param>
    /// <returns>The decoded zone record plus counts of visible and successfully decoded characters.</returns>
    private static (NpcExtractedZone Zone, int Characters, int Visible) ExtractStage(
        IDiscSource disc,
        IReadOnlyList<DiscFileEntry> entries,
        int stageId,
        INpcActorTableSource tableSource,
        DataPackWriteContext context)
    {
        string proPath = $"AAA/PRO/WSTAG{stageId}.PRO";
        byte[] overlay = disc.ReadFile(RequireEntry(entries, proPath, "Required NPC stage overlay is absent"));
        int characters = 0;
        int visible = 0;
        var extractedActors = new List<NpcExtractedActor>();

        foreach (NpcActorReference actorReference in tableSource.GetStageActors(stageId))
        {
            NpcActorEntry actor = NpcOverlayReader.ReadActorEntry(overlay, actorReference.Address, StageVram);
            IReadOnlyList<NpcCondition> conditions = NpcOverlayReader.ReadConditionList(
                overlay, actor.ConditionsPtr, StageVram);
            bool actorVisible = tableSource.IsVisible(conditions);
            bool isPlayerLead = tableSource.IsPlayerLead(actor.ActorId);
            int? width = tableSource.GetActorWidth(actor.ActorId);
            var packedEntry = tableSource.GetActorSpriteEntry(actor.ActorId);
            (string? archivePath, NpcFileEntry? fileEntry) = ResolveActorArchive(tableSource, packedEntry);
            extractedActors.Add(new NpcExtractedActor(
                actorReference.Index,
                actor.ActorId,
                actor.Image,
                actor.X,
                actor.Y,
                actor.Direction,
                width,
                isPlayerLead,
                actorVisible,
                conditions,
                fileEntry,
                archivePath));

            if (!actorVisible || isPlayerLead)
            {
                continue;
            }

            visible++;
            if (packedEntry is null || archivePath is null)
            {
                continue;
            }

            byte[] archiveBytes = disc.ReadFile(RequireEntry(entries, archivePath, "NPC archive is absent from disc"));
            NpcExtractedCharacter character = DecodeCharacter(
                archiveBytes,
                archivePath,
                packedEntry.Value.ArchiveEntry,
                actor.ActorId,
                stageId,
                actorReference.Index);
            NpcDomainWriter.Write(context, character);
            characters++;
        }

        return (
            new NpcExtractedZone(
                stageId,
                proPath,
                Convert.ToHexStringLower(SHA256.HashData(overlay)),
                extractedActors),
            characters,
            visible);
    }

    /// <summary>Resolves the disc archive path and file entry record for an actor sprite, or returns nulls when absent.</summary>
    /// <param name="tableSource">Source that maps file numbers to disc paths.</param>
    /// <param name="packedEntry">Packed file and archive entry numbers, or null when the actor has no sprite archive.</param>
    /// <returns>Tuple of disc path and file-entry record; both are null when packedEntry is null.</returns>
    private static (string? ArchivePath, NpcFileEntry? FileEntry) ResolveActorArchive(
        INpcActorTableSource tableSource,
        (int FileNumber, int ArchiveEntry)? packedEntry)
    {
        if (packedEntry is null)
        {
            return (null, null);
        }

        string? path = tableSource.GetDiscFilePath(packedEntry.Value.FileNumber);
        return (path, new NpcFileEntry(packedEntry.Value.FileNumber, packedEntry.Value.ArchiveEntry));
    }

    /// <summary>
    /// Decodes the animation frames, sets, and sequences from an NPC character archive and
    /// returns a fully populated <see cref="NpcExtractedCharacter"/>.
    /// </summary>
    /// <param name="archiveBytes">The raw bytes of the NPC character archive (WSTAG-relative sprite BIN).</param>
    /// <param name="discPath">The ISO path of the archive, stored as provenance in the character record.</param>
    /// <param name="archiveEntry">The archive entry index of the animation-set table (frames precede it, 0-based).</param>
    /// <param name="actorId">The disc actor id (FIELDSTG_fileEntries index).</param>
    /// <param name="stageId">The three-digit stage number the actor was placed in.</param>
    /// <param name="actorIndex">The zero-based actor placement index within the stage overlay.</param>
    /// <returns>A fully decoded NPC character with frames, sets, sequences, and metadata.</returns>
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
        {
            foreach (int seqIndex in set.SequenceIndices)
            {
                sequenceIndices.Add(seqIndex);
            }
        }

        var sequences = new List<NpcAnimSequence>();
        int maxFrame = -1;
        foreach (int seqIndex in sequenceIndices)
        {
            ActionAnimationSequence raw = ActionArchiveDecoder.DecodeSequence(archiveBytes, offsets, seqIndex, archiveEntry);
            maxFrame = raw.Records.Select(record => record.Frame).Prepend(maxFrame).Max();

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
            actorId,
            stageId,
            actorIndex,
            ManifestSchema,
            discPath,
            sha256,
            archiveBytes.Length,
            archiveEntry,
            frames,
            sets,
            sequences);
    }
}
