using Dw3.Data;
using Dw3.Formats.Battle;
using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;
using Microsoft.Data.Sqlite;

namespace Dw3.Game.Tests.Session;

internal sealed class BlastProductionFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"blast-pack-{Guid.NewGuid():N}");

    public BlastProductionFixture(int gauge = 0, int partnerIndex = 0, int level = 1, bool duplicateBases = false, IReadOnlyList<int>? equipment = null)
    {
        new DataPackWriter(_root).Write(
            new DataPackMetadata("blast-test", "synthetic", SyntheticDataPackFixture.SyntheticEdition, DateTimeOffset.UnixEpoch),
            WritePack);
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));
        Fighters = loader.LoadFighters();
        Techniques = loader.LoadTechs();
        PartnerSeed[] seeds = [.. BattleSetupFactory.BuildParty(Fighters, level, [101 + partnerIndex, duplicateBases ? 101 + partnerIndex : 102, 103]).OfType<PartnerSeed>()];
        seeds[0].MaxHp = 300;
        seeds[0].Hp = 300;
        seeds[0].BlastGauge = gauge;
        Session = new PlayerSession([.. seeds.Select(seed => new PlayerSessionPartner(seed, [seed.Id, 109], [109], [], equipment ?? []))], new Dictionary<int, int>());
    }

    public FighterData Fighters { get; }

    public TechniqueData Techniques { get; }

    public PlayerSession Session { get; }

    public BattleFlowController Build()
    {
        var encounters = new EncounterData([], [new Dw3Production.Conversion.BattleEnemy(1, 200, 1, 9000, 100, 16)], [new EncounterEntry(1, [1], [200], 0, 0, new int[12])], new Dictionary<string, EncounterStage>());
        var text = new BattleTextData([], [], [], [], [], [], [], new Dictionary<string, BattleTextTemplate>(), new Dictionary<string, int>());
        return GameHost.BattleSetupCoordinator.BuildForSession(new(Fighters, Techniques, encounters, 1, Session, new BattleItemsData([]), text, new Rng(127)));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private static void WritePack(DataPackWriteContext context)
    {
        DigimonRecord[] digimon = [.. Enumerable.Range(101, 55).Select(id => new DigimonRecord(
            (ushort)id,
            [(ushort)(id == 110 ? 40 : 10), 10, 10, 10, 100],
            new ushort[7],
            [1, (ushort)(id == 110 ? 3 : 2), 0, 0, 0, 0, (ushort)(id == 110 ? 4 : 0)],
            0,
            new byte[5],
            new byte[6],
            new byte[5],
            0,
            0,
            0,
            200,
            100,
            0,
            0,
            new byte[5],
            new byte[7],
            [10, 11, 12, 13, 14],
            1,
            0))];
        var technique = new TechniqueRecord(0, 30, 0, 0, 128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);
        var enemy = new BattleTableRecord(200, 0, 0, 1, [1, 0, 0], [10, 10, 10, 10, 100], new short[12], 0, [new(1, 0, 0)], new(1, 0, 0));
        TechniqueRecord[] techniques = [.. Enumerable.Repeat(technique, 443)];
        techniques[3] = technique with { Mp = 75 };
        string json = BattleJsonWriter.WriteBattle(digimon, techniques, [enemy], [], [], new([], [], [], [], []));
        string path = context.GetImagePath("battle/battle.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
        context.RegisterImage("battle/battle.json");
    }

    internal sealed class Rng(int value = 0) : IBattleRng
    {
        public int Calls { get; private set; }

        public int NextValue()
        {
            Calls++;
            return value;
        }
    }
}
