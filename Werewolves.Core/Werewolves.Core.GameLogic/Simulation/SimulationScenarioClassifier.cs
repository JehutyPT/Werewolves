using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public static class SimulationScenarioClassifier
{
	public static SimulationScenarioClassification Classify(
		SimulationScenario scenario,
		SimulatorCapability capability)
	{
		ArgumentNullException.ThrowIfNull(capability);
		return Classify(
			scenario,
			capability.ClassifySupport,
			composition => AlreadyDecidedRoleCompositionClassifier.Classify(
				composition,
				capability),
			capability.CreateCompatibilityIdentity);
	}

	internal static SimulationScenarioClassification Classify(
		SimulationScenario scenario,
		SimulatorProfile profile)
	{
		ArgumentNullException.ThrowIfNull(profile);
		return Classify(
			scenario,
			profile.ClassifySupport,
			composition => AlreadyDecidedRoleCompositionClassifier.Classify(
				composition,
				profile),
			candidate => new SimulationCompatibilityIdentity(
				candidate.ToCanonical(),
				profile.Identity));
	}

	private static SimulationScenarioClassification Classify(
		SimulationScenario scenario,
		Func<SimulationScenario, AppSupportResult, SimulatorSupportResult>
			classifySimulatorSupport,
		Func<CanonicalRoleComposition, AlreadyDecidedRoleCompositionResult>
			classifyAlreadyDecided,
		Func<SimulationScenario, SimulationCompatibilityIdentity>
			createCompatibilityIdentity)
	{
		ArgumentNullException.ThrowIfNull(scenario);

		List<GameConfigValidationError> errors;
		if (scenario.ThiefOfferBranchPolicy is not null &&
			scenario.Offer1Role is { } offer1Role &&
			scenario.Offer2Role is { } offer2Role)
		{
			GameSessionConfig.TryGetRoleLockInPhysicalSetupIssues(
				scenario.PlayerCount,
				scenario.DealPoolCards,
				offer1Role,
				offer2Role,
				scenario.ActorSetupCards,
				out errors);
		}
		else
		{
			GameSessionConfig.TryGetPhysicalSetupIssues(
				scenario.PlayerCount,
				scenario.RoleCompositionCards,
				scenario.ActorSetupCards,
				out errors);
		}
		var prejudicedManipulatorIsReachable = scenario.RoleCompositionCards.Contains(
			MainRoleType.PrejudicedManipulator);
		if (prejudicedManipulatorIsReachable != (scenario.PublicGroupPartition is not null))
		{
			errors.Add(new GameConfigValidationError(
				GameConfigValidationErrorType.PublicGroupPartitionMismatch,
				"A Public Group Partition is required exactly when Prejudiced Manipulator is reachable in the complete Role Composition."));
		}
		var appSupportErrors = errors
			.Where(IsAppSupportError)
			.ToArray();
		var rulesValidity = new RulesValidityResult(
			scenario,
			errors.Where(error => !IsAppSupportError(error)));
		if (!rulesValidity.IsValid)
		{
			return new SimulationScenarioClassification(
				scenario,
				rulesValidity,
				appSupport: null,
				simulatorSupport: null,
				alreadyDecided: null,
				cacheability: null);
		}

		var appSupport = new AppSupportResult(
			scenario,
			rulesValidity,
			SupportedRoleCatalog.GetUnsupportedRoles(scenario.RoleCompositionCards)
				.OrderBy(role => role.ToString(), StringComparer.Ordinal),
			appSupportErrors);
		if (!appSupport.IsSupported)
		{
			return new SimulationScenarioClassification(
				scenario,
				rulesValidity,
				appSupport,
				simulatorSupport: null,
				alreadyDecided: null,
				cacheability: null);
		}

		var simulatorSupport = classifySimulatorSupport(scenario, appSupport);
		if (!simulatorSupport.IsSupported)
		{
			return new SimulationScenarioClassification(
				scenario,
				rulesValidity,
				appSupport,
				simulatorSupport,
				alreadyDecided: null,
				cacheability: null);
		}

		var alreadyDecided = classifyAlreadyDecided(
			scenario.ToCanonical().RoleComposition);
		if (alreadyDecided.IsAlreadyDecided)
		{
			return new SimulationScenarioClassification(
				scenario,
				rulesValidity,
				appSupport,
				simulatorSupport,
				alreadyDecided,
				cacheability: null);
		}

		var cacheability = new CacheabilityResult(
			scenario,
			simulatorSupport,
			createCompatibilityIdentity(scenario));

		return new SimulationScenarioClassification(
			scenario,
			rulesValidity,
			appSupport,
			simulatorSupport,
			alreadyDecided,
			cacheability);
	}

	private static bool IsAppSupportError(GameConfigValidationError error) =>
		error.Type is GameConfigValidationErrorType.TooFewPlayers
			or GameConfigValidationErrorType.TooManyPlayers;
}

public sealed class SimulationScenarioClassification
{
	public SimulationScenario Scenario { get; }

	public RulesValidityResult RulesValidity { get; }

	public AppSupportResult? AppSupport { get; }

	public SimulatorSupportResult? SimulatorSupport { get; }

	public AlreadyDecidedRoleCompositionResult? AlreadyDecided { get; }

	public CacheabilityResult? Cacheability { get; }

	internal SimulationScenarioClassification(
		SimulationScenario scenario,
		RulesValidityResult rulesValidity,
		AppSupportResult? appSupport,
		SimulatorSupportResult? simulatorSupport,
		AlreadyDecidedRoleCompositionResult? alreadyDecided,
		CacheabilityResult? cacheability)
	{
		Scenario = scenario;
		RulesValidity = rulesValidity;
		AppSupport = appSupport;
		SimulatorSupport = simulatorSupport;
		AlreadyDecided = alreadyDecided;
		Cacheability = cacheability;
	}
}

public sealed class RulesValidityResult
{
	public SimulationScenario Scenario { get; }

	public bool IsValid => Errors.Count == 0;

	public IReadOnlyList<GameConfigValidationError> Errors { get; }

	internal RulesValidityResult(
		SimulationScenario scenario,
		IEnumerable<GameConfigValidationError> errors)
	{
		Scenario = scenario;
		Errors = Array.AsReadOnly(errors.ToArray());
	}
}

public sealed class AppSupportResult
{
	public SimulationScenario Scenario { get; }

	public RulesValidityResult RulesValidity { get; }

	public bool IsSupported => UnsupportedRoles.Count == 0 && Errors.Count == 0;

	public IReadOnlyList<MainRoleType> UnsupportedRoles { get; }

	public IReadOnlyList<GameConfigValidationError> Errors { get; }

	internal AppSupportResult(
		SimulationScenario scenario,
		RulesValidityResult rulesValidity,
		IEnumerable<MainRoleType> unsupportedRoles,
		IEnumerable<GameConfigValidationError> errors)
	{
		Scenario = scenario;
		RulesValidity = rulesValidity;
		UnsupportedRoles = Array.AsReadOnly(unsupportedRoles.ToArray());
		Errors = Array.AsReadOnly(errors.ToArray());
	}
}

public sealed class SimulatorSupportResult
{
	public SimulationScenario Scenario { get; }

	public AppSupportResult AppSupport { get; }

	public SimulatorProfile Profile { get; }

	public SimulatorCapability Capability =>
		Profile as SimulatorCapability
		?? throw new InvalidOperationException(
			"Legacy simulator-profile classifications do not select a current Simulator Capability.");

	public IReadOnlyList<MainRoleType> UnsupportedRoles { get; }

	public bool HasUnsupportedActorSetupCards { get; }

	public bool HasUnsupportedRuleState { get; }

	public bool IsSupported =>
		UnsupportedRoles.Count == 0
		&& !HasUnsupportedActorSetupCards
		&& !HasUnsupportedRuleState;

	internal SimulatorSupportResult(
		SimulationScenario scenario,
		AppSupportResult appSupport,
		SimulatorProfile profile,
		IEnumerable<MainRoleType> unsupportedRoles,
		bool hasUnsupportedActorSetupCards,
		bool hasUnsupportedRuleState)
	{
		Scenario = scenario;
		AppSupport = appSupport;
		Profile = profile;
		UnsupportedRoles = Array.AsReadOnly(unsupportedRoles.ToArray());
		HasUnsupportedActorSetupCards = hasUnsupportedActorSetupCards;
		HasUnsupportedRuleState = hasUnsupportedRuleState;
	}
}

public sealed class CacheabilityResult
{
	public SimulationScenario Scenario { get; }

	public SimulatorSupportResult SimulatorSupport { get; }

	public bool IsCacheable => true;

	public SimulationCompatibilityIdentity CompatibilityIdentity { get; }

	internal CacheabilityResult(
		SimulationScenario scenario,
		SimulatorSupportResult simulatorSupport,
		SimulationCompatibilityIdentity compatibilityIdentity)
	{
		Scenario = scenario;
		SimulatorSupport = simulatorSupport;
		CompatibilityIdentity = compatibilityIdentity;
	}
}
