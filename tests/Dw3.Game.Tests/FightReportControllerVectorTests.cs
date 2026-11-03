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

    // ------------------------------------------------------------------ count check
    [Fact]
    public void FightReportControllerVectorCount_AtLeast300()
    {
        Assert.True(
            File.Exists(VectorsPath),
            $"Vectors file not found: {VectorsPath}");

        var doc = JsonNode.Parse(File.ReadAllText(VectorsPath))!;
        int count = doc["cases"]!.AsArray().Count;
        Assert.True(
            count >= 300,
            $"Expected at least 300 vectors, got {count}");
    }

    // ------------------------------------------------------------------ main replay
    [Fact]
    public void FightReportControllerAllVectorsMatchCurrentImplementation()
    {
        Assert.True(
            File.Exists(VectorsPath),
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
        {
            Assert.Fail($"{errors.Count} vector(s) failed:\n" + string.Join("\n", errors.Take(30)));
        }
    }

    // ------------------------------------------------------------------ helpers
    private static int[] ToIntArray(JsonNode? node)
    {
        var arr = node!.AsArray();
        var r = new int[arr.Count];
        for (int i = 0; i < arr.Count; i++)
        {
            r[i] = arr[i]!.GetValue<int>();
        }

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
        foreach (var pn in partnersNode)
        {
            partners.Add(PartnerFromJson(pn!));
        }

        Dictionary<int, int>? tokenCounts = null;
        var tcNode = c["input"]!["messageTokenCounts"];
        if (tcNode is JsonObject tcObj)
        {
            tokenCounts = [];
            foreach (var kv in tcObj)
            {
                tokenCounts[int.Parse(kv.Key, System.Globalization.CultureInfo.InvariantCulture)] = kv.Value!.GetValue<int>();
            }
        }

        return new FightReportInput(
            partners,
            c["input"]!["money"]!.GetValue<int>(),
            c["input"]!["item"]!.GetValue<int>(),
            tokenCounts);
    }

    // ------------------------------------------------------------------ per-fn verifiers
    private static void VerifyRollStep(JsonNode c, int id, List<string> errors)
    {
        int targetExp = c["targetExp"]!.GetValue<int>();
        int expected = c["expected"]!.GetValue<int>();
        int got = FightReportController.RollStep(targetExp);
        if (got != expected)
        {
            errors.Add($"[{id}] RollStep({targetExp}): got {got} expected {expected}");
        }
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

        var view = new VectorFieldCheck(c, pfx, errors);
        view.Expect("HeaderFade", "out_headerFade", v.HeaderFade);
        view.Expect("FooterFade", "out_footerFade", v.FooterFade);
        view.Expect("BgScroll", "out_bgScroll", v.BgScroll);
        view.Expect("ExitFade", "out_exitFade", v.ExitFade);
        view.Expect("Done", "out_done", v.Done);

        VerifyPartners(c["out_partners"]!.AsArray(), v.Partners, pfx, errors);
        VerifyMessage(c["out_message"], v.Message, pfx, errors);

        // Logs
        var expLogsArr = c["out_logs"]!.AsArray();
        var expLogs = expLogsArr.Select(n => n!.GetValue<string>()).ToList();
        var gotLogs = ctrl.Logs.ToList();
        if (!gotLogs.SequenceEqual(expLogs))
        {
            errors.Add($"{pfx} Logs: got [{string.Join(",", gotLogs)}] expected [{string.Join(",", expLogs)}]");
        }
    }

    private static void VerifyPartners(JsonArray expected, IReadOnlyList<FightReportPartnerView> partners, string pfx, List<string> errors)
    {
        if (partners.Count != expected.Count)
        {
            errors.Add($"{pfx} Partners.Count: got {partners.Count} expected {expected.Count}");
            return;
        }

        for (int pi = 0; pi < partners.Count; pi++)
        {
            var ep = expected[pi]!;
            var gp = partners[pi];
            string pp = $"{pfx}[P{pi}]";
            var check = new VectorFieldCheck(ep, pp, errors);
            check.Expect("partyIndex", "partyIndex", gp.PartyIndex);
            check.Expect("partnerId", "partnerId", gp.PartnerId);
            check.Expect("fighterId", "fighterId", gp.FighterId);
            check.Expect("Fade", "fade", gp.Fade);
            check.Expect("Selected", "selected", gp.Selected);
            check.Expect("CursorFrame", "cursorFrame", gp.CursorFrame);
            check.Expect("LevelBlinkFrame", "levelBlinkFrame", gp.LevelBlinkFrame);
            check.Expect("ShownExp", "shownExp", gp.ShownExp);
            check.Expect("AnimationFrame", "animationFrame", gp.AnimationFrame);
            VerifySlots(ep["slots"]!.AsArray(), gp.Slots, pp, errors);
        }
    }

    private static void VerifySlots(JsonArray expected, IReadOnlyList<FightReportSlotView> slots, string pp, List<string> errors)
    {
        if (slots.Count != expected.Count)
        {
            errors.Add($"{pp} Slots.Count: got {slots.Count} expected {expected.Count}");
            return;
        }

        for (int si = 0; si < slots.Count; si++)
        {
            var check = new VectorFieldCheck(expected[si]!, $"{pp}[S{si}]", errors);
            check.Expect("DigimonId", "digimonId", slots[si].DigimonId);
            check.Expect("Visible", "visible", slots[si].Visible);
            check.Expect("ExpApplied", "expApplied", slots[si].ExpApplied);
            check.Expect("BlinkFrame", "blinkFrame", slots[si].BlinkFrame);
        }
    }

    // A null message is written either as a JSON null or as a missing key.
    private static void VerifyMessage(JsonNode? expected, FightReportMessage? message, string pfx, List<string> errors)
    {
        bool expectedNull = expected is null || (expected is JsonValue jv && jv.GetValue<object?>() is null);
        if (expectedNull || message is null)
        {
            if (expectedNull != (message is null))
            {
                errors.Add($"{pfx} Message: got {(message is null ? "null" : "non-null")} expected {(expectedNull ? "null" : "non-null")}");
            }

            return;
        }

        var check = new VectorFieldCheck(expected!, pfx, errors);
        check.Expect("Message.Entry", "entry", message.Entry);
        check.Expect("Message.TokensRevealed", "tokensRevealed", message.TokensRevealed);
        check.Expect("Message.TokenCount", "tokenCount", message.TokenCount);
        check.Expect("Message.ArrowVisible", "arrowVisible", message.ArrowVisible);
        check.Expect("Message.ArrowFrame", "arrowFrame", message.ArrowFrame);
    }
}
