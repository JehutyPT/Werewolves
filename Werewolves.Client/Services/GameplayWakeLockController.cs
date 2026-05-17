namespace Werewolves.Client.Services;

public enum GameplayWakeLockArea
{
	None,
	Lobby,
	Dashboard
}

public interface IScreenWakeLock
{
	bool KeepScreenOn { get; set; }
}

public sealed class GameplayWakeLockController
{
	private readonly IScreenWakeLock _wakeLock;
	private GameplayWakeLockArea _currentArea = GameplayWakeLockArea.None;
	private bool _isForegrounded = true;

	public GameplayWakeLockController(IScreenWakeLock wakeLock)
	{
		_wakeLock = wakeLock;
	}

	public void MoveTo(GameplayWakeLockArea area)
	{
		_currentArea = area;
		Apply();
	}

	public void AppEnteredBackground()
	{
		_isForegrounded = false;
		Apply();
	}

	public void AppEnteredForeground()
	{
		_isForegrounded = true;
		Apply();
	}

	private void Apply()
	{
		try
		{
			_wakeLock.KeepScreenOn = _isForegrounded
				&& _currentArea is GameplayWakeLockArea.Lobby or GameplayWakeLockArea.Dashboard;
		}
		catch (Exception)
		{
			// Wake locks are optional; platform failures must not abort launch or navigation.
		}
	}
}
