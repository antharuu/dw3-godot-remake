using System.Text.Json;
using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Inventories every physical technique through the production loader and script provider.</summary>
internal static class BattlePhysicalRegistry
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Writes inventory and parsing evidence, without mislabelling either as rendered support.</summary>
    /// <param name="loader">Validated extracted pack.</param>
    /// <param name="directory">Private evidence directory.</param>
    public static void Write(BattleDataPackLoader loader, string directory)
    {
        var fighters = loader.LoadFighters();
        var provider = new PackActionScriptProvider(loader);
        var entries = loader.LoadTechs().Techs.Values.Where(tech => tech.Icon == BattleConst.TechPhysical).OrderBy(tech => tech.Id).Select(tech => new
        {
            Technique = tech,
            Fighters = Owners(fighters, tech.Id).Select(id => Inspect(provider, id, tech.Script)).ToArray(),
            Simulation = "not-exercised-by-inventory",
            Dispatch = "not-exercised-by-inventory",
            Render = "not-exercised-by-inventory",
        }).ToArray();
        File.WriteAllText(Path.Combine(directory, "physical-registry.json"), JsonSerializer.Serialize(entries, Options));
    }

    /// <summary>Finds the natural partner skills and enemy technique references without guessing a fighter.</summary>
    /// <param name="fighters">Extracted fighter registry.</param>
    /// <param name="technique">Technique identity.</param>
    /// <returns>Distinct natural owners in stable order.</returns>
    private static IEnumerable<int> Owners(FighterData fighters, int technique) =>
        fighters.Digimon.Values.Where(fighter => fighter.Skills.Select(BattleSetupFactory.SkillId).Contains(technique) || fighter.PairTech == technique).Select(fighter => fighter.Id)
        .Concat(fighters.BattleTable.Where(fighter => fighter.Techs.Contains(technique)).Select(fighter => fighter.Id)).Distinct().Order();

    /// <summary>Parses and validates one real script, retaining the precise failed layer.</summary>
    /// <param name="provider">Production provider.</param>
    /// <param name="fighter">Natural fighter identity.</param>
    /// <param name="selector">Technique-owned selector.</param>
    /// <returns>Source commands and parsing result, never a rendering claim.</returns>
    private static object Inspect(PackActionScriptProvider provider, int fighter, int selector)
    {
        var script = provider.TryLoad(fighter, selector);
        if (script is null)
        {
            return new { Fighter = fighter, Selector = selector, Parsing = "unavailable", Commands = (object?)null };
        }

        string validation = "validated";
        try
        {
            BattleActionValidation.Validate(script);
        }
        catch (FormatException error)
        {
            validation = error.Message;
        }

        return new { Fighter = fighter, Selector = selector, Parsing = validation, Commands = script.Commands };
    }
}
