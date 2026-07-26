using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public class SimulationScenarioClassificationTests : DiagnosticTestBase
{
	public SimulationScenarioClassificationTests(ITestOutputHelper output) : base(output)
	{
	}

	[Fact]
	public void Classify_WithWerewolfControlAtLobbyExit_ReturnsSingleFactionGameResult()
	{
		var scenario = CreateWerewolfControlAtLobbyExitScenario();

		var classification = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		classification.AlreadyDecided.Should().NotBeNull();
		classification.AlreadyDecided!.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.Werewolf));
		classification.AlreadyDecided.Reason.Should().Be(
			AlreadyDecidedReason.WerewolfControlShortcut);
		classification.Cacheability.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void Classify_WithSupportedRulesValidScenario_ReturnsCompleteCacheableGateChain()
	{
		var scenario = CreateSupportedScenario();

		var classification = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		classification.Scenario.Should().BeSameAs(scenario);
		classification.RulesValidity.IsValid.Should().BeTrue();
		classification.RulesValidity.Errors.Should().BeEmpty();
		classification.AppSupport.Should().NotBeNull();
		classification.AppSupport!.RulesValidity.Should().BeSameAs(classification.RulesValidity);
		classification.AppSupport.IsSupported.Should().BeTrue();
		classification.AppSupport.Scenario.Should().BeSameAs(scenario);
		classification.SimulatorSupport.Should().NotBeNull();
		classification.SimulatorSupport!.AppSupport.Should().BeSameAs(classification.AppSupport);
		classification.SimulatorSupport.IsSupported.Should().BeTrue();
		classification.SimulatorSupport.Scenario.Should().BeSameAs(scenario);
		classification.AlreadyDecided.Should().NotBeNull();
		classification.AlreadyDecided!.IsAlreadyDecided.Should().BeFalse();
		classification.AlreadyDecided.GameResult.Should().BeNull();
		classification.AlreadyDecided.Reason.Should().Be(
			AlreadyDecidedReason.NoLobbyExitVictoryPredicateSatisfied);
		classification.Cacheability.Should().NotBeNull();
		classification.Cacheability!.SimulatorSupport.Should().BeSameAs(classification.SimulatorSupport);
		classification.Cacheability.IsCacheable.Should().BeTrue();
		classification.Cacheability.Scenario.Should().BeSameAs(scenario);
		classification.Cacheability.CompatibilityIdentity.Should().Be(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
			SimulatorCapability.FullProbability.Identity));
		MarkTestCompleted();
	}

	[Fact]
	public void Classify_WithRulesInvalidComposition_ReturnsStructuredErrorsWithoutEvaluatingLaterGates()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var classification = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		classification.RulesValidity.IsValid.Should().BeFalse();
		classification.RulesValidity.Scenario.Should().BeSameAs(scenario);
		classification.RulesValidity.Errors.Should().ContainSingle(error =>
			error.Type == GameConfigValidationErrorType.MissingHardAlignedWerewolf);
		classification.AppSupport.Should().BeNull();
		classification.SimulatorSupport.Should().BeNull();
		classification.AlreadyDecided.Should().BeNull();
		classification.Cacheability.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void Classify_WithAppUnsupportedRole_StopsAfterAppGateAndPreservesInput()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.BigBadWolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var classification = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		classification.RulesValidity.IsValid.Should().BeTrue();
		classification.AppSupport.Should().NotBeNull();
		classification.AppSupport!.IsSupported.Should().BeFalse();
		classification.AppSupport.Scenario.Should().BeSameAs(scenario);
		classification.AppSupport.UnsupportedRoles.Should().Equal(MainRoleType.BigBadWolf);
		classification.SimulatorSupport.Should().BeNull();
		classification.AlreadyDecided.Should().BeNull();
		classification.Cacheability.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void Classify_WithRulesValidUnsupportedPlayerCount_StopsAfterAppGate()
	{
		var scenario = new SimulationScenario(
			4,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		var classification = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		classification.RulesValidity.IsValid.Should().BeTrue();
		classification.AppSupport.Should().NotBeNull();
		classification.AppSupport!.IsSupported.Should().BeFalse();
		classification.AppSupport.Errors.Should().ContainSingle(error =>
			error.Type == GameConfigValidationErrorType.TooFewPlayers);
		classification.SimulatorSupport.Should().BeNull();
		classification.AlreadyDecided.Should().BeNull();
		classification.Cacheability.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void Classify_WithUnsupportedActorArtifact_StopsAfterSimulatorGateAndPreservesInput()
	{
		var scenario = new SimulationScenario(
			5,
			CreateSupportedScenario().RoleCompositionCards,
			new ActorSetupCards(
				[MainRoleType.Cupid, MainRoleType.Defender, MainRoleType.Elder]));

		var classification = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		classification.RulesValidity.IsValid.Should().BeTrue();
		classification.AppSupport!.IsSupported.Should().BeTrue();
		classification.SimulatorSupport.Should().NotBeNull();
		classification.SimulatorSupport!.IsSupported.Should().BeFalse();
		classification.SimulatorSupport.Scenario.Should().BeSameAs(scenario);
		classification.SimulatorSupport.HasUnsupportedActorSetupCards.Should().BeTrue();
		classification.SimulatorSupport.HasUnsupportedRuleState.Should().BeFalse();
		classification.AlreadyDecided.Should().BeNull();
		classification.Cacheability.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void Classify_WithUnsupportedRequiredRuleState_StopsAfterSimulatorGateAndPreservesInput()
	{
		var scenario = new SimulationScenario(
			5,
			CreateSupportedScenario().RoleCompositionCards,
			ruleState: new SimulationRuleState(NewMoonEnabled: true));

		var classification = SimulationScenarioClassifier.Classify(
			scenario,
			SimulatorCapability.FullProbability);

		classification.RulesValidity.IsValid.Should().BeTrue();
		classification.AppSupport!.IsSupported.Should().BeTrue();
		classification.SimulatorSupport.Should().NotBeNull();
		classification.SimulatorSupport!.IsSupported.Should().BeFalse();
		classification.SimulatorSupport.Scenario.Should().BeSameAs(scenario);
		classification.SimulatorSupport.HasUnsupportedActorSetupCards.Should().BeFalse();
		classification.SimulatorSupport.HasUnsupportedRuleState.Should().BeTrue();
		classification.AlreadyDecided.Should().BeNull();
		classification.Cacheability.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void Classify_WithDifferentRoleInputOrder_ReturnsTheSameAlreadyDecidedResult()
	{
		var first = CreateWerewolfControlAtLobbyExitScenario();
		var second = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf
			]);

		var firstResult = SimulationScenarioClassifier.Classify(
			first,
			SimulatorCapability.FullProbability).AlreadyDecided;
		var secondResult = SimulationScenarioClassifier.Classify(
			second,
			SimulatorCapability.FullProbability).AlreadyDecided;

		firstResult.Should().Be(secondResult);
		MarkTestCompleted();
	}

	[Fact]
	public void Classify_WithNotAlreadyDecidedCompositionInDifferentRoleInputOrder_ReturnsTheSameResult()
	{
		var first = CreateSupportedScenario();
		var second = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleVillager,
				MainRoleType.WildChild,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.Seer
			]);

		var firstResult = SimulationScenarioClassifier.Classify(
			first,
			SimulatorCapability.FullProbability).AlreadyDecided;
		var secondResult = SimulationScenarioClassifier.Classify(
			second,
			SimulatorCapability.FullProbability).AlreadyDecided;

		firstResult.Should().Be(
			new AlreadyDecidedRoleCompositionResult(
				null,
				AlreadyDecidedReason.NoLobbyExitVictoryPredicateSatisfied));
		secondResult.Should().Be(firstResult);
		MarkTestCompleted();
	}

	private static SimulationScenario CreateSupportedScenario() =>
		new(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.WildChild,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

	private static SimulationScenario CreateWerewolfControlAtLobbyExitScenario() =>
		new(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
}
