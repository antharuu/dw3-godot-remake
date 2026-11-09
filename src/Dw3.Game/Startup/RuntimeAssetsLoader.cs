using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;

namespace Dw3Production.Startup;

/// <summary>
/// Reads the session's startup data from a validated data pack: field, animation, battle and
/// dialogue tables, then derives the merged action animations, the runtime growth tables and
/// the test team. It never touches Godot nodes or textures, so a broken pack fails here before
/// any presenter exists.
/// </summary>
internal static class RuntimeAssetsLoader
{
    /// <summary>Loads the startup data of a session.</summary>
    /// <param name="dataRoot">Directory of the extracted data pack.</param>
    /// <returns>The loaded assets; the caller owns and disposes the three loaders it carries.</returns>
    /// <exception cref="CacheResourceException">A table is missing or does not match its contract.</exception>
    /// <exception cref="DataPackException">The pack database cannot be opened.</exception>
    /// <exception cref="InvalidOperationException">Climb and drop animations reuse the same set id.</exception>
    public static RuntimeAssets Load(string dataRoot)
    {
        FieldDataPackLoader? fieldLoader = null;
        FieldAnimationDataPackLoader? animationLoader = null;
        BattleDataPackLoader? battleLoader = null;
        try
        {
            fieldLoader = new FieldDataPackLoader(new DataPackReader(dataRoot));
            animationLoader = new FieldAnimationDataPackLoader(new DataPackReader(dataRoot));
            battleLoader = new BattleDataPackLoader(new DataPackReader(dataRoot));
            return Read(dataRoot, fieldLoader, animationLoader, battleLoader);
        }
        catch
        {
            // Readers opened before the failure would otherwise keep the pack database open.
            battleLoader?.Dispose();
            animationLoader?.Dispose();
            fieldLoader?.Dispose();
            throw;
        }
    }

    /// <summary>Reads every startup table through the opened loaders.</summary>
    /// <param name="dataRoot">Directory of the extracted data pack, for the short-lived dialogue reader.</param>
    /// <param name="fieldLoader">Open field reader.</param>
    /// <param name="animationLoader">Open field animation reader.</param>
    /// <param name="battleLoader">Open battle reader.</param>
    /// <returns>The loaded assets.</returns>
    private static RuntimeAssets Read(
        string dataRoot,
        FieldDataPackLoader fieldLoader,
        FieldAnimationDataPackLoader animationLoader,
        BattleDataPackLoader battleLoader)
    {
        var climb = animationLoader.LoadClimb();
        var drop = animationLoader.LoadDrop();
        var fighters = battleLoader.LoadFighters();
        var rewards = battleLoader.LoadRewards();
        var partyIds = BattleSetupFactory.StarterPartnerDigimonIds(fighters);
        using var dialogueLoader = new DialogueDataPackLoader(new DataPackReader(dataRoot));
        return new RuntimeAssets
        {
            FieldLoader = fieldLoader,
            AnimationLoader = animationLoader,
            BattleLoader = battleLoader,
            Player = fieldLoader.LoadPlayer(),
            ExitCue = animationLoader.LoadExitCue(),
            Climb = climb,
            Drop = drop,
            Actions = MergeActions(climb, drop),
            Water = animationLoader.LoadWater(),
            Icon = animationLoader.LoadPlayerIcon(),
            Npc = fieldLoader.LoadNpcs(),
            Encounters = battleLoader.LoadEncounters(),
            Fighters = fighters,
            Techniques = battleLoader.LoadTechs(),
            Digivolutions = battleLoader.LoadDigivolutions(),
            BattleItems = battleLoader.LoadBattleItems(),
            BattleText = battleLoader.LoadBattleText(),
            Rewards = rewards,
            GrowthTables = GrowthTablesBuilder.Build(rewards.Growth),
            PartyDigimonIds = partyIds,
            PartyGrowth = TestTeamGrowth.Create(partyIds, fighters),
            PlayerSession = PlayerSession.CreateDebug(fighters, battleLoader.LoadBattleItems()),
            Dialogue = dialogueLoader.LoadDialogue(),
            DialogueFrames = dialogueLoader.LoadDialogueFrame(),
        };
    }

    /// <summary>
    /// Merges the climb and drop animation sets. Both come from the same player action sprite
    /// bank, so their set ids never overlap on a valid disc.
    /// </summary>
    /// <param name="climb">Climb sets.</param>
    /// <param name="drop">Drop sets.</param>
    /// <returns>Both families of sets under one lookup.</returns>
    /// <exception cref="InvalidOperationException">A set id appears in both families.</exception>
    private static ClimbData MergeActions(ClimbData climb, ClimbData drop)
    {
        var collision = drop.Sequences.Keys.Where(climb.Sequences.ContainsKey).Take(1).ToList();
        if (collision.Count > 0)
        {
            throw new InvalidOperationException($"action animation set collision: {collision[0]:X}");
        }

        var merged = new Dictionary<int, ClimbSequence>(climb.Sequences);
        foreach (var pair in drop.Sequences)
        {
            merged.Add(pair.Key, pair.Value);
        }

        return new ClimbData(merged);
    }
}
