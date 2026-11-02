using System.Text.Json;
using System.Text.Json.Nodes;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Generates tests/Dw3.Game.Tests/Vectors/fight-report-controller.json when the
/// environment variable GENERATE_VECTORS=1 is set.  Run via:
///   GENERATE_VECTORS=1 dotnet test --filter "GenerateFightReportControllerVectors"
///
/// The generated file records fabricated (invented) inputs with the outputs produced by
/// the current FightReportController implementation.  No game values are embedded.
/// </summary>
public sealed class FightReportControllerVectorGenerator
{
    // ------------------------------------------------------------------ helpers

    /// <summary>Build a FightReportPartner with invented values.</summary>
    private static FightReportPartner Partner(
        int partyIndex, int partnerId, int fighterId,
        (int Before, int After) levels, int expBefore, int expGained, int[] slotIds)
        => new(partyIndex, partnerId, fighterId, levels.Before, expBefore, expGained, levels.After, slotIds);

    /// <summary>Serialize a FightReportPartner to JSON input fields.</summary>
    private static JsonObject PartnerToJson(FightReportPartner p)
        => new()
        {
            ["partyIndex"] = p.PartyIndex,
            ["partnerId"] = p.PartnerId,
            ["fighterId"] = p.FighterId,
            ["levelBefore"] = p.LevelBefore,
            ["expBefore"] = p.ExpBefore,
            ["expGained"] = p.ExpGained,
            ["levelAfter"] = p.LevelAfter,
            ["slotDigimonIds"] = IntArrayToJson([.. p.SlotDigimonIds]),
        };

    private static JsonArray IntArrayToJson(int[] arr)
    {
        var a = new JsonArray();
        foreach (var v in arr) a.Add(v);
        return a;
    }

    private static JsonArray StringArrayToJson(IEnumerable<string> strs)
    {
        var a = new JsonArray();
        foreach (var s in strs) a.Add(s);
        return a;
    }

    /// <summary>Serialize FightReportSlotView list (exactly 3 entries) to JSON.</summary>
    private static JsonArray SlotsToJson(IReadOnlyList<FightReportSlotView> slots)
    {
        var a = new JsonArray();
        foreach (var s in slots)
            a.Add(new JsonObject
            {
                ["digimonId"] = s.DigimonId,
                ["visible"] = s.Visible,
                ["expApplied"] = s.ExpApplied,
                ["blinkFrame"] = s.BlinkFrame,
            });
        return a;
    }

    private static JsonObject? MessageToJson(FightReportMessage? m)
    {
        if (m is null) return null;
        var subs = new JsonArray();
        foreach (var v in m.Substitutions) subs.Add(v);
        return new JsonObject
        {
            ["entry"] = m.Entry,
            ["substitutions"] = subs,
            ["tokensRevealed"] = m.TokensRevealed,
            ["tokenCount"] = m.TokenCount,
            ["arrowVisible"] = m.ArrowVisible,
            ["arrowFrame"] = m.ArrowFrame,
        };
    }

    private static JsonObject ViewToJson(FightReportView v)
    {
        var partners = new JsonArray();
        foreach (var p in v.Partners)
            partners.Add(new JsonObject
            {
                ["partyIndex"] = p.PartyIndex,
                ["partnerId"] = p.PartnerId,
                ["fighterId"] = p.FighterId,
                ["fade"] = p.Fade,
                ["selected"] = p.Selected,
                ["cursorFrame"] = p.CursorFrame,
                ["levelBlinkFrame"] = p.LevelBlinkFrame,
                ["shownExp"] = p.ShownExp,
                ["animationFrame"] = p.AnimationFrame,
                ["slots"] = SlotsToJson(p.Slots),
            });

        var o = new JsonObject
        {
            ["headerFade"] = v.HeaderFade,
            ["footerFade"] = v.FooterFade,
            ["bgScroll"] = v.BgScroll,
            ["partners"] = partners,
            ["exitFade"] = v.ExitFade,
            ["done"] = v.Done,
        };
        var msg = MessageToJson(v.Message);
        o["message"] = msg is null ? null : msg;
        return o;
    }

    // ------------------------------------------------------------------ case builders

    /// Build a RollStep vector.
    private static JsonObject RollStepCase(int id, int targetExp)
    {
        int result = FightReportController.RollStep(targetExp);
        return new JsonObject
        {
            ["id"] = id,
            ["fn"] = "RollStep",
            ["targetExp"] = targetExp,
            ["expected"] = result,
        };
    }

    /// <summary>
    /// Build a StepSequence vector: construct a controller, run stepCount frames,
    /// pressing Cross on every frame where frameNumber % crossEvery == 0 (when crossEvery > 0).
    /// Records the final View and Logs.
    /// </summary>
    private static JsonObject StepCase(
        int id,
        FightReportInput input,
        int stepCount,
        int crossEvery,      // 0 = never, positive = press on multiples of this
        string label = "")
    {
        var ctrl = new FightReportController(input);
        for (int frame = 1; frame <= stepCount; frame++)
        {
            bool cross = crossEvery > 0 && frame % crossEvery == 0;
            ctrl.Step(cross);
        }

        var inputPartners = new JsonArray();
        foreach (var p in input.Partners) inputPartners.Add(PartnerToJson(p));

        JsonObject? tokenCounts = null;
        if (input.MessageTokenCounts is not null)
        {
            tokenCounts = [];
            foreach (var kv in input.MessageTokenCounts) tokenCounts[kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)] = kv.Value;
        }

        var o = new JsonObject
        {
            ["id"] = id,
            ["fn"] = "StepSequence",
            ["label"] = label,
            ["input"] = new JsonObject
            {
                ["partners"] = inputPartners,
                ["money"] = input.Money,
                ["item"] = input.Item,
                ["messageTokenCounts"] = tokenCounts is null ? null : tokenCounts,
            },
            ["stepCount"] = stepCount,
            ["crossEvery"] = crossEvery,
        };

        var view = ViewToJson(ctrl.View);
        foreach (var (k, v) in view)
            o["out_" + k] = v?.DeepClone();

        o["out_logs"] = StringArrayToJson(ctrl.Logs);

        return o;
    }

    // ------------------------------------------------------------------ standard inputs

    /// Single partner, exp=4, no level up, money=50, item=0.
    private static FightReportInput InputA() => new(
        [Partner(0, 1, 383, (25, 25), 0, 4, [])],
        Money: 50, Item: 0);

    /// Single partner, exp=4, level up 25→26, money=50, item=0.
    private static FightReportInput InputB() => new(
        [Partner(0, 1, 383, (25, 26), 0, 4, [])],
        Money: 50, Item: 0);

    /// Single partner, exp=0 (skipped), money=100, item=0.
    private static FightReportInput InputC() => new(
        [Partner(0, 1, 383, (25, 25), 0, 0, [])],
        Money: 100, Item: 0);

    /// Single partner, exp=100, no level up, money=0, item=0.
    private static FightReportInput InputD() => new(
        [Partner(0, 1, 383, (25, 25), 0, 100, [])],
        Money: 0, Item: 0);

    /// Single partner, exp=4, money=50, item=3.
    private static FightReportInput InputE() => new(
        [Partner(0, 1, 383, (25, 25), 0, 4, [])],
        Money: 50, Item: 3);

    /// Two partners, both with exp, money=100, item=0.
    private static FightReportInput InputF() => new(
        [
            Partner(0, 1, 383, (25, 25), 0,  4, []),
            Partner(1, 2, 400, (30, 30), 50, 10, [201, 202]),
        ],
        Money: 100, Item: 0);

    /// Two partners: first exp=0 (skipped), second exp=10, money=50, item=0.
    private static FightReportInput InputG() => new(
        [
            Partner(0, 1, 383, (25,  25), 0, 0, []),
            Partner(1, 2, 400, (20, 21), 0, 10, [201]),
        ],
        Money: 50, Item: 0);

    /// Three partners, mixed: one skipped, one level up, one normal.
    private static FightReportInput InputH() => new(
        [
            Partner(0, 1, 383, (25,  25), 0,  0, []),
            Partner(1, 2, 400, (30,  31), 50, 8, [201]),
            Partner(2, 3, 450, (15,  15), 0,  4, [301, 302, 303]),
        ],
        Money: 200, Item: 7);

    /// Single partner, 3 slot digimons, exp=50, no level up.
    private static FightReportInput InputI() => new(
        [Partner(0, 1, 383, (25, 25), 0, 50, [101, 102, 103])],
        Money: 75, Item: 0);

    /// Single partner, custom token counts (3 tokens for entry 2).
    private static FightReportInput InputJ() => new(
        [Partner(0, 1, 383, (25, 25), 0, 4, [])],
        Money: 50, Item: 0,
        MessageTokenCounts: new Dictionary<int, int> { { 2, 3 }, { 10, 2 } });

    /// Single partner, high exp (999990), no level up.
    private static FightReportInput InputK() => new(
        [Partner(0, 1, 383, (50, 50), 500000, 250, [101])],
        Money: 9999, Item: 0);

    // ------------------------------------------------------------------ generator

    [Fact]
    public void GenerateFightReportControllerVectors()
    {
        if (Environment.GetEnvironmentVariable("GENERATE_VECTORS") != "1")
            return;

        var cases = new JsonArray();
        int id = 1;

        // ---- RollStep cases ----
        int[] rollInputs = [-100, -1, 0, 1, 2, 4, 9, 10, 11, 50, 99, 100, 101, 250, 500,
                             999, 1000, 1001, 4999, 5000, 9999, 10000, 99999, 100000, 999999];
        foreach (var ri in rollInputs)
            cases.Add(RollStepCase(id++, ri));

        // ---- StepSequence cases ----

        // A: single partner, exp=4, no level-up, money=50, item=0
        // Capture at many frame counts to test all report phases
        int[] framesA = [1, 2, 3, 5, 10, 14, 15, 20, 25, 26, 30, 37, 38, 40, 44, 50,
                          60, 61, 63, 65, 69, 70, 72, 73, 78, 79, 80, 87, 100, 101,
                          107, 108, 120, 138, 139, 200];
        foreach (var f in framesA)
            cases.Add(StepCase(id++, InputA(), f, crossEvery: 20, label: $"A-frame{f}"));

        // A with no cross presses (report blocks at first message)
        foreach (var f in new[] { 50, 100, 200, 500, 1000, 10000 })
            cases.Add(StepCase(id++, InputA(), f, crossEvery: 0, label: $"A-nocross-frame{f}"));

        // A with aggressive cross (every 5 frames)
        foreach (var f in new[] { 37, 50, 75, 100, 150, 200 })
            cases.Add(StepCase(id++, InputA(), f, crossEvery: 5, label: $"A-cross5-frame{f}"));

        // B: single partner with level up
        foreach (var f in new[] { 60, 70, 80, 90, 100, 130, 150, 200, 250 })
            cases.Add(StepCase(id++, InputB(), f, crossEvery: 20, label: $"B-frame{f}"));

        // C: single partner, exp=0 (skipped), goes straight to money
        foreach (var f in new[] { 14, 25, 30, 40, 50, 60, 70, 80, 100, 120, 130 })
            cases.Add(StepCase(id++, InputC(), f, crossEvery: 20, label: $"C-frame{f}"));

        // D: exp=100 (3-digit, step=111), check RollStep effect
        foreach (var f in new[] { 60, 65, 70, 75, 80, 100, 120 })
            cases.Add(StepCase(id++, InputD(), f, crossEvery: 20, label: $"D-frame{f}"));

        // E: item reward
        foreach (var f in new[] { 90, 100, 110, 120, 130, 150, 200 })
            cases.Add(StepCase(id++, InputE(), f, crossEvery: 20, label: $"E-frame{f}"));

        // F: two partners
        foreach (var f in new[] { 25, 50, 75, 100, 125, 150, 175, 200, 225, 250, 275, 300 })
            cases.Add(StepCase(id++, InputF(), f, crossEvery: 20, label: $"F-frame{f}"));

        // G: first partner skipped
        foreach (var f in new[] { 25, 50, 75, 100, 125, 150, 200 })
            cases.Add(StepCase(id++, InputG(), f, crossEvery: 20, label: $"G-frame{f}"));

        // H: three partners, item reward
        foreach (var f in new[] { 50, 100, 150, 200, 250, 300, 350, 400 })
            cases.Add(StepCase(id++, InputH(), f, crossEvery: 20, label: $"H-frame{f}"));

        // I: 3-slot partner, exp=50 (step=11)
        foreach (var f in new[] { 60, 70, 75, 80, 100, 130 })
            cases.Add(StepCase(id++, InputI(), f, crossEvery: 20, label: $"I-frame{f}"));

        // J: custom token counts
        foreach (var f in new[] { 40, 50, 60, 70, 80, 90, 100, 120, 150 })
            cases.Add(StepCase(id++, InputJ(), f, crossEvery: 20, label: $"J-frame{f}"));

        // K: high exp, check RollStep(999999)
        foreach (var f in new[] { 60, 65, 70, 75, 80, 100, 130 })
            cases.Add(StepCase(id++, InputK(), f, crossEvery: 20, label: $"K-frame{f}"));

        // Cross-every-1 (every single frame) — fastest possible completion
        cases.Add(StepCase(id++, InputA(), 200, crossEvery: 1, label: "A-cross1-200"));
        cases.Add(StepCase(id++, InputB(), 200, crossEvery: 1, label: "B-cross1-200"));
        cases.Add(StepCase(id++, InputH(), 300, crossEvery: 1, label: "H-cross1-300"));

        // Cross every 50 frames (slow cadence)
        foreach (var f in new[] { 100, 150, 200, 300 })
            cases.Add(StepCase(id++, InputA(), f, crossEvery: 50, label: $"A-cross50-frame{f}"));

        // Very high step count with no cross — report blocked at EXP message
        cases.Add(StepCase(id++, InputA(), 50_000, crossEvery: 0, label: "A-nocross-long"));
        cases.Add(StepCase(id++, InputC(), 50_000, crossEvery: 0, label: "C-nocross-long"));

        // Various expBefore values to test ShownExp initial value
        foreach (var expBefore in new[] { 0, 50, 999, 12345, 999990 })
        {
            var input = new FightReportInput(
                [Partner(0, 1, 383, (25, 25), expBefore, 4, [])],
                Money: 50, Item: 0);
            cases.Add(StepCase(id++, input, 70, crossEvery: 20, label: $"expBefore{expBefore}"));
        }

        // ExpGained = 1 (single digit)
        cases.Add(StepCase(id++, new FightReportInput(
            [Partner(0, 1, 383, (25, 25), 0, 1, [])], 50, 0), 140, 20, "expGained1"));

        // ExpGained = 9
        cases.Add(StepCase(id++, new FightReportInput(
            [Partner(0, 1, 383, (25, 25), 0, 9, [])], 50, 0), 140, 20, "expGained9"));

        // ExpGained = 10 (two digits, step=11)
        cases.Add(StepCase(id++, new FightReportInput(
            [Partner(0, 1, 383, (25, 25), 0, 10, [])], 50, 0), 140, 20, "expGained10"));

        // ExpGained = 11
        cases.Add(StepCase(id++, new FightReportInput(
            [Partner(0, 1, 383, (25, 25), 0, 11, [])], 50, 0), 140, 20, "expGained11"));

        // ExpGained = 999 (three digits, step=111)
        cases.Add(StepCase(id++, new FightReportInput(
            [Partner(0, 1, 383, (25, 25), 0, 999, [])], 50, 0), 140, 20, "expGained999"));

        // ExpGained = 1000 (four digits, step=1111)
        cases.Add(StepCase(id++, new FightReportInput(
            [Partner(0, 1, 383, (25, 25), 0, 1000, [])], 50, 0), 140, 20, "expGained1000"));

        // TargetExp capped at 999999 (expBefore+expGained > 999999)
        cases.Add(StepCase(id++, new FightReportInput(
            [Partner(0, 1, 383, (25, 25), 999990, 20, [])], 50, 0), 140, 20, "targetExpCap"));

        // Money=0, Item=0
        cases.Add(StepCase(id++, new FightReportInput(
            [Partner(0, 1, 383, (25, 25), 0, 4, [])], 0, 0), 140, 20, "money0-item0"));

        // Money=9999, Item=99
        cases.Add(StepCase(id++, new FightReportInput(
            [Partner(0, 1, 383, (25, 25), 0, 4, [])], 9999, 99), 200, 20, "money9999-item99"));

        // Three partners all with ExpGained=0 (all skipped, go straight to money)
        cases.Add(StepCase(id++, new FightReportInput(
            [
                Partner(0, 1, 383, (25, 25), 0, 0, []),
                Partner(1, 2, 400, (30, 30), 0, 0, []),
                Partner(2, 3, 450, (15, 15), 0, 0, []),
            ], 50, 0), 120, 20, "allSkipped"));

        // All partners skipped but item present
        cases.Add(StepCase(id++, new FightReportInput(
            [
                Partner(0, 1, 383, (25, 25), 0, 0, []),
                Partner(1, 2, 400, (30, 30), 0, 0, []),
            ], 50, 5), 200, 20, "allSkippedWithItem"));

        // MessageTokenCounts with entry 10 = 5 (slow money message reveal)
        cases.Add(StepCase(id++, new FightReportInput(
            [Partner(0, 1, 383, (25, 25), 0, 4, [])], 50, 0,
            new Dictionary<int, int> { { 10, 5 } }), 160, 20, "tokenCount10-5"));

        // Padding: varied single-partner frame captures to reach 300
        int padFrame = 50;
        while (cases.Count < 300)
        {
            int extra = cases.Count;
            int expG = 1 + extra % 50;
            int money = 10 + extra % 200;
            int fr = padFrame + (extra % 8) * 10;
            var inputPad = new FightReportInput(
                [Partner(0, 1, 383, (25, 25), extra % 1000, expG, [])],
                Money: money, Item: 0);
            cases.Add(StepCase(id++, inputPad, fr, crossEvery: 20, label: $"pad{extra}"));
        }

        var root = new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["seed"] = 42,
                ["count"] = cases.Count,
                ["note"] = "Fabricated vectors generated from the current FightReportController implementation. All inputs are invented; no game values are embedded.",
            },
            ["cases"] = cases,
        };

        var outPath = Path.Combine(
            Path.GetDirectoryName(typeof(FightReportControllerVectorGenerator).Assembly.Location)!,
            "Vectors", "fight-report-controller.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        Assert.True(cases.Count >= 300, $"Generated {cases.Count} vectors, need at least 300.");
    }
}
