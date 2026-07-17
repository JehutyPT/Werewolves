using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public static class SimulationScenarioClassifier
{
	public static SimulationScenarioClassification Classify(SimulationScenario scenario)
		=> Classify(scenario, SimulatorProfile.Active);

	internal static SimulationScenarioClassification Classify(
		SimulationScenario scenario,
		SimulatorProfile profile)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		ArgumentNullException.ThrowIfNull(profile);

		GameSessionConfig.TryGetPhysicalSetupIssues(
			scenario.PlayerCount,
			scenario.RoleCompositionCards,
			scenario.ActorSetupCards,
			out var errors);
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

		var simulatorUnsupportedRoles = scenario.RoleCompositionCards
			.Distinct()
			.Where(role => !profile.SupportsRole(role))
			.OrderBy(role => role.ToString(), StringComparer.Ordinal)
			.ToArray();
		var simulatorSupport = new SimulatorSupportResult(
			scenario,
			appSupport,
			profile,
			simulatorUnsupportedRoles,
			hasUnsupportedActorSetupCards:
				scenario.ActorSetupCards.Cards.Count > 0
				&& !profile.SupportsActorSetupCards,
			hasUnsupportedRuleState: !profile.SupportsRuleState(scenario.RuleState));
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

		var alreadyDecided = AlreadyDecidedRoleCompositionClassifier.Classify(
			scenario.ToCanonical().RoleComposition,
			profile);
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
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				profile.Identity));

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
