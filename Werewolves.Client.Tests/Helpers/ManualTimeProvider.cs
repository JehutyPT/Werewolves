namespace Werewolves.Client.Tests.Helpers;

internal sealed class ManualTimeProvider(DateTimeOffset? start = null) : TimeProvider
{
	private readonly object _sync = new();
	private readonly List<ManualTimer> _timers = [];
	private DateTimeOffset _utcNow = start ?? DateTimeOffset.UnixEpoch;

	public override DateTimeOffset GetUtcNow()
	{
		lock (_sync)
		{
			return _utcNow;
		}
	}

	public override ITimer CreateTimer(
		TimerCallback callback,
		object? state,
		TimeSpan dueTime,
		TimeSpan period)
	{
		var timer = new ManualTimer(this, callback, state, dueTime, period);
		lock (_sync)
		{
			_timers.Add(timer);
		}
		return timer;
	}

	public void Advance(TimeSpan amount)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(amount, TimeSpan.Zero);
		List<(TimerCallback Callback, object? State)> callbacks = [];
		lock (_sync)
		{
			_utcNow += amount;
			foreach (var timer in _timers.ToArray())
			{
				timer.CollectDueCallbacks(_utcNow, callbacks);
			}
		}

		foreach (var callback in callbacks)
		{
			callback.Callback(callback.State);
		}
	}

	private void Remove(ManualTimer timer)
	{
		lock (_sync)
		{
			_timers.Remove(timer);
		}
	}

	private sealed class ManualTimer : ITimer
	{
		private readonly ManualTimeProvider _owner;
		private readonly TimerCallback _callback;
		private readonly object? _state;
		private DateTimeOffset? _dueAt;
		private TimeSpan _period;
		private bool _disposed;

		public ManualTimer(
			ManualTimeProvider owner,
			TimerCallback callback,
			object? state,
			TimeSpan dueTime,
			TimeSpan period)
		{
			_owner = owner;
			_callback = callback;
			_state = state;
			Change(dueTime, period);
		}

		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			lock (_owner._sync)
			{
				if (_disposed)
				{
					return false;
				}
				_dueAt = dueTime == Timeout.InfiniteTimeSpan
					? null
					: _owner._utcNow + dueTime;
				_period = period;
				return true;
			}
		}

		public void Dispose()
		{
			lock (_owner._sync)
			{
				if (_disposed)
				{
					return;
				}
				_disposed = true;
				_dueAt = null;
			}
			_owner.Remove(this);
		}

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}

		public void CollectDueCallbacks(
			DateTimeOffset now,
			List<(TimerCallback Callback, object? State)> callbacks)
		{
			if (_disposed || _dueAt is not { } dueAt || dueAt > now)
			{
				return;
			}

			callbacks.Add((_callback, _state));
			_dueAt = _period == Timeout.InfiniteTimeSpan
				? null
				: now + _period;
		}
	}
}
