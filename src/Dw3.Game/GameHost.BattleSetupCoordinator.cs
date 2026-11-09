using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;

namespace Dw3Production;

/// <summary>Contains GameHost's battle-setup coordination implementation.</summary>
public partial class GameHost
{
    /// <summary>Builds source-backed battle flows from the host's validated battle manifests.</summary>
    internal sealed class BattleSetupCoordinator
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
            PlayerSession session = _host._playerSession ?? throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "player session unavailable");
            BattleTextData text = _host._battleTextData ?? throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "battle text manifest unavailable");
            BattleItemsData items = _host._battleItemsData ?? throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", "battle items manifest unavailable");
            var input = new BattleSessionBuildInput(fighters, techs, encounters, encounterIndex, session, items, text, _host._battleRng, _host.DigivolutionData);
            BattleFlowController flow = BuildUnstarted(input, out BattleSetup setup, out int activeDigimon);
            flow.Start(CreateOpeningState(setup, activeDigimon, _host._battleRng));
            _host._battleNameIds = new Dictionary<int, int>(flow.NameIds);
            return flow;
        }

        /// <summary>Builds the session-backed production flow without requiring Godot node lifetime.</summary>
        /// <param name="input">Decoded battle inputs and the persistent session to wire.</param>
        /// <returns>Started flow wired to the session inventory.</returns>
        internal static BattleFlowController BuildForSession(BattleSessionBuildInput input)
        {
            ArgumentNullException.ThrowIfNull(input);
            BattleFlowController flow = BuildUnstarted(input, out BattleSetup setup, out int activeDigimon);
            flow.Start(CreateOpeningState(setup, activeDigimon, input.Rng));
            return flow;
        }

        /// <summary>Builds a configured flow and opening setup without consuming the opening random draw.</summary>
        /// <param name="input">Decoded battle inputs and the persistent session to wire.</param>
        /// <param name="setup">Prepared setup that the caller starts with its authoritative random stream.</param>
        /// <param name="activeDigimon">Active partner form used for the opening state.</param>
        /// <returns>Configured but not started battle flow.</returns>
        internal static BattleFlowController BuildUnstarted(BattleSessionBuildInput input, out BattleSetup setup, out int activeDigimon)
        {
            int[] party = [.. input.Session.BuildParty().OfType<PartnerSeed>().Select(seed => seed.Id)];
            if (party.Length != 3)
            {
                throw new CacheResourceException("CACHE_INVALID", "the balanced starter party could not be resolved");
            }

            Dictionary<int, int> nameIds = BattleNameIdCatalog.Build(party, input.Fighters, input.Encounters, input.EncounterIndex);
            EncounterEntry entry = input.Encounters.Encounters.FirstOrDefault(candidate => candidate.Index == input.EncounterIndex)
                ?? throw new InvalidOperationException($"encounter {input.EncounterIndex} not in the manifest");
            setup = new BattleSetup
            {
                Battle = input.EncounterIndex,
                Stage = BattleStageResolver.Resolve(input.Encounters, input.EncounterIndex),
                Partners = input.Session.BuildParty(),
                StarterDigimonIds = [.. input.Fighters.DigimonOrdered.Take(8).Select(digimon => digimon.Id)],
                Enemies = BattleSetupFactory.BuildEnemies(input.Encounters, input.EncounterIndex, input.Fighters),
                Blocks = new int[12],
            };
            BattleSetupFactory.ApplyEncounterSettings(entry, setup);
            BattleFlowController flow = BattleSetupFactory.BuildFlow(
                input.Fighters,
                input.Techs,
                input.Encounters,
                input.EncounterIndex,
                party,
                nameIds,
                new BattleTextCatalog(input.Text));
            WireCatalogs(flow, input.Items, input.Session.Inventory);
            SessionBattleCatalogFactory.ConfigureActivePartner(flow, input.Session, input.Fighters, input.Techs, input.Text);
            activeDigimon = party[0];
            return flow;
        }

        /// <summary>Commits completed player resources through the host's battle-resolution boundary.</summary>
        /// <param name="session">Session receiving the completed battle resources.</param>
        /// <param name="flow">Resolved battle flow.</param>
        internal static void CommitResolvedBattle(PlayerSession? session, BattleFlowController flow)
        {
            ArgumentNullException.ThrowIfNull(flow);
            if (session is null)
            {
                return;
            }

            session.CommitBattle(flow.State.Players);
        }

        /// <summary>Wires converted item definitions and the session-owned inventory into a battle flow.</summary>
        /// <param name="flow">Flow receiving the catalogues.</param>
        /// <param name="items">Decoded battle item catalogue.</param>
        /// <param name="inventory">Session-owned mutable item quantities.</param>
        internal static void WireCatalogs(BattleFlowController flow, BattleItemsData items, Dictionary<int, int> inventory)
        {
            var catalog = new Dictionary<int, BattleItem>();
            var itemText = new Dictionary<int, BattleTextLine>();
            foreach (BattleItemData item in items.Items)
            {
                catalog[item.Id] = BattleSetupFactory.ToItem(item);
                itemText[item.Id] = BattleTextCatalog.Tokens(item.Name, $"FSITMNAM id={item.NameId}");
            }

            flow.Items = catalog;
            flow.ItemStock = inventory;
            flow.ItemText = itemText;
        }

        /// <summary>Initializes production fighters with the mandatory shared battle stream.</summary>
        /// <param name="setup">Prepared encounter and party.</param>
        /// <param name="digimon">Active partner form.</param>
        /// <param name="rng">Host-owned stream used by subsequent battle consumers.</param>
        /// <returns>The initialized battle state, including its injected opening order.</returns>
        internal static BattleState CreateOpeningState(BattleSetup setup, int digimon, IBattleRng rng)
        {
            ArgumentNullException.ThrowIfNull(rng);
            BattleState state = BattleState.InitFighters(setup, digimon, rng);
            state.ActivePlayerFighter?.RefreshStats();
            return state;
        }

        /// <summary>Groups every engine-free input needed to create one session-backed battle flow.</summary>
        /// <param name="Fighters">Decoded fighter definitions.</param>
        /// <param name="Techs">Decoded technique definitions.</param>
        /// <param name="Encounters">Decoded encounter definitions.</param>
        /// <param name="EncounterIndex">Encounter to start.</param>
        /// <param name="Session">Persistent player resources shared by consecutive battles.</param>
        /// <param name="Items">Decoded battle item catalogue.</param>
        /// <param name="Text">Decoded battle text catalogue.</param>
        /// <param name="Rng">Shared host random stream.</param>
        /// <param name="Digivolutions">Optional extracted form requirements used to filter session-owned forms.</param>
        internal sealed record BattleSessionBuildInput(
            FighterData Fighters,
            TechniqueData Techs,
            EncounterData Encounters,
            int EncounterIndex,
            PlayerSession Session,
            BattleItemsData Items,
            BattleTextData Text,
            IBattleRng Rng,
            DigivolutionData? Digivolutions = null);
    }
}
