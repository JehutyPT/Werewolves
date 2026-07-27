using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public static class LegacyTerminalLobbyCacheCompatibility
{
	private static readonly MainRoleType[] FrozenLegacyRoles =
	[
		MainRoleType.SimpleWerewolf,
		MainRoleType.Seer,
		MainRoleType.WildChild,
		MainRoleType.SimpleVillager
	];

	private static readonly MainRoleType[] FrozenSafetyOnlyRoles =
	[
		MainRoleType.VillagerVillager,
		MainRoleType.TwoSisters,
		MainRoleType.ThreeBrothers
	];

	private static readonly ModeratorInstructionSemantic[] FrozenSafetyOnlySemantics =
	[
		ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal,
		ModeratorInstructionSemantic.RecognizeRoleHolders,
		ModeratorInstructionSemantic.CommunicateAsRoleHolders
	];

	public static bool TryProject(
		TerminalLobbyCacheRecord legacyRecord,
		SimulatorCapability consumerCapability,
		SimulationCompatibilityIdentity consumerIdentity,
		LobbyEvaluationDepth depth,
		out LegacyTerminalLobbyCacheProjection projection)
	{
		ArgumentNullException.ThrowIfNull(legacyRecord);
		ArgumentNullException.ThrowIfNull(consumerCapability);
		ArgumentNullException.ThrowIfNull(consumerIdentity);
		projection = null!;

		if (!IsKnownConsumer(consumerCapability, consumerIdentity, depth)
			|| !legacyRecord.CompatibilityIdentity.Profile.Equals(
				SimulatorProfile.LegacyCore.Identity)
			|| !legacyRecord.CompatibilityIdentity.Scenario.Equals(consumerIdentity.Scenario)
			|| !IsFrozenCompatibleScenario(consumerIdentity.Scenario)
			|| !HasFrozenCompatibilitySemantics(consumerCapability))
		{
			return false;
		}

		projection = legacyRecord switch
		{
			AlreadyDecidedTerminalCacheRecord already =>
				new LegacyAlreadyDecidedTerminalLobbyCacheProjection(
					consumerIdentity,
					already.GameResult,
					already.Reason),
			DegenerateTerminalCacheRecord =>
				new LegacyDegenerateTerminalLobbyCacheProjection(consumerIdentity),
			ProbabilityTerminalCacheRecord probability
				when depth == LobbyEvaluationDepth.DegenerateScreeningOnly =>
				new LegacyScreeningPassedTerminalLobbyCacheProjection(consumerIdentity),
			ProbabilityTerminalCacheRecord probability =>
				new LegacyProbabilityTerminalLobbyCacheProjection(
					consumerIdentity,
					probability.GameResultFrequencies,
					probability.GameResultFrequencyByTurn),
			_ => null!
		};
		return projection is not null;
	}

	private static bool IsKnownConsumer(
		SimulatorCapability capability,
		SimulationCompatibilityIdentity consumerIdentity,
		LobbyEvaluationDepth depth)
	{
		if (!consumerIdentity.Profile.Equals(capability.Identity))
		{
			return false;
		}

		if (capability.Identity.Equals(SimulatorCapability.SafetyScreening.Identity)
			&& depth == LobbyEvaluationDepth.DegenerateScreeningOnly)
		{
			return true;
		}

		if (capability.Identity.Equals(SimulatorCapability.FullProbability.Identity)
			&& depth is LobbyEvaluationDepth.DegenerateScreeningOnly
				or LobbyEvaluationDepth.FullProbability)
		{
			return true;
		}

		return false;
	}

	private static bool HasFrozenCompatibilitySemantics(
		SimulatorCapability capability)
	{
		if (capability.Identity.Equals(SimulatorCapability.FullProbability.Identity))
		{
			return capability.HasSameCompatibilitySemanticsAs(
				SimulatorProfile.LegacyCore);
		}

		if (!capability.Identity.Equals(SimulatorCapability.SafetyScreening.Identity)
			|| !capability.SupportedRoles.ToHashSet().SetEquals(
				FrozenLegacyRoles.Concat(FrozenSafetyOnlyRoles)))
		{
			return false;
		}

		var legacy = SimulatorProfile.LegacyCore;
		var hasFrozenRoleSemantics = FrozenLegacyRoles.All(role =>
			legacy.TryGetBeneficiaryFaction(role, out var legacyFaction)
			&& capability.TryGetBeneficiaryFaction(role, out var currentFaction)
			&& currentFaction == legacyFaction);
		var hasSafetyOnlyRoleSemantics = FrozenSafetyOnlyRoles.All(role =>
			capability.TryGetBeneficiaryFaction(role, out var faction)
			&& faction == Faction.Villager);

		return hasFrozenRoleSemantics
			&& hasSafetyOnlyRoleSemantics
			&& capability.SupportsActorSetupCards == legacy.SupportsActorSetupCards
			&& capability.SupportedRuleStates.ToHashSet()
				.SetEquals(legacy.SupportedRuleStates)
			&& capability.SharedVictoryCapabilities.ToHashSet()
				.SetEquals(legacy.SharedVictoryCapabilities)
			&& capability.HeadlessResponsePolicy.StrategyIdentity.Equals(
				legacy.HeadlessResponsePolicy.StrategyIdentity)
			&& capability.HeadlessResponsePolicy.AdmittedSemantics.SetEquals(
				legacy.HeadlessResponsePolicy.AdmittedSemantics.Concat(
					FrozenSafetyOnlySemantics));
	}

	private static bool IsFrozenCompatibleScenario(CanonicalSimulationScenario canonical)
	{
		if (canonical.ActorSetupCards.Count != 0
			|| canonical.RuleState != SimulationRuleState.Default
			|| canonical.RoleComposition.Entries.Any(entry =>
				!FrozenLegacyRoles.Contains(entry.Role)))
		{
			return false;
		}

		SimulationScenario scenario;
		try
		{
			scenario = new SimulationScenario(
				canonical.PlayerCount,
				canonical.RoleComposition.Entries.SelectMany(entry =>
					Enumerable.Repeat(entry.Role, entry.Count)),
				new ActorSetupCards(canonical.ActorSetupCards),
				canonical.RuleState);
		}
		catch (ArgumentException)
		{
			return false;
		}

		if (!scenario.ToCanonical().Equals(canonical))
		{
			return false;
		}

		var legacy = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorProfile.LegacyCore);
		var safety = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.SafetyScreening);
		var probability = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);
		return legacy.SimulatorSupport is { IsSupported: true }
			&& safety.SimulatorSupport is { IsSupported: true }
			&& probability.SimulatorSupport is { IsSupported: true };
	}
}
