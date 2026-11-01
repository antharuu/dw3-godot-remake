using Dw3.Data;
using Dw3.Disc;

namespace Dw3.Extractor;

/// <summary>
/// Groups all shared context for a zone-writing pass so that the per-stage helpers stay
/// within the seven-parameter limit. Every field is read-only and set once at the start
/// of <c>WriteZones</c>.
/// </summary>
/// <param name="Pack">The data pack write context for SQL and image output.</param>
/// <param name="Disc">The disc source used to read binary content from the supported disc.</param>
/// <param name="Entries">Full disc file listing returned by <see cref="DiscSource.ListFiles"/>.</param>
/// <param name="Overlays">Pre-loaded WSTAG overlay bytes indexed by stage ID.</param>
/// <param name="StageModes">Disc-proven mode-to-stage-ID index used in each zone manifest.</param>
/// <param name="Registry">Field-stage registry indexed by stage ID, used for slot lookups.</param>
/// <param name="Output">Progress log writer for event JSON lines.</param>
internal sealed record ZoneWriteJob(
    DataPackWriteContext Pack,
    DiscSource Disc,
    IReadOnlyList<DiscFileEntry> Entries,
    Dictionary<int, byte[]> Overlays,
    Dictionary<ushort, int> StageModes,
    Dictionary<int, ExtractedFieldStage> Registry,
    TextWriter Output);
