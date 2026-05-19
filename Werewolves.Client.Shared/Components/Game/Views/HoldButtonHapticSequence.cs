namespace Werewolves.Client.Components.Game.Views;

internal sealed class HoldButtonHapticSequence
{
	public static readonly int[] PendingLongPressHapticOffsetsMs = [0, 200, 280, 330, 360, 380];

	private readonly object _sync = new();
	private int _reservedPendingLongPressCount;

	public void Reset()
	{
		lock (_sync)
		{
			_reservedPendingLongPressCount = 0;
		}
	}

	public bool TryReservePendingLongPress(int offsetIndex)
	{
		if (offsetIndex < 0 || offsetIndex >= PendingLongPressHapticOffsetsMs.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(offsetIndex));
		}

		lock (_sync)
		{
			if (_reservedPendingLongPressCount > offsetIndex)
			{
				return false;
			}

			_reservedPendingLongPressCount = offsetIndex + 1;
			return true;
		}
	}

	public int ReserveRemainingPendingLongPresses()
	{
		lock (_sync)
		{
			var remainingCount = PendingLongPressHapticOffsetsMs.Length - _reservedPendingLongPressCount;
			_reservedPendingLongPressCount = PendingLongPressHapticOffsetsMs.Length;
			return remainingCount;
		}
	}
}
