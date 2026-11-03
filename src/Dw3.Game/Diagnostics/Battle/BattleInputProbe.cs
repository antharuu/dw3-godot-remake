using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Drives native battle input through every sourced command submenu and switch path.</summary>
internal sealed class BattleInputProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattleInputProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleInputProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_INPUT_PROBE";

    /// <summary>
    /// Runs the input probe: opens every battle submenu in sequence, injects directional keys
    /// and confirm, and verifies that each page transitions correctly.
    /// </summary>
    /// <returns>A task that completes when the probe assertions pass or throws on failure.</returns>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory("probe/battle-input");
        Host.ApplyS200();
        Host.StartDebugBattle();
        ProbeRequirement.Require(Host.HasActiveBattle, "F8 did not start a battle");
        var inputFlow = Host.Flow;
        var fighterData = Host.FighterData ?? throw new InvalidOperationException("fighter data unavailable");
        RequireSourcedNames(inputFlow, fighterData);
        ProbeRequirement.Require(Host.Menu.Menu == BattleMenuKind.Root, $"F8 menu is not root menu={Host.Menu.Menu}");
        var before = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "switch-before.png");
        await VerifyTechniqueMenuAsync(captureRoot, inputFlow);
        RequireTeamTechniques(fighterData);
        InjectBattleKey(Key.X);
        ProbeRequirement.Require(Host.Menu.Menu == BattleMenuKind.Root, "Triangle from technique did not return to root");
        await VerifyDigivolveMenuAsync(captureRoot);
        await VerifyItemMenuAsync(captureRoot);
        await VerifyMessageArrowAsync(captureRoot);
        await VerifySwitchPagesAsync(captureRoot);
        await VerifySwitchSpendsTurnAsync(captureRoot, before);
    }

    /// <summary>Checks that every submenu line has decoded source text.</summary>
    /// <param name="menu">Menu to inspect.</param>
    /// <param name="surface">Stable menu name for failures.</param>
    private static void RequireSubmenuText(BattleMenuView menu, string surface)
    {
        foreach (var (line, index) in menu.Lines.Select((line, index) => (line, index)))
        {
            ProbeRequirement.Require(
                line.Text is { } text && text != BattleTextLine.Missing && text.Tokens.Count > 1,
                $"{surface} line={index} is empty or a single glyph debug={line.Text?.Debug}");
        }
    }

    /// <summary>Checks that each submenu line has rendered text pixels.</summary>
    /// <param name="image">Captured menu image.</param>
    /// <param name="menu">Rendered menu data.</param>
    /// <param name="surface">Stable menu name for failures.</param>
    /// <param name="x">Source x coordinate.</param>
    /// <param name="y">Source y coordinate.</param>
    /// <param name="step">Source vertical line spacing.</param>
    private static void RequireRenderedSubmenuLines(Image image, BattleMenuView menu, string surface, int x, int y, int step)
    {
        foreach (var (_, index) in menu.Lines.Select((line, index) => (line, index)))
        {
            var (screenX, screenY) = BattleHudLayout.ToViewport(x, y + (index * step));
            ProbeRequirement.Require(
                BattlePixelMetrics.CountBright(image, new Rect2(screenX, screenY, 120 * BattleHudLayout.Scale, 12 * BattleHudLayout.Scale)) > 10,
                $"{surface} line={index} has no rendered text pixels");
        }
    }

    /// <summary>Checks that a named source frame has visible pixels.</summary>
    /// <param name="image">Captured menu image.</param>
    /// <param name="frame">Source frame label.</param>
    /// <param name="sourceX">Source x coordinate.</param>
    /// <param name="sourceY">Source y coordinate.</param>
    /// <param name="sourceWidth">Source width.</param>
    /// <param name="sourceHeight">Source height.</param>
    private static void RequireBattleFramePixels(Image image, string frame, int sourceX, int sourceY, int sourceWidth, int sourceHeight)
    {
        var (x, y) = BattleHudLayout.ToViewport(sourceX, sourceY);
        var pixels = BattlePixelMetrics.CountBright(image, new Rect2(x, y, sourceWidth * BattleHudLayout.Scale, sourceHeight * BattleHudLayout.Scale));
        ProbeRequirement.Require(pixels > 0, $"battle frame={frame} has no bright pixels at source=({sourceX},{sourceY},{sourceWidth},{sourceHeight}) pixels={pixels}");
    }

    /// <summary>
    /// Checks the opening state of the debug battle: the player command phase shows no action message, and
    /// both HUD names come from the digimon name table through their nameId (enemy fighter 51 and the
    /// active partner).
    /// </summary>
    /// <param name="inputFlow">The battle flow right after F8.</param>
    /// <param name="fighterData">The extracted fighter and battle tables.</param>
    /// <exception cref="InvalidOperationException">No partner is active, or an assertion fails.</exception>
    private void RequireSourcedNames(BattleFlowController inputFlow, FighterData fighterData)
    {
        ProbeRequirement.Require(
            inputFlow.Phase == BattleFlowPhase.PlayerCommand && !inputFlow.MessageVisible,
            "PlayerCommand retained an action message window");
        var enemyNameId = fighterData.BattleTable.Single(entry => entry.Id == 51).NameId;
        var activePlayer = inputFlow.State.ActivePlayerFighter ?? throw new InvalidOperationException("active player unavailable");
        var partnerNameId = fighterData.Digimon[activePlayer.Id].NameId;
        ProbeRequirement.Require(
            Host.NameLine(51).Tokens.SequenceEqual(Host.BattleText().DigimonByNameId(enemyNameId).Tokens)
            && Host.NameLine(activePlayer.Id).Tokens.SequenceEqual(Host.BattleText().DigimonByNameId(partnerNameId).Tokens),
            $"HUD names do not use nameId enemy=51:{enemyNameId} partner={activePlayer.Id}:{partnerNameId}");
        QuietLog.Info($"battle-input nameId PASS enemy=51:{enemyNameId} partner={activePlayer.Id}:{partnerNameId} messageVisible=false");
    }

    /// <summary>
    /// Opens the technique submenu (Down, Cross) and checks its line text, icon, cost, description,
    /// rendered frames 0x2A, 0x29 and 0x31, the stat window glyphs and the MFIGHT status labels.
    /// </summary>
    /// <param name="captureRoot">Directory receiving <c>submenu-tech.png</c>.</param>
    /// <param name="inputFlow">The battle flow, used to log the selected technique.</param>
    /// <returns>A task that completes once the submenu has been captured and checked.</returns>
    /// <exception cref="InvalidOperationException">The status text is missing, or an assertion fails.</exception>
    private async Task VerifyTechniqueMenuAsync(string captureRoot, BattleFlowController inputFlow)
    {
        InjectBattleKey(Key.S);
        InjectBattleKey(Key.Enter);
        ProbeRequirement.Require(
            Host.Menu.Menu == BattleMenuKind.Technique && Host.Menu.Cursor == 0 && Host.Menu.Lines.Count > 0,
            $"tech menu did not open at source line 0 menu={Host.Menu.Menu} lines={Host.Menu.Lines.Count}");
        RequireSubmenuText(Host.Menu.View, "technique");
        ProbeRequirement.Require(
            Host.Menu.View.Lines[0].Icon >= 0 && Host.Menu.View.Lines[0].Cost >= 0,
            "tech first line lacks source icon/cost metadata");
        ProbeRequirement.Require(
            Host.Menu.View.Lines[0].DescriptionText is { } techDescription && techDescription != BattleTextLine.Missing && techDescription.Tokens.Count > 0,
            "tech first line lacks SKLINF token description");
        var techMenu = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "submenu-tech.png");
        RequireRenderedSubmenuLines(techMenu, Host.Menu.View, "technique", 0x2A, 0x45, 0xE);
        RequireBattleFramePixels(techMenu, "0x2A", 8, 0x3E, 154, 122);
        RequireBattleFramePixels(techMenu, "0x29", 0xA3, 0x21, 96, 39);
        RequireBattleFramePixels(techMenu, "0x31", 0xB, 0xBC, 296, 38);
        var (techIconX, techIconY) = BattleHudLayout.ToViewport(0x1D, 0x45);
        ProbeRequirement.Require(
            BattlePixelMetrics.CountBright(techMenu, new Rect2(techIconX, techIconY, 12 * BattleHudLayout.Scale, 14 * BattleHudLayout.Scale)) > 0,
            "tech menu CMFONTCS icon rectangle has no nonzero pixels");
        foreach (var (x, y) in new[] { (0x1A, 0x9F), (0x80, 0x9F), (0xAC, 0x3A), (0xD8, 0x3A), (0xD9, 0x3A), (0xFB, 0x3A) })
        {
            var (screenX, screenY) = BattleHudLayout.ToViewport(x, y);
            ProbeRequirement.Require(
                BattlePixelMetrics.CountBright(techMenu, new Rect2(screenX, screenY, 28 * BattleHudLayout.Scale, 12 * BattleHudLayout.Scale)) > 0,
                $"tech source stat window lacks glyphs x=0x{x:X} y=0x{y:X}");
        }

        var techStatus = Host.Menu.View.StatusText ?? throw new InvalidOperationException("tech source status text unavailable");
        ProbeRequirement.Require(
            techStatus.Header.Debug == "MFIGHT entry=13" && techStatus.Separator?.Debug == "MFIGHT entry=16"
            && Host.Menu.View.PreviousPageText?.Debug == "MFIGHT entry=17" && Host.Menu.View.NextPageText?.Debug == "MFIGHT entry=18"
            && Host.Menu.View.Lines[0].CostLabelText?.Debug == "MFIGHT entry=13",
            "tech status labels use MFIGHT 0x11/0x12/0xD/0x10");
        var selectedTech = inputFlow.PlayerTechniques[Host.Menu.SelectedIndex];
        QuietLog.Info($"battle-tech-menu tech={selectedTech.Id} name={selectedTech.Text?.Debug} cost={selectedTech.Tech.Mp} power={selectedTech.Tech.Power}");
    }

    /// <summary>
    /// Checks the technique list of each team digimon (383, 373, 31): it equals the SKILL_ID of every
    /// non-empty skill slot after the first, and each name is a sourced SKLNAM line of more than one glyph.
    /// </summary>
    /// <param name="fighterData">The extracted fighter and battle tables.</param>
    /// <exception cref="InvalidOperationException">An assertion fails.</exception>
    private void RequireTeamTechniques(FighterData fighterData)
    {
        foreach (var digimonId in new[] { 383, 373, 31 })
        {
            var decoded = fighterData.Digimon[digimonId];
            var listed = BattleSetupFactory.PlayerTechniques(fighterData, Host.TechData!, digimonId, Host.BattleText());
            var expected = decoded.Skills.Skip(1).Where(raw => (raw & BattleSetupFactory.SkillIdMask) != 0)
                .Select(BattleSetupFactory.SkillId).ToArray();
            ProbeRequirement.Require(
                listed.Select(tech => tech.Id).SequenceEqual(expected),
                $"team {digimonId} techniques use SKILL_ID expected=[{string.Join(',', expected)}]");
            foreach (var technique in listed)
            {
                ProbeRequirement.Require(
                    technique.Text is { } name && name != BattleTextLine.Missing && name.Tokens.Count > 1,
                    $"team {digimonId} technique {technique.Id} has sourced non-single-glyph SKLNAM");
                QuietLog.Info($"battle-tech-menu tech={technique.Id} name={technique.Text!.Debug} cost={technique.Tech.Mp} power={technique.Tech.Power} team={digimonId}");
            }
        }
    }

    /// <summary>
    /// Opens the digivolution submenu (two Down, Cross), checks its line text and frames 0x2B and 0x24,
    /// then closes it with Triangle.
    /// </summary>
    /// <param name="captureRoot">Directory receiving <c>submenu-digivolve.png</c>.</param>
    /// <returns>A task that completes once the submenu has been checked and closed.</returns>
    /// <exception cref="InvalidOperationException">An assertion fails.</exception>
    private async Task VerifyDigivolveMenuAsync(string captureRoot)
    {
        InjectBattleKey(Key.S, 2);
        InjectBattleKey(Key.Enter);
        ProbeRequirement.Require(
            Host.Menu.Menu == BattleMenuKind.Digivolve && Host.Menu.Cursor == 0 && Host.Menu.Lines.Count > 0,
            $"digivolve menu did not open at source line 0 menu={Host.Menu.Menu} lines={Host.Menu.Lines.Count}");
        RequireSubmenuText(Host.Menu.View, "digivolve");
        var digivolveMenu = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "submenu-digivolve.png");
        RequireRenderedSubmenuLines(digivolveMenu, Host.Menu.View, "digivolve", 0xBB, 0x92, 0x13);
        RequireBattleFramePixels(digivolveMenu, "0x2B", 0xA7, 0x8F, 129, 19);
        RequireBattleFramePixels(digivolveMenu, "0x24", 167, 146, 144, 19);
        InjectBattleKey(Key.X);
        ProbeRequirement.Require(Host.Menu.Menu == BattleMenuKind.Root, "Triangle from digivolve did not return to root");
    }

    /// <summary>
    /// Opens the item submenu (four Down, Cross), checks its labels, stock and description, then checks
    /// that ten Down presses clamp on the first page and PAD_R1 turns to the second page of seven items,
    /// and closes it with Triangle.
    /// </summary>
    /// <param name="captureRoot">Directory receiving <c>submenu-item.png</c> and <c>submenu-item-scrolled.png</c>.</param>
    /// <returns>A task that completes once the submenu has been checked and closed.</returns>
    /// <exception cref="InvalidOperationException">The status text is missing, or an assertion fails.</exception>
    private async Task VerifyItemMenuAsync(string captureRoot)
    {
        InjectBattleKey(Key.S, 4);
        InjectBattleKey(Key.Enter);
        ProbeRequirement.Require(
            Host.Menu.Menu == BattleMenuKind.Item && Host.Menu.Cursor == 0 && Host.Menu.Lines.Count > 0,
            $"item menu did not open at source line 0 menu={Host.Menu.Menu} lines={Host.Menu.Lines.Count}");
        RequireSubmenuText(Host.Menu.View, "item");
        var itemStatus = Host.Menu.View.StatusText ?? throw new InvalidOperationException("item source status text unavailable");
        ProbeRequirement.Require(itemStatus.Header.Debug == "MFIGHT entry=12", "item quantity label uses MFIGHT 0xC");
        ProbeRequirement.Require(Host.Menu.View.Lines[0].Quantity > 0, "item first line lacks a positive source stock quantity");
        ProbeRequirement.Require(
            Host.Menu.View.Lines[0].DescriptionText is { } itemDescription && itemDescription != BattleTextLine.Missing && itemDescription.Tokens.Count > 0,
            "item first line lacks ITMINF token description");
        var itemMenuInitial = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "submenu-item.png");
        RequireRenderedSubmenuLines(itemMenuInitial, Host.Menu.View, "item", 0x2A, 0x45, 0xE);
        RequireBattleFramePixels(itemMenuInitial, "0x27", 8, 0x3E, 154, 113);
        InjectBattleKey(Key.S, 10);
        ProbeRequirement.Require(
            Host.Menu.Cursor <= 6 && Host.Menu.First == 0,
            $"item Down x10 must clamp on source page cursor={Host.Menu.Cursor} first={Host.Menu.First}");
        InjectBattleKey(Key.Pagedown);
        ProbeRequirement.Require(
            Host.Menu.Cursor == 0 && Host.Menu.First == 7 && Host.Menu.View.Lines.Count == 7,
            $"item PAD_R1 page did not advance source first={Host.Menu.First} cursor={Host.Menu.Cursor}");
        QuietLog.Info($"battle-menu scroll first={Host.Menu.First} cursor={Host.Menu.Cursor} source=PAD_R1-page");
        var itemMenu = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "submenu-item-scrolled.png");
        RequireBattleFramePixels(itemMenu, "0x27", 8, 0x3E, 154, 113);
        RequireBattleFramePixels(itemMenu, "0x28", 0xA6, 0xA0, 44, 24);
        RequireBattleFramePixels(itemMenu, "0x31", 0xB, 0xBC, 296, 38);
        var (itemIconX, itemIconY) = BattleHudLayout.ToViewport(0x1D, 0x45);
        ProbeRequirement.Require(Host.Menu.View.Lines[0].Icon >= 0, "item page first line lacks source category frame metadata");
        ProbeRequirement.Require(
            BattlePixelMetrics.CountBright(itemMenu, new Rect2(itemIconX, itemIconY, 12 * BattleHudLayout.Scale, 14 * BattleHudLayout.Scale)) > 0,
            "item menu CMFONTCS icon rectangle has no nonzero pixels");
        InjectBattleKey(Key.X);
        ProbeRequirement.Require(Host.Menu.Menu == BattleMenuKind.Root, "Triangle from item did not return to root");
    }

    /// <summary>Renders the battle message wait view and checks that the arrow (frame 10) is drawn at source (0x123, 0xD0).</summary>
    /// <param name="captureRoot">Directory receiving <c>message-arrow.png</c>.</param>
    /// <returns>A task that completes once the arrow has been checked.</returns>
    /// <exception cref="InvalidOperationException">The wait view cannot be built, or the arrow rectangle is empty.</exception>
    private async Task VerifyMessageArrowAsync(string captureRoot)
    {
        // the battle-message layout contract draws frame 10 only while it waits for Cross. Render the same active
        // battle through that wait view and prove its source rectangle is populated.
        var waiting = (Host.BuildBattleView() ?? throw new InvalidOperationException("battle wait view unavailable"))
            with
        { Message = "MESSAGE_WAIT_PROBE", MessageText = BattleTextLine.Missing, MessageVisible = true };
        Host.Renderer.Present(waiting);
        var messageArrow = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "message-arrow.png");
        var (arrowX, arrowY) = BattleHudLayout.ToViewport(0x123, 0xD0);
        ProbeRequirement.Require(
            BattlePixelMetrics.CountBright(messageArrow, new Rect2(arrowX, arrowY, 16 * BattleHudLayout.Scale, 16 * BattleHudLayout.Scale)) > 0,
            "message wait arrow rectangle has no nonzero pixels");
    }

    /// <summary>
    /// Walks the switch pages: partner list (frame 0x2F, two lines), form page, pair choice, then unwinds
    /// each page with Triangle back to the command root.
    /// </summary>
    /// <param name="captureRoot">Directory receiving the switch page captures.</param>
    /// <returns>A task that completes once every page has been checked.</returns>
    /// <exception cref="InvalidOperationException">An assertion fails.</exception>
    private async Task VerifySwitchPagesAsync(string captureRoot)
    {
        // Bas x3 reaches the source root command index 3 (Changement), then Cross opens it.
        InjectBattleKey(Key.S, 3);
        ProbeRequirement.Require(Host.Menu.Cursor == 3, $"root switch cursor={Host.Menu.Cursor}, expected=3");
        InjectBattleKey(Key.Enter);
        ProbeRequirement.Require(
            Host.Menu.Menu == BattleMenuKind.Switch && Host.Menu.Lines.Count == 2,
            $"battle-menu open=Switch lines={Host.Menu.Lines.Count} menu={Host.Menu.Menu}");
        var switchList = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "switch-list.png");
        RequireBattleFramePixels(switchList, "0x2F-line-0", 16, 77, 186, 31);
        RequireBattleFramePixels(switchList, "0x2F-line-1", 16, 109, 186, 31);

        // switch_menu -> switch_in_menu form list -> Changement/Technique de paire.
        InjectBattleKey(Key.Enter);
        ProbeRequirement.Require(Host.Menu.Menu == BattleMenuKind.SwitchIn, $"switch partner did not open form page menu={Host.Menu.Menu}");
        var switchIn = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "switch-in.png");
        RequireBattleFramePixels(switchIn, "0x2B", 0xA7, 0x8F, 129, 19);
        RequireBattleFramePixels(switchIn, "0x24-line-0", 167, 146, 144, 19);
        InjectBattleKey(Key.Enter);
        ProbeRequirement.Require(Host.Menu.Menu == BattleMenuKind.PairSwitch, $"form did not open pair choice menu={Host.Menu.Menu}");
        var switchChoice = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "switch-choice.png");
        RequireBattleFramePixels(switchChoice, "0x2B", 0xA7, 0x8F, 129, 19);
        RequireBattleFramePixels(switchChoice, "0x22-line-0", 167, 165, 16, 16);

        // Triangle unwinds one source page at a time, then returns to the command root.
        InjectBattleKey(Key.X);
        ProbeRequirement.Require(Host.Menu.Menu == BattleMenuKind.SwitchIn, "Triangle from pair choice did not return to form page");
        InjectBattleKey(Key.X);
        ProbeRequirement.Require(Host.Menu.Menu == BattleMenuKind.Switch, "Triangle from form page did not return to partner list");
        InjectBattleKey(Key.X);
        ProbeRequirement.Require(Host.Menu.Menu == BattleMenuKind.Root, "Triangle from partner list did not return to root");
    }

    /// <summary>
    /// Confirms a switch to Renamon (fighter 373) through the three switch pages and checks that it spends
    /// the turn, renders the new partner and changes more than 500 pixels of the left half of the screen.
    /// </summary>
    /// <param name="captureRoot">Directory receiving <c>switch-after-renamon.png</c>.</param>
    /// <param name="before">The capture taken at the command root before any menu was opened.</param>
    /// <returns>A task that completes once the switch has been checked.</returns>
    /// <exception cref="InvalidOperationException">An assertion fails.</exception>
    private async Task VerifySwitchSpendsTurnAsync(string captureRoot, Image before)
    {
        InjectBattleKey(Key.S, 3);
        InjectBattleKey(Key.Enter);
        InjectBattleKey(Key.Enter);
        InjectBattleKey(Key.Enter);
        Host.Input.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = true });
        Host.TickTestSimulation();
        var switchedFlow = Host.Flow;
        ProbeRequirement.Require(switchedFlow.Phase == BattleFlowPhase.EnemyTurn, $"switch did not spend turn phase={switchedFlow.Phase}");
        Host.Input.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = false });
        Host.TickTestSimulation();
        ProbeRequirement.Require(
            switchedFlow.State.ActivePlayerFighter?.Id == 373 && Host.RenderedPlayerId == 373,
            $"switch did not render Renamon active={switchedFlow.State.ActivePlayerFighter?.Id} rendered={Host.RenderedPlayerId}");
        var after = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "switch-after-renamon.png");
        var changed = BattlePixelMetrics.CountRgbChanges(before, after, new Rect2(0, 0, before.GetWidth() / 2, before.GetHeight()));
        ProbeRequirement.Require(changed > 500, $"switch capture did not change enough pixels={changed}");
        QuietLog.Info($"battle-input probe PASS battle-menu open=Switch lines=2 active=373 rendered=373 pixels={changed} captures={captureRoot}");
    }
}
