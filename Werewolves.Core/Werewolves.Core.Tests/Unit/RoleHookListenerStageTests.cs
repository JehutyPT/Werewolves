using FluentAssertions;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Enums;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class RoleHookListenerStageTests
{
	[Fact]
	public void AdvancingNonInitialStage_RejectsNonSkipResultThatRemainsInStartingState()
	{
		var stage = CreateStage(
			startStage: TestRoleStage.AwaitingInput,
			result: HookListenerActionResult.Complete(TestRoleStage.AwaitingInput),
			shouldAdvanceState: true);

		var act = () => stage.Execute(
			new TestRoleStageExecutionKey(),
			null!,
			null!,
			TestRoleStage.AwaitingInput);

		act.Should()
			.Throw<InvalidOperationException>()
			.WithMessage("*requires state advancement*");
	}

	[Fact]
	public void ExplicitlyNonAdvancingTerminalStage_AllowsResultThatRemainsInStartingState()
	{
		var stage = CreateStage(
			startStage: TestRoleStage.AwaitingInput,
			result: HookListenerActionResult.Complete(TestRoleStage.AwaitingInput),
			shouldAdvanceState: false);

		var result = stage.Execute(
			new TestRoleStageExecutionKey(),
			null!,
			null!,
			TestRoleStage.AwaitingInput);

		result.Outcome.Should().Be(HookListenerOutcome.Complete);
		result.NextListenerPhase.Should().Be(nameof(TestRoleStage.AwaitingInput));
	}

	[Fact]
	public void SkippedAdvancingStage_ReturnsWithoutAStateTransition()
	{
		var stage = CreateStage(
			startStage: TestRoleStage.AwaitingInput,
			result: HookListenerActionResult.Skip(),
			shouldAdvanceState: true);

		var result = stage.Execute(
			new TestRoleStageExecutionKey(),
			null!,
			null!,
			TestRoleStage.AwaitingInput);

		result.Outcome.Should().Be(HookListenerOutcome.Skip);
		result.NextListenerPhase.Should().BeNull();
	}

	private static RoleHookListener<TestRoleStage>.RoleStateMachineStage CreateStage(
		TestRoleStage startStage,
		HookListenerActionResult result,
		bool shouldAdvanceState)
		=> new(
			MainRoleType.SimpleWerewolf,
			GameHook.NightMainActionLoop,
			startStage,
			(_, _) => result,
			[startStage],
			ShouldAdvanceState: shouldAdvanceState);

	private sealed class TestRoleStageExecutionKey :
		RoleHookListener<TestRoleStage>.IRoleStageExecutionKey;

	private enum TestRoleStage
	{
		AwaitingInput
	}
}
