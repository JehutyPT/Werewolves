using FluentAssertions;
using Werewolves.Client.Services;
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

	private sealed class FakeScreenWakeLock : IScreenWakeLock
	{
		public bool KeepScreenOn { get; set; }
	}
}
