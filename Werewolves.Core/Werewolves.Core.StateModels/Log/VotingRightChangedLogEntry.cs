using Werewolves.Core.StateModels.Core;

namespace Werewolves.Core.StateModels.Log;

public record VotingRightChangedLogEntry : GameLogEntryBase
{
	public required Guid PlayerId { get; init; }
	public required bool HasVotingRight { get; init; }

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		mutator.SetVotingRight(PlayerId, HasVotingRight);
		return this;
	}

	public override string ToString() =>
		$"VotingRight: {PlayerId} = {HasVotingRight}";
}
