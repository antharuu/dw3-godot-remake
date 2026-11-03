using Dw3.Data;
using Dw3.Formats.Field;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>
/// Runs the NPC step on a fabricated stage overlay with an in-memory actor table: one visible
/// character with an archive, one player lead, one hidden actor and one visible actor without archive.
/// </summary>
public sealed class NpcExtractionStepTests : IDisposable
{
    private const uint OverlayBase = 0x800A5DE0;
    private const int Stage = 200;
    private const string OverlayPath = "AAA/PRO/WSTAG200.PRO";
    private const string ArchivePath = "AAA/DAT/FIELD/SPRT/N010.BIN";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-npc-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void DecodesVisibleCharactersAndRecordsEveryPlacement()
    {
        var disc = new FabricatedDisc().Add(OverlayPath, Overlay()).Add(ArchivePath, CharacterArchive());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = NpcExtractionStep.Extract(disc.Open, _root, new ActorTable(), output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Equal("{\"event\":\"domain-complete\",\"domain\":\"npc\",\"zones\":1,\"visible_npcs\":2,\"decoded\":1}" + Environment.NewLine, output.ToString());
        using SqliteConnection db = new DataPackReader(_root).OpenDatabase();
        Assert.True(Scalar(db, "SELECT count(*) FROM pack_images") is long images && images > 0);
        Assert.Equal(0, disc.OpenHandles);
    }

    [Fact]
    public void VisibleCharacterWhoseArchiveIsAbsentFailsTheStep()
    {
        var disc = new FabricatedDisc().Add(OverlayPath, Overlay());
        using var error = new StringWriter();

        int exitCode = NpcExtractionStep.Extract(disc.Open, _root, new ActorTable(), TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Equal($"Extraction error: NPC archive is absent from disc: {ArchivePath}" + Environment.NewLine, error.ToString());
        Assert.False(File.Exists(Path.Combine(_root, "data", "dw3.sqlite")));
    }

    [Fact]
    public void MissingStageOverlayFailsTheStep()
    {
        var disc = new FabricatedDisc();
        using var error = new StringWriter();

        int exitCode = NpcExtractionStep.Extract(disc.Open, _root, new ActorTable(), TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains($"Required NPC stage overlay is absent: {OverlayPath}", error.ToString());
    }

    [Fact]
    public void ActorWithAnInvalidDirectionFailsTheStep()
    {
        byte[] overlay = Overlay();
        Fabricate.U16(overlay, 0x100 + 16, 9);
        var disc = new FabricatedDisc().Add(OverlayPath, overlay).Add(ArchivePath, CharacterArchive());
        using var error = new StringWriter();

        int exitCode = NpcExtractionStep.Extract(disc.Open, _root, new ActorTable(), TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("invalid direction 9", error.ToString());
    }

    /// <summary>
    /// Four 18-byte actor records at overlay offsets 0x100, 0x120, 0x140 and 0x160 (actor ids 10, 1,
    /// 20 and 30), each facing direction 2. Actor 20 points at a condition list holding code 9, which
    /// the fake table treats as false; actor 10 points at a list with code 1.
    /// </summary>
    private static byte[] Overlay()
    {
        var overlay = new byte[0x400];
        WriteActor(overlay, 0x100, conditions: 0x200, actorId: 10);
        WriteActor(overlay, 0x120, conditions: 0, actorId: 1);
        WriteActor(overlay, 0x140, conditions: 0x210, actorId: 20);
        WriteActor(overlay, 0x160, conditions: 0, actorId: 30);
        Fabricate.U16(overlay, 0x200, 1);
        Fabricate.U16(overlay, 0x202, 2);
        Fabricate.U16(overlay, 0x204, 0xFFFF);
        Fabricate.U16(overlay, 0x210, 9);
        Fabricate.U16(overlay, 0x214, 0xFFFF);
        return overlay;
    }

    private static void WriteActor(byte[] overlay, int offset, int conditions, short actorId)
    {
        Fabricate.U32(overlay, offset, conditions == 0 ? 0 : OverlayBase + (uint)conditions);
        Fabricate.U16(overlay, offset + 8, (ushort)actorId);
        Fabricate.U16(overlay, offset + 12, 100);
        Fabricate.U16(overlay, offset + 14, 50);
        Fabricate.U16(overlay, offset + 16, 2);
    }

    /// <summary>Entry 0 a frame, entry 1 the sequence, entry 2 filler, entry 3 the set table (set 5).</summary>
    private static byte[] CharacterArchive() =>
        Fabricate.Archive([Fabricate.DirectTim(), Fabricate.Words(4, 0, -1, -2, -1), new byte[4], Fabricate.SetTable([5], sequence: 1)]);

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    /// <summary>Actor table of one stage: actor 10 has archive file 0x400 entry 3, actor 1 is the player lead.</summary>
    private sealed class ActorTable : INpcActorTableSource
    {
        public (int FileNumber, int ArchiveEntry)? GetActorSpriteEntry(int actorId) => actorId == 10 ? (0x400, 3) : null;

        public int? GetActorWidth(int actorId) => actorId == 10 ? 32 : null;

        public bool IsPlayerLead(int actorId) => actorId == 1;

        public IReadOnlyList<NpcActorReference> GetStageActors(int stageId) =>
            [.. Enumerable.Range(0, 4).Select(index => new NpcActorReference(OverlayBase + 0x100 + (uint)(index * 0x20), index))];

        public bool IsVisible(IReadOnlyList<NpcCondition> conditions) => conditions.All(condition => condition.Code != 9);

        public string? GetDiscFilePath(int fileNumber) => fileNumber == 0x400 ? ArchivePath : null;

        public int? GetTriggerArchiveEntry(int stageId) => null;

        public IReadOnlyList<int> GetRequiredStageIds() => [Stage];
    }
}
