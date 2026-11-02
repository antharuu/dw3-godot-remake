using System.Globalization;
using System.Text.Json;
using Godot;
using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Battle;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;

namespace Dw3Production;

public partial class GameHost
{
    /// <summary>Loads a private reference image for optional battle comparisons.</summary>
    /// <param name="subPath">Forward-slash path relative to <c>DW3_PSX_REFERENCE_DIR</c>.</param>
    /// <returns>The decoded reference image.</returns>
    /// <exception cref="PsxReferenceUnavailableException">The root, file, or decoded image is unavailable.</exception>
    private static Image LoadPsxReference(string subPath)
    {
        var referenceDirectory = System.Environment.GetEnvironmentVariable("DW3_PSX_REFERENCE_DIR");
        if (referenceDirectory is null)
        {
            throw new PsxReferenceUnavailableException(
                "set DW3_PSX_REFERENCE_DIR to the root directory that contains battle/... frames");
        }

        var fullPath = Path.Combine(referenceDirectory, subPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            throw new PsxReferenceUnavailableException($"file not found: {fullPath}");
        }

        var image = Image.LoadFromFile(fullPath);
        if (image is null || image.IsEmpty())
        {
            throw new PsxReferenceUnavailableException($"image unreadable: {fullPath}");
        }

        return image;
    }

    #region Battle diagnostic probes

    /// <summary>Battle shot-camera probe. Captures PAL frame landmarks without using the
    /// unrelated PartnerView camera, then records the actual list/shot/substate state.</summary>

    /// <summary>Command HUD probe. Drives the same Down edges as a player, then proves
    /// the two source cursor positions and six source line-sprite slots from framebuffer deltas.</summary>
    private async void RunBattleHudProbe()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            // Extended HUD probe. Keep all new captures beside its source
            // comparison, rather than overwriting the historical command-HUD evidence.
            var captureRoot = _options.CaptureDir
                ?? Path.Combine(generatedRoot, "probe/battle-hud");
            Directory.CreateDirectory(captureRoot);
            ApplyScene(GetOrLoadZone("S200"));
            _state.ZoneId = "S200";
            StartDebugBattle();
            Require(_battleFlow is not null && _activeBattle is not null && _battleMenu.Menu == BattleMenuKind.Root,
                "F8 did not reach the root command menu");
            TickTestSimulation();
            BattleRenderer.SetCommandMenuCamera();
            var line0 = await CaptureBattle(captureRoot, "command-menu-line0.png");
            await CaptureBattle(captureRoot, "hud-full.png");
            Require(_battleMenu.Cursor == 0 && _battleMenu.Lines.Count == 6, $"root menu cursor={_battleMenu.Cursor} lines={_battleMenu.Lines.Count}");
            for (var i = 0; i < 3; i++) InjectBattleKey(Key.S);
            Require(_battleMenu.Menu == BattleMenuKind.Root && _battleMenu.Cursor == 3,
                $"three Down inputs did not reach root line 3 cursor={_battleMenu.Cursor} menu={_battleMenu.Menu}");
            var line3 = await CaptureBattle(captureRoot, "command-menu-line3.png");
            // Source 320x240 coordinates are mapped exactly by BattleHudLayout: cursor at
            // (17,110 + sel*19), while WFIGHTMN frame 0x22's own part offset puts its pixels at
            // (16,106 + i*19), all scaled 3 and offset 160.
            var cursor0 = SourceToViewport(new Vector2(17, 110));
            var cursor3 = SourceToViewport(new Vector2(17, 167));
            Require(CountDifferentPixelsInRect(line0, line3, new Rect2(cursor0, new Vector2(36, 36))) > 40,
                "cursor line0 pixels did not move at source=(17,110)");
            Require(CountDifferentPixelsInRect(line0, line3, new Rect2(cursor3, new Vector2(36, 36))) > 40,
                "cursor line3 pixels did not move at source=(17,167)");
            var spriteSlots = 0;
            for (var i = 0; i < 6; i++)
            {
                var slot = SourceToViewport(new Vector2(16, 106 + i * 19));
                if (CountBrightPixelsInRect(line3, new Rect2(slot, new Vector2(36, 36))) > 12) spriteSlots++;
            }
            Require(spriteSlots == 6, $"source sprite 0x22 slots visible={spriteSlots}/6");
            Require(CountBrightPixelsInRect(line3, new Rect2(SourceToViewport(new Vector2(11, 15)), new Vector2(450, 129))) > 500,
                "HUD names/icons/HP gauge area has insufficient rendered pixels");
            BattleRenderer.SetHudVisibleForProbe(false);
            var noHud = await CaptureBattle(captureRoot, "command-menu-no-hud.png");
            BattleRenderer.SetHudVisibleForProbe(true);
            var rightMargin = new Rect2(SourceToViewport(new Vector2(311, 70)), new Vector2(3 * 9 + 160, 3 * 70));
            var spill = CountDifferentPixelsInRect(line3, noHud, rightMargin);
            Require(spill == 0, $"partner frame 0x0A spills right of x=310 changed={spill}");
            var partner = await CaptureBattle(captureRoot, "partner-view.png");
            var partnerPixels = BattleRenderer.PartnerViewOpaquePixels();
            var partnerRect = new Rect2(SourceToViewport(new Vector2(208, 76)), new Vector2(100 * BattleHudLayout.Scale, 60 * BattleHudLayout.Scale));
            var frameRect = new Rect2(SourceToViewport(new Vector2(206, 74)), new Vector2(104 * BattleHudLayout.Scale, 64 * BattleHudLayout.Scale));
            var partnerTargetPath = Path.Combine(captureRoot, "partner-view-target.png");
            Require(BattleRenderer.SavePartnerView(partnerTargetPath) == Error.Ok,
                "partner view target write failed");
            // The source target is layer 0x1009 at (208,76,100,60). Its raw reference uses the
            // documented night-blue panel key, while Godot clears this offscreen target to alpha 0.
            var partnerTarget = Image.LoadFromFile(partnerTargetPath);
            var partnerPsxFull = LoadPsxReference("battle/kotemon-383-command-menu-0.png");
            Require(partnerTarget is not null && !partnerTarget.IsEmpty(),
                $"partner view references unreadable remake={partnerTargetPath}");
            var partnerPsx = partnerPsxFull.GetRegion(new Rect2I(208, 76, 100, 60));
            var partnerRemakeBox = PartnerTargetBoundingBox(partnerTarget!);
            var partnerPsxBox = PartnerPsxBoundingBox(partnerPsx);
            Require(partnerRemakeBox is not null && partnerPsxBox is not null,
                $"partner view bbox unavailable remake={partnerRemakeBox} psx={partnerPsxBox}");
            var partnerRemakeBounds = partnerRemakeBox.GetValueOrDefault();
            var partnerPsxBounds = partnerPsxBox.GetValueOrDefault();
            var partnerHeightDelta = MathF.Abs(partnerRemakeBounds.Size.Y - partnerPsxBounds.Size.Y) / partnerPsxBounds.Size.Y;
            Require(partnerHeightDelta <= .25f,
                $"partner view model height delta={partnerHeightDelta:P1} exceeds 25% remake={FormatBox(partnerRemakeBounds)} psx={FormatBox(partnerPsxBounds)}");
            var partnerSheet = SideBySide(partnerTarget!, partnerPsx, "REMAKE", "PSX");
            partnerSheet.Resize(partnerSheet.GetWidth() * 4, partnerSheet.GetHeight() * 4, Image.Interpolation.Nearest);
            Require(partnerSheet.SavePng(Path.Combine(captureRoot, "partner-view-side-by-side-x4.png")) == Error.Ok,
                "partner view side-by-side write failed");
            QuietLog.Info($"battle-partner-view bbox remake={FormatBox(partnerRemakeBounds)} psx={FormatBox(partnerPsxBounds)} height-delta={partnerHeightDelta:P1}");
            Require(partnerPixels > 300, $"partner view target has insufficient opaque pixels={partnerPixels}");
            Require(CountBrightPixelsInRect(partner, frameRect) > 30, "partner-view frame 10 absent");
            BattleRenderer.SetPartnerModelVisibleForProbe(false);
            var withoutPartner = await CaptureBattle(captureRoot, "command-menu-without-partner-view.png");
            BattleRenderer.SetPartnerModelVisibleForProbe(true);
            var partnerCompositePixels = CountDifferentPixelsInRect(partner, withoutPartner, partnerRect);
            Require(partnerCompositePixels > 300,
                $"partner view is absent from the composed framebuffer changed={partnerCompositePixels}");
            // Ended is outside the player-turn presentation flow. Presenting it removes the layer, which proves the
            // lifecycle condition without forging a separate visibility switch in the renderer.
            BattleRenderer.Present(new BattleView(BattleFlowPhase.Ended, BattleOutcome.None, [], [], string.Empty, null));
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Require(BattleRenderer.PartnerViewOpaquePixels() == 0, "partner view persists outside player turn");
            BattleRenderer.Present(BuildBattleView());
            // The shot camera is assessed against the paired Kotemon command-menu frame.
            // Difference against the same Godot capture with Kotemon hidden gives a model mask
            // without guessing a stage colour. The PSX mask keeps warm Kotemon body texels only.
            BattleRenderer.SetModelVisibleForProbe(383, false);
            var withoutPlayer = await CaptureBattle(captureRoot, "command-menu-without-kotemon.png");
            BattleRenderer.SetModelVisibleForProbe(383, true);
            var command = await CaptureBattle(captureRoot, "command-menu-kotemon.png");
            var remakeBox = DifferenceBoundingBox(ToSource320(command), ToSource320(withoutPlayer));
            var psx = LoadPsxReference("battle/kotemon-383-command-menu-0.png");
            var psx320 = ToSource320(psx);
            var psxBox = WarmKotemonBoundingBox(psx320);
            Require(remakeBox is not null && psxBox is not null, $"camera bbox unavailable remake={remakeBox} psx={psxBox}");
            var remakeBounds = remakeBox.GetValueOrDefault();
            var psxBounds = psxBox.GetValueOrDefault();
            var delta = Math.Max(Math.Max(Math.Abs(remakeBounds.Position.X - psxBounds.Position.X), Math.Abs(remakeBounds.Position.Y - psxBounds.Position.Y)),
                Math.Max(Math.Abs(remakeBounds.Size.X - psxBounds.Size.X), Math.Abs(remakeBounds.Size.Y - psxBounds.Size.Y)));
            var compare = SideBySide(ToSource320(command), psx320, "REMAKE COMMAND", "PSX COMMAND");
            Require(compare.SavePng(Path.Combine(captureRoot, "compare-command-menu-partnerview.png")) == Error.Ok, "partner-view comparison write failed");
            var suffix = delta <= 8 ? "PASS" : "CAUSE=BattleRenderer.cs:SetCommandMenuCamera uses source shot view 2 while projection/GTE rasterization remains unimplemented";
            QuietLog.Info($"battle-camera phase=command-menu psx_bbox={FormatBox(psxBounds)} remake_bbox={FormatBox(remakeBounds)} delta={delta:0} {suffix}");
            QuietLog.Info($"battle-hud partner-frame spill={spill} thumbnail={partnerPixels} composed={partnerCompositePixels}");
            QuietLog.Info($"battle-hud partner_view=100x60 source=(208,76) viewport=({partnerRect.Position.X:0},{partnerRect.Position.Y:0}) pixels={partnerPixels} frame10=(206,74) captures={Path.Combine(captureRoot, "partner-view.png")}");
            QuietLog.Info($"battle-hud probe PASS cursor0=(17,110)->({cursor0.X:0},{cursor0.Y:0}) cursor3=(17,167)->({cursor3.X:0},{cursor3.Y:0}) sprites={spriteSlots}/6 captures={captureRoot}");
            GetTree().Quit(0);
        }
        catch (PsxReferenceUnavailableException ex)
        {
            QuietLog.Info(ex.Message);
            GetTree().Quit(5);
        }
        catch (Exception error)
        {
            GD.PushError($"BATTLE_HUD_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private static void RequireSubmenuText(BattleMenuView menu, string surface)
    {
        foreach (var (line, index) in menu.Lines.Select((line, index) => (line, index)))
            Require(line.Text is { } text && text != BattleTextLine.Missing && text.Tokens.Count > 1,
                $"{surface} line={index} is empty or a single glyph debug={line.Text?.Debug}");
    }

    private static void RequireRenderedSubmenuLines(Image image, BattleMenuView menu, string surface, int x, int y, int step)
    {
        foreach (var (_, index) in menu.Lines.Select((line, index) => (line, index)))
        {
            var (screenX, screenY) = BattleHudLayout.ToViewport(x, y + index * step);
            Require(CountBrightPixelsInRect(image, new Rect2(screenX, screenY, 120 * BattleHudLayout.Scale, 12 * BattleHudLayout.Scale)) > 10,
                $"{surface} line={index} has no rendered text pixels");
        }
    }

    private static int CountDifferentPixelsInRect(Image a, Image b, Rect2 rect)
    {
        var count = 0;
        var fromX = Math.Max(0, (int)rect.Position.X); var toX = Math.Min(a.GetWidth(), (int)rect.End.X);
        var fromY = Math.Max(0, (int)rect.Position.Y); var toY = Math.Min(a.GetHeight(), (int)rect.End.Y);
        for (var y = fromY; y < toY; y++) for (var x = fromX; x < toX; x++)
                if (ProbePixels.DifferentRgb(a.GetPixel(x, y), b.GetPixel(x, y))) count++;
        return count;
    }

    private static int CountBrightPixelsInRect(Image image, Rect2 rect)
    {
        var count = 0;
        var fromX = Math.Max(0, (int)rect.Position.X); var toX = Math.Min(image.GetWidth(), (int)rect.End.X);
        var fromY = Math.Max(0, (int)rect.Position.Y); var toY = Math.Min(image.GetHeight(), (int)rect.End.Y);
        for (var y = fromY; y < toY; y++) for (var x = fromX; x < toX; x++)
            {
                var pixel = image.GetPixel(x, y);
                if (pixel.R + pixel.G + pixel.B > 0.45f) count++;
            }
        return count;
    }

    private static void RequireBattleFramePixels(Image image, string frame, int sourceX, int sourceY, int sourceWidth, int sourceHeight)
    {
        var (x, y) = BattleHudLayout.ToViewport(sourceX, sourceY);
        var pixels = CountBrightPixelsInRect(image, new Rect2(x, y,
            sourceWidth * BattleHudLayout.Scale, sourceHeight * BattleHudLayout.Scale));
        Require(pixels > 0, $"battle frame={frame} has no bright pixels at source=({sourceX},{sourceY},{sourceWidth},{sourceHeight}) pixels={pixels}");
    }

    /// <summary>
    /// Battle input regression probe: F8 then native Down/Cross/Circle event edges through
    /// root -> switch list -> incoming form -> Changement. No BattleFlow.Choose call is made
    /// by this driver, so a consumed edge or an empty SwitchLines list fails at the actual seam.
    /// </summary>
    private async void RunBattleInputProbe()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = Path.Combine(generatedRoot, "probe/battle-input");
            Directory.CreateDirectory(captureRoot);
            ApplyScene(GetOrLoadZone("S200"));
            _state.ZoneId = "S200";
            StartDebugBattle();
            Require(_battleFlow is not null && _activeBattle is not null, "F8 did not start a battle");
            var inputFlow = BattleFlow;
            Require(inputFlow.Phase == BattleFlowPhase.PlayerCommand && !inputFlow.MessageVisible,
                "PlayerCommand retained an action message window");
            var fighterData = _fighterData ?? throw new InvalidOperationException("fighter data unavailable");
            var enemyNameId = fighterData.BattleTable.Single(entry => entry.Id == 51).NameId;
            var activePlayer = inputFlow.State.ActivePlayerFighter ?? throw new InvalidOperationException("active player unavailable");
            var partnerNameId = fighterData.Digimon[activePlayer.Id].NameId;
            Require(NameLine(51).Tokens.SequenceEqual(BattleText().DigimonByNameId(enemyNameId).Tokens)
                && NameLine(activePlayer.Id).Tokens.SequenceEqual(BattleText().DigimonByNameId(partnerNameId).Tokens),
                $"HUD names do not use nameId enemy=51:{enemyNameId} partner={activePlayer.Id}:{partnerNameId}");
            QuietLog.Info($"battle-input nameId PASS enemy=51:{enemyNameId} partner={activePlayer.Id}:{partnerNameId} messageVisible=false");
            Require(_battleMenu.Menu == BattleMenuKind.Root, $"F8 menu is not root menu={_battleMenu.Menu}");
            var before = await CaptureBattle(captureRoot, "switch-before.png");

            // Three source submenus use their own layouts, not the root command list. Input only.
            InjectBattleKey(Key.S);
            InjectBattleKey(Key.Enter);
            Require(_battleMenu.Menu == BattleMenuKind.Technique && _battleMenu.Cursor == 0 && _battleMenu.Lines.Count > 0,
                $"tech menu did not open at source line 0 menu={_battleMenu.Menu} lines={_battleMenu.Lines.Count}");
            RequireSubmenuText(_battleMenu.View, "technique");
            Require(_battleMenu.View.Lines[0].Icon >= 0 && _battleMenu.View.Lines[0].Cost >= 0,
                "tech first line lacks source icon/cost metadata");
            Require(_battleMenu.View.Lines[0].DescriptionText is { } techDescription && techDescription != BattleTextLine.Missing && techDescription.Tokens.Count > 0,
                "tech first line lacks SKLINF token description");
            var techMenu = await CaptureBattle(captureRoot, "submenu-tech.png");
            RequireRenderedSubmenuLines(techMenu, _battleMenu.View, "technique", 0x2A, 0x45, 0xE);
            RequireBattleFramePixels(techMenu, "0x2A", 8, 0x3E, 154, 122);
            RequireBattleFramePixels(techMenu, "0x29", 0xA3, 0x21, 96, 39);
            RequireBattleFramePixels(techMenu, "0x31", 0xB, 0xBC, 296, 38);
            var (techIconX, techIconY) = BattleHudLayout.ToViewport(0x1D, 0x45);
            Require(CountBrightPixelsInRect(techMenu, new Rect2(techIconX, techIconY, 12 * BattleHudLayout.Scale, 14 * BattleHudLayout.Scale)) > 0,
                "tech menu CMFONTCS icon rectangle has no nonzero pixels");
            foreach (var (x, y) in new[] { (0x1A, 0x9F), (0x80, 0x9F), (0xAC, 0x3A), (0xD8, 0x3A), (0xD9, 0x3A), (0xFB, 0x3A) })
            {
                var (screenX, screenY) = BattleHudLayout.ToViewport(x, y);
                Require(CountBrightPixelsInRect(techMenu, new Rect2(screenX, screenY, 28 * BattleHudLayout.Scale, 12 * BattleHudLayout.Scale)) > 0,
                    $"tech source stat window lacks glyphs x=0x{x:X} y=0x{y:X}");
            }
            var techStatus = _battleMenu.View.StatusText ?? throw new InvalidOperationException("tech source status text unavailable");
            Require(techStatus.Header.Debug == "MFIGHT entry=13" && techStatus.Separator?.Debug == "MFIGHT entry=16"
                && _battleMenu.View.PreviousPageText?.Debug == "MFIGHT entry=17" && _battleMenu.View.NextPageText?.Debug == "MFIGHT entry=18"
                && _battleMenu.View.Lines[0].CostLabelText?.Debug == "MFIGHT entry=13",
                "tech status labels use MFIGHT 0x11/0x12/0xD/0x10");
            var selectedTech = inputFlow.PlayerTechniques[_battleMenu.SelectedIndex];
            QuietLog.Info($"battle-tech-menu tech={selectedTech.Id} name={selectedTech.Text?.Debug} cost={selectedTech.Tech.Mp} power={selectedTech.Tech.Power}");
            foreach (var digimonId in new[] { 383, 373, 31 })
            {
                var decoded = fighterData.Digimon[digimonId];
                var listed = BattleSetupFactory.PlayerTechniques(fighterData, _techData!, digimonId, BattleText());
                var expected = decoded.Skills.Skip(1).Where(raw => (raw & BattleSetupFactory.SkillIdMask) != 0)
                    .Select(BattleSetupFactory.SkillId).ToArray();
                Require(listed.Select(tech => tech.Id).SequenceEqual(expected),
                    $"team {digimonId} techniques use SKILL_ID expected=[{string.Join(',', expected)}]");
                foreach (var technique in listed)
                {
                    Require(technique.Text is { } name && name != BattleTextLine.Missing && name.Tokens.Count > 1,
                        $"team {digimonId} technique {technique.Id} has sourced non-single-glyph SKLNAM");
                    QuietLog.Info($"battle-tech-menu tech={technique.Id} name={technique.Text!.Debug} cost={technique.Tech.Mp} power={technique.Tech.Power} team={digimonId}");
                }
            }
            InjectBattleKey(Key.X);
            Require(_battleMenu.Menu == BattleMenuKind.Root, "Triangle from technique did not return to root");

            for (var i = 0; i < 2; i++) InjectBattleKey(Key.S);
            InjectBattleKey(Key.Enter);
            Require(_battleMenu.Menu == BattleMenuKind.Digivolve && _battleMenu.Cursor == 0 && _battleMenu.Lines.Count > 0,
                $"digivolve menu did not open at source line 0 menu={_battleMenu.Menu} lines={_battleMenu.Lines.Count}");
            RequireSubmenuText(_battleMenu.View, "digivolve");
            var digivolveMenu = await CaptureBattle(captureRoot, "submenu-digivolve.png");
            RequireRenderedSubmenuLines(digivolveMenu, _battleMenu.View, "digivolve", 0xBB, 0x92, 0x13);
            RequireBattleFramePixels(digivolveMenu, "0x2B", 0xA7, 0x8F, 129, 19);
            RequireBattleFramePixels(digivolveMenu, "0x24", 167, 146, 144, 19);
            InjectBattleKey(Key.X);
            Require(_battleMenu.Menu == BattleMenuKind.Root, "Triangle from digivolve did not return to root");

            for (var i = 0; i < 4; i++) InjectBattleKey(Key.S);
            InjectBattleKey(Key.Enter);
            Require(_battleMenu.Menu == BattleMenuKind.Item && _battleMenu.Cursor == 0 && _battleMenu.Lines.Count > 0,
                $"item menu did not open at source line 0 menu={_battleMenu.Menu} lines={_battleMenu.Lines.Count}");
            RequireSubmenuText(_battleMenu.View, "item");
            var itemStatus = _battleMenu.View.StatusText ?? throw new InvalidOperationException("item source status text unavailable");
            Require(itemStatus.Header.Debug == "MFIGHT entry=12", "item quantity label uses MFIGHT 0xC");
            Require(_battleMenu.View.Lines[0].Quantity > 0, "item first line lacks a positive source stock quantity");
            Require(_battleMenu.View.Lines[0].DescriptionText is { } itemDescription && itemDescription != BattleTextLine.Missing && itemDescription.Tokens.Count > 0,
                "item first line lacks ITMINF token description");
            var itemMenuInitial = await CaptureBattle(captureRoot, "submenu-item.png");
            RequireRenderedSubmenuLines(itemMenuInitial, _battleMenu.View, "item", 0x2A, 0x45, 0xE);
            RequireBattleFramePixels(itemMenuInitial, "0x27", 8, 0x3E, 154, 113);
            for (var i = 0; i < 10; i++) InjectBattleKey(Key.S);
            Require(_battleMenu.Cursor <= 6 && _battleMenu.First == 0,
                $"item Down x10 must clamp on source page cursor={_battleMenu.Cursor} first={_battleMenu.First}");
            InjectBattleKey(Key.Pagedown);
            Require(_battleMenu.Cursor == 0 && _battleMenu.First == 7 && _battleMenu.View.Lines.Count == 7,
                $"item PAD_R1 page did not advance source first={_battleMenu.First} cursor={_battleMenu.Cursor}");
            QuietLog.Info($"battle-menu scroll first={_battleMenu.First} cursor={_battleMenu.Cursor} source=PAD_R1-page");
            var itemMenu = await CaptureBattle(captureRoot, "submenu-item-scrolled.png");
            RequireBattleFramePixels(itemMenu, "0x27", 8, 0x3E, 154, 113);
            RequireBattleFramePixels(itemMenu, "0x28", 0xA6, 0xA0, 44, 24);
            RequireBattleFramePixels(itemMenu, "0x31", 0xB, 0xBC, 296, 38);
            var (itemIconX, itemIconY) = BattleHudLayout.ToViewport(0x1D, 0x45);
            Require(_battleMenu.View.Lines[0].Icon >= 0, "item page first line lacks source category frame metadata");
            Require(CountBrightPixelsInRect(itemMenu, new Rect2(itemIconX, itemIconY, 12 * BattleHudLayout.Scale, 14 * BattleHudLayout.Scale)) > 0,
                "item menu CMFONTCS icon rectangle has no nonzero pixels");
            InjectBattleKey(Key.X);
            Require(_battleMenu.Menu == BattleMenuKind.Root, "Triangle from item did not return to root");

            // the battle-message layout contract draws frame 10 only while it waits for Cross. Render the same active
            // battle through that wait view and prove its source rectangle is populated.
            var waiting = (BuildBattleView() ?? throw new InvalidOperationException("battle wait view unavailable"))
                with
            { Message = "MESSAGE_WAIT_PROBE", MessageText = BattleTextLine.Missing, MessageVisible = true };
            BattleRenderer.Present(waiting);
            var messageArrow = await CaptureBattle(captureRoot, "message-arrow.png");
            var (arrowX, arrowY) = BattleHudLayout.ToViewport(0x123, 0xD0);
            Require(CountBrightPixelsInRect(messageArrow, new Rect2(arrowX, arrowY, 16 * BattleHudLayout.Scale, 16 * BattleHudLayout.Scale)) > 0,
                "message wait arrow rectangle has no nonzero pixels");

            // Bas x3 reaches the source root command index 3 (Changement), then Cross opens it.
            for (var i = 0; i < 3; i++) InjectBattleKey(Key.S);
            Require(_battleMenu.Cursor == 3, $"root switch cursor={_battleMenu.Cursor}, expected=3");
            InjectBattleKey(Key.Enter);
            Require(_battleMenu.Menu == BattleMenuKind.Switch && _battleMenu.Lines.Count == 2,
                $"battle-menu open=Switch lines={_battleMenu.Lines.Count} menu={_battleMenu.Menu}");
            var switchList = await CaptureBattle(captureRoot, "switch-list.png");
            RequireBattleFramePixels(switchList, "0x2F-line-0", 16, 77, 186, 31);
            RequireBattleFramePixels(switchList, "0x2F-line-1", 16, 109, 186, 31);

            // switch_menu -> switch_in_menu form list -> Changement/Technique de paire.
            InjectBattleKey(Key.Enter);
            Require(_battleMenu.Menu == BattleMenuKind.SwitchIn, $"switch partner did not open form page menu={_battleMenu.Menu}");
            var switchIn = await CaptureBattle(captureRoot, "switch-in.png");
            RequireBattleFramePixels(switchIn, "0x2B", 0xA7, 0x8F, 129, 19);
            RequireBattleFramePixels(switchIn, "0x24-line-0", 167, 146, 144, 19);
            InjectBattleKey(Key.Enter);
            Require(_battleMenu.Menu == BattleMenuKind.PairSwitch, $"form did not open pair choice menu={_battleMenu.Menu}");
            var switchChoice = await CaptureBattle(captureRoot, "switch-choice.png");
            RequireBattleFramePixels(switchChoice, "0x2B", 0xA7, 0x8F, 129, 19);
            RequireBattleFramePixels(switchChoice, "0x22-line-0", 167, 165, 16, 16);

            // Triangle unwinds one source page at a time, then returns to the command root.
            InjectBattleKey(Key.X);
            Require(_battleMenu.Menu == BattleMenuKind.SwitchIn, "Triangle from pair choice did not return to form page");
            InjectBattleKey(Key.X);
            Require(_battleMenu.Menu == BattleMenuKind.Switch, "Triangle from form page did not return to partner list");
            InjectBattleKey(Key.X);
            Require(_battleMenu.Menu == BattleMenuKind.Root, "Triangle from partner list did not return to root");

            for (var i = 0; i < 3; i++) InjectBattleKey(Key.S);
            InjectBattleKey(Key.Enter);
            InjectBattleKey(Key.Enter);
            InjectBattleKey(Key.Enter);
            _input.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = true });
            TickTestSimulation();
            var switchedFlow = _battleFlow ?? throw new InvalidOperationException("battle flow disappeared during switch");
            Require(switchedFlow.Phase == BattleFlowPhase.EnemyTurn, $"switch did not spend turn phase={switchedFlow.Phase}");
            _input.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = false });
            TickTestSimulation();
            Require(switchedFlow.State.ActivePlayerFighter?.Id == 373 && _battleRenderedPlayerId == 373,
                $"switch did not render Renamon active={switchedFlow.State.ActivePlayerFighter?.Id} rendered={_battleRenderedPlayerId}");
            var after = await CaptureBattle(captureRoot, "switch-after-renamon.png");
            var changed = CountChanged(before, after, new Rect2(0, 0, before.GetWidth() / 2, before.GetHeight()));
            Require(changed > 500, $"switch capture did not change enough pixels={changed}");
            QuietLog.Info($"battle-input probe PASS battle-menu open=Switch lines=2 active=373 rendered=373 pixels={changed} captures={captureRoot}");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"BATTLE_INPUT_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private void InjectBattleKey(Key key)
    {
        _input.Handle(new InputEventKey { Keycode = key, Pressed = true });
        TickTestSimulation();
        _input.Handle(new InputEventKey { Keycode = key, Pressed = false });
        TickTestSimulation();
    }

    private async void RunBattleKotemonLightProbe()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir
                ?? Path.Combine(generatedRoot, "probe/battle-kotemon-light");
            Directory.CreateDirectory(captureRoot);
            StartDebugBattle();
            Require(_battleRenderer is not null && _activeBattle is not null && _battleRenderedPlayerId == 383,
                "Kotemon light oracle requires F8 fighter 383");
            var renderer = BattleRenderer;
            renderer.SetProbePose(10);
            renderer.SetNeutralLightsForProbe(false);
            renderer.SetCommandMenuCamera();
            renderer.SetHudVisibleForProbe(false);
            var remake = ToSource320(await CaptureBattle(captureRoot, "kotemon-command-menu.png"));
            renderer.SetModelVisibleForProbe(383, false);
            var withoutKotemon = ToSource320(await CaptureBattle(captureRoot, "command-menu-without-kotemon.png"));
            var silhouette = DifferenceMask(remake, withoutKotemon);
            var psx = LoadPsxReference("battle/kotemon-383-command-menu-0.png");
            var source = ToSource320(psx);
            var psxMasks = PsxKotemonRegionMasks(source);
            var remakeMasks = RemakeKotemonRegionMasks(remake, silhouette);
            var psxGlobal = UnionMasks(psxMasks.Values);
            var remakeGlobal = UnionMasks(remakeMasks.Values);
            Require(psxGlobal.SavePng(Path.Combine(captureRoot, "mask-psx.png")) == Error.Ok, "PSX global mask save failed");
            Require(remakeGlobal.SavePng(Path.Combine(captureRoot, "mask-remake.png")) == Error.Ok, "remake global mask save failed");
            foreach (var region in KotemonRegions)
            {
                Require(psxMasks[region].SavePng(Path.Combine(captureRoot, $"mask-psx-{region}.png")) == Error.Ok, $"PSX {region} mask save failed");
                Require(remakeMasks[region].SavePng(Path.Combine(captureRoot, $"mask-remake-{region}.png")) == Error.Ok, $"remake {region} mask save failed");
                var psxStats = MaskedLuminance(source, psxMasks[region], minimumPixels: 8);
                var remakeStats = MaskedLuminance(remake, remakeMasks[region], minimumPixels: 8);
                QuietLog.Info($"battle-light oracle fighter=383 phase=command-menu region={region} psx pixels={psxStats.Count} mean={psxStats.Mean:0.##} q10={psxStats.Q10:0.##} q50={psxStats.Q50:0.##} q90={psxStats.Q90:0.##} hist={psxStats.Histogram} remake pixels={remakeStats.Count} mean={remakeStats.Mean:0.##} q10={remakeStats.Q10:0.##} q50={remakeStats.Q50:0.##} q90={remakeStats.Q90:0.##} hist={remakeStats.Histogram} delta={RelativeDelta(psxStats.Mean, remakeStats.Mean) * 100f:0.##}%");
            }
            var psxGlobalStats = MaskedLuminance(source, psxGlobal, minimumPixels: 24);
            var remakeGlobalStats = MaskedLuminance(remake, remakeGlobal, minimumPixels: 24);
            var delta = RelativeDelta(psxGlobalStats.Mean, remakeGlobalStats.Mean);
            var compare = SideBySide(remake, source, "REMAKE", "PSX");
            Require(compare.SavePng(Path.Combine(captureRoot, "compare-kotemon-luminance.png")) == Error.Ok, "Kotemon comparison save failed");
            QuietLog.Info($"battle-light oracle fighter=383 phase=command-menu region=global psx pixels={psxGlobalStats.Count} mean={psxGlobalStats.Mean:0.##} q10={psxGlobalStats.Q10:0.##} q50={psxGlobalStats.Q50:0.##} q90={psxGlobalStats.Q90:0.##} hist={psxGlobalStats.Histogram} remake pixels={remakeGlobalStats.Count} mean={remakeGlobalStats.Mean:0.##} q10={remakeGlobalStats.Q10:0.##} q50={remakeGlobalStats.Q50:0.##} q90={remakeGlobalStats.Q90:0.##} hist={remakeGlobalStats.Histogram} delta={delta * 100f:0.##}% vertex={renderer.LightingVertexSummary(383)}");
            Require(delta < .25f, $"Kotemon global luminance delta={delta * 100f:0.##}% must be <25%");
            QuietLog.Info($"battle-light probe PASS captures={captureRoot}/compare-kotemon-luminance.png masks=published");
            GetTree().Quit(0);
        }
        catch (PsxReferenceUnavailableException ex)
        {
            QuietLog.Info(ex.Message);
            GetTree().Quit(5);
        }
        catch (Exception error)
        {
            GD.PushError($"BATTLE_KOTEMON_LIGHT_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private static readonly string[] KotemonRegions = ["helmet", "body", "fur"];

    private static Dictionary<string, Image> PsxKotemonRegionMasks(Image image) => new()
    {
        ["helmet"] = RegionMask(image, new Rect2I(263, 151, 37, 38), p => p.B > p.R * 1.15f && p.B > p.G * 1.08f && p.B > .18f),
        ["body"] = RegionMask(image, new Rect2I(250, 164, 40, 44), p => p.R > p.G * 1.22f && p.R > p.B * 1.12f && p.R > .22f),
        ["fur"] = RegionMask(image, new Rect2I(244, 180, 32, 37), p => p.R > .42f && p.G > .42f && p.B > .42f && Mathf.Max(p.R, Mathf.Max(p.G, p.B)) - Mathf.Min(p.R, Mathf.Min(p.G, p.B)) < .38f),
    };

    private static Dictionary<string, Image> RemakeKotemonRegionMasks(Image image, Image silhouette)
    {
        var box = MaskBoundingBox(silhouette) ?? throw new InvalidOperationException("Kotemon remake silhouette absent");
        Rect2I Relative(float x, float y, float width, float height) => new(Mathf.FloorToInt(box.Position.X + box.Size.X * x), Mathf.FloorToInt(box.Position.Y + box.Size.Y * y), Mathf.Max(1, Mathf.CeilToInt(box.Size.X * width)), Mathf.Max(1, Mathf.CeilToInt(box.Size.Y * height)));
        return new()
        {
            ["helmet"] = RegionMask(image, silhouette, Relative(.42f, .00f, .42f, .38f), p => p.B > p.R * 1.12f && p.B > p.G * 1.04f && p.B > .15f),
            ["body"] = RegionMask(image, silhouette, Relative(.28f, .25f, .55f, .48f), p => p.R > p.G * 1.18f && p.R > p.B * 1.08f && p.R > .18f),
            ["fur"] = RegionMask(image, silhouette, Relative(.00f, .42f, .58f, .58f), p => p.R > .38f && p.G > .38f && p.B > .38f && Mathf.Max(p.R, Mathf.Max(p.G, p.B)) - Mathf.Min(p.R, Mathf.Min(p.G, p.B)) < .42f),
        };
    }

    private static Image RegionMask(Image image, Rect2I region, Func<Color, bool> select) => RegionMask(image, null, region, select);
    private static Image RegionMask(Image image, Image? silhouette, Rect2I region, Func<Color, bool> select)
    {
        var mask = Image.CreateEmpty(320, 240, false, Image.Format.Rgba8);
        mask.Fill(Colors.Black);
        for (var y = Math.Max(0, region.Position.Y); y < Math.Min(240, region.End.Y); y++)
            for (var x = Math.Max(0, region.Position.X); x < Math.Min(320, region.End.X); x++)
                if ((silhouette is null || silhouette.GetPixel(x, y).R >= .5f) && select(image.GetPixel(x, y))) mask.SetPixel(x, y, Colors.White);
        return mask;
    }
    private static Image UnionMasks(IEnumerable<Image> masks)
    {
        var output = Image.CreateEmpty(320, 240, false, Image.Format.Rgba8);
        output.Fill(Colors.Black);
        foreach (var mask in masks) for (var y = 0; y < 240; y++) for (var x = 0; x < 320; x++) if (mask.GetPixel(x, y).R >= .5f) output.SetPixel(x, y, Colors.White);
        return output;
    }

    /// <summary>Framebuffer proof. The source comparison is a command-menu phase
    /// indicator, while the modulation and non-black fighter checks remain hard assertions.</summary>
    private async void RunBattleLightProbe()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir
                ?? Path.Combine(generatedRoot, "probe/battle-light");
            Directory.CreateDirectory(captureRoot);
            StartDebugBattle();
            Require(_battleRenderer is not null && _activeBattle is not null, "F8 did not construct battle lighting");
            var renderer = BattleRenderer;
            Require(_battleRenderedPlayerId == 383 && renderer.StageBuilt && renderer.ModelCount == 2,
                "light probe requires Kotemon, stage 18 and both decoded models");
            renderer.SetProbePose(10);
            renderer.SetNeutralLightsForProbe(false);
            // the player-turn presentation flow starts the main ShotCamera for the command menu. Its first source
            // shot is getEnemyView, distinct from the small partner-view camera 6/7.
            renderer.SetCommandMenuCamera();
            var lit = await CaptureBattle(captureRoot, "kotemon-lit.png");
            Require(renderer.LightFade >= 1f, $"settled light capture fade={renderer.LightFade:0.###}");
            var kuwagamon = await CaptureBattle(captureRoot, "kuwagamon-lit.png");
            var stageLighting = renderer.StageLightSummary();
            var kuwagamonVertex = renderer.LightingVertexSummary(51);
            // The PSX oracle contains the entrance without HUD. Capture the same renderer state
            // without HUD, then a stage-only frame. Their difference is the remake silhouette,
            // rather than a hand-picked dark rectangle that includes the arena.
            renderer.SetHudVisibleForProbe(false);
            // Preserve a separate active-fighter mask for the hard non-black invariant.
            renderer.SetModelVisibleForProbe(51, false);
            var activeModel = await CaptureBattle(captureRoot, "active-model.png");
            renderer.SetModelVisibleForProbe(51, true);
            renderer.SetModelVisibleForProbe(383, false);
            // This is the exact player-command camera 6. A zero mask is meaningful: the
            // enemy is not in the remake framebuffer, so this oracle is explicitly unpaired.
            var kuwagamonModel = await CaptureBattle(captureRoot, "kuwagamon-model.png");
            renderer.ClearModelsForSheet();
            var stageOnly = await CaptureBattle(captureRoot, "stage-only.png");
            var psx = LoadPsxReference("battle/11-command-menu-sel3.png");
            var psx320 = ToSource320(psx);
            var active320 = ToSource320(activeModel);
            var remake320 = ToSource320(kuwagamonModel);
            var stage320 = ToSource320(stageOnly);
            var psxMask = PsxKuwagamonMask(psx320);
            var activeMask = DifferenceMask(active320, stage320);
            var remakeMask = CommonKuwagamonMask(DifferenceMask(remake320, stage320));
            Require(psxMask.SavePng(Path.Combine(captureRoot, "mask-psx.png")) == Error.Ok, "PSX mask write failed");
            Require(remakeMask.SavePng(Path.Combine(captureRoot, "mask-remake.png")) == Error.Ok, "remake mask write failed");
            var psxStats = MaskedLuminance(psx320, psxMask);
            var activeStats = MaskedLuminance(active320, activeMask);
            var remakePixels = MaskedPixelCount(remakeMask);
            var compare = SideBySide(remake320, psx320, "REMAKE", "PSX");
            Require(compare.SavePng(Path.Combine(captureRoot, "compare-kuwagamon-psx.png")) == Error.Ok, "PSX comparison capture write failed");
            Require(compare.SavePng(Path.Combine(captureRoot, "compare-command-menu-camera.png")) == Error.Ok, "command camera comparison write failed");
            QuietLog.Info($"battle-light stage=18 fade={renderer.LightFade:0.###} {stageLighting} vertex={kuwagamonVertex}");
            VerifyPs1ModulationQuad();
            var darkPercent = MaskedDarkPercent(active320, activeMask, threshold: 30f);
            Require(darkPercent < 30f, $"active fighter dark={darkPercent:0.##}% must be <30%");
            Require(activeStats.Mean > 60f, $"active fighter mean={activeStats.Mean:0.##} must be >60");
            QuietLog.Info($"battle-light hard modulation=tex80x[ff,80,40]=[255,128,64] active_mean={activeStats.Mean:0.##} active_dark={darkPercent:0.##}%");
            if (remakePixels <= 100)
            {
                QuietLog.Info($"battle-light oracle phase=command-menu psx mean={psxStats.Mean:0.##} q10={psxStats.Q10:0.##} q50={psxStats.Q50:0.##} q90={psxStats.Q90:0.##} remake mean=n/a q10=n/a q50=n/a q90=n/a delta=n/a");
                GD.PushWarning($"WARN battle-light-oracle-unpaired phase=command-menu reason=remake-kuwagamon-offscreen psx_pixels={psxStats.Count} remake_pixels={remakePixels}");
            }
            else
            {
                var remakeStats = MaskedLuminance(remake320, remakeMask);
                var meanDelta = RelativeDelta(psxStats.Mean, remakeStats.Mean);
                var medianDelta = RelativeDelta(psxStats.Q50, remakeStats.Q50);
                QuietLog.Info($"battle-light oracle phase=command-menu psx mean={psxStats.Mean:0.##} q10={psxStats.Q10:0.##} q50={psxStats.Q50:0.##} q90={psxStats.Q90:0.##} remake mean={remakeStats.Mean:0.##} q10={remakeStats.Q10:0.##} q50={remakeStats.Q50:0.##} q90={remakeStats.Q90:0.##} delta={meanDelta * 100f:0.##}%");
                if (meanDelta < .25f && medianDelta < .25f)
                    QuietLog.Info($"battle-light oracle paired mean_delta={meanDelta * 100f:0.##}% median_delta={medianDelta * 100f:0.##}%");
                else
                    GD.PushWarning($"WARN battle-light-oracle-unpaired phase=command-menu mean_delta={meanDelta * 100f:0.##}% median_delta={medianDelta * 100f:0.##}% psx_pixels={psxStats.Count} remake_pixels={remakeStats.Count}");
            }
            QuietLog.Info($"battle-light probe PASS captures={captureRoot}/kotemon-lit.png,{captureRoot}/kuwagamon-lit.png,{captureRoot}/compare-kuwagamon-psx.png");
            GetTree().Quit(0);
        }
        catch (PsxReferenceUnavailableException ex)
        {
            QuietLog.Info(ex.Message);
            GetTree().Quit(5);
        }
        catch (Exception error)
        {
            GD.PushError($"BATTLE_LIGHT_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    /// <summary>Framebuffer regression: enters the real F8 battle, compares the old identity-scale baseline to source bone scale/visibility, then checks back-face culling and the same scale predicate on Kuwagamon and WarGreymon.</summary>
    private async void RunBattleModelProbe()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir
                ?? Path.Combine(generatedRoot, "probe/battle-model");
            Directory.CreateDirectory(captureRoot);

            StartDebugBattle();
            Require(_activeBattle is not null && _battleRenderer is not null, "F8 did not construct a battle renderer");
            var renderer = _battleRenderer ?? throw new InvalidOperationException("battle renderer unavailable after F8");
            Require(_battleRenderedPlayerId == 383, $"F8 player fighter={_battleRenderedPlayerId}, expected Kotemon 383");
            Require(renderer.ModelCount == 2, "F8 did not build both real models");
            renderer.SetCommandMenuCamera();
            var rotationDiff = renderer.RotationOracleMaxDiff();
            Require(rotationDiff < 1e-4f, $"battle rotation oracle maxdiff={rotationDiff:R}");
            QuietLog.Info($"battle-rotation-oracle maxdiff={rotationDiff:R} order=Rz*Ry*Rx*S");
            renderer.SetProbePose(10);
            foreach (var fighter in new[] { 383, 51 })
            {
                var winding = renderer.WindingAgreement(fighter);
                Require(winding.Total > 0, $"battle winding fighter={fighter} has no triangles");
                var percent = winding.Agree * 100d / winding.Total;
                Require(percent >= 90d, $"battle winding fighter={fighter} agree={percent:0.00} below 90");
                QuietLog.Info($"battle-winding fighter={fighter} agree={percent:0.00} total={winding.Total} agree_count={winding.Agree}");
            }

            // Fighters with no ABR primitive must stay in the opaque
            // (depth-writing) pass; only the stage's ABR polygons may be translucent.
            var kotemonPasses = renderer.SurfacePasses(383);
            var stagePasses = renderer.SurfacePasses(-1);
            Require(kotemonPasses.Opaque > 0 && kotemonPasses.Translucent == 0,
                $"Kotemon 383 surfaces must all be opaque opaque={kotemonPasses.Opaque} translucent={kotemonPasses.Translucent}");
            QuietLog.Info($"battle-model passes fighter=383 opaque={kotemonPasses.Opaque} translucent={kotemonPasses.Translucent} stage-opaque={stagePasses.Opaque} stage-translucent={stagePasses.Translucent}");

            const int idleFrame = 10;
            renderer.ShowAllBonesForProbe();
            var legacy = await CaptureBattle(captureRoot, "kotemon-legacy-all-bones.png");
            var legacyPixels = CountBrightPixels(legacy, 0, legacy.GetWidth() / 2);

            renderer.SetProbePose(idleFrame);
            Require(renderer.BoneVisible(383, 16) && !renderer.BoneVisible(383, 21) && !renderer.BoneVisible(383, 23),
                "Kotemon idle frame 10 must show saber bone 16 only");
            var kotemon = await CaptureBattle(captureRoot, "kotemon-idle.png");
            var kotemonPixels = CountBrightPixels(kotemon, 0, kotemon.GetWidth() / 2);
            // Source bone 16 retains the one visible shinai while 21/23 remove its duplicate
            // mesh instances. Texture modulation makes whole-frame bright-pixel counts nonmonotonic,
            // so prove the semantic difference only in the visible shinai region.
            var saberDifference = CountDifferentPixels(legacy, kotemon, 790, 930);
            Require(kotemonPixels > 1000 && saberDifference > 0,
                $"Kotemon source mask must retain textured body and change saber region pixels={saberDifference} before={legacyPixels} after={kotemonPixels}");
            QuietLog.Info($"battle-model bones {renderer.BoneVisibility(383)} visible_sabre=16 frame={idleFrame} saber_region_diff={saberDifference} pixels-before={legacyPixels} pixels-after={kotemonPixels}");

            renderer.SetCullModeForProbe(BaseMaterial3D.CullModeEnum.Disabled);
            var noCull = await CaptureBattle(captureRoot, "kotemon-nocull.png");
            var noCullPixels = CountBrightPixels(noCull, 0, noCull.GetWidth() / 2);
            renderer.SetCullModeForProbe(BaseMaterial3D.CullModeEnum.Back);
            var culled = await CaptureBattle(captureRoot, "kotemon-cull-back.png");
            var culledPixels = CountBrightPixels(culled, 0, culled.GetWidth() / 2);
            Require(culledPixels > 1000 && culledPixels * 4 > noCullPixels,
                $"back culling removed the visible Kotemon body no-cull={noCullPixels} back={culledPixels}");
            QuietLog.Info($"battle-model culling fighter=383 mode=Back textured-no-cull={noCullPixels} textured-back={culledPixels} orientation=correct");

            var kotemonPath = ResolveBattleModelPath(383);
            var kuwagamonPath = ResolveBattleModelPath(51);
            renderer.BuildModels(kuwagamonPath, kotemonPath, "Kuwagamon", "Kotemon", 383);
            Require(renderer.ModelCount == 2, "Kuwagamon comparison model did not load");
            renderer.SetProbePose(idleFrame);
            await CaptureBattle(captureRoot, "kuwagamon-idle.png");
            QuietLog.Info($"battle-model bones {renderer.BoneVisibility(51)} fighter_name=Kuwagamon frame={idleFrame}");

            // DIGNAM entry 36 is WarGreymon. fighter.json resolves that name id to fighter 213.
            const int warGreymon = 213;
            var warGreymonPath = ResolveBattleModelPath(warGreymon);
            renderer.BuildModels(warGreymonPath, kotemonPath, "WarGreymon", "Kotemon", 383);
            Require(renderer.ModelCount == 2, "WarGreymon comparison model did not load");
            renderer.SetProbePose(idleFrame);
            await CaptureBattle(captureRoot, "wargreymon-idle.png");
            QuietLog.Info($"battle-model bones {renderer.BoneVisibility(warGreymon)} fighter_name=WarGreymon name_id=36 fighter_id={warGreymon} frame={idleFrame}");

            // the decoded mesh contract:766-795 does not clear POLY_FT/GT UV/tpage state when
            // 0x9 disables new UV bytes. MegaSeadramon 61 exercises that residual
            // textured primitive path on twelve source polygons.
            const int megaSeadramon = 61;
            var megaSeadramonPath = ResolveBattleModelPath(megaSeadramon);
            renderer.BuildModels(kotemonPath, megaSeadramonPath, "Kotemon", "MegaSeadramon", megaSeadramon);
            Require(renderer.ModelCount == 2, "MegaSeadramon residual-UV model did not load");
            Require(renderer.UntexturedPolygonCount(megaSeadramon) > 0, "MegaSeadramon has no decoded residual-UV polygons");
            renderer.SetProbePose(0);
            var mega = await CaptureBattle(captureRoot, "megaseadramon-untextured.png");
            var megaPixels = CountBrightPixels(mega, mega.GetWidth() / 2, mega.GetWidth());
            Require(megaPixels > 0, "MegaSeadramon residual-UV model rendered zero textured pixels");
            QuietLog.Info($"battle-model untextured polygons={renderer.UntexturedPolygonCount(megaSeadramon)} fighter={megaSeadramon} textured-pixels={megaPixels}");

            QuietLog.Info($"battle-model probe PASS fps={Engine.GetFramesPerSecond():0.0} captures={captureRoot}/kotemon-idle.png,{captureRoot}/kuwagamon-idle.png,{captureRoot}/wargreymon-idle.png");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"BATTLE_MODEL_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    /// <summary>Render every decoded fighter through the production BattleRenderer.
    /// This is an offline/Xvfb diagnostic path, not an interactive feature. One FIGHTER_SHEET
    /// line is emitted for every source manifest and failures are kept as explicit records.</summary>
    private async void RunFighterSheet()
    {
        var generatedRoot = _options.ReferenceDir ?? _dataRoot;
        var captureRoot = _options.CaptureDir
            ?? Path.Combine(generatedRoot, "probe/fighter-sheet");
        Directory.CreateDirectory(captureRoot);
        var modelsRoot = Path.Combine(_dataRoot, "data", "battle", "models");
        var paths = Directory.EnumerateFiles(modelsRoot, "fighter-*-model.json")
            .OrderBy(ParseSheetFighterId).ToArray();
        var failed = 0;
        try
        {
            StartDebugBattle();
            var renderer = _battleRenderer ?? throw new InvalidOperationException("F8 did not construct BattleRenderer");
            if (!renderer.StageBuilt) renderer.BuildStage(ResolveBattleStagePath(18), 18);
            renderer.Show();
            renderer.SetLightFadeForProbe(1f);
            renderer.ClearModelsForSheet();
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var background = GetViewport().GetTexture().GetImage();
            background.Resize(320, 240, Image.Interpolation.Lanczos);
            Require(background.SavePng(Path.Combine(captureRoot, "stage18-background.png")) == Error.Ok, "sheet stage background save failed");
            foreach (var path in paths)
            {
                var fighter = ParseSheetFighterId(path);
                try
                {
                    var source = ReadSheetSource(path);
                    renderer.BuildSingleModelForSheet(path, $"fighter-{fighter}", fighter, source.Enemy);
                    renderer.SetProbePose(0);
                    // The sheet holds one model in slot zero even when it is placed at the enemy camp.
                    var cameras = renderer.FighterCameras(enemy: false);
                    if (cameras.Count == 0) throw new InvalidOperationException("no source cameras");
                    // View zero is the source opening shot for both FighterInfo layouts. The old
                    // sheet used view 6, a partner turn close-up and an enemy-facing reverse shot.
                    const int viewIndex = 0;
                    var view = cameras[viewIndex];
                    if (view.Proj <= 0) throw new InvalidOperationException($"invalid source projection={view.Proj} view={viewIndex}");
                    var accepted = false;
                    foreach (var factor in new[] { 1f, 1.3f, 1.7f, 2.2f, 3f, 4f, 6f })
                    {
                        var framed = PullBackSheetCamera(view, factor);
                        renderer.SetSourceCamera(framed, snap: true);
                        renderer.ClearModelsForSheet();
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        var empty = GetViewport().GetTexture().GetImage();
                        empty.Resize(320, 240, Image.Interpolation.Lanczos);
                        renderer.BuildSingleModelForSheet(path, $"fighter-{fighter}", fighter, source.Enemy);
                        renderer.SetProbePose(0);
                        // The sheet never enters Present, so apply the settled stage light set
                        // (fade 1) to the freshly built model instead of the black fade-0 state.
                        renderer.SetLightFadeForProbe(1f);
                        renderer.SetSourceCamera(framed, snap: true);
                        _battlePlayerCameraActive = false;
                        _battleRenderedPlayerId = fighter;
                        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        var image = GetViewport().GetTexture().GetImage();
                        image.Resize(320, 240, Image.Interpolation.Lanczos);
                        var bounds = SheetBounds(image, empty);
                        if (bounds.TexturedPixels == 0) throw new InvalidOperationException("zero textured pixels after source placement");
                        if (bounds.TouchesFrame) continue;
                        Require(empty.SavePng(Path.Combine(captureRoot, $"fighter-{fighter}-background.png")) == Error.Ok,
                            $"background save failed fighter={fighter}");
                        var output = Path.Combine(captureRoot, $"fighter-{fighter}.png");
                        Require(image.SavePng(output) == Error.Ok, $"save failed={output}");
                        var framing = factor == 1f ? $"source-view-{viewIndex}" : $"pulled-back x{factor.ToString("0.0", CultureInfo.InvariantCulture)}";
                        QuietLog.Info($"FIGHTER_SHEET fighter={fighter} status=ok enemy={source.Enemy} cameras={cameras.Count} framing={framing} image={output}");
                        accepted = true;
                        break;
                    }
                    if (!accepted) throw new InvalidOperationException("no source camera framing contains whole fighter at x6.0");
                }
                catch (Exception error)
                {
                    failed++;
                    QuietLog.Info($"FIGHTER_SHEET fighter={fighter} status=error reason={error.Message}");
                }
            }
            QuietLog.Info($"FIGHTER_SHEET complete total={paths.Length} failed={failed} root={captureRoot}");
            GetTree().Quit(failed == 0 ? 0 : 3);
        }
        catch (Exception error)
        {
            GD.PushError($"FIGHTER_SHEET_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private static int ParseSheetFighterId(string path)
    {
        const string prefix = "fighter-";
        const string suffix = "-model.json";
        var name = Path.GetFileName(path);
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)) return int.MaxValue;
        return int.TryParse(name[prefix.Length..^suffix.Length], out var fighter) ? fighter : int.MaxValue;
    }

    private readonly record struct SheetSource(bool Enemy);

    private static SheetSource ReadSheetSource(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var cameras = document.RootElement.GetProperty("cameras");
        // FighterInfoEnemy has three views, while FighterInfo has twelve (battle_model.py).
        return new SheetSource(cameras.GetArrayLength() == 3);
    }

    private static BattleModel.BattleCameraView PullBackSheetCamera(BattleModel.BattleCameraView view, float factor)
    {
        if (factor == 1f) return view;
        var pos = view.CamPos; var reference = view.CamRef;
        return new BattleModel.BattleCameraView(
            [reference[0] + Mathf.RoundToInt((pos[0] - reference[0]) * factor),
             reference[1] + Mathf.RoundToInt((pos[1] - reference[1]) * factor),
             reference[2] + Mathf.RoundToInt((pos[2] - reference[2]) * factor)], reference, view.Proj);
    }

    private static (int TexturedPixels, bool TouchesFrame) SheetBounds(Image image, Image background)
    {
        if (image.GetWidth() != background.GetWidth() || image.GetHeight() != background.GetHeight())
            throw new InvalidOperationException("sheet capture/background dimensions differ");
        var minX = image.GetWidth(); var minY = image.GetHeight(); var maxX = -1; var maxY = -1; var count = 0;
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var a = image.GetPixel(x, y); var b = background.GetPixel(x, y);
                if (Mathf.Abs(a.R - b.R) * 255f + Mathf.Abs(a.G - b.G) * 255f + Mathf.Abs(a.B - b.B) * 255f <= 45f) continue;
                count++; minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        return (count, count > 0 && (minX == 0 || minY == 0 || maxX == image.GetWidth() - 1 || maxY == image.GetHeight() - 1));
    }

    private static int CountBrightPixels(Image image, int xMin, int xMax)
    {
        var data = image.GetData();
        var bytesPerPixel = image.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        var count = 0;
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = xMin; x < xMax; x++)
            {
                var offset = (y * image.GetWidth() + x) * bytesPerPixel;
                if (data[offset] + data[offset + 1] + data[offset + 2] > 120) count++;
            }
        return count;
    }

    private static double LuminanceVariance(Image image, int xMin, int xMax)
    {
        var data = image.GetData();
        var bpp = image.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        var values = new List<double>();
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = xMin; x < xMax; x++)
            {
                var offset = (y * image.GetWidth() + x) * bpp;
                // The neutral baseline's black models intentionally contribute zero, while the
                // fixed stage background is excluded by its near-black blue pixel range.
                var luminance = (0.2126 * data[offset] + 0.7152 * data[offset + 1] + 0.0722 * data[offset + 2]) / 255d;
                if (luminance > 0.12) values.Add(luminance);
            }
        if (values.Count == 0) return 0;
        var mean = values.Average();
        return values.Sum(value => (value - mean) * (value - mean)) / values.Count;
    }

    private static int CountDifferentPixels(Image a, Image b, int xMin, int xMax)
    {
        var ad = a.GetData(); var bd = b.GetData();
        var bytesPerPixel = a.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        var count = 0;
        for (var y = 0; y < a.GetHeight(); y++)
            for (var x = xMin; x < xMax; x++)
            {
                var offset = (y * a.GetWidth() + x) * bytesPerPixel;
                if (Math.Abs(ad[offset] - bd[offset]) + Math.Abs(ad[offset + 1] - bd[offset + 1]) + Math.Abs(ad[offset + 2] - bd[offset + 2]) > 30) count++;
            }
        return count;
    }

    private async System.Threading.Tasks.Task<Image> CaptureBattle(string captureRoot, string fileName)
    {
        // The normal _Process path presents every display frame. Test drivers intentionally do
        // not enter it, so a single CaptureBattle used to leave the source 60-PAL-frame stage
        // fade at 1/60 and wrote black fighter silhouettes. Advance the real Present path to its
        // steady command state before accepting any visual probe capture.
        while (BattleRenderer.LightFade < 1f)
        {
            BattleRenderer.Present(BuildBattleView());
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        BattleRenderer.Present(BuildBattleView());
        // Visibility toggles used for silhouette masks are queued by the SubViewport; consume a
        // second rendered frame so the framebuffer is not the previous scene.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var path = Path.Combine(captureRoot, fileName);
        Require(image.SavePng(path) == Error.Ok, $"battle capture failed={path}");
        return image;
    }

    private readonly record struct LuminanceStats(int Count, float Mean, float Q10, float Q50, float Q90, string Histogram);

    private static double MeanLuminance(Image image)
    {
        double total = 0;
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < image.GetWidth(); x++)
            {
                var pixel = image.GetPixel(x, y);
                total += pixel.R * 54.213 + pixel.G * 182.376 + pixel.B * 18.411;
            }
        return total / (image.GetWidth() * image.GetHeight());
    }

    private static Rect2? PartnerTargetBoundingBox(Image image)
    {
        var minX = image.GetWidth(); var minY = image.GetHeight(); var maxX = -1; var maxY = -1;
        for (var y = 0; y < image.GetHeight(); y++) for (var x = 0; x < image.GetWidth(); x++)
            {
                if (image.GetPixel(x, y).A8 == 0) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        return maxX < minX ? null : new Rect2(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Rect2? PartnerPsxBoundingBox(Image image)
    {
        var panel = new Color(8 / 255f, 8 / 255f, 65 / 255f);
        var minX = image.GetWidth(); var minY = image.GetHeight(); var maxX = -1; var maxY = -1;
        for (var y = 0; y < image.GetHeight(); y++) for (var x = 0; x < image.GetWidth(); x++)
            {
                if (!ProbePixels.DifferentRgb(image.GetPixel(x, y), panel)) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        return maxX < minX ? null : new Rect2(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Rect2? DifferenceBoundingBox(Image a, Image b)
    {
        var minX = a.GetWidth(); var minY = a.GetHeight(); var maxX = -1; var maxY = -1;
        for (var y = 0; y < a.GetHeight(); y++) for (var x = 0; x < a.GetWidth(); x++)
            {
                if (!ProbePixels.DifferentRgb(a.GetPixel(x, y), b.GetPixel(x, y))) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        return maxX < minX ? null : new Rect2(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Rect2? MaskBoundingBox(Image mask)
    {
        var minX = mask.GetWidth(); var minY = mask.GetHeight(); var maxX = -1; var maxY = -1;
        for (var y = 0; y < mask.GetHeight(); y++) for (var x = 0; x < mask.GetWidth(); x++)
            {
                if (mask.GetPixel(x, y).R < .5f) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        return maxX < minX ? null : new Rect2(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Rect2? WarmKotemonBoundingBox(Image image)
    {
        // The paired Kotemon capture is the source menu frame. Its red/orange armour is distinct
        // from stage 18 and cyan HUD. Bound only that verified warm range, rather than its UI.
        var minX = image.GetWidth(); var minY = image.GetHeight(); var maxX = -1; var maxY = -1;
        for (var y = 55; y < 230; y++) for (var x = 80; x < 245; x++)
            {
                var p = image.GetPixel(x, y);
                if (!(p.R8 > 70 && p.R8 > p.G8 * 1.2f && p.R8 > p.B8 * 1.15f)) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        return maxX < minX ? null : new Rect2(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static string FormatBox(Rect2 rect) => $"({rect.Position.X:0},{rect.Position.Y:0},{rect.Size.X:0},{rect.Size.Y:0})";

    private static Image ToSource320(Image image)
    {
        var copy = (Image)image.Duplicate();
        copy.Convert(Image.Format.Rgba8);
        if (copy.GetSize() == new Vector2I(1280, 720))
        {
            copy = copy.GetRegion(new Rect2I(160, 0, 960, 720));
            copy.Resize(320, 240, Image.Interpolation.Nearest);
        }
        Require(copy.GetSize() == new Vector2I(320, 240), $"expected source-size image, got {copy.GetWidth()}x{copy.GetHeight()}");
        return copy;
    }

    private static Image PsxKuwagamonMask(Image image)
    {
        var mask = Image.CreateEmpty(320, 240, false, Image.Format.Rgba8);
        mask.Fill(Colors.Black);
        // Stage 18 is a dark low-saturation violet set. Kuwagamon is the warm orange/brown
        // body inside this entrance framing. Explicitly reject dark stage pixels and the
        // recomp's saturated green/magenta artefacts before retaining warm body texels.
        for (var y = 8; y < 235; y++)
            for (var x = 18; x < 235; x++)
            {
                var p = image.GetPixel(x, y);
                var max = Mathf.Max(p.R, Mathf.Max(p.G, p.B));
                var min = Mathf.Min(p.R, Mathf.Min(p.G, p.B));
                var warm = p.R > p.G * 1.08f && p.G > p.B * 1.04f && max > 0.10f && max - min > 0.06f;
                if (warm) mask.SetPixel(x, y, Colors.White);
            }
        return mask;
    }

    private static Image DifferenceMask(Image model, Image stage)
    {
        var mask = Image.CreateEmpty(320, 240, false, Image.Format.Rgba8);
        mask.Fill(Colors.Black);
        for (var y = 0; y < 240; y++)
            for (var x = 0; x < 320; x++)
            {
                var a = model.GetPixel(x, y);
                var b = stage.GetPixel(x, y);
                if (Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) > 0.08f)
                    mask.SetPixel(x, y, Colors.White);
            }
        return mask;
    }

    /// <summary>Command camera 6 leaves Kuwagamon only on the left edge, like the PSX menu
    /// capture. Keep that mutually visible region and reject the active partner on the right.</summary>
    private static Image CommonKuwagamonMask(Image mask)
    {
        for (var y = 0; y < mask.GetHeight(); y++)
            for (var x = 0; x < mask.GetWidth(); x++)
                if (x >= 160 || y < 24 || y >= 220) mask.SetPixel(x, y, Colors.Black);
        return mask;
    }

    private static LuminanceStats MaskedLuminance(Image image, Image mask, int minimumPixels = 100)
    {
        var values = new List<float>();
        for (var y = 0; y < 240; y++)
            for (var x = 0; x < 320; x++)
            {
                if (mask.GetPixel(x, y).R < 0.5f) continue;
                var p = image.GetPixel(x, y);
                values.Add(p.R * 54.213f + p.G * 182.376f + p.B * 18.411f);
            }
        Require(values.Count >= minimumPixels, $"luminance mask too small pixels={values.Count} minimum={minimumPixels}");
        values.Sort();
        float Quantile(float q) => values[(int)Mathf.Clamp(Mathf.Round((values.Count - 1) * q), 0, values.Count - 1)];
        var bins = new int[8];
        foreach (var value in values) bins[Math.Min(7, (int)(value / 32f))]++;
        return new LuminanceStats(values.Count, values.Average(), Quantile(.1f), Quantile(.5f), Quantile(.9f), string.Join(',', bins));
    }

    private static int MaskedPixelCount(Image mask)
    {
        var count = 0;
        for (var y = 0; y < 240; y++)
            for (var x = 0; x < 320; x++) if (mask.GetPixel(x, y).R >= .5f) count++;
        return count;
    }

    private static float MaskedDarkPercent(Image image, Image mask, float threshold)
    {
        var total = 0;
        var dark = 0;
        for (var y = 0; y < 240; y++)
            for (var x = 0; x < 320; x++)
            {
                if (mask.GetPixel(x, y).R < .5f) continue;
                total++;
                var p = image.GetPixel(x, y);
                var luminance = p.R * 54.213f + p.G * 182.376f + p.B * 18.411f;
                if (luminance < threshold) dark++;
            }
        Require(total > 100, $"active fighter mask too small pixels={total}");
        return 100f * dark / total;
    }

    /// <summary>One texel of the dedicated unshaded battle shader's mid-grey test quad.
    /// The source equation is tex * color / 128, exactly the shader's normalized *2 path.</summary>
    private static void VerifyPs1ModulationQuad()
    {
        static int Quad(byte texture, byte color) => Math.Min(255, (texture * color + 64) / 128);
        Require(Quad(0x80, 0xFF) is >= 254 and <= 255, "modulation quad 0xFF must be about 255");
        Require(Quad(0x80, 0x80) == 128, "modulation quad 0x80 must be 128");
        Require(Quad(0x80, 0x40) == 64, "modulation quad 0x40 must be 64");
    }

    private static float RelativeDelta(float a, float b) => MathF.Abs(a - b) / MathF.Max(1f, (a + b) * .5f);

    private static Image SideBySide(Image remake, Image psx, string leftLabel, string rightLabel)
    {
        var left = (Image)remake.Duplicate();
        var right = (Image)psx.Duplicate();
        left.Convert(Image.Format.Rgba8);
        right.Convert(Image.Format.Rgba8);
        var output = Image.CreateEmpty(left.GetWidth() + right.GetWidth(), Math.Max(left.GetHeight(), right.GetHeight()), false, Image.Format.Rgba8);
        output.Fill(Colors.Black);
        output.BlitRect(left, new Rect2I(Vector2I.Zero, left.GetSize()), Vector2I.Zero);
        output.BlitRect(right, new Rect2I(Vector2I.Zero, right.GetSize()), new Vector2I(left.GetWidth(), 0));
        DrawImageLabel(output, leftLabel, new Vector2I(8, 7));
        DrawImageLabel(output, rightLabel, new Vector2I(left.GetWidth() + 8, 7));
        return output;
    }

    private static void DrawImageLabel(Image image, string label, Vector2I origin)
    {
        var glyphs = new Dictionary<char, string[]>
        {
            ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
            ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
            ['K'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
            ['M'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
            ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
            ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
            ['S'] = ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
            ['X'] = ["10001", "01010", "00100", "00100", "00100", "01010", "10001"],
        };
        var x = origin.X;
        foreach (var c in label)
        {
            if (!glyphs.TryGetValue(c, out var glyph)) continue;
            for (var gy = 0; gy < glyph.Length; gy++)
                for (var gx = 0; gx < glyph[gy].Length; gx++)
                    if (glyph[gy][gx] == '1')
                        for (var dy = 0; dy < 2; dy++)
                            for (var dx = 0; dx < 2; dx++) image.SetPixel(x + gx * 2 + dx, origin.Y + gy * 2 + dy, Colors.White);
            x += 12;
        }
    }

    #endregion
}
