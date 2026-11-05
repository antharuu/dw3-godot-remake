using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production;

/// <summary>Contains GameHost's battle-setup coordination implementation.</summary>
public partial class GameHost
{
    /// <summary>Builds source-backed battle flows from the host's validated battle manifests.</summary>
    private sealed class BattleSetupCoordinator
    {
        private readonly GameHost _host;

        /// <summary>Initializes a new instance of the <see cref="BattleSetupCoordinator"/> class.</summary>
        /// <param name="host">Runtime host that owns validated manifest data.</param>
        public BattleSetupCoordinator(GameHost host)
        {
            _host = host;
        }

        /// <summary>Builds and starts a real 1v1 flow for a manifest encounter.</summary>
        /// <param name="encounterIndex">Manifest encounter index.</param>
        /// <returns>Started battle flow.</returns>
        public BattleFlowController Build(int encounterIndex)
        {
            FighterData fighters = _host._fighterData ?? throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "fighter manifest unavailable");
            TechniqueData techs = _host._techData ?? throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "tech manifest unavailable");
            EncounterData encounters = _host._encounterData ?? throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "encounter manifest unavailable");
            _ = _host._digivolutionData ?? throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "digivolution manifest unavailable");
            _ = _host._battleItemsData ?? throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "battle items manifest unavailable");
            IReadOnlyList<int> party = BattleSetupFactory.StarterPartnerDigimonIds(fighters);
            if (party.Count != 3)
            {
                throw new CacheResourceException("CACHE_INVALID", "the balanced starter party could not be resolved");
            }

            _host._battleNameIds = BattleNameIdCatalog.Build(party, fighters, encounters, encounterIndex);
            EncounterEntry entry = encounters.Encounters.FirstOrDefault(candidate => candidate.Index == encounterIndex)
                ?? throw new InvalidOperationException($"encounter {encounterIndex} not in the manifest");
            var setup = new BattleSetup
            {
                Battle = encounterIndex,
                Stage = BattleStageResolver.Resolve(encounters, encounterIndex),
                EncounterKind = entry.Kind,
                Partners = BattleSetupFactory.BuildParty(fighters, TestTeamGrowth.Level, party),
                Enemies = BattleSetupFactory.BuildEnemies(encounters, encounterIndex, fighters),
                Blocks = new int[12],
            };
            CopyBlocks(entry, setup);
            BattleFlowController flow = BattleSetupFactory.BuildFlow(
                fighters,
                techs,
                encounters,
                encounterIndex,
                party,
                _host._battleNameIds,
                new BattleTextCatalog(_host._battleTextData ?? throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "battle text manifest unavailable")));
            _host._battleMenuCoordinator.WireCatalogs(flow);
            flow.Start(CreateOpeningState(setup, party[0], _host._battleRng));
            return flow;
        }

        /// <summary>Initializes production fighters with the mandatory shared battle stream.</summary>
        /// <param name="setup">Prepared encounter and party.</param>
        /// <param name="digimon">Active partner form.</param>
        /// <param name="rng">Host-owned stream used by subsequent battle consumers.</param>
        /// <returns>The initialized battle state, including its injected opening order.</returns>
        internal static BattleState CreateOpeningState(BattleSetup setup, int digimon, IBattleRng rng)
        {
            ArgumentNullException.ThrowIfNull(rng);
            return BattleState.InitFighters(setup, digimon, rng);
        }

        /// <summary>Copies the encounter's bounded block list into the simulation setup.</summary>
        /// <param name="entry">Decoded encounter entry.</param>
        /// <param name="setup">Setup receiving the block values.</param>
        private static void CopyBlocks(EncounterEntry entry, BattleSetup setup)
        {
            for (var i = 0; i < Math.Min(entry.Blocks.Count, setup.Blocks.Length); i++)
            {
                setup.Blocks[i] = entry.Blocks[i];
            }
        }
    }
}
