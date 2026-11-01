using System.Security.Cryptography;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Binary.Tim;
using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>Extracts the climb and cliff-drop player action archives from the supported disc.</summary>
internal static class ActionExtractionStep
{
    private const string ClimbDomain = "climb";
    private const string DropDomain = "drop";

    // These file identifiers and archive entries are EU disc layout coordinates.
    private static readonly ActionDefinition Climb = new(
        ClimbDomain,
        "m3-climb-v1",
        0x3CC,
        0x17,
        "AAA/DAT/FIELD/SPRT/P002PLYL.BIN",
        new Dictionary<int, string>
        {
            [0x1A] = "CLIMB_UP",
            [0x1B] = "CLIMB_DOWN",
            [0x1C] = "GET_ON_WALL",
            [0x1D] = "CLIMB_OFF_TOP",
            [0x1E] = "GET_OVER_EDGE",
            [0x1F] = "CLIMB_OFF_BOTTOM",
            [0x20] = "CLIMB",
        });

    private static readonly ActionDefinition Drop = new(
        DropDomain,
        "m3-climb-v1",
        0x3C9,
        0x0B,
        "AAA/DAT/FIELD/SPRT/P002PLYD.BIN",
        new Dictionary<int, string>
        {
            [0x16] = "DROP_OFF",
            [0x17] = "FALL",
            [0x18] = "FALL_FAR",
            [0x19] = "LAND",
        });

    internal static int Extract(string cuePath, string outputDirectory, string domain, TextWriter output, TextWriter error)
    {
        ActionDefinition definition = domain switch
        {
            ClimbDomain => Climb,
            DropDomain => Drop,
            _ => throw new ArgumentException($"Unsupported action extraction domain: {domain}", nameof(domain)),
        };

        try
        {
            using var disc = DiscSource.Open(cuePath);
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            DiscFileEntry entry = entries.SingleOrDefault(item => string.Equals(item.IsoPath, definition.DiscPath, StringComparison.Ordinal))
                ?? throw new InvalidDataException($"Required disc file missing: {definition.DiscPath}");
            byte[] archive = disc.ReadFile(entry);
            ActionDomain decoded = Decode(definition, archive, entry.IsoPath);

            new DataPackWriter(outputDirectory).Write(new DataPackMetadata(
                typeof(ActionExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1,
                edition.Label,
                DateTimeOffset.UtcNow), context => ActionDomainWriter.Write(context, decoded));
            output.WriteLine($"{{\"event\":\"domain-complete\",\"domain\":\"{definition.Domain}\",\"sets\":{decoded.Sets.Count},\"frames\":{decoded.Frames.Count}}}");
            return 0;
        }
        catch (Exception exception) when (exception is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error: {exception.Message}");
            return 1;
        }
    }

    private static ActionDomain Decode(ActionDefinition definition, byte[] archive, string archiveIsoPath)
    {
        int[] offsets = ActionArchiveDecoder.DecodeOffsetTable(archive, definition.AnimationSetEntry);
        IReadOnlyList<ActionAnimationSet> groups = ActionArchiveDecoder.DecodeSetTable(archive, offsets, definition.AnimationSetEntry);
        if (groups.Count == 0)
            throw new FormatException($"{definition.Domain} action archive contains no animation sets");

        var sequences = new Dictionary<int, ActionAnimationSequence>();
        foreach (ActionAnimationSet group in groups)
        {
            int firstSequence = group.SequenceIndices[0];
            if (group.SequenceIndices.Any(sequence => sequence != firstSequence))
                throw new FormatException($"{definition.Domain} action set {group.SetId:X} maps directions to different sequences");
            if (!sequences.ContainsKey(firstSequence))
                sequences.Add(firstSequence, ActionArchiveDecoder.DecodeSequence(archive, offsets, firstSequence, definition.AnimationSetEntry));
        }

        int[] frameIds = [.. sequences.Values.SelectMany(sequence => sequence.Records).Select(record => record.Frame).Distinct().Order()];
        var frames = new List<ActionFrame>(frameIds.Length);
        foreach (int frameId in frameIds)
        {
            int start = offsets[frameId];
            int end = offsets[frameId + 1];
            TimImage image = TimDecoder.Decode(archive.AsSpan(start, end - start));
            frames.Add(new ActionFrame(frameId, image.Width, image.Height, image.Pixels));
        }

        var sets = new List<ActionSet>(groups.Count);
        foreach (ActionAnimationSet group in groups)
        {
            if (!definition.SetNames.TryGetValue(group.SetId, out string? name))
                throw new FormatException($"{definition.Domain} action set {group.SetId:X} is not identified by its disc layout");
            ActionAnimationSequence sequence = sequences[group.SequenceIndices[0]];
            sets.Add(new ActionSet(group.SetId, name, sequence.Terminator, sequence.Records));
        }

        return new ActionDomain(definition.Domain, definition.ManifestSchema, definition.FileId, definition.AnimationSetEntry,
            archiveIsoPath, Convert.ToHexStringLower(SHA256.HashData(archive)), archive.Length, frames, sets);
    }

    private sealed record ActionDefinition(string Domain, string ManifestSchema, int FileId, int AnimationSetEntry, string DiscPath, IReadOnlyDictionary<int, string> SetNames);
}
