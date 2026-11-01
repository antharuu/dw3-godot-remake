
namespace Dw3Production.Conversion;

public sealed record Layer(string Path, int SourceDepth);
public sealed record Exit(int Slot, string Destination, int X, int Y, int Facing, int? Condition = null, string ConditionStatus = "unconditional")
{
    /// <summary>Gets a value indicating whether true when this physical edge is story-gated and opened for the exploration
    /// phase without evaluating its condition. The raw condition stays on <see cref="Condition"/>.</summary>
    public bool IsUnevaluated => ConditionStatus == "unevaluated";
}
/// <summary>A SLOT_CLIMB_UP (Type 2) or SLOT_CLIMB_DOWN (Type 3) slot. X/Y are the source
/// slot pixel coordinates the actor is placed at when the climb starts; Height is the wall
/// height in source pixels ((arg - 1) * 16).</summary>
public sealed record Climb(int Slot, int Type, int X, int Y, int Height);
/// <summary>A SLOT_DROP (Type 4) slot. Height is arg * 16 source pixels; direction is read from the trigger cell.</summary>
public sealed record Drop(int Slot, int Height);
/// <summary>
/// Optional PS1 stream modulate color for the depth-11 background layer.
/// Source: FIELDSTG_spriteDepths[2]=11 always uses spriteColor in the field-layer record contract (i==2).
/// Neutral is null (maps to Color(1,1,1) = no modulation).
/// </summary>
public sealed record StreamModulate(float R, float G, float B);
public sealed record Zone(string Id, int Width, int Height, IReadOnlyList<Layer> Layers, string FloorCellsPath, string TriggerCellsPath, IReadOnlyList<Exit> Exits, IReadOnlyList<Climb> Climbs, string SourceHash, StreamModulate? Depth11Modulate = null, string Status = "loadable", IReadOnlyList<Drop>? Drops = null, string? AreasCellsPath = null);
public sealed record AnimationRecord(int Frame, int Duration, int OffsetX, int OffsetY, int FootGroundX, int FootGroundY);
public sealed record PlayerSequence(int Set, int Direction, IReadOnlyList<AnimationRecord> Records);
public sealed record PlayerData(IReadOnlyList<PlayerSequence> Sequences, int AnchorX, int AnchorY);
public sealed record CollisionActor(string ZoneId, string ActorId, int X, int Y, int Width);
/// <summary>One frame of a visible NPC's STAND idle loop, already bound to its source
/// decoded PNG, per-frame top-left offset and source-tick duration.</summary>
public sealed record NpcFrame(string Path, int OffsetX, int OffsetY, int Duration);
/// <summary>A visible city NPC: source position/direction/width plus its resolved idle loop.</summary>
public sealed record NpcRecord(string ActorId, int X, int Y, int Width, int SpriteDirection, bool FlipH, IReadOnlyList<NpcFrame> IdleFrames);
/// <summary>All visible NPCs at the disc snapshot (progress=0x04): per-zone render
/// records plus the occupancy occupants used by FIELDSTG_isTileFree.</summary>
public sealed record NpcData(IReadOnlyDictionary<string, IReadOnlyList<NpcRecord>> Zones, IReadOnlyList<CollisionActor> Occupants);

// ---------------------------------------------------------------------------
// Battle data records: disc-decoded encounter, fighter and technique entries. These
// mirror the decoder JSON contracts verbatim and are engine-free: no Godot type
// and no runtime behaviour, just the disc-decoded values.
// ---------------------------------------------------------------------------

/// <summary>One FIELDSTG_enemies entry: a battle-ready enemy at a fixed level/HP/MP.</summary>
public sealed record BattleEnemy(int Index, int Fighter, int Level, int Hp, int Mp, int Strength);
/// <summary>One FIELDSTG_encounters entry. <see cref="EnemyIndices"/> indexes the flat
/// enemy list; <see cref="Fighters"/> is the resolved fighter id per slot (0 = empty).</summary>
public sealed record EncounterEntry(int Index, IReadOnlyList<int> EnemyIndices, IReadOnlyList<int> Fighters, int AmbushChance, int Kind, IReadOnlyList<int> Blocks);
/// <summary>One AreaBattle (field_map.h): the encounter index, fight stage and battle music.</summary>
public sealed record EncounterAreaBattle(int Encounter, int Stage, int Music, string MusicName);
/// <summary>One BattleList: the battle-rate index and its eight area battles.</summary>
public sealed record EncounterBattleList(int Count, IReadOnlyList<EncounterAreaBattle> Battles);
/// <summary>One FieldBattles place: four battle areas (areas[3] is the event battle area).</summary>
public sealed record EncounterFieldBattles(int Serial, int PlaceId, int Pad8, IReadOnlyList<EncounterBattleList> Areas);
/// <summary>One stageBattles array: the source overlay RAM address and its places.</summary>
public sealed record EncounterStageArray(uint Addr, IReadOnlyList<EncounterFieldBattles> Places);
/// <summary>One stage's encounter registry: overlay identity plus its battle arrays.</summary>
public sealed record EncounterStage(string Overlay, string OverlaySha256, IReadOnlyList<EncounterStageArray> StageBattles);
/// <summary>The m6-encounter-v1 contract: battle rates, the enemy pool, every encounter and per-stage battles.</summary>
public sealed record EncounterData(IReadOnlyList<int> BattleRates, IReadOnlyList<BattleEnemy> Enemies, IReadOnlyList<EncounterEntry> Encounters, IReadOnlyDictionary<string, EncounterStage> Stages);

/// <summary>One DIGIMON_DATA entry: a Digimon's base battle stats, resistances, learned skills and growth.</summary>
public sealed record DigimonData(int Id, IReadOnlyList<int> BattleStats, IReadOnlyList<int> Resistances, IReadOnlyList<int> Skills, int PairTech, IReadOnlyList<int> StatusResists, IReadOnlyList<int> SkillLevels, IReadOnlyList<int> KnownLevels, int ExpLevel, int PairPartner, int ExpRate, int Hp, int Mp, int HpGrowth, int MpGrowth, IReadOnlyList<int> StatGrowth, IReadOnlyList<int> ResistGrowth, IReadOnlyList<int> BlastForms, int NameId, int Family, bool Placeholder);
/// <summary>One enemy battle-table action (target, condition, condition argument).</summary>
public sealed record BattleAction(int Target, int Condition, int ConditionArg);
/// <summary>One SDIGIEDT.PRO BattleTableEntry: an enemy's stats, resistances, techniques and AI actions.</summary>
public sealed record BattleTableEntry(int Id, int Item, int ItemChance, int NameId, IReadOnlyList<int> Techs, IReadOnlyList<int> Stats, IReadOnlyList<int> Resist, int Family, IReadOnlyList<BattleAction> Actions, BattleAction Counter);
/// <summary>The m6-fighter-v1 contract: DIGIMON_DATA keyed by id, the same list in its source
/// array order (DIGIMON_DATA[0..54], so "the first 8 are the partners" — game_state.h), plus
/// the enemy battle table. The ordered list lets the starter party resolve partner i to
/// DIGIMON_DATA[i].id without inventing a mapping.</summary>
public sealed record FighterData(IReadOnlyDictionary<int, DigimonData> Digimon, IReadOnlyList<DigimonData> DigimonOrdered, IReadOnlyList<BattleTableEntry> BattleTable);

/// <summary>One TECHS entry (TechData): the fields FIGHTSTG_adjustDamage consumes.</summary>
public sealed record TechData(int Id, int Mp, int Power, int Icon, int Kind, int Accuracy, int Element, int ElementPower, int Family, int Effect, int EffectChance, int EffectPower, int ScriptStage, int ScriptEffect, int ScriptSound, int Script, int HitCount);
/// <summary>The m6-tech-v1 contract: every technique keyed by id.</summary>
public sealed record TechniqueData(IReadOnlyDictionary<int, TechData> Techs);

// ---------------------------------------------------------------------------
// M6-C4 records (m6-digivolution-v1 / m6-battle-item-v1). The digivolution tree
// is STFGTREP_evolutionLists (permanent post-battle evolution); the battle items
// are the usable items carrying ITEM_USE_BATTLE. Engine-free and disc-decoded.
// ---------------------------------------------------------------------------

/// <summary>One prerequisite of a digivolution entry: a Digimon the partner must
/// already have at least at a level (DigimonId 0 = none).</summary>
public sealed record EvolutionNeed(int DigimonIndex, int DigimonId, int Level);
/// <summary>One STFGTREP_evolutionLists entry: a Digimon a partner can learn, its
/// prerequisites, and a stat requirement (1-6 battle stat, 7 level, 8-14 resistance)
/// of at least <see cref="Value"/> (0 = no requirement).</summary>
public sealed record DigivolutionEntry(int DigimonIndex, int DigimonId, IReadOnlyList<EvolutionNeed> Needs, int Stat, int Value);
/// <summary>One partner's evolution tree: its base Digimon and its 44 learnable forms.</summary>
public sealed record PartnerDigivolution(int Partner, int BaseId, IReadOnlyList<DigivolutionEntry> Evolutions);
/// <summary>The in-battle Digivolve command's level tiers (WFIGHTMN_blast): the partner
/// level below which each DigimonData.BlastForms tier applies.</summary>
public sealed record DigivolutionBlast(IReadOnlyList<int> TierLevels);
/// <summary>The m6-digivolution-v1 contract: the eight partners' evolution trees plus the blast tiers.</summary>
public sealed record DigivolutionData(IReadOnlyList<PartnerDigivolution> Partners, DigivolutionBlast Blast);

/// <summary>One battle-usable item: its disc-decoded flags/kind/amount and price, the
/// code-derived battle effect and target (the item-record contract), and its FSITMNAM name token stream.</summary>
public sealed record BattleItemData(int Id, int NameId, IReadOnlyList<TextToken> Name, int Flags, int EffectKind, int Amount, int Price, int SellPrice, int Type, int Category, string Effect, string Target);
/// <summary>The m6-battle-item-v1 contract: every battle-usable item.</summary>
public sealed record BattleItemsData(IReadOnlyList<BattleItemData> Items);

// ---------------------------------------------------------------------------
// M6-C6 records (m6-rewards-v1). The reward a battle gives (STFGTREP_rewards,
// indexed by the battle id) and the growth tables the growth-table contract reads when a partner
// levels up. Engine-free and disc-decoded.
// ---------------------------------------------------------------------------

/// <summary>One battle's reward (BattleReward: digimonExp, exp, money).</summary>
public sealed record RewardEntry(int Index, int DigimonExp, int Exp, int Money);
/// <summary>The growth tables from STFGTREP.PRO (the growth-table contract): level-exp per tier,
/// the HP/MP vital cuts and random swing, the six battle-stat gain tiers and the
/// resistance gains.</summary>
public sealed record GrowthTablesData(IReadOnlyList<int> LevelExp, IReadOnlyList<int> VitalCuts, IReadOnlyList<int> VitalRandom, IReadOnlyList<IReadOnlyList<int>> StatGains, IReadOnlyList<int> ResistGains);
/// <summary>The m6-rewards-v1 contract: every battle reward plus the growth tables.</summary>
public sealed record RewardsData(IReadOnlyList<RewardEntry> Rewards, GrowthTablesData Growth);

/// <summary>One battle-HUD sprite frame (HP-bar cap, background or technique gauge): its PNG
/// path and pixel size. Loaded by LoadBattleHud; every frame is a disc-decoded sheet sprite.</summary>
public sealed record BattleHudFrame(int FrameId, IReadOnlyList<string> ClutRows, int Width, int Height, int OffsetX, int OffsetY);
/// <summary>The battle-HUD cursor bar: one PNG holding 12 frames of 12x12 side by side.</summary>
public sealed record BattleHudCursor(string Path, string OffPath, int Width, int Height, int Frames, int FrameW, int FrameH);
/// <summary>The m6-battle-hud-v1 contract: the sheet frames FIGHTSTG_drawHud draws plus the cursor.</summary>
public sealed record BattleHudData(IReadOnlyDictionary<int, BattleHudFrame> Frames, BattleHudCursor Cursor);
/// <summary>The m6-battle-report-sprites-v1 STFGTREP sheet, partner animation rows and CMFONTCS arrow.</summary>
public sealed record BattleReportSprites(IReadOnlyDictionary<int, BattleHudFrame> Frames, IReadOnlyList<int[]> Animations, BattleHudFrame Arrow);
/// <summary>One CMFONTCS battle-menu frame, including source anchor offset and CLUT variants.</summary>
public sealed record BattleMenuIconFrame(int FrameId, IReadOnlyList<string> ClutRows, int Width, int Height, int OffsetX, int OffsetY);
/// <summary>CMFONTCS icons used by Technique/Item plus the battle-message layout contract's five-row wait arrow.</summary>
public sealed record BattleMenuIconsData(IReadOnlyDictionary<int, BattleMenuIconFrame> Frames, int ArrowFrame, int ArrowX, int ArrowY, int ArrowTimeStep);
/// <summary>One named MFIGHT template, with the source role and its TextWindow substitutions.</summary>
public sealed record BattleTextTemplate(int Entry, string Role, IReadOnlyList<string> Substitutions, string Source);
/// <summary>The m6-battle-text-v2 contract: French token streams from MFIGHT, DIGNAM, SKLNAM and FGTRPT.</summary>
public sealed record BattleTextData(IReadOnlyList<IReadOnlyList<TextToken>> Menu, IReadOnlyList<IReadOnlyList<TextToken>> DigimonNames, IReadOnlyList<IReadOnlyList<TextToken>> TechniqueNames, IReadOnlyList<IReadOnlyList<TextToken>> SkillInfo, IReadOnlyList<IReadOnlyList<TextToken>> ItemInfo, IReadOnlyList<IReadOnlyList<TextToken>> Report, IReadOnlyList<BattleTextTemplate> Commands, IReadOnlyDictionary<string, BattleTextTemplate> Messages, IReadOnlyDictionary<string, int> ReportTemplates);
