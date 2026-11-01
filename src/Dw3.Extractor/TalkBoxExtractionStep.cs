using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary;
using Dw3.Formats.Dialogue;

namespace Dw3.Extractor;

/// <summary>
/// Extracts the field talk box. Sources: AAA/DAT/COUNTRY/CMFONTCS.BIN (menu sprite sheet),
/// AAA/DAT/COUNTRY/CMFONTTM.BIN (its 4-bit texture, which also holds the frame CLUTs) and the
/// executable's 128-byte layout table at RAM 0x8004DCF4.
/// </summary>
internal static class TalkBoxExtractionStep
{
    private const string Domain = "talk-box";
    private const string ExecutablePath = "SLES_039.36";
    private const string SheetPath = "AAA/DAT/COUNTRY/CMFONTCS.BIN";
    private const string TexturePath = "AAA/DAT/COUNTRY/CMFONTTM.BIN";
    private const uint LayoutTableAddress = 0x8004DCF4;

    // Sheet frames the talk box draws: 0 and 2 are the edges, 3..6 the four corner sprites,
    // 7 the "next" arrow. The arrow blinks through four consecutive CLUT rows.
    private static readonly int[] FrameIds = [0, 2, 3, 4, 5, 6, 7];
    private const int ArrowFrame = 7;
    private const int ArrowClutFrames = 4;

    internal static int Extract(string cuePath, string outputDirectory, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = DiscSource.Open(cuePath);
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            PsxExecutable executable = PsxExecutable.Parse(disc.ReadFile(Require(entries, ExecutablePath)));
            byte[] sheet = disc.ReadFile(Require(entries, SheetPath));
            byte[] texture = disc.ReadFile(Require(entries, TexturePath));
            TalkBoxDomain domain = Decode(executable, sheet, texture);

            new DataPackWriter(outputDirectory).Write(new DataPackMetadata(
                typeof(TalkBoxExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1, edition.Label, DateTimeOffset.UtcNow),
                context => TalkBoxDomainWriter.Write(context, domain));
            output.WriteLine($"{{\"event\":\"domain-complete\",\"domain\":\"{Domain}\",\"frames\":{domain.Frames.Count},\"layouts\":{domain.Layouts.Count}}}");
            return 0;
        }
        catch (Exception exception) when (exception is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error [{Domain}]: {exception.Message}");
            return 1;
        }
    }

    internal static TalkBoxDomain Decode(PsxExecutable executable, byte[] sheet, byte[] texture)
    {
        IReadOnlyList<HudTexturePage> pages = BattleHudDecoder.BuildMenuTexturePages(texture);
        var (parts, ids, offsets) = BattleHudDecoder.ParseSheet(sheet);
        var frames = FrameIds
            .Select(frameId => TalkBoxFrameDecoder.RenderFrame(parts, ids, offsets, sheet, pages, frameId, frameId == ArrowFrame ? ArrowClutFrames : 1))
            .ToArray();
        TalkBoxPanelImage panel = TalkBoxFrameDecoder.DecodePanel(pages);
        IReadOnlyList<TalkBoxLayoutRecord> layouts = TalkBoxFrameDecoder.ReadLayouts(
            executable.Slice(LayoutTableAddress, TalkBoxFrameDecoder.LayoutCount * TalkBoxFrameDecoder.LayoutRecordBytes));
        return new TalkBoxDomain(frames, panel, layouts);
    }

    private static DiscFileEntry Require(IReadOnlyList<DiscFileEntry> entries, string path) =>
        entries.SingleOrDefault(entry => string.Equals(entry.IsoPath, path, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Required disc file is absent: {path}");
}
