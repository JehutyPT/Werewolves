namespace Werewolves.Client.Components.Game.Views;

public enum HoldConfirmationResult
{
	None,
	Canceled,
	Completed
}

public sealed class HoldConfirmationGate
{
	private readonly object _sync = new();
	private readonly TimeSpan _requiredDuration;
	private bool _isHolding;
	private bool _isComplete;

	public HoldConfirmationGate(TimeSpan requiredDuration)
	{
		_requiredDuration = requiredDuration;
	}

	public void Begin()
	{
		lock (_sync)
		{
			_isHolding = true;
			_isComplete = false;
		}
	}

	public HoldConfirmationResult ReleaseAfter(TimeSpan elapsed)
	{
		lock (_sync)
		{
			if (!_isHolding || _isComplete)
			{
				return HoldConfirmationResult.None;
			}

			if (elapsed < _requiredDuration)
			{
				_isHolding = false;
				return HoldConfirmationResult.Canceled;
			}

			return Complete();
		}
	}

	public HoldConfirmationResult ThresholdReached()
	{
		lock (_sync)
		{
			if (!_isHolding || _isComplete)
			{
				return HoldConfirmationResult.None;
			}

			return Complete();
		}
	}

	public HoldConfirmationResult Cancel()
	{
		lock (_sync)
		{
			if (!_isHolding || _isComplete)
			{
				return HoldConfirmationResult.None;
			}

			_isHolding = false;
			return HoldConfirmationResult.Canceled;
		}
	}

	private HoldConfirmationResult Complete()
	{
		_isHolding = false;
		_isComplete = true;
		return HoldConfirmationResult.Completed;
	}
}
