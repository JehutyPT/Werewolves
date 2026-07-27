using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public class SimulatorProfile
{
	private static readonly SimulatorProfileRoleDescriptor[] LegacyRoleDescriptors =
	[
		new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
		new(MainRoleType.Seer, Faction.Villager),
		new(MainRoleType.WildChild, Faction.Villager),
		new(MainRoleType.SimpleVillager, Faction.Villager)
	];

	private readonly IReadOnlyDictionary<MainRoleType, Faction> _beneficiaryFactions;
	private readonly SharedVictoryGameResult[] _sharedVictoryCapabilities;
	private readonly SimulationRuleState[] _supportedRuleStates;

	public static SimulatorProfile LegacyCore { get; } = new(
		new SimulatorProfileIdentity("core-simulator", "1"),
		LegacyRoleDescriptors,
		sharedVictoryCapabilities: [],
		headlessResponsePolicy: new HeadlessResponsePolicy(
			BaselineRandomDecisionStrategy.Identity,
			[
				ModeratorInstructionSemantic.StartGame,
				ModeratorInstructionSemantic.FinishedGame,
				ModeratorInstructionSemantic.StartNight,
				ModeratorInstructionSemantic.FinishNightActions,
				ModeratorInstructionSemantic.WakeRole,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
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
				ModeratorInstructionSemantic.AnnounceLynchingImmunity,
				ModeratorInstructionSemantic.AnnounceDayElimination
			]),
		supportsActorSetupCards: false,
		supportedRuleStates: [SimulationRuleState.Default]);

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

	internal bool HasSameCompatibilitySemanticsAs(SimulatorProfile other)
	{
		ArgumentNullException.ThrowIfNull(other);

		return _beneficiaryFactions.Count == other._beneficiaryFactions.Count
			&& _beneficiaryFactions.All(pair =>
				other._beneficiaryFactions.TryGetValue(pair.Key, out var faction)
				&& faction == pair.Value)
			&& SupportsActorSetupCards == other.SupportsActorSetupCards
			&& _supportedRuleStates.ToHashSet().SetEquals(other._supportedRuleStates)
			&& _sharedVictoryCapabilities.ToHashSet().SetEquals(other._sharedVictoryCapabilities)
			&& HeadlessResponsePolicy.StrategyIdentity.Equals(
				other.HeadlessResponsePolicy.StrategyIdentity)
			&& HeadlessResponsePolicy.AdmittedSemantics.SetEquals(
				other.HeadlessResponsePolicy.AdmittedSemantics);
	}

	internal static HeadlessResponsePolicy CreateBaselinePolicy() => new(
		BaselineRandomDecisionStrategy.Identity,
		BaselineRandomDecisionStrategy.Policy.AdmittedSemantics);
}

internal sealed record SimulatorProfileRoleDescriptor(
	MainRoleType Role,
	Faction BeneficiaryFaction);

public sealed class SimulatorCapability : SimulatorProfile
{
	private static readonly SimulatorProfileRoleDescriptor[] SafetyScreeningRoleDescriptors =
	[
		new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
		new(MainRoleType.Seer, Faction.Villager),
		new(MainRoleType.WildChild, Faction.Villager),
		new(MainRoleType.SimpleVillager, Faction.Villager),
		new(MainRoleType.VillagerVillager, Faction.Villager),
		new(MainRoleType.TwoSisters, Faction.Villager)
	];

	private static readonly SimulatorProfileRoleDescriptor[] FullProbabilityRoleDescriptors =
	[
		new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
		new(MainRoleType.Seer, Faction.Villager),
		new(MainRoleType.WildChild, Faction.Villager),
		new(MainRoleType.SimpleVillager, Faction.Villager)
	];

	public static SimulatorCapability SafetyScreening =>
		SimulatorCapabilityRegistry.Production.SafetyScreening;

	public static SimulatorCapability FullProbability =>
		SimulatorCapabilityRegistry.Production.FullProbability;

	internal static SimulatorCapability CreateSafetyScreening() => new(
		new SimulatorProfileIdentity("safety-screening", "3"),
		SafetyScreeningRoleDescriptors,
		headlessResponsePolicy: new HeadlessResponsePolicy(
			BaselineRandomDecisionStrategy.Identity,
			[
				ModeratorInstructionSemantic.StartGame,
				ModeratorInstructionSemantic.FinishedGame,
				ModeratorInstructionSemantic.StartNight,
				ModeratorInstructionSemantic.FinishNightActions,
				ModeratorInstructionSemantic.WakeRole,
				ModeratorInstructionSemantic.IdentifyRoleHolders,
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
				ModeratorInstructionSemantic.AnnounceLynchingImmunity,
				ModeratorInstructionSemantic.AnnounceDayElimination,
				ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal,
				ModeratorInstructionSemantic.RecognizeRoleHolders,
				ModeratorInstructionSemantic.CommunicateAsRoleHolders
			]),
		supportsActorSetupCards: false,
		supportedRuleStates: [SimulationRuleState.Default]);

	internal static SimulatorCapability CreateFullProbability() => new(
		new SimulatorProfileIdentity("full-probability", "1"),
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
				ModeratorInstructionSemantic.AnnounceLynchingImmunity,
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
