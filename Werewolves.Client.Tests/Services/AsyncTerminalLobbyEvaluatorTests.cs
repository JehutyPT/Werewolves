using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class AsyncTerminalLobbyEvaluatorTests
{
	[Fact]
	public async Task EvaluateAsync_AtExactlyTenSecondsRequestsCancellationAndReturnsWithoutWaiting()
	{
		var clock = new ManualTimeProvider();
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var adapter = new AsyncTerminalLobbyEvaluator(
			(_, token) =>
			{
				using var registration = token.Register(() => cancelled.TrySetResult());
				started.TrySetResult();
				release.Task.GetAwaiter().GetResult();
				return new AlreadyDecidedTerminalEvaluation(
					new SingleFactionGameResult(Faction.Werewolf),
					AlreadyDecidedReason.WerewolfControlShortcut);
			},
			clock);

		var evaluation = adapter.EvaluateAsync(SupportedScenario());
		await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		clock.Advance(TimeSpan.FromMilliseconds(9_999));
		evaluation.IsCompleted.Should().BeFalse();

		clock.Advance(TimeSpan.FromMilliseconds(1));
		(await evaluation).Should().BeOfType<CouldNotEvaluateLobbyEvaluation>();
		await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

		release.TrySetResult();
		await Task.Yield();
	}

	[Fact]
	public async Task EvaluateAsync_SuccessBeforeTimeoutReturnsTerminalResult()
	{
		var expected = new AlreadyDecidedTerminalEvaluation(
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut);
		var adapter = new AsyncTerminalLobbyEvaluator((_, _) => expected, new ManualTimeProvider());

		(await adapter.EvaluateAsync(SupportedScenario())).Should().BeSameAs(expected);
	}

	[Fact]
	public async Task EvaluateAsync_ExecutionFailureCollapsesToCouldNotEvaluate()
	{
		var adapter = new AsyncTerminalLobbyEvaluator(
			(_, _) => throw new InvalidOperationException("injected evaluator failure"),
			new ManualTimeProvider());

		(await adapter.EvaluateAsync(SupportedScenario()))
			.Should().BeOfType<CouldNotEvaluateLobbyEvaluation>();
	}

	private static SimulationScenario SupportedScenario() =>
		new(5,
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		]);
}
