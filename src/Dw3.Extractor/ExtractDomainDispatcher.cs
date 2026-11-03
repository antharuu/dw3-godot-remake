using Dw3.Disc;

namespace Dw3.Extractor;

/// <summary>
/// Maps each <c>extract --only</c> domain name to the extraction step that writes it. A step that
/// extracts two related domains (climb and drop, cue and icon, font and text, battle and
/// digivolution) receives the name so it knows which of the two to write.
/// </summary>
internal static class ExtractDomainDispatcher
{
    /// <summary>
    /// The step of each domain name. Names are matched ordinally, so <c>Zones</c> is not <c>zones</c>.
    /// Each step gets the disc opener, the data root, and the output and error writers.
    /// </summary>
    private static readonly Dictionary<string, Func<Func<IDiscSource>, string, TextWriter, TextWriter, int>> Steps = new(StringComparer.Ordinal)
    {
        ["zones"] = (disc, root, output, error) => ZoneExtractionStep.Extract(disc, root, "zones", output, error),
        ["collision"] = (disc, root, output, error) => CollisionExtractionStep.Extract(disc, root, "collision", output, error),
        ["player"] = (disc, root, output, error) => PlayerExtractionStep.Extract(disc, root, "player", output, error),
        ["water"] = WaterExtractionStep.Extract,
        ["climb"] = (disc, root, output, error) => ActionExtractionStep.Extract(disc, root, "climb", output, error),
        ["drop"] = (disc, root, output, error) => ActionExtractionStep.Extract(disc, root, "drop", output, error),
        ["npc"] = NpcExtractionStep.Extract,
        ["cue"] = (disc, root, output, error) => FieldcomExtractionStep.Extract(disc, root, "cue", output, error),
        ["icon"] = (disc, root, output, error) => FieldcomExtractionStep.Extract(disc, root, "icon", output, error),
        ["font"] = (disc, root, output, error) => TextExtractionStep.Extract(disc, root, "font", output, error),
        ["text"] = (disc, root, output, error) => TextExtractionStep.Extract(disc, root, "text", output, error),
        ["dialogue"] = DialogueExtractionStep.Extract,
        ["talk-box"] = TalkBoxExtractionStep.Extract,
        ["encounter"] = EncounterExtractionStep.Extract,
        ["battle"] = (disc, root, output, error) => BattleExtractionStep.Extract(disc, root, "battle", output, error),
        ["digivolution"] = (disc, root, output, error) => BattleExtractionStep.Extract(disc, root, "digivolution", output, error),
        ["battle-models"] = BattleModelExtractionStep.Extract,
        ["battle-stages"] = BattleStageExtractionStep.Extract,
        ["battle-hud"] = BattleHudExtractionStep.Extract,
        ["battle-report"] = BattleReportExtractionStep.Extract,
    };

    /// <summary>Runs the extraction step that writes <paramref name="domain"/> into the data pack.</summary>
    /// <param name="domain">The domain name given to <c>--only</c>, for example <c>zones</c> or <c>battle-hud</c>.</param>
    /// <param name="openDisc">Opens the disc for the step; the step disposes the handle before returning.</param>
    /// <param name="outputPath">Root of the data pack to write into.</param>
    /// <param name="output">Receives the step's JSON event line on success.</param>
    /// <param name="error">Receives the step's error line, or the unsupported-domain line.</param>
    /// <returns>The step's exit code (0 on success, 1 on extraction error), or 2 when the domain name is unknown.</returns>
    public static int Dispatch(string domain, Func<IDiscSource> openDisc, string outputPath, TextWriter output, TextWriter error)
    {
        if (!Steps.TryGetValue(domain, out Func<Func<IDiscSource>, string, TextWriter, TextWriter, int>? step))
        {
            error.WriteLine($"Unsupported extraction domain: {domain}");
            return 2;
        }

        return step(openDisc, outputPath, output, error);
    }
}
