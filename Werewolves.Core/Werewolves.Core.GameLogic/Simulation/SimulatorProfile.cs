using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public class SimulatorProfile
{
	private readonly IReadOnlyDictionary<MainRoleType, Faction> _beneficiaryFactions;
	private readonly IReadOnlyDictionary<MainRoleType, IReadOnlySet<Faction>> _agentFactions;
	private readonly SharedVictoryGameResult[] _sharedVictoryCapabilities;
	private readonly SimulationRuleState[] _supportedRuleStates;

	public SimulatorProfileIdentity Identity { get; }

	public IReadOnlyList<MainRoleType> SupportedRoles { get; }
	public IReadOnlyList<SharedVictoryGameResult> SharedVictoryCapabilities { get; }
	public HeadlessResponsePolicy HeadlessResponsePolicy { get; }
	public IReadOnlyList<SimulationRuleState> SupportedRuleStates { get; }

	public bool SupportsActorSetupCards { get; }

	internal SimulatorProfile(
		SimulatorProfileIdentity identity,
		IEnumerable<SimulatorProfileRoleDescriptor> roleDescriptors,
		IEnumerable<SharedVictoryGameResult>? sharedVictoryCapabilities = null,
		HeadlessResponsePolicy? headlessResponsePolicy = null,
		bool supportsActorSetupCards = false,
		IEnumerable<SimulationRuleState>? supportedRuleStates = null)
	{
		ArgumentNullException.ThrowIfNull(identity);
		ArgumentNullException.ThrowIfNull(roleDescriptors);
		Identity = identity;
		var snapshot = roleDescriptors.ToArray();
		_beneficiaryFactions = snapshot.ToDictionary(
			descriptor => descriptor.Role,
			descriptor => descriptor.BeneficiaryFaction);
		_agentFactions = snapshot.ToDictionary(
			descriptor => descriptor.Role,
			descriptor => descriptor.AgentFactions);
		SupportedRoles = Array.AsReadOnly(snapshot.Select(descriptor => descriptor.Role).ToArray());
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
	}

	public bool SupportsRole(MainRoleType role) => _beneficiaryFactions.ContainsKey(role);

	internal bool TryGetBeneficiaryFaction(MainRoleType role, out Faction faction) =>
		_beneficiaryFactions.TryGetValue(role, out faction);

	internal bool IsFactionAgent(MainRoleType role, Faction faction)
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

	internal GameResult[] CreatePossibleGameResults(IEnumerable<Faction> possibleFactions)
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

	internal static HeadlessResponsePolicy CreateBaselinePolicy() => new(
		BaselineRandomDecisionStrategy.Identity,
		BaselineRandomDecisionStrategy.Policy.AdmittedSemantics);
}

internal sealed class SimulatorProfileRoleDescriptor
{
	internal MainRoleType Role { get; }

	internal Faction BeneficiaryFaction { get; }

	internal IReadOnlySet<Faction> AgentFactions { get; }

	internal SimulatorProfileRoleDescriptor(
		MainRoleType role,
		Faction beneficiaryFaction,
		params Faction[] agentFactions)
	{
		if (!Enum.IsDefined(role))
		{
			throw new ArgumentOutOfRangeException(nameof(role));
		}
		if (!Enum.IsDefined(beneficiaryFaction))
		{
			throw new ArgumentOutOfRangeException(nameof(beneficiaryFaction));
		}
		ArgumentNullException.ThrowIfNull(agentFactions);
		if (agentFactions.Any(faction => !Enum.IsDefined(faction)))
		{
			throw new ArgumentOutOfRangeException(nameof(agentFactions));
		}

		Role = role;
		BeneficiaryFaction = beneficiaryFaction;
		AgentFactions = agentFactions.ToHashSet();
	}
}

public sealed class SimulatorCapability : SimulatorProfile
{
	private static readonly SimulatorProfileRoleDescriptor[] SafetyScreeningRoleDescriptors =
	[
		new(MainRoleType.SimpleWerewolf, Faction.Werewolf, Faction.Werewolf),
		new(MainRoleType.BigBadWolf, Faction.Werewolf, Faction.Werewolf),
		new(MainRoleType.Seer, Faction.Villager),
		new(MainRoleType.WildChild, Faction.Villager),
		new(MainRoleType.SimpleVillager, Faction.Villager),
		new(MainRoleType.VillagerVillager, Faction.Villager),
		new(MainRoleType.TwoSisters, Faction.Villager),
		new(MainRoleType.ThreeBrothers, Faction.Villager),
		new(MainRoleType.Witch, Faction.Villager),
			new(MainRoleType.Hunter, Faction.Villager),
			new(MainRoleType.LittleGirl, Faction.Villager),
			new(MainRoleType.Defender, Faction.Villager),
				new(MainRoleType.StutteringJudge, Faction.Villager),
				new(MainRoleType.Scapegoat, Faction.Villager),
				new(MainRoleType.VillageIdiot, Faction.Villager),
				new(MainRoleType.WolfHound, Faction.Villager),
			new(
				MainRoleType.AccursedWolfFather,
				Faction.Werewolf,
				Faction.Werewolf),
			new(
				MainRoleType.WhiteWerewolf,
				Faction.WhiteWerewolf,
				Faction.Werewolf),
			new(MainRoleType.Piper, Faction.Piper),
			new(MainRoleType.BearTamer, Faction.Villager),
			new(MainRoleType.Fox, Faction.Villager),
			new(MainRoleType.KnightWithRustySword, Faction.Villager),
			new(MainRoleType.Cupid, Faction.Villager)
	];

	private static readonly SimulatorProfileRoleDescriptor[] FullProbabilityRoleDescriptors =
	[
		new(MainRoleType.SimpleWerewolf, Faction.Werewolf, Faction.Werewolf),
		new(MainRoleType.Seer, Faction.Villager),
		new(MainRoleType.WildChild, Faction.Villager),
		new(MainRoleType.SimpleVillager, Faction.Villager)
	];

	public static SimulatorCapability SafetyScreening =>
		SimulatorCapabilityRegistry.Production.SafetyScreening;

	public static SimulatorCapability FullProbability =>
		SimulatorCapabilityRegistry.Production.FullProbability;

	internal static SimulatorCapability CreateSafetyScreening() => new(
		new SimulatorProfileIdentity("safety-screening", "23"),
		SafetyScreeningRoleDescriptors,
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
						.ChooseAccursedWolfFatherInfection
				]),
		supportsActorSetupCards: false,
		supportedRuleStates: [SimulationRuleState.Default]);

	internal static SimulatorCapability CreateFullProbability() => new(
			new SimulatorProfileIdentity("full-probability", "4"),
		FullProbabilityRoleDescriptors,
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
		supportedRuleStates: [SimulationRuleState.Default]);

	internal SimulatorCapability(
		SimulatorProfileIdentity identity,
		IEnumerable<SimulatorProfileRoleDescriptor> roleDescriptors,
		IEnumerable<SharedVictoryGameResult>? sharedVictoryCapabilities = null,
		HeadlessResponsePolicy? headlessResponsePolicy = null,
		bool supportsActorSetupCards = false,
		IEnumerable<SimulationRuleState>? supportedRuleStates = null)
		: base(
			identity,
			roleDescriptors,
			sharedVictoryCapabilities,
			headlessResponsePolicy,
			supportsActorSetupCards,
			supportedRuleStates)
	{
	}
}

public sealed class SimulatorCapabilityRegistry
{
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

		SafetyScreening = safetyScreening;
		FullProbability = fullProbability;
	}
}
