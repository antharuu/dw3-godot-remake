using System.Text.Json.Nodes;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Differential regression suite for FightReportController.
/// Reads 300+ fabricated test vectors from Vectors/fight-report-controller.json,
/// replays each against the current implementation, and asserts that the output
/// matches the recorded expected values.
///
/// Vectors were generated from the current implementation with fabricated inputs.
/// All inputs are invented; no game values are embedded.
/// </summary>
public sealed class FightReportControllerVectorTests
{
    private static readonly string VectorsPath = Path.Combine(
        AppContext.BaseDirectory, "Vectors", "fight-report-controller.json");

    // ------------------------------------------------------------------ helpers

    private static int[] ToIntArray(JsonNode? node)
    {
        var arr = node!.AsArray();
        var r = new int[arr.Count];
        for (int i = 0; i < arr.Count; i++) r[i] = arr[i]!.GetValue<int>();
        return r;
    }

    private static FightReportPartner PartnerFromJson(JsonNode n)
        => new(
            n["partyIndex"]!.GetValue<int>(),
            n["partnerId"]!.GetValue<int>(),
            n["fighterId"]!.GetValue<int>(),
            n["levelBefore"]!.GetValue<int>(),
            n["expBefore"]!.GetValue<int>(),
            n["expGained"]!.GetValue<int>(),
            n["levelAfter"]!.GetValue<int>(),
            ToIntArray(n["slotDigimonIds"]));

    private static FightReportInput InputFromJson(JsonNode c)
    {
        var partnersNode = c["input"]!["partners"]!.AsArray();
        var partners = new List<FightReportPartner>();
        foreach (var pn in partnersNode) partners.Add(PartnerFromJson(pn!));

        Dictionary<int, int>? tokenCounts = null;
        var tcNode = c["input"]!["messageTokenCounts"];
        if (tcNode is JsonObject tcObj)
        {
            tokenCounts = [];
            foreach (var kv in tcObj) tokenCounts[int.Parse(kv.Key)] = kv.Value!.GetValue<int>();
        }

        return new FightReportInput(
            partners,
            c["input"]!["money"]!.GetValue<int>(),
            c["input"]!["item"]!.GetValue<int>(),
            tokenCounts);
    }

    // ------------------------------------------------------------------ count check

    [Fact]
    public void FightReportControllerVectorCount_AtLeast300()
    {
        Assert.True(File.Exists(VectorsPath),
            $"Vectors file not found: {VectorsPath}");

        var doc = JsonNode.Parse(File.ReadAllText(VectorsPath))!;
        int count = doc["cases"]!.AsArray().Count;
        Assert.True(count >= 300,
            $"Expected at least 300 vectors, got {count}");
    }

    // ------------------------------------------------------------------ main replay

    [Fact]
    public void FightReportControllerAllVectorsMatchCurrentImplementation()
    {
        Assert.True(File.Exists(VectorsPath),
            $"Vectors file not found: {VectorsPath}");

        var doc = JsonNode.Parse(File.ReadAllText(VectorsPath))!;
        var cases = doc["cases"]!.AsArray();
        var errors = new List<string>();

        foreach (var c in cases)
        {
            int id = c!["id"]!.GetValue<int>();
            string fn = c["fn"]!.GetValue<string>();

            try
            {
                switch (fn)
                {
                    case "RollStep":
                        VerifyRollStep(c, id, errors);
                        break;
                    case "StepSequence":
                        VerifyStepSequence(c, id, errors);
                        break;
                    default:
                        errors.Add($"[{id}] Unknown fn '{fn}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"[{id}] {fn} threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
            Assert.Fail($"{errors.Count} vector(s) failed:\n" + string.Join("\n", errors.Take(30)));
    }

    // ------------------------------------------------------------------ per-fn verifiers

    private static void VerifyRollStep(JsonNode c, int id, List<string> errors)
    {
        int targetExp = c["targetExp"]!.GetValue<int>();
        int expected = c["expected"]!.GetValue<int>();
        int got = FightReportController.RollStep(targetExp);
        if (got != expected)
            errors.Add($"[{id}] RollStep({targetExp}): got {got} expected {expected}");
    }

    private static void VerifyStepSequence(JsonNode c, int id, List<string> errors)
    {
        var input = InputFromJson(c);
        int stepCount = c["stepCount"]!.GetValue<int>();
        int crossEvery = c["crossEvery"]!.GetValue<int>();

        var ctrl = new FightReportController(input);
        for (int frame = 1; frame <= stepCount; frame++)
        {
            bool cross = crossEvery > 0 && frame % crossEvery == 0;
            ctrl.Step(cross);
        }

        var v = ctrl.View;
        string pfx = $"[{id}]";

        // Scalar view fields
        int expHeaderFade = c["out_headerFade"]!.GetValue<int>();
        int expFooterFade = c["out_footerFade"]!.GetValue<int>();
        int expBgScroll = c["out_bgScroll"]!.GetValue<int>();
        int expExitFade = c["out_exitFade"]!.GetValue<int>();
        bool expDone = c["out_done"]!.GetValue<bool>();

        if (v.HeaderFade != expHeaderFade) errors.Add($"{pfx} HeaderFade: got {v.HeaderFade} expected {expHeaderFade}");
        if (v.FooterFade != expFooterFade) errors.Add($"{pfx} FooterFade: got {v.FooterFade} expected {expFooterFade}");
        if (v.BgScroll != expBgScroll) errors.Add($"{pfx} BgScroll: got {v.BgScroll} expected {expBgScroll}");
        if (v.ExitFade != expExitFade) errors.Add($"{pfx} ExitFade: got {v.ExitFade} expected {expExitFade}");
        if (v.Done != expDone) errors.Add($"{pfx} Done: got {v.Done} expected {expDone}");

        // Partners
        var expPartnersArr = c["out_partners"]!.AsArray();
        if (v.Partners.Count != expPartnersArr.Count)
        {
            errors.Add($"{pfx} Partners.Count: got {v.Partners.Count} expected {expPartnersArr.Count}");
        }
        else
        {
            for (int pi = 0; pi < v.Partners.Count; pi++)
            {
                var ep = expPartnersArr[pi]!;
                var gp = v.Partners[pi];
                string pp = $"{pfx}[P{pi}]";
                if (gp.PartyIndex != ep["partyIndex"]!.GetValue<int>()) errors.Add($"{pp} partyIndex");
                if (gp.PartnerId != ep["partnerId"]!.GetValue<int>()) errors.Add($"{pp} partnerId");
                if (gp.FighterId != ep["fighterId"]!.GetValue<int>()) errors.Add($"{pp} fighterId");
                if (gp.Fade != ep["fade"]!.GetValue<int>()) errors.Add($"{pp} Fade: got {gp.Fade} expected {ep["fade"]!.GetValue<int>()}");
                if (gp.Selected != ep["selected"]!.GetValue<bool>()) errors.Add($"{pp} Selected: got {gp.Selected} expected {ep["selected"]!.GetValue<bool>()}");
                if (gp.CursorFrame != ep["cursorFrame"]!.GetValue<int>()) errors.Add($"{pp} CursorFrame: got {gp.CursorFrame} expected {ep["cursorFrame"]!.GetValue<int>()}");
                if (gp.LevelBlinkFrame != ep["levelBlinkFrame"]!.GetValue<int>()) errors.Add($"{pp} LevelBlinkFrame");
                if (gp.ShownExp != ep["shownExp"]!.GetValue<int>()) errors.Add($"{pp} ShownExp: got {gp.ShownExp} expected {ep["shownExp"]!.GetValue<int>()}");
                if (gp.AnimationFrame != ep["animationFrame"]!.GetValue<int>()) errors.Add($"{pp} AnimationFrame");

                // Slots
                var expSlots = ep["slots"]!.AsArray();
                if (gp.Slots.Count != expSlots.Count)
                {
                    errors.Add($"{pp} Slots.Count: got {gp.Slots.Count} expected {expSlots.Count}");
                }
                else
                {
                    for (int si = 0; si < gp.Slots.Count; si++)
                    {
                        var es = expSlots[si]!;
                        var gs = gp.Slots[si];
                        if (gs.DigimonId != es["digimonId"]!.GetValue<int>()) errors.Add($"{pp}[S{si}] DigimonId");
                        if (gs.Visible != es["visible"]!.GetValue<bool>()) errors.Add($"{pp}[S{si}] Visible");
                        if (gs.ExpApplied != es["expApplied"]!.GetValue<bool>()) errors.Add($"{pp}[S{si}] ExpApplied");
                        if (gs.BlinkFrame != es["blinkFrame"]!.GetValue<int>()) errors.Add($"{pp}[S{si}] BlinkFrame");
                    }
                }
            }
        }

        // Message
        var expMsgNode = c["out_message"];
        bool expMsgNull = expMsgNode is null || expMsgNode is JsonValue jv && jv.GetValue<object?>() is null;
        // JSON null can appear as JsonValue null or as a missing key
        bool gotMsgNull = v.Message is null;

        if (expMsgNull && !gotMsgNull)
        {
            errors.Add($"{pfx} Message: got non-null expected null");
        }
        else if (!expMsgNull && gotMsgNull)
        {
            errors.Add($"{pfx} Message: got null expected non-null");
        }
        else if (!expMsgNull && v.Message is not null)
        {
            var em = expMsgNode!;
            var gm = v.Message;
            if (gm.Entry != em["entry"]!.GetValue<int>()) errors.Add($"{pfx} Message.Entry: got {gm.Entry} expected {em["entry"]!.GetValue<int>()}");
            if (gm.TokensRevealed != em["tokensRevealed"]!.GetValue<int>()) errors.Add($"{pfx} Message.TokensRevealed: got {gm.TokensRevealed} expected {em["tokensRevealed"]!.GetValue<int>()}");
            if (gm.TokenCount != em["tokenCount"]!.GetValue<int>()) errors.Add($"{pfx} Message.TokenCount");
            if (gm.ArrowVisible != em["arrowVisible"]!.GetValue<bool>()) errors.Add($"{pfx} Message.ArrowVisible: got {gm.ArrowVisible} expected {em["arrowVisible"]!.GetValue<bool>()}");
            if (gm.ArrowFrame != em["arrowFrame"]!.GetValue<int>()) errors.Add($"{pfx} Message.ArrowFrame: got {gm.ArrowFrame} expected {em["arrowFrame"]!.GetValue<int>()}");
        }

        // Logs
        var expLogsArr = c["out_logs"]!.AsArray();
        var expLogs = expLogsArr.Select(n => n!.GetValue<string>()).ToList();
        var gotLogs = ctrl.Logs.ToList();
        if (!gotLogs.SequenceEqual(expLogs))
            errors.Add($"{pfx} Logs: got [{string.Join(",", gotLogs)}] expected [{string.Join(",", expLogs)}]");
    }
}
