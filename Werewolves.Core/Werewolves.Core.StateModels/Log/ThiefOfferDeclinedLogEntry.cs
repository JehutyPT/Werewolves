using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Commits the Thief's one-time decision to keep the owned Thief card and set
/// aside both locked offers.
/// </summary>
public sealed record ThiefOfferDeclinedLogEntry : GameLogEntryBase
{
	public required long RoleLockInVersion { get; init; }
	public required Guid PlayerId { get; init; }
	public required Guid ThiefCardId { get; init; }
	public required Guid Offer1CardId { get; init; }
	public required Guid Offer2CardId { get; init; }

	internal override void EnforceValidity()
	{
		var cardIds = new[] { ThiefCardId, Offer1CardId, Offer2CardId };
		if (RoleLockInVersion <= 0 ||
		    PlayerId == Guid.Empty ||
		    cardIds.Any(cardId => cardId == Guid.Empty) ||
		    cardIds.Distinct().Count() != cardIds.Length ||
		    TurnNumber != 1 ||
		    CurrentPhase != GamePhase.Night)
		{
			throw new InvalidOperationException(
				"The Thief offer decline is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		mutator.ApplyThiefOfferDecline(this);
		return this;
	}

	public override string ToString() =>
		$"ThiefOfferDeclined: {PlayerId} kept {ThiefCardId}";
}
