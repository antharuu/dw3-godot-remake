using System.Buffers.Binary;
using System.Text.Json;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary;
using Dw3.Formats.Text;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>Extracts battle records and fight-report data directly from the supported disc.</summary>
internal static class BattleExtractionStep
{
    private const string SlesPath = "SLES_039.36";
    private const string BattleTablePath = "AAA/PRO/SDIGIEDT.PRO";
    private const string ReportPath = "AAA/PRO/STFGTREP.PRO";
    private const uint DigimonAddress = 0x8003EF5C;
    private const int DigimonCount = 55;
    private const uint TechniqueAddress = 0x80042BDC;
    private const int TechniqueCount = 443;
    private const uint ItemsAddress = 0x80041844;
    private const int ItemCount = 402;
    private const uint ReportBase = 0x80082CB0;
    private const uint RewardsAddress = 0x80086A88;
    private const int RewardCount = 335;
    private const uint EvolutionAddress = 0x80087A3C;
    private const uint EvolutionPointersAddress = 0x8008903C;
    private const uint GrowthAddress = 0x8008916C;
    private const int Partners = 8;
    private const int EvolutionsPerPartner = 44;
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };
    private static readonly string[] FrenchTextPaths =
    [
        "AAA/DAT/COUNTRY/FRA/FSMFIGHT.BIN", "AAA/DAT/COUNTRY/FRA/FSDIGNAM.BIN",
        "AAA/DAT/COUNTRY/FRA/FSSKLNAM.BIN", "AAA/DAT/COUNTRY/FRA/FSSKLINF.BIN",
        "AAA/DAT/COUNTRY/FRA/FSITMNAM.BIN", "AAA/DAT/COUNTRY/FRA/FSITMINF.BIN",
        "AAA/DAT/COUNTRY/FRA/FSFGTRPT.BIN"
    ];

    /// <summary>
    /// Reads the SLES executable and STFGTREP.PRO from the disc, then dispatches to
    /// <c>WriteBattle</c> (digimon, techniques, items, rewards, growth, text tables) or
    /// <c>WriteDigivolution</c> (evolution prerequisites per partner) based on <paramref name="domain"/>.
    /// </summary>
    /// <param name="cuePath">Path to the CUE sheet of the supported disc image.</param>
    /// <param name="outputDirectory">Root of the data pack to write into.</param>
    /// <param name="domain">Either <c>"battle"</c> or <c>"digivolution"</c>.</param>
    /// <param name="output">Receives one JSON event line on success.</param>
    /// <param name="error">Receives one plain-text error line on failure.</param>
    /// <returns>0 on success, 1 on extraction error.</returns>
    internal static int Extract(string cuePath, string outputDirectory, string domain, TextWriter output, TextWriter error)
    {
        try
        {
            using var disc = DiscSource.Open(cuePath);
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            byte[] executableBytes = disc.ReadFile(Require(entries, SlesPath));
            PsxExecutable executable = PsxExecutable.Parse(executableBytes);
            byte[] report = disc.ReadFile(Require(entries, ReportPath));
            new DataPackWriter(outputDirectory).Write(new DataPackMetadata(
                typeof(BattleExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1, edition.Label, DateTimeOffset.UtcNow), context =>
            {
                if (domain == "battle") WriteBattle(context, disc, entries, executable, executableBytes);
                else WriteDigivolution(context, executable, report);
            });
            output.WriteLine($"{{\"event\":\"domain-complete\",\"domain\":\"{domain}\"}}");
            return 0;
        }
        catch (Exception ex) when (ex is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error [{domain}]: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Decodes digimon, techniques, items, rewards, growth tables, and French text tables
    /// from the executable and SDIGIEDT.PRO, then persists them to SQLite and JSON.
    /// </summary>
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="disc">The open disc source for reading overlay files.</param>
    /// <param name="entries">The full disc file entry list.</param>
    /// <param name="executable">The parsed PS1 executable for RAM-relative data reads.</param>
    /// <param name="executableBytes">The raw executable bytes (retained for forward compatibility; currently unused).</param>
    private static void WriteBattle(DataPackWriteContext context, DiscSource disc, IReadOnlyList<DiscFileEntry> entries, PsxExecutable executable, byte[] executableBytes)
    {
        _ = executableBytes;
        var digimon = new List<DigimonRecord>();
        for (int i = 0; i < DigimonCount; i++)
            digimon.Add(BattleDataDecoder.DecodeDigimon(executable.Slice(DigimonAddress + (uint)(i * BattleDataDecoder.DigimonStride), BattleDataDecoder.DigimonStride), 0));
        var techniques = new List<TechniqueRecord>();
        for (int i = 0; i < TechniqueCount; i++)
            techniques.Add(BattleDataDecoder.DecodeTechnique(executable.Slice(TechniqueAddress + (uint)(i * BattleDataDecoder.TechniqueStride), BattleDataDecoder.TechniqueStride), 0));
        byte[] tableBytes = disc.ReadFile(Require(entries, BattleTablePath));
        IReadOnlyList<BattleTableRecord> table = BattleDataDecoder.DecodeBattleTable(tableBytes);
        for (int i = 0; i < digimon.Count; i++) Insert(context, "INSERT INTO battle_digimon (id,name_id,family) VALUES ($1,$2,$3)", i + 1, digimon[i].NameId, digimon[i].Family);
        for (int i = 0; i < techniques.Count; i++) Insert(context, "INSERT INTO battle_techniques (id,mp,power,icon,kind,accuracy,element,family) VALUES ($1,$2,$3,$4,$5,$6,$7,$8)", i + 1, techniques[i].Mp, techniques[i].Power, techniques[i].Icon, techniques[i].Kind, techniques[i].Accuracy, techniques[i].Element, techniques[i].Family);
        foreach (BattleTableRecord record in table) Insert(context, "INSERT INTO battle_enemy_table (id,item,item_chance,name_id,family) VALUES ($1,$2,$3,$4,$5)", record.Id, record.Item, record.ItemChance, record.NameId, record.Family);
        var items = new List<(ItemRecord Item, ItemEffect? Effect)>();
        for (int i = 0; i < ItemCount; i++)
        {
            ItemRecord item = BattleDataDecoder.DecodeItem(executable.Slice(ItemsAddress + (uint)(i * BattleDataDecoder.ItemStride), BattleDataDecoder.ItemStride), 0);
            ItemEffect? effect = item.DataPointer == 0 ? null : BattleDataDecoder.DecodeItemEffect(executable.Slice(item.DataPointer, BattleDataDecoder.ItemEffectStride), 0);
            items.Add((item, effect));
            Insert(context, "INSERT INTO battle_items (id,data_pointer,price,sell_price,kind,type,flags,effect_kind,amount) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)", i + 1, unchecked((int)item.DataPointer), item.Price, item.SellPrice, item.Kind, item.Type, effect is null ? DBNull.Value : effect.Flags, effect is null ? DBNull.Value : effect.Kind, effect is null ? DBNull.Value : effect.Amount);
        }
        int rewardOffset = ReportOffset(RewardsAddress, RewardCount * BattleDataDecoder.RewardStride, "reward table");
        byte[] report = disc.ReadFile(Require(entries, ReportPath));
        var rewards = Enumerable.Range(0, RewardCount).Select(i => BattleDataDecoder.DecodeReward(report, rewardOffset + i * BattleDataDecoder.RewardStride)).ToArray();
        BattleGrowthData growth = BattleDataDecoder.DecodeGrowth(report, ReportOffset(GrowthAddress, 316, "growth tables"));
        for (int i = 0; i < rewards.Length; i++) Insert(context, "INSERT INTO battle_rewards (id,digimon_experience,experience,money) VALUES ($1,$2,$3,$4)", i, rewards[i].DigimonExperience, rewards[i].Experience, rewards[i].Money);
        WriteGrowth(context, growth);
        WriteBattleJson(context, digimon, techniques, table, items, rewards, growth);
        foreach (string path in FrenchTextPaths) WriteTextTable(context, path, disc.ReadFile(Require(entries, path)));
    }

    /// <summary>
    /// Reads the evolution prerequisite table (8 partners × 44 entries, each 12 bytes)
    /// from STFGTREP.PRO and inserts rows into <c>digivolution_records</c> and <c>digivolution_needs</c>.
    /// Writes <c>digivolution/digivolution.json</c> for parity verification.
    /// </summary>
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="executable">The parsed PS1 executable, used to read digimon ids for index-to-id mapping.</param>
    /// <param name="report">The raw bytes of STFGTREP.PRO containing the evolution and pointer tables.</param>
    private static void WriteDigivolution(DataPackWriteContext context, PsxExecutable executable, byte[] report)
    {
        var ids = new int[DigimonCount];
        for (int i = 0; i < ids.Length; i++) ids[i] = BattleDataDecoder.DecodeDigimon(executable.Slice(DigimonAddress + (uint)(i * BattleDataDecoder.DigimonStride), BattleDataDecoder.DigimonStride), 0).Id;
        ValidateEvolutionPointers(report);
        int evolutionOffset = ReportOffset(EvolutionAddress, Partners * EvolutionsPerPartner * BattleDataDecoder.EvolutionStride, "evolution list");
        for (int partner = 0; partner < Partners; partner++)
            for (int entry = 0; entry < EvolutionsPerPartner; entry++)
            {
                EvolutionRecord evolution = BattleDataDecoder.DecodeEvolution(report, evolutionOffset + ((partner * EvolutionsPerPartner + entry) * BattleDataDecoder.EvolutionStride), ids);
                Insert(context, "INSERT INTO digivolution_records (partner,ordinal,digimon_index,digimon_id,stat,value) VALUES ($1,$2,$3,$4,$5,$6)", partner, entry, evolution.DigimonIndex, evolution.DigimonId, evolution.Stat, evolution.Value);
                for (int need = 0; need < evolution.Needs.Count; need++) Insert(context, "INSERT INTO digivolution_needs (partner,ordinal,need_ordinal,digimon_index,digimon_id,level) VALUES ($1,$2,$3,$4,$5,$6)", partner, entry, need, evolution.Needs[need].DigimonIndex, evolution.Needs[need].DigimonId, evolution.Needs[need].Level);
            }
        var partnerRecords = new List<EvolutionRecord>[Partners];
        for (int partner = 0; partner < Partners; partner++)
        {
            partnerRecords[partner] = [];
            for (int entry = 0; entry < EvolutionsPerPartner; entry++)
                partnerRecords[partner].Add(BattleDataDecoder.DecodeEvolution(report, evolutionOffset + ((partner * EvolutionsPerPartner + entry) * BattleDataDecoder.EvolutionStride), ids));
        }
        string path = context.GetImagePath("digivolution/digivolution.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var digimon = ids.Select(id => new DigimonRecord((ushort)id, [], [], [], 0, [], [], [], 0, 0, 0, 0, 0, 0, 0, [], [], [], 0, 0)).ToArray();
        File.WriteAllText(path, BattleJsonWriter.WriteDigivolution(digimon, partnerRecords));
    }

    /// <summary>
    /// Writes <c>battle/battle.json</c> containing all digimon, techniques, battle table entries,
    /// items, rewards, and growth data in the parity-verified JSON layout.
    /// </summary>
    /// <param name="context">The data pack write context providing image path resolution.</param>
    /// <param name="digimon">All decoded digimon records in table order.</param>
    /// <param name="techniques">All decoded technique records in table order.</param>
    /// <param name="table">All decoded battle encounter table records.</param>
    /// <param name="items">All decoded item records with optional effect records.</param>
    /// <param name="rewards">All decoded battle reward records indexed by encounter.</param>
    /// <param name="growth">The growth curve tables (level exp, vital cuts, stat gains, resistance gains).</param>
    private static void WriteBattleJson(DataPackWriteContext context, IReadOnlyList<DigimonRecord> digimon, IReadOnlyList<TechniqueRecord> techniques, IReadOnlyList<BattleTableRecord> table, IReadOnlyList<(ItemRecord Item, ItemEffect? Effect)> items, IReadOnlyList<BattleReward> rewards, BattleGrowthData growth)
    {
        string path = context.GetImagePath("battle/battle.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, BattleJsonWriter.WriteBattle(digimon, techniques, table, items, rewards, growth));
    }

    /// <summary>
    /// Decodes the text token table from a French BIN file and inserts rows into
    /// <c>battle_text_tables</c> and <c>battle_text_tokens</c>; also writes a JSON copy.
    /// </summary>
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="discPath">The ISO path of the source BIN file (stored as provenance key).</param>
    /// <param name="bytes">The raw bytes of the decoded text table BIN file.</param>
    private static void WriteTextTable(DataPackWriteContext context, string discPath, byte[] bytes)
    {
        IReadOnlyList<IReadOnlyList<TextToken>> entries = TextTableDecoder.DecodeFile(bytes);
        string fileName = Path.GetFileNameWithoutExtension(discPath).ToLowerInvariant();
        string path = context.GetImagePath($"battle/text/{fileName}.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Insert(context, "INSERT INTO battle_text_tables (disc_path,entry_count) VALUES ($1,$2)", discPath, entries.Count);
        for (int entry = 0; entry < entries.Count; entry++) for (int ordinal = 0; ordinal < entries[entry].Count; ordinal++)
            {
                TextToken token = entries[entry][ordinal];
                Insert(context, "INSERT INTO battle_text_tokens (disc_path,entry_ordinal,token_ordinal,kind,code,args_json) VALUES ($1,$2,$3,$4,$5,$6)", discPath, entry, ordinal, (int)token.Kind, token.Code, JsonSerializer.Serialize(token.Args));
            }
        File.WriteAllText(path, JsonSerializer.Serialize(new { discPath, entries = entries.Select(entry => entry.Select(token => new { kind = (int)token.Kind, token.Code, token.Args })) }, IndentedJsonOptions));
    }

    /// <summary>
    /// Writes all rows of the <c>battle_growth</c> table from the decoded growth data,
    /// preserving the fixed write order: levelExperience, vitalCuts, vitalRandom,
    /// statGains (one row per curve), resistanceGains.
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="growth">The decoded growth data containing all curve arrays.</param>
    private static void WriteGrowth(DataPackWriteContext context, BattleGrowthData growth)
    {
        WriteGrowthRow(context, "levelExperience", 0, growth.LevelExperience);
        WriteGrowthRow(context, "vitalCuts", 0, growth.VitalCuts);
        WriteGrowthRow(context, "vitalRandom", 0, growth.VitalRandom);
        for (int row = 0; row < growth.StatGains.Count; row++) WriteGrowthRow(context, "statGains", row, growth.StatGains[row]);
        WriteGrowthRow(context, "resistanceGains", 0, growth.ResistanceGains);
    }

    /// <summary>
    /// Inserts one value per ordinal for a single growth curve row into <c>battle_growth</c>.
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="table">The curve group name (e.g. <c>"levelExperience"</c> or <c>"statGains"</c>).</param>
    /// <param name="row">The row index within the curve group (0 for single-row tables).</param>
    /// <param name="values">The ordered growth values for this row.</param>
    private static void WriteGrowthRow(DataPackWriteContext context, string table, int row, IReadOnlyList<int> values)
    { for (int ordinal = 0; ordinal < values.Count; ordinal++) Insert(context, "INSERT INTO battle_growth (table_name,row_ordinal,value_ordinal,value) VALUES ($1,$2,$3,$4)", table, row, ordinal, values[ordinal]); }

    /// <summary>
    /// Verifies that the STFGTREP.PRO evolution pointer table at RAM 0x8008903C holds
    /// the expected contiguous-table pointers for all eight partners.
    /// </summary>
    /// <param name="report">The raw bytes of STFGTREP.PRO.</param>
    /// <exception cref="FormatException">A pointer does not reference the expected entry in the contiguous evolution table.</exception>
    private static void ValidateEvolutionPointers(byte[] report)
    {
        int offset = ReportOffset(EvolutionPointersAddress, Partners * 4, "evolution pointers");
        for (int i = 0; i < Partners; i++) if (BinaryPrimitives.ReadUInt32LittleEndian(report.AsSpan(offset + i * 4, 4)) != EvolutionAddress + (uint)(i * EvolutionsPerPartner * BattleDataDecoder.EvolutionStride)) throw new FormatException($"Evolution pointer {i} does not reference the contiguous evolution table.");
    }

    /// <summary>Maps a PS1 RAM address into an offset within the STFGTREP.PRO overlay byte array, throwing on range violations.</summary>
    /// <param name="address">The PS1 RAM address to map.</param>
    /// <param name="length">The number of bytes required at the offset.</param>
    /// <param name="name">A human-readable name for the data structure, used in error messages.</param>
    /// <returns>The byte offset within the overlay byte array.</returns>
    private static int ReportOffset(uint address, int length, string name)
    {
        if (address < ReportBase)
            throw new FormatException($"{name} lies below the fight-report overlay.");
        long offset = address - ReportBase;
        if (offset + length > int.MaxValue)
            throw new FormatException($"{name} offset overflows.");
        return (int)offset;
    }

    /// <summary>Finds and returns the disc file entry with the given ISO path, throwing if absent.</summary>
    /// <param name="entries">The full list of disc file entries to search.</param>
    /// <param name="path">The ISO path to match.</param>
    /// <returns>The matching <see cref="DiscFileEntry"/>.</returns>
    private static DiscFileEntry Require(IReadOnlyList<DiscFileEntry> entries, string path) =>
        entries.SingleOrDefault(entry => entry.IsoPath == path) ?? throw new InvalidDataException($"Required disc member missing: {path}");

    /// <summary>Executes a parameterized INSERT command against the current transaction.</summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="sql">The parameterized SQL command text.</param>
    /// <param name="values">The parameter values in positional order ($1, $2, …).</param>
    private static void Insert(DataPackWriteContext context, string sql, params object[] values)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int i = 0; i < values.Length; i++)
        {
            command.Parameters.AddWithValue($"${i + 1}", values[i]);
        }

        command.ExecuteNonQuery();
    }
}
