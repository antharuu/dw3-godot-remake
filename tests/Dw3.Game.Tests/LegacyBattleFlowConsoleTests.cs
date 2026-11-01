using Xunit;

namespace Dw3.Game.Tests;

using Dw3Production.Simulation;
using BattleTextCatalog = Dw3Production.Conversion.BattleTextCatalog;
using BattleTextData = Dw3Production.Conversion.BattleTextData;
using BattleTextLine = Dw3Production.Conversion.BattleTextLine;
using BattleTextTemplate = Dw3Production.Conversion.BattleTextTemplate;
using TextToken = Dw3Production.Conversion.TextToken;
using DigimonData = Dw3Production.Conversion.DigimonData;
using FighterData = Dw3Production.Conversion.FighterData;
using TechniqueData = Dw3Production.Conversion.TechniqueData;

public sealed class LegacyBattleFlowConsoleTests
{

    [Fact]
    public void PreservesLegacyConsoleAssertions()
    {
        StartBeginsOnPlayerCommand();
        AttackResolvesAndAdvancesPhase();
        EnemyAttackReturnsToPlayer();
        VictoryWhenAllEnemiesKo();
        DefeatWhenAllPlayersKo();
        FleeEndsAsFled();
        TechniqueConsumesMpAndSpendsTurn();
        UnaffordableTechniqueKeepsMenuOpen();
        DeterministicBattleReachesVictory();
        DigivolveSpendsTurnAndChangesForm();
        DigivolveBarredByNoDigivolveKeepsMenuOpen();
        SwitchChangesActiveAndSpendsTurn();
        ItemConsumesStockAndHeals();
        ItemMissingFromStockKeepsMenuOpen();
        MenuNavigationClampsAndCancels();
        SourceSubmenuPaginationUsesL1R1Pages();
        SubmenuLineCarriesSourcePresentationFields();
        RootMenuGreysDigivolveWhenNoDigivolve();
        EnemyUsesTechniqueFromTable();
        EnemyCounterDealsDamage();
        EnemySwitchChangesActiveEnemy();
        EnemyRunAwayEndsFled();
        SourceMessagesKeepTheirTwoLines();
        FlaggedTechniqueUsesSkillIdForDataAndText();
    }

    private static void StartBeginsOnPlayerCommand()
    {
        var flow = Controller(SimpleBattle());

        Check(flow.Phase == BattleFlowPhase.PlayerCommand, "a battle starts on the player's command phase");
        Check(flow.Outcome == BattleOutcome.None, "a fresh battle is unresolved");
    }

    private static void AttackResolvesAndAdvancesPhase()
    {
        var flow = Controller(SimpleBattle(playerAttack: 50, enemyRawDefense: 10, enemyHp: 500, enemySpeed: 1));

        var before = flow.State.Enemies[0]!.Hp;
        Check(flow.Choose(BattleCommand.Attack, 0, new NoRng()), "a basic attack is spendable");
        Check(flow.State.Enemies[0]!.Hp < before, "the attack dealt damage");
        Check(flow.MessageText.Tokens.Count > 0 && flow.MessageText.Substitutions.Count == 1, "the attack uses a sourced actor message");
    }

    private static void EnemyAttackReturnsToPlayer()
    {
        // A very fast enemy acts first: after the player's turn the enemy attacks back,
        // and since the enemy stays faster the phase becomes EnemyTurn, not PlayerCommand.
        var flow = Controller(SimpleBattle(playerSpeed: 1, enemySpeed: 200, enemyRawAttack: 10, playerHp: 10000, enemyHp: 10000));

        flow.Choose(BattleCommand.Attack, 0, new NoRng());
        Check(flow.Phase is BattleFlowPhase.EnemyTurn or BattleFlowPhase.PlayerCommand, "phase advanced after the player's turn");
        var hpBefore = flow.State.Players[0]!.Hp;
        while (flow.Phase == BattleFlowPhase.EnemyTurn)
            flow.EnemyAct(new NoRng());
        Check(flow.State.Players[0]!.Hp <= hpBefore, "the enemy attack resolved against the player");
        Check(flow.Phase == BattleFlowPhase.PlayerCommand, "control returns to the player once the enemy is done");
    }

    private static void SourceMessagesKeepTheirTwoLines()
    {
        var text = TestText();
        var actor = text.DigimonByNameId(1).Tokens;
        var technique = text.Technique(777).Tokens;
        var attack = text.Message("basicAttack", actor);
        var skill = text.Message("technique", actor, technique);
        var won = text.Message("won");
        Check(attack.Second is not null && attack.Type == 2 && attack.Second.Debug.Contains("entry=8"), "basic attack is source type 2 with action on line 2");
        Check(skill.Second is not null && skill.Type == 3 && skill.Second.Debug.Contains("entry=9"), "technique is source type 3 with skill on line 2");
        Check(won.Second is null && won.Type == 1 && won.Debug.Contains("entry=82"), "victory is source type 1 MFIGHT 0x52");
    }

    // game_state.h: SKILL_ID=0x1FFF. Upper bits are PartnerEntry skill flags,
    // never TECHS/SKLNAM indices. This is the active-team menu/action seam.
    private static void FlaggedTechniqueUsesSkillIdForDataAndText()
    {
        var flagged = 0x6001; // SKILL_KNOWN | SKILL_MARKED | id 1
        var digimon = new DigimonData(383, [], [], [1, flagged], 0, [], [], [], 0, 0, 0, 0, 0, 0, 0, [], [], [], 1, 0, false);
        var fighters = new FighterData(new Dictionary<int, DigimonData> { [383] = digimon }, [digimon], []);
        var techs = new TechniqueData(new Dictionary<int, Dw3Production.Conversion.TechData>
        {
            [1] = new(1, 9, 42, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1),
        });
        var listed = BattleSetupFactory.PlayerTechniques(fighters, techs, 383, TestText());
        Check(BattleSetupFactory.SkillId(flagged) == 1, "SKILL_ID strips PartnerEntry flag bits");
        Check(listed.Count == 1 && listed[0].Id == 1 && listed[0].Tech.Power == 42,
            "flagged PartnerEntry skill resolves TECHS by SKILL_ID");
        Check(listed[0].Text?.Debug == "SKLNAM id=1", "flagged PartnerEntry skill resolves SKLNAM by SKILL_ID");
    }

    private static void VictoryWhenAllEnemiesKo()
    {
        var flow = Controller(SimpleBattle(playerAttack: 50, enemyRawDefense: 10, enemyHp: 50));

        flow.Choose(BattleCommand.Attack, 0, new NoRng());
        Check(flow.Outcome == BattleOutcome.Won, "knocking out every enemy wins the battle");
        Check(flow.Phase == BattleFlowPhase.Ended, "a won battle reaches the Ended phase");
        Check(flow.MessageText.Tokens.Count > 0, "victory retains the last sourced action message");
    }

    private static void DefeatWhenAllPlayersKo()
    {
        var flow = Controller(SimpleBattle(playerDefense: 10, playerHp: 50, enemyRawAttack: 100, enemyStrength: 16, enemySpeed: 200));
        // The menu always opens first: the player attacks once, then the faster enemy
        // counter-attacks for 100 damage and knocks the lone partner out.
        flow.Choose(BattleCommand.Attack, 0, new NoRng());
        while (flow.Phase == BattleFlowPhase.EnemyTurn)
            flow.EnemyAct(new NoRng());
        Check(flow.Outcome == BattleOutcome.Lost, "knocking out every partner loses the battle");
        Check(flow.Phase == BattleFlowPhase.Ended, "a lost battle reaches the Ended phase");
    }

    private static void FleeEndsAsFled()
    {
        var flow = Controller(SimpleBattle());

        Check(flow.Choose(BattleCommand.Flee, 0, new NoRng()), "fleeing is spendable when unblocked");
        Check(flow.Outcome == BattleOutcome.Fled, "fleeing ends the battle as fled");
    }

    private static void TechniqueConsumesMpAndSpendsTurn()
    {
        var flow = Controller(SimpleBattle(playerAttack: 50, playerMp: 30, enemyRawDefense: 10, enemyHp: 500));
        flow.PlayerBasicTech = new TechData { Power = 20, Mp = 0 };
        flow.PlayerTechniques = [new BattleTechnique(9, "test", new TechData { Power = 20, Mp = 10 })];

        Check(flow.Choose(BattleCommand.Technique, 9, new NoRng()), "a technique the partner can afford is spendable");
        Check(flow.State.Players[0]!.Mp == 20, "the technique subtracted its MP cost");
    }

    private static void UnaffordableTechniqueKeepsMenuOpen()
    {
        var flow = Controller(SimpleBattle(playerMp: 5, enemyHp: 500));
        flow.PlayerTechniques = [new BattleTechnique(9, "test", new TechData { Power = 20, Mp = 10 })];

        Check(!flow.Choose(BattleCommand.Technique, 9, new NoRng()), "an unaffordable technique is refused");
        Check(flow.Phase == BattleFlowPhase.PlayerCommand, "the menu stays open after a refused technique");
        Check(flow.State.Players[0]!.Mp == 5, "nothing was spent on the refused technique");
    }

    private static void DeterministicBattleReachesVictory()
    {
        // A full loop: the player keeps attacking (and the enemy counter-attacks on its
        // turns) until the battle reaches victory, under a deterministic zero RNG.
        var flow = Controller(SimpleBattle(playerAttack: 50, playerDefense: 50, playerHp: 1000,
            enemyRawAttack: 10, enemyRawDefense: 10, enemySpeed: 100, enemyHp: 200));

        var guard = 0;
        while (flow.Phase != BattleFlowPhase.Ended && guard++ < 64)
        {
            if (flow.Phase == BattleFlowPhase.PlayerCommand) flow.Choose(BattleCommand.Attack, 0, new NoRng());
            else flow.EnemyAct(new NoRng());
        }
        Check(flow.Outcome == BattleOutcome.Won, "a deterministic attack loop reaches victory");
        Check(flow.Phase == BattleFlowPhase.Ended, "the loop terminates on the Ended phase");
    }

    private static void DigivolveSpendsTurnAndChangesForm()
    {
        var flow = Controller(SimpleBattle());
        flow.PlayerDigivolutions = [555];

        Check(flow.Choose(BattleCommand.Digivolve, 555, new NoRng()), "digivolving into another form is spendable");
        Check(flow.State.Players[0]!.Id == 555, "the active partner changed form");
    }

    private static void DigivolveBarredByNoDigivolveKeepsMenuOpen()
    {
        var flow = Controller(SimpleBattle());
        flow.PlayerDigivolutions = [555];
        flow.State.Players[0]!.Flags |= FighterFlags.NoDigivolve;

        Check(!flow.Choose(BattleCommand.Digivolve, 555, new NoRng()), "digivolving is refused when barred");
        Check(flow.Phase == BattleFlowPhase.PlayerCommand, "the menu stays open after a barred digivolution");
    }

    private static void SwitchChangesActiveAndSpendsTurn()
    {
        var flow = Controller(TwoPartnerBattle());

        Check(flow.Choose(BattleCommand.Switch, 1, new NoRng()), "switching to a living partner is spendable");
        Check(flow.State.ActivePlayer == 1, "the active partner changed to the picked slot");
    }

    private static void ItemConsumesStockAndHeals()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 50, MaxHp = 100, Mp = 10, MaxMp = 10, Stats = Stats() };
        setup.Enemies[0] = new BattleEnemy { Fighter = 101, Hp = 500, Mp = 0, Strength = 16, BaseStats = Stats() };
        var state = BattleState.InitFighters(setup, digimon: 999);
        var flow = Controller(state);
        flow.Items = new Dictionary<int, BattleItem> { [43] = new BattleItem(43, "heal", "partner", 500) };
        flow.ItemStock = new Dictionary<int, int> { [43] = 3 };

        Check(flow.Choose(BattleCommand.Item, 43, new NoRng()), "using a stocked item is spendable");
        Check(flow.State.Players[0]!.Hp == 100, "the heal applied (50 + 500 capped at 100)");
        Check(flow.ItemStock[43] == 2, "the item was consumed from the bag");
    }

    private static void ItemMissingFromStockKeepsMenuOpen()
    {
        var flow = Controller(SimpleBattle());
        flow.Items = new Dictionary<int, BattleItem> { [43] = new BattleItem(43, "heal", "partner", 500) };
        flow.ItemStock = [];

        Check(!flow.Choose(BattleCommand.Item, 43, new NoRng()), "an item with no stock is refused");
        Check(flow.Phase == BattleFlowPhase.PlayerCommand, "the menu stays open after a refused item");
    }

    private static void MenuNavigationClampsAndCancels()
    {
        var menu = new BattleMenuController();
        var root = new BattleMenuLine[]
        {
            new("Attaquer", true), new("Technique", true), new("Fuir", true),
        };

        menu.OpenRoot(root);
        Check(menu.Menu == BattleMenuKind.Root && menu.Cursor == 0, "the menu opens on the root with the cursor on the first line");
        Check(!menu.MoveUp(), "up on the first line is a no-op (the cursor clamps at the top)");
        Check(menu.Cursor == 0, "the cursor stays at the top");
        Check(menu.MoveDown() && menu.Cursor == 1, "down moves to the second line");
        Check(menu.MoveDown() && menu.Cursor == 2, "down moves to the third line");
        Check(!menu.MoveDown(), "down on the last line is a no-op (the cursor clamps at the bottom)");
        Check(menu.Cursor == 2, "the cursor stays at the bottom");

        var submenu = new BattleMenuLine[] { new("Tech A", true), new("Tech B", false) };
        menu.Open(BattleMenuKind.Technique, submenu);
        Check(menu.Menu == BattleMenuKind.Technique && menu.Cursor == 0, "opening a submenu switches kind and resets the cursor");
        Check(menu.CanConfirm, "the first submenu line is confirmable");
        Check(!menu.View.Lines[1].Enabled, "the view carries the greyed second line");

        menu.OpenRoot(root);
        Check(menu.Menu == BattleMenuKind.Root && menu.Cursor == 0, "cancelling returns to the root menu on the first line");
    }

    private static void SubmenuLineCarriesSourcePresentationFields()
    {
        var line = new BattleMenuLine("Test", true, null, Icon: 0x39, Cost: 12, Quantity: 3, Description: "source description");
        Check(line.Icon == 0x39 && line.Cost == 12 && line.Quantity == 3 && line.Description == "source description",
            "a submenu line carries source icon, cost, quantity and description");
    }

    private static void SourceSubmenuPaginationUsesL1R1Pages()
    {
        var lines = Enumerable.Range(0, 29).Select(index => new BattleMenuLine($"Item {index}", true)).ToArray();
        var menu = new BattleMenuController();
        menu.Open(BattleMenuKind.Item, lines);
        Check(menu.PageSize == 7 && menu.PageCount == 5 && menu.View.Lines.Count == 7,
            "item_menu.c exposes seven rows and five pages for 29 items");
        for (var i = 0; i < 10; i++) menu.MoveDown();
        Check(menu.Cursor == 6 && menu.First == 0, "Down clamps at item page row six and does not invent scrolling");
        Check(menu.NextPage() && menu.First == 7 && menu.Cursor == 0 && menu.SelectedIndex == 7,
            "PAD_R1 advances one item page and resets the source cursor to row zero");
        Check(menu.View.Lines.Count == 7 && menu.View.Lines[0].Label == "Item 7", "the view renders the new first source item");
        while (menu.NextPage()) { }
        Check(menu.First == 28 && menu.View.Lines.Count == 1 && !menu.NextPage(), "the final item page has only its populated row");
        Check(menu.PreviousPage() && menu.First == 21 && menu.Cursor == 0, "PAD_L1 returns one page and resets cursor");

        menu.Open(BattleMenuKind.Technique, lines);
        Check(menu.PageSize == 6 && menu.PageCount == 5 && menu.View.Lines.Count == 6,
            "tech_menu.c exposes six rows per page");
        Check(menu.NextPage() && menu.First == 6 && menu.Cursor == 0, "technique PAD_R1 uses a six-row source page");
    }

    private static void RootMenuGreysDigivolveWhenNoDigivolve()
    {
        var flow = Controller(SimpleBattle());
        var lines = flow.RootMenuLines();
        Check(lines.Count == 6, "the root menu lists the six commands");
        Check(lines[(int)BattleCommand.Digivolve].Enabled, "digivolve is enabled by default");

        flow.State.Players[0]!.Flags |= FighterFlags.NoDigivolve;
        lines = flow.RootMenuLines();
        Check(!lines[(int)BattleCommand.Digivolve].Enabled, "FIGHTER_NO_DIGIVOLVE greys the Digivolve line");
        Check(lines[(int)BattleCommand.Attack].Enabled, "attack stays enabled");
    }

    // ---- fixtures ----

    private static void EnemyUsesTechniqueFromTable()
    {
        var flow = Controller(SimpleBattle(enemyRawAttack: 100, playerDefense: 10, playerHp: 1000, enemyMp: 100));
        var enemy = flow.State.Enemies[0]!;
        enemy.Techs = [777, 888, 0];
        enemy.Actions = [new BattleTableAction(2, 0, 0)];   // target 2 = technique 1, always
        flow.State.Techs = new Dictionary<int, TechData>
        {
            [777] = new TechData { Power = 10, Mp = 0 },
            [888] = new TechData { Power = 30, Mp = 20, Icon = BattleConst.TechPhysical },
        };
        flow.Choose(BattleCommand.Attack, 0, new NoRng());
        var mpBefore = enemy.Mp;
        flow.EnemyAct(new NoRng());
        Check(enemy.Mp == mpBefore - 20, "the enemy's chosen technique spends its MP");
        Check(flow.Phase == BattleFlowPhase.PlayerCommand && !flow.MessageVisible && flow.MessageText == BattleTextLine.Missing,
            "the enemy action message is destroyed before PlayerCommand opens the menu");
    }

    private static void EnemyCounterDealsDamage()
    {
        var flow = Controller(SimpleBattle(playerAttack: 50, enemyRawDefense: 10, enemyHp: 1000,
            playerHp: 1000, enemyRawAttack: 100, playerDefense: 10));
        var enemy = flow.State.Enemies[0]!;
        enemy.Techs = [777, 0, 0];
        enemy.Counter = new BattleTableAction(1, 3, 0);   // counter with the basic attack, hp >= 0 (always)
        flow.State.Techs = new Dictionary<int, TechData>
        {
            [777] = new TechData { Power = 10, Mp = 0, EffectPower = 64 },   // counter = 64/64 of the received damage
        };
        var hpBefore = flow.State.Players[0]!.Hp;
        flow.Choose(BattleCommand.Attack, 0, new NoRng());
        Check(flow.State.Players[0]!.Hp < hpBefore, "the enemy's counter-attack dealt damage inside the player's hit");
    }

    private static void EnemySwitchChangesActiveEnemy()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 1000, MaxHp = 1000, Mp = 100, MaxMp = 100, Stats = Stats(attack: 100, defense: 100) };
        setup.Enemies[0] = new BattleEnemy
        {
            Fighter = 101,
            Hp = 1000,
            Mp = 100,
            Strength = 16,
            BaseStats = Stats(attack: 100, defense: 100),
            Techs = [777, 0, 0],
            Actions = [new BattleTableAction(6, 0, 0)],   // switch to slot 1
        };
        setup.Enemies[1] = new BattleEnemy { Fighter = 102, Hp = 1000, Mp = 100, Strength = 16, BaseStats = Stats(attack: 100, defense: 100) };
        var flow = Controller(BattleState.InitFighters(setup, digimon: 999));
        flow.Choose(BattleCommand.Attack, 0, new NoRng());
        if (flow.Phase == BattleFlowPhase.EnemyTurn) flow.EnemyAct(new NoRng());
        Check(flow.State.ActiveEnemy == 1, "the enemy's switch action brings in the other enemy");
    }

    private static void EnemyRunAwayEndsFled()
    {
        var flow = Controller(SimpleBattle(enemyHp: 1000, playerHp: 1000));
        var enemy = flow.State.Enemies[0]!;
        enemy.Techs = [777, 0, 0];
        enemy.Actions = [new BattleTableAction(4, 0, 0)];   // run away, always
        flow.Choose(BattleCommand.Attack, 0, new NoRng());
        if (flow.Phase == BattleFlowPhase.EnemyTurn) flow.EnemyAct(new NoRng());
        Check(flow.Outcome == BattleOutcome.Fled, "the enemy's run-away action ends the battle as fled");
    }

    private static BattleState SimpleBattle(
        int playerAttack = 100, int playerDefense = 100, int playerSpeed = 100,
        int playerHp = 500, int playerMp = 100,
        int enemyRawAttack = 100, int enemyRawDefense = 100, int enemySpeed = 100,
        int enemyHp = 500, int enemyMp = 100, int enemyStrength = 16)
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed
        {
            Id = 1,
            Hp = playerHp,
            MaxHp = playerHp,
            Mp = playerMp,
            MaxMp = playerMp,
            Stats = Stats(playerAttack, playerDefense, speed: playerSpeed),
        };
        setup.Enemies[0] = new BattleEnemy
        {
            Fighter = 101,
            Hp = enemyHp,
            Mp = enemyMp,
            Strength = enemyStrength,
            BaseStats = Stats(enemyRawAttack, enemyRawDefense, speed: enemySpeed),
            Techs = [777, 0, 0],
            Actions = [new BattleTableAction(1, 0, 0)],   // one unconditional fallback: attack
        };
        return BattleState.InitFighters(setup, digimon: 999);
    }

    private static BattleState TwoPartnerBattle()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 10, MaxMp = 10, Stats = Stats(defense: 100) };
        setup.Partners[1] = new PartnerSeed { Id = 2, Hp = 100, MaxHp = 100, Mp = 10, MaxMp = 10, Stats = Stats(defense: 100) };
        setup.Enemies[0] = new BattleEnemy { Fighter = 101, Hp = 500, Mp = 0, Strength = 16, BaseStats = Stats() };
        return BattleState.InitFighters(setup, digimon: 999);
    }

    private static BattleFlowController Controller(BattleState state)
    {
        var flow = new BattleFlowController
        {
            PlayerBasicTech = new TechData { Power = 20, Mp = 0 },
            EnemyBasicTech = new TechData { Power = 10, Mp = 0 },
            Techs = new Dictionary<int, TechData> { [777] = new TechData { Power = 10, Mp = 0 } },
            NameIds = new Dictionary<int, int> { [1] = 1, [2] = 2, [101] = 101 },
            Text = TestText(),
        };
        flow.Start(state);
        return flow;
    }

    private static BattleTextCatalog TestText()
    {
        var token = (IReadOnlyList<TextToken>)[new TextToken("g", 14, 0, [])];
        var table = Enumerable.Range(0, 500).Select(_ => token).ToArray();
        var data = new BattleTextData(table, table, table, table, table, table,
            [.. Enumerable.Range(0, 6).Select(i => new BattleTextTemplate(i + 1, "menu", [], "disc battle menu"))],
            new Dictionary<string, BattleTextTemplate>
            {
                ["actor"] = new(22, "message", ["actor"], "disc battle behavior"),
                ["basicAttack"] = new(8, "message", ["actor"], "disc battle behavior"),
                ["technique"] = new(9, "message", ["actor", "technique"], "disc battle behavior"),
                ["item"] = new(9, "message", ["actor", "item"], "disc battle behavior"),
                ["digivolve"] = new(55, "message", ["actor", "digimon"], "disc battle behavior"),
                ["noMp"] = new(37, "message", [], "disc battle behavior"),
                ["won"] = new(82, "message", [], "disc battle behavior"),
                ["lost"] = new(83, "message", [], "disc battle behavior"),
                ["switch"] = new(56, "message", ["actor"], "disc battle behavior"),
                ["fledPlayer"] = new(67, "message", ["actor"], "disc battle behavior"),
            }, new Dictionary<string, int> { ["expLabel"] = 1, ["expGained"] = 2, ["partnerLevel"] = 7, ["moneyGained"] = 10 });
        return new BattleTextCatalog(data);
    }

    private static BattleStats Stats(int attack = 0, int defense = 0, int spirit = 0, int wisdom = 0, int speed = 0, int family = 0)
    {
        var s = new BattleStats();
        s.Stats[BattleStat.Attack] = attack;
        s.Stats[BattleStat.Defense] = defense;
        s.Stats[BattleStat.Spirit] = spirit;
        s.Stats[BattleStat.Wisdom] = wisdom;
        s.Stats[BattleStat.Speed] = speed;
        s.Family = family;
        return s;
    }

    private sealed class NoRng : BattleRng
    {
        public int Next() => 0;
    }

    private static void Check(bool condition, string name)
    {
        Assert.True(condition, name);
    }
}
