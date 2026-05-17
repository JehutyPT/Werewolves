using FluentAssertions;
using Werewolves.Client.Components.Game.Views;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class HoldButtonHapticSequenceTests
{
	[Fact]
	public void ReserveRemainingPendingLongPresses_WhenTimerContinuationsMissOffsets_ReservesOnlyMissingPulses()
	{
		var sequence = new HoldButtonHapticSequence();

		sequence.TryReservePendingLongPress(0).Should().BeTrue();
		sequence.TryReservePendingLongPress(1).Should().BeTrue();

		sequence.ReserveRemainingPendingLongPresses().Should().Be(4);
		sequence.TryReservePendingLongPress(2).Should().BeFalse();
		sequence.ReserveRemainingPendingLongPresses().Should().Be(0);
	}

	[Fact]
	public void Reset_AfterCompletion_AllowsNewHoldToReservePresetAgain()
	{
		var sequence = new HoldButtonHapticSequence();
		sequence.ReserveRemainingPendingLongPresses().Should().Be(6);

		sequence.Reset();

		sequence.TryReservePendingLongPress(0).Should().BeTrue();
	}
}
