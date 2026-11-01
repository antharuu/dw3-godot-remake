namespace Dw3.Game.Tests;

internal static class SyntheticDataPackFixture
{
    /// <summary>
    /// A fabricated edition label shared by data-pack fixtures.
    /// It must never identify an original game release.
    /// </summary>
    public const string SyntheticEdition = "synthetic-edition";

    // These values are protocol tags required by BattleDataPackLoader, not disc-derived fixture data.
    public const string SupportedEncounterEdition = "SLES-03936";
    public const string EncounterSchema = "m6-encounter-v1";
    public const string BattleSchema = "m9b-battle-v2";
    public const string DigivolutionSchema = "m9b-digivolution-v2";
    public const string BattleHudSchema = "m6-battle-hud-v3";
    public const string BattleReportSchema = "m6-battle-report-sprites-v1";
    public const string BattleMenuIconSchema = "m6-battle-menu-icons-v1";
}
