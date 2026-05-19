using System.Diagnostics;

namespace Werewolves.Client.Components.Game.Views;

internal interface IHoldButtonTiming
{
	long GetTimestamp();

	TimeSpan GetElapsedTime(long startingTimestamp);

	Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);
}

internal sealed class SystemHoldButtonTiming : IHoldButtonTiming
{
	public static readonly SystemHoldButtonTiming Instance = new();

	private SystemHoldButtonTiming()
	{
	}

	public long GetTimestamp() => Stopwatch.GetTimestamp();

	public TimeSpan GetElapsedTime(long startingTimestamp) =>
		Stopwatch.GetElapsedTime(startingTimestamp);

	public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default) =>
		Task.Delay(delay, cancellationToken);
}
