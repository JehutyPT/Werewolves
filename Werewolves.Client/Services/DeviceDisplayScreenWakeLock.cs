using Microsoft.Maui.Devices;

namespace Werewolves.Client.Services;

public sealed class DeviceDisplayScreenWakeLock : IScreenWakeLock
{
	private readonly IDeviceDisplay _deviceDisplay;

	public DeviceDisplayScreenWakeLock(IDeviceDisplay deviceDisplay)
	{
		_deviceDisplay = deviceDisplay;
	}

	public bool KeepScreenOn
	{
		get => _deviceDisplay.KeepScreenOn;
		set => _deviceDisplay.KeepScreenOn = value;
	}
}
