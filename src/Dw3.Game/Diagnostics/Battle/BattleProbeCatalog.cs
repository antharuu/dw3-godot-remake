namespace Dw3Production.Diagnostics.Battle;

/// <summary>Maps diagnostic runner entry names to battle probe objects.</summary>
internal static class BattleProbeCatalog
{
    /// <summary>Creates the battle probe registered under an entry name.</summary>
    /// <param name="entryName">Deferred method name selected by the diagnostic runner.</param>
    /// <param name="host">Restricted live battle host.</param>
    /// <returns>The probe, or null when another diagnostic family owns the name.</returns>
    public static BattleProbe? Create(string entryName, IBattleProbeHost host) => entryName switch
    {
        "RunBattleCameraProbe" => new BattleCameraProbe(host),
        "RunBattleMotionProbe" => new BattleMotionProbe(host),
        "RunBattleActionProbe" => new BattleActionProbe(host),
        "RunBattleFontProbe" => new BattleFontProbe(host),
        "RunBattleEntranceProbe" => new BattleEntranceProbe(host),
        "RunBattleFaceProbe" => new BattleFaceProbe(host),
        "RunBattlePlayProbe" => new BattlePlayProbe(host),
        "RunBattleReportProbe" => new BattleReportProbe(host),
        "RunBattleKotemonLightProbe" => new BattleKotemonLightProbe(host),
        "RunBattleHudProbe" => new BattleHudProbe(host),
        "RunBattleInputProbe" => new BattleInputProbe(host),
        "RunBattleLightProbe" => new BattleLightProbe(host),
        "RunBattleModelProbe" => new BattleModelProbe(host),
        "RunFighterSheet" => new FighterSheetProbe(host),
        _ => null,
    };
}
