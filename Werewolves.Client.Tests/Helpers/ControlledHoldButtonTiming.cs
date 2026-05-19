using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Werewolves.Client.Components.Game.Views;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;

namespace Werewolves.Client.Tests.Helpers;

internal sealed class ControlledHoldButtonTiming : IHoldButtonTiming
{
	private readonly object _sync = new();
	private readonly List<ScheduledDelay> _scheduledDelays = [];
	private long _elapsedTicks;

	public long GetTimestamp()
	{
		lock (_sync)
		{
			return _elapsedTicks;
		}
	}

	public TimeSpan GetElapsedTime(long startingTimestamp)
	{
		lock (_sync)
		{
			return TimeSpan.FromTicks(_elapsedTicks - startingTimestamp);
		}
	}

	public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
	{
		if (delay <= TimeSpan.Zero)
		{
			return Task.CompletedTask;
		}

		lock (_sync)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled(cancellationToken);
			}

			var scheduledDelay = new ScheduledDelay(_elapsedTicks + delay.Ticks, cancellationToken);
			_scheduledDelays.Add(scheduledDelay);
			return scheduledDelay.Task;
		}
	}

	public void AdvanceBy(TimeSpan duration)
	{
		if (duration < TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(duration), duration, "Cannot move controlled time backwards.");
		}

		List<ScheduledDelay> dueDelays;
		lock (_sync)
		{
			_elapsedTicks += duration.Ticks;
			dueDelays = _scheduledDelays
				.Where(delay => delay.DueTimestamp <= _elapsedTicks)
				.ToList();
			_scheduledDelays.RemoveAll(dueDelays.Contains);
		}

		foreach (var delay in dueDelays)
		{
			delay.Complete();
		}
	}

	private sealed class ScheduledDelay
	{
		private readonly TaskCompletionSource _completion = new();
		private readonly CancellationToken _cancellationToken;
		private readonly CancellationTokenRegistration _cancellationRegistration;

		public ScheduledDelay(long dueTimestamp, CancellationToken cancellationToken)
		{
			DueTimestamp = dueTimestamp;
			_cancellationToken = cancellationToken;
			_cancellationRegistration = cancellationToken.Register(
				static state => ((ScheduledDelay)state!).Cancel(),
				this);
		}

		public long DueTimestamp { get; }

		public Task Task => _completion.Task;

		public void Complete()
		{
			_cancellationRegistration.Dispose();
			_completion.TrySetResult();
		}

		private void Cancel()
		{
			_completion.TrySetCanceled(_cancellationToken);
		}
	}
}

internal static class RenderedHoldButtonDriver
{
	public static readonly TimeSpan HoldDuration =
		TimeSpan.FromMilliseconds(HoldButtonTimingContract.HoldDurationMs);

	public static readonly TimeSpan SuccessFlashDuration =
		TimeSpan.FromMilliseconds(HoldButtonTimingContract.SuccessFlashMs);

	public static async Task CompleteHoldAsync<TComponent>(
		IRenderedComponent<TComponent> rendered,
		IElement holdButton,
		ControlledHoldButtonTiming timing)
		where TComponent : IComponent
	{
		var holdTask = StartHoldAsync(holdButton);
		await FlushAsync(rendered);

		timing.AdvanceBy(HoldDuration);
		await FlushAsync(rendered);

		timing.AdvanceBy(SuccessFlashDuration);
		await holdTask;
		await FlushAsync(rendered);
	}

	public static Task StartHoldAsync(IElement holdButton) =>
		holdButton.TriggerEventAsync(Html.Events.PointerDown, new PointerEventArgs());

	public static Task ReleaseHoldAsync(IElement holdButton) =>
		holdButton.TriggerEventAsync(Html.Events.PointerUp, new PointerEventArgs());

	public static Task LeaveHoldAsync(IElement holdButton) =>
		holdButton.TriggerEventAsync(Html.Events.PointerLeave, new PointerEventArgs());

	public static Task FlushAsync<TComponent>(IRenderedComponent<TComponent> rendered)
		where TComponent : IComponent =>
		rendered.InvokeAsync(() => Task.CompletedTask);
}
