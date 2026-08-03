using Werewolves.Core.StateModels.Core;

namespace Werewolves.Core.StateModels.Log;

public record VotingRightChangedLogEntry
	: GameLogEntryBase,
		IGameFactLogEntry
{
	public required Guid PlayerId { get; init; }
	public required bool HasVotingRight { get; init; }
	public int? DurableVotingPower { get; init; }

	internal override void EnforceValidity()
	{
		if (PlayerId == Guid.Empty || DurableVotingPower is < 0)
		{
			throw new InvalidOperationException(
				"The voting-state change is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		if (DurableVotingPower is { } durableVotingPower)
		{
			mutator.SetDurableVotingPower(PlayerId, durableVotingPower);
		}

		mutator.SetVotingRight(PlayerId, HasVotingRight);
		return this;
	}

	public override string ToString() =>
		DurableVotingPower is { } durableVotingPower
			? $"VotingState: {PlayerId}, right = {HasVotingRight}, power = {durableVotingPower}"
			: $"VotingRight: {PlayerId} = {HasVotingRight}";
}
