using FluentAssertions;
using Werewolves.Client.Components.Game.Views;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class HoldConfirmationGateTests
{
	private static readonly TimeSpan RequiredDuration = TimeSpan.FromMilliseconds(400);

	[Fact]
	public void ReleaseBeforeRequiredDurationCancelsWithoutCompleting()
	{
		var gate = new HoldConfirmationGate(RequiredDuration);

		gate.Begin();
		var result = gate.ReleaseAfter(TimeSpan.FromMilliseconds(399));
		var timerResult = gate.ThresholdReached();
		var effects = ApplyCompletionEffects(result, timerResult);

		result.Should().Be(HoldConfirmationResult.Canceled);
		timerResult.Should().Be(HoldConfirmationResult.None);
		effects.Submissions.Should().Be(0);
		effects.HapticLongPresses.Should().Be(0);
	}

	[Fact]
	public void ReleaseAtRequiredDurationCompletesExactlyOnceWhenTimerAlsoCompletes()
	{
		var gate = new HoldConfirmationGate(RequiredDuration);

		gate.Begin();
		var releaseResult = gate.ReleaseAfter(RequiredDuration);
		var timerResult = gate.ThresholdReached();

		new[] { releaseResult, timerResult }
			.Should()
			.ContainSingle(result => result == HoldConfirmationResult.Completed);
		ApplyCompletionEffects(releaseResult, timerResult)
			.Should()
			.Be(new CompletionEffects(Submissions: 1, HapticLongPresses: 1));
	}

	[Fact]
	public void ReleaseJustAfterRequiredDurationCompletesExactlyOnce()
	{
		var gate = new HoldConfirmationGate(RequiredDuration);

		gate.Begin();
		var releaseResult = gate.ReleaseAfter(TimeSpan.FromMilliseconds(401));
		var secondReleaseResult = gate.ReleaseAfter(TimeSpan.FromMilliseconds(450));

		new[] { releaseResult, secondReleaseResult }
			.Should()
			.ContainSingle(result => result == HoldConfirmationResult.Completed);
		ApplyCompletionEffects(releaseResult, secondReleaseResult)
			.Should()
			.Be(new CompletionEffects(Submissions: 1, HapticLongPresses: 1));
	}

	[Fact]
	public void TimerCompletionBeforeReleaseCompletesExactlyOnce()
	{
		var gate = new HoldConfirmationGate(RequiredDuration);

		gate.Begin();
		var timerResult = gate.ThresholdReached();
		var releaseResult = gate.ReleaseAfter(RequiredDuration);

		new[] { timerResult, releaseResult }
			.Should()
			.ContainSingle(result => result == HoldConfirmationResult.Completed);
		ApplyCompletionEffects(timerResult, releaseResult)
			.Should()
			.Be(new CompletionEffects(Submissions: 1, HapticLongPresses: 1));
	}

	private static CompletionEffects ApplyCompletionEffects(params HoldConfirmationResult[] results)
	{
		var completedCount = results.Count(result => result == HoldConfirmationResult.Completed);

		return new CompletionEffects(
			Submissions: completedCount,
			HapticLongPresses: completedCount);
	}

	private sealed record CompletionEffects(int Submissions, int HapticLongPresses);
}
