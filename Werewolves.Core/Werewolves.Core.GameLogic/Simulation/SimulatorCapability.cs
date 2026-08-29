using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public sealed class SimulatorCapability
{
	private readonly IReadOnlyDictionary<MainRoleType, Faction> _beneficiaryFactions;
	private readonly IReadOnlyDictionary<MainRoleType, IReadOnlySet<Faction>> _agentFactions;
	private readonly SharedVictoryGameResult[] _sharedVictoryCapabilities;
	private readonly SimulationRuleState[] _supportedRuleStates;
	private readonly LobbyEvaluationDepth[] _supportedEvaluationDepths;

	public SimulatorProfileIdentity Identity { get; }

	public IReadOnlyList<MainRoleType> SupportedRoles { get; }
	public IReadOnlyList<SharedVictoryGameResult> SharedVictoryCapabilities { get; }
	public HeadlessResponsePolicy HeadlessResponsePolicy { get; }
	public IReadOnlyList<SimulationRuleState> SupportedRuleStates { get; }
	public IReadOnlyList<LobbyEvaluationDepth> SupportedEvaluationDepths { get; }

	public bool SupportsActorSetupCards { get; }

	private static readonly (
		MainRoleType Role,
		Faction BeneficiaryFaction,
		Faction[] AgentFactions)[] SafetyScreeningRoleFacts =
	[
		(MainRoleType.SimpleWerewolf, Faction.Werewolf, [Faction.Werewolf]),
		(MainRoleType.BigBadWolf, Faction.Werewolf, [Faction.Werewolf]),
		(MainRoleType.Seer, Faction.Villager, []),
		(MainRoleType.WildChild, Faction.Villager, []),
		(MainRoleType.SimpleVillager, Faction.Villager, []),
		(MainRoleType.VillagerVillager, Faction.Villager, []),
		(MainRoleType.TwoSisters, Faction.Villager, []),
		(MainRoleType.ThreeBrothers, Faction.Villager, []),
		(MainRoleType.Witch, Faction.Villager, []),
		(MainRoleType.Hunter, Faction.Villager, []),
		(MainRoleType.LittleGirl, Faction.Villager, []),
		(MainRoleType.Defender, Faction.Villager, []),
		(MainRoleType.Elder, Faction.Villager, []),
		(MainRoleType.StutteringJudge, Faction.Villager, []),
		(MainRoleType.Scapegoat, Faction.Villager, []),
		(MainRoleType.VillageIdiot, Faction.Villager, []),
		(MainRoleType.WolfHound, Faction.Villager, []),
		(MainRoleType.AccursedWolfFather, Faction.Werewolf, [Faction.Werewolf]),
		(MainRoleType.WhiteWerewolf, Faction.WhiteWerewolf, [Faction.Werewolf]),
		(MainRoleType.Piper, Faction.Piper, []),
		(MainRoleType.BearTamer, Faction.Villager, []),
		(MainRoleType.Fox, Faction.Villager, []),
		(MainRoleType.KnightWithRustySword, Faction.Villager, []),
		(MainRoleType.Cupid, Faction.Villager, []),
		(MainRoleType.Thief, Faction.Villager, []),
		(MainRoleType.DevotedServant, Faction.Villager, []),
		(MainRoleType.Angel, Faction.Villager, []),
		(MainRoleType.PrejudicedManipulator, Faction.PrejudicedManipulator, []),
		(MainRoleType.Actor, Faction.Villager, [])
	];

	private static readonly SharedVictoryGameResult[] SafetyScreeningSharedVictoryCapabilities =
	[
		new([Faction.Angel, Faction.Villager]),
		new([Faction.Angel, Faction.Werewolf]),
		new([Faction.Angel, Faction.WhiteWerewolf]),
		new([Faction.Angel, Faction.Piper]),
		new([Faction.Angel, Faction.CrossFactionLovers]),
		new([Faction.Angel, Faction.PrejudicedManipulator]),
		new([Faction.Piper, Faction.PrejudicedManipulator]),
		new(
			[
				Faction.Angel,
				Faction.Piper,
				Faction.PrejudicedManipulator
			])
	];

	private static readonly (
		MainRoleType Role,
		Faction BeneficiaryFaction,
		Faction[] AgentFactions)[] FullProbabilityRoleFacts =
	[
		(MainRoleType.SimpleWerewolf, Faction.Werewolf, [Faction.Werewolf]),
		(MainRoleType.Seer, Faction.Villager, []),
		(MainRoleType.WildChild, Faction.Villager, []),
		(MainRoleType.SimpleVillager, Faction.Villager, [])
	];

	public static SimulatorCapability SafetyScreening =>
		SimulatorCapabilityRegistry.Production.SafetyScreening;

	public static SimulatorCapability FullProbability =>
		SimulatorCapabilityRegistry.Production.FullProbability;

	public bool SupportsEvaluationDepth(LobbyEvaluationDepth depth) =>
		_supportedEvaluationDepths.Contains(depth);

	public SimulationCompatibilityIdentity CreateCompatibilityIdentity(
		SimulationScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		return new SimulationCompatibilityIdentity(scenario.ToCanonical(), Identity);
	}

	public bool SupportsRole(MainRoleType role) =>
		_beneficiaryFactions.ContainsKey(role);

	public bool TryGetBeneficiaryFaction(
		MainRoleType role,
		out Faction faction) =>
		_beneficiaryFactions.TryGetValue(role, out faction);

	public bool IsFactionAgent(MainRoleType role, Faction faction)
	{
		if (!Enum.IsDefined(faction))
		{
			throw new ArgumentOutOfRangeException(nameof(faction));
		}
		if (!_agentFactions.TryGetValue(role, out var factions))
		{
			throw new ArgumentOutOfRangeException(nameof(role));
		}

		return factions.Contains(faction);
	}

	internal GameResult[] CreatePossibleGameResults(
		IEnumerable<Faction> possibleFactions)
	{
		ArgumentNullException.ThrowIfNull(possibleFactions);
		var factions = possibleFactions.ToArray();
		return factions
			.Select(faction => (GameResult)new SingleFactionGameResult(faction))
			.Concat(_sharedVictoryCapabilities
				.Where(result => result.Factions.All(factions.Contains)))
			.Append(new NoWinnerGameResult())
			.ToArray();
	}

	public bool SupportsRuleState(SimulationRuleState ruleState) =>
		_supportedRuleStates.Contains(ruleState);

	internal SimulatorSupportResult ClassifySupport(
		SimulationScenario scenario,
		AppSupportResult appSupport)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		ArgumentNullException.ThrowIfNull(appSupport);
		var unsupportedRoles = scenario.RoleCompositionCards
			.Distinct()
			.Where(role => !SupportsRole(role))
			.OrderBy(role => role.ToString(), StringComparer.Ordinal);
		return new SimulatorSupportResult(
			scenario,
			appSupport,
			this,
			unsupportedRoles,
			hasUnsupportedActorSetupCards:
				scenario.ActorSetupCards.Cards.Count > 0
				&& !SupportsActorSetupCards,
			hasUnsupportedRuleState: !SupportsRuleState(scenario.RuleState));
	}

	internal static HeadlessResponsePolicy CreateBaselinePolicy() => new(
		BaselineRandomDecisionStrategy.Identity,
		BaselineRandomDecisionStrategy.Policy.AdmittedSemantics);

	internal static SimulatorCapability CreateSafetyScreening() => new(
			new SimulatorProfileIdentity("safety-screening", "30"),
		SafetyScreeningRoleFacts,
		sharedVictoryCapabilities: SafetyScreeningSharedVictoryCapabilities,
		headlessResponsePolicy: new HeadlessResponsePolicy(
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			[
				ModeratorInstructionSemantic.StartGame,
				ModeratorInstructionSemantic.FinishedGame,
				ModeratorInstructionSemantic.StartNight,
				ModeratorInstructionSemantic.FinishNightActions,
				ModeratorInstructionSemantic.WakeRole,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ModeratorInstructionSemantic.SelectWerewolfVictim,
				ModeratorInstructionSemantic.SelectBigBadWolfTarget,
				ModeratorInstructionSemantic.SelectDefenderTarget,
				ModeratorInstructionSemantic.SelectWhiteWerewolfTarget,
				ModeratorInstructionSemantic.SelectPiperTargets,
				ModeratorInstructionSemantic.RecognizeCharmedPlayers,
				ModeratorInstructionSemantic.AnnounceBearTamerGrowl,
				ModeratorInstructionSemantic.SelectFoxCenter,
				ModeratorInstructionSemantic.RevealFoxResult,
				ModeratorInstructionSemantic.SelectCupidLovers,
				ModeratorInstructionSemantic.RecognizeLovers,
				ModeratorInstructionSemantic.ChooseThiefOffer,
				ModeratorInstructionSemantic.ResolveDevotedServantVoteWindow,
				ModeratorInstructionSemantic.RecordDevotedServantAcquiredCard,
				ModeratorInstructionSemantic.SelectSeerTarget,
				ModeratorInstructionSemantic.RevealSeerResult,
				ModeratorInstructionSemantic.SelectWildChildModel,
				ModeratorInstructionSemantic.AnnounceDawnVictims,
				ModeratorInstructionSemantic.AssignDawnVictimRoles,
				ModeratorInstructionSemantic.StartDayDebate,
				ModeratorInstructionSemantic.RecordDayVote,
				ModeratorInstructionSemantic.AssignDayVoteTargetRole,
		    ModeratorInstructionSemantic.AnnounceVillageIdiotPardon,
				ModeratorInstructionSemantic.AnnounceDayElimination,
				ModeratorInstructionSemantic.ConductDayVote,
				ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal,
				ModeratorInstructionSemantic.RecognizeRoleHolders,
				ModeratorInstructionSemantic.CommunicateAsRoleHolders,
				ModeratorInstructionSemantic.SelectWitchHealingTarget,
				ModeratorInstructionSemantic.SelectWitchPoisonTarget,
				ModeratorInstructionSemantic.AnnounceEliminationCascadeVictims,
				ModeratorInstructionSemantic.AssignEliminationCascadeRoles,
				ModeratorInstructionSemantic.SelectHunterFinalShotTarget,
				ModeratorInstructionSemantic.EstablishStutteringJudgeSignal,
				ModeratorInstructionSemantic.ObserveStutteringJudgeSignal,
				ModeratorInstructionSemantic.ObserveScapegoatHolderForTie,
				ModeratorInstructionSemantic.RevealScapegoatForTie,
					ModeratorInstructionSemantic.SelectScapegoatPermittedVoters,
					ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters,
					ModeratorInstructionSemantic.ChooseWolfHoundAlignment,
					ModeratorInstructionSemantic
						.ChooseAccursedWolfFatherInfection,
					ModeratorInstructionSemantic
						.AnnounceVillagerRolePowerSuppression,
					ModeratorInstructionSemantic.ChooseActorSetupCard
				]),
		supportsActorSetupCards: true,
			supportedRuleStates: [SimulationRuleState.Default],
			supportedEvaluationDepths:
			[
				LobbyEvaluationDepth.DegenerateScreeningOnly
			]);

	internal static SimulatorCapability CreateFullProbability() => new(
			new SimulatorProfileIdentity("full-probability", "4"),
		FullProbabilityRoleFacts,
		headlessResponsePolicy: new HeadlessResponsePolicy(
			BaselineRandomDecisionStrategy.Identity,
			[
				ModeratorInstructionSemantic.StartGame,
				ModeratorInstructionSemantic.FinishedGame,
				ModeratorInstructionSemantic.StartNight,
				ModeratorInstructionSemantic.FinishNightActions,
				ModeratorInstructionSemantic.WakeRole,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
				ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ModeratorInstructionSemantic.SelectWerewolfVictim,
				ModeratorInstructionSemantic.SelectSeerTarget,
				ModeratorInstructionSemantic.RevealSeerResult,
				ModeratorInstructionSemantic.SelectWildChildModel,
				ModeratorInstructionSemantic.AnnounceDawnVictims,
				ModeratorInstructionSemantic.AssignDawnVictimRoles,
				ModeratorInstructionSemantic.StartDayDebate,
				ModeratorInstructionSemantic.RecordDayVote,
				ModeratorInstructionSemantic.AssignDayVoteTargetRole,
		    ModeratorInstructionSemantic.AnnounceVillageIdiotPardon,
				ModeratorInstructionSemantic.AnnounceDayElimination
			]),
		supportsActorSetupCards: false,
			supportedRuleStates: [SimulationRuleState.Default],
			supportedEvaluationDepths:
			[
				LobbyEvaluationDepth.DegenerateScreeningOnly,
				LobbyEvaluationDepth.FullProbability
			]);

	internal SimulatorCapability(
		SimulatorProfileIdentity identity,
		IEnumerable<(
			MainRoleType Role,
			Faction BeneficiaryFaction,
			Faction[] AgentFactions)> roleFacts,
		IEnumerable<SharedVictoryGameResult>? sharedVictoryCapabilities = null,
		HeadlessResponsePolicy? headlessResponsePolicy = null,
		bool supportsActorSetupCards = false,
		IEnumerable<SimulationRuleState>? supportedRuleStates = null,
		IEnumerable<LobbyEvaluationDepth>? supportedEvaluationDepths = null)
	{
		ArgumentNullException.ThrowIfNull(identity);
		ArgumentNullException.ThrowIfNull(roleFacts);
		Identity = identity;
		var snapshot = roleFacts.ToArray();
		if (snapshot.Any(fact => !Enum.IsDefined(fact.Role)))
		{
			throw new ArgumentOutOfRangeException(nameof(roleFacts));
		}
		if (snapshot.Any(fact => !Enum.IsDefined(fact.BeneficiaryFaction)))
		{
			throw new ArgumentOutOfRangeException(nameof(roleFacts));
		}
		if (snapshot.Any(fact => fact.AgentFactions is null
			|| fact.AgentFactions.Any(faction => !Enum.IsDefined(faction))))
		{
			throw new ArgumentOutOfRangeException(nameof(roleFacts));
		}
		if (snapshot.GroupBy(fact => fact.Role).Any(group => group.Count() > 1))
		{
			throw new ArgumentException(
				"Each Role can have only one Simulator Capability declaration.",
				nameof(roleFacts));
		}

		_beneficiaryFactions = snapshot.ToDictionary(
			fact => fact.Role,
			fact => fact.BeneficiaryFaction);
		_agentFactions = snapshot.ToDictionary(
			fact => fact.Role,
			fact => (IReadOnlySet<Faction>)fact.AgentFactions.ToHashSet());
		SupportedRoles = Array.AsReadOnly(snapshot.Select(fact => fact.Role).ToArray());
		_sharedVictoryCapabilities = (sharedVictoryCapabilities ?? [])
			.Distinct()
			.OrderBy(result => string.Join(',', result.Factions))
			.ToArray();
		SharedVictoryCapabilities = Array.AsReadOnly(_sharedVictoryCapabilities);
		HeadlessResponsePolicy = headlessResponsePolicy ?? CreateBaselinePolicy();
		SupportsActorSetupCards = supportsActorSetupCards;
		_supportedRuleStates = (supportedRuleStates ?? [SimulationRuleState.Default])
			.Distinct()
			.ToArray();
		SupportedRuleStates = Array.AsReadOnly(_supportedRuleStates);
		_supportedEvaluationDepths = (supportedEvaluationDepths ??
			[ LobbyEvaluationDepth.DegenerateScreeningOnly ])
			.Distinct()
			.ToArray();
		if (_supportedEvaluationDepths.Any(depth => !Enum.IsDefined(depth)))
		{
			throw new ArgumentOutOfRangeException(nameof(supportedEvaluationDepths));
		}

		SupportedEvaluationDepths = Array.AsReadOnly(_supportedEvaluationDepths);
	}
}

public sealed class SimulatorCapabilityRegistry
{
	private readonly IReadOnlyDictionary<SimulatorProfileIdentity, SimulatorCapability>
		_capabilitiesByIdentity;

	public static SimulatorCapabilityRegistry Production { get; } = new(
		SimulatorCapability.CreateSafetyScreening(),
		SimulatorCapability.CreateFullProbability());

	public SimulatorCapability SafetyScreening { get; }

	public SimulatorCapability FullProbability { get; }

	public SimulatorCapabilityRegistry(
		SimulatorCapability safetyScreening,
		SimulatorCapability fullProbability)
	{
		ArgumentNullException.ThrowIfNull(safetyScreening);
		ArgumentNullException.ThrowIfNull(fullProbability);
		if (fullProbability.SupportedRoles.Except(safetyScreening.SupportedRoles).Any())
		{
			throw new ArgumentException(
				"The Full-Probability Role Set must be a subset of the Safety-Screening Role Set.",
				nameof(fullProbability));
		}
		if (safetyScreening.Identity.Equals(fullProbability.Identity))
		{
			throw new ArgumentException(
				"Simulator Capabilities must have distinct identities.",
				nameof(fullProbability));
		}

		SafetyScreening = safetyScreening;
		FullProbability = fullProbability;
		_capabilitiesByIdentity = new Dictionary<SimulatorProfileIdentity, SimulatorCapability>
		{
			[safetyScreening.Identity] = safetyScreening,
			[fullProbability.Identity] = fullProbability
		};
	}

	public bool TryGet(
		SimulatorProfileIdentity identity,
		out SimulatorCapability capability)
	{
		ArgumentNullException.ThrowIfNull(identity);
		return _capabilitiesByIdentity.TryGetValue(identity, out capability!);
	}
}
