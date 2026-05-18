using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class GameplayWakeLockControllerTests
{
	[Theory]
	[InlineData(GameplayWakeLockArea.Lobby)]
	[InlineData(GameplayWakeLockArea.Dashboard)]
	public void MoveToGameplayArea_WhenAppIsForegrounded_EngagesWakeLock(GameplayWakeLockArea area)
	{
		var wakeLock = new FakeScreenWakeLock();
		var controller = new GameplayWakeLockController(wakeLock);

		controller.MoveTo(area);

		wakeLock.KeepScreenOn.Should().BeTrue();
	}

	[Fact]
	public void AppEnteredBackground_WhenInGameplayArea_ReleasesWakeLock()
	{
		var wakeLock = new FakeScreenWakeLock();
		var controller = new GameplayWakeLockController(wakeLock);
		controller.MoveTo(GameplayWakeLockArea.Lobby);

		controller.AppEnteredBackground();

		wakeLock.KeepScreenOn.Should().BeFalse();
	}

	[Fact]
	public void AppEnteredForeground_WhenStillInGameplayArea_ReengagesWakeLock()
	{
		var wakeLock = new FakeScreenWakeLock();
		var controller = new GameplayWakeLockController(wakeLock);
		controller.MoveTo(GameplayWakeLockArea.Dashboard);
		controller.AppEnteredBackground();

		controller.AppEnteredForeground();

		wakeLock.KeepScreenOn.Should().BeTrue();
	}

	[Fact]
	public void MoveToNone_WhenWakeLockIsEngaged_ReleasesWakeLock()
	{
		var wakeLock = new FakeScreenWakeLock();
		var controller = new GameplayWakeLockController(wakeLock);
		controller.MoveTo(GameplayWakeLockArea.Dashboard);

		controller.MoveTo(GameplayWakeLockArea.None);

		wakeLock.KeepScreenOn.Should().BeFalse();
	}

	[Fact]
	public void MoveToGameplayArea_WhenWakeLockPlatformThrows_DoesNotPropagate()
	{
		var wakeLock = new ThrowingScreenWakeLock();
		var controller = new GameplayWakeLockController(wakeLock);

		var act = () => controller.MoveTo(GameplayWakeLockArea.Lobby);

		act.Should().NotThrow();
	}

	[Fact]
	public void AppEnteredBackground_WhenWakeLockPlatformThrows_DoesNotPropagate()
	{
		var wakeLock = new ThrowingScreenWakeLock();
		var controller = new GameplayWakeLockController(wakeLock);

		var act = () => controller.AppEnteredBackground();

		act.Should().NotThrow();
	}

	private sealed class FakeScreenWakeLock : IScreenWakeLock
	{
		public bool KeepScreenOn { get; set; }
	}

	private sealed class ThrowingScreenWakeLock : IScreenWakeLock
	{
		public bool KeepScreenOn
		{
			get => throw new InvalidOperationException(ClientTestReferences.ExceptionMessages.PlatformWakeLockUnavailable);
			set => throw new InvalidOperationException(ClientTestReferences.ExceptionMessages.PlatformWakeLockUnavailable);
		}
	}
}
