using System.Collections.Immutable;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Moderator-private atomic Devoted Servant card transfer. Its runtime type is
/// internal so ordinary public history consumers cannot inspect the acquired
/// printed or current Role.
/// </summary>
internal sealed record DevotedServantRoleTakenCommittedLogEntry
	: GameLogEntryBase,
	  IFactionFactBatchLogEntry,
	  IPermanentRoleSwapCommittedLogEntry
{
	public required long RoleLockInVersion { get; init; }
	public required Guid ActingPlayerId { get; init; }
	public required Guid VoteTargetId { get; init; }
	public required MainRoleType ObservedPrintedRole { get; init; }
	public required MainRoleType NewCurrentRole { get; init; }
	public required MainRoleType? ExpectedTargetCurrentRole { get; init; }
	public required PermanentRoleSwapCardMovement PhysicalCards { get; init; }
	public required PermanentRoleSwapPolicy Policy { get; init; }
	public required PermanentRoleSwapStateChanges StateChanges { get; init; }
	public required FactionFactSource Source { get; init; }
	public required ImmutableArray<FactionFact> Facts { get; init; }
	public required Guid NewPowerInstanceId { get; init; }
	public required RolePowerInstanceOrigin PowerInstanceOrigin { get; init; }

	Guid IPermanentRoleSwapCommittedLogEntry.PlayerId => ActingPlayerId;

	internal override void EnforceValidity()
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RoleLockInVersion);
		if (CurrentPhase != GamePhase.Day ||
			ActingPlayerId == Guid.Empty ||
			VoteTargetId == Guid.Empty ||
			ActingPlayerId == VoteTargetId ||
			NewPowerInstanceId == Guid.Empty ||
			!Enum.IsDefined(ObservedPrintedRole) ||
			!Enum.IsDefined(NewCurrentRole) ||
			(ExpectedTargetCurrentRole is { } targetRole &&
				!Enum.IsDefined(targetRole)) ||
			(ObservedPrintedRole != NewCurrentRole &&
				(ObservedPrintedRole != MainRoleType.Angel ||
				 NewCurrentRole != MainRoleType.SimpleVillager)) ||
			PowerInstanceOrigin != RolePowerInstanceOrigin.Swapped)
		{
			throw new InvalidOperationException(
				"The Devoted Servant Role take has invalid identities or Roles.");
		}

		ArgumentNullException.ThrowIfNull(PhysicalCards);
		ArgumentNullException.ThrowIfNull(Policy);
		ArgumentNullException.ThrowIfNull(StateChanges);
		ArgumentNullException.ThrowIfNull(Source);
		if (!Policy.IsExplicit ||
			!StateChanges.IsCoherentWith(Policy) ||
			Policy.PrivateRoleKnowledge != PermanentRoleSwapDisposition.Change ||
			Policy.PublicRevealHistory != PermanentRoleSwapDisposition.Preserve ||
			Policy.RolePowerState != PermanentRoleSwapDisposition.Change ||
			PhysicalCards.AdditionalSetAsideCardIds.Count != 0 ||
			Facts.IsDefault || Facts.Any(fact => fact is null) ||
			!PermanentRoleSwapFactionFacts.IsCanonicalSource(
				Source,
				ActingPlayerId,
				NewPowerInstanceId) ||
			!PermanentRoleSwapFactionFacts.IsValidCommittedBatch(
				ActingPlayerId,
				Policy,
				Facts,
				TurnNumber,
				CurrentPhase,
				expectedOrder: null))
		{
			throw new InvalidOperationException(
				"The Devoted Servant Role take policy or Faction facts are invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		if (mutator is not IDevotedServantSessionMutator devotedServantMutator)
		{
			throw new NotSupportedException(
				"This Session Mutator does not project Devoted Servant Role takes.");
		}

		devotedServantMutator.ApplyDevotedServantRoleTake(this);
		return this;
	}

	public override string ToString() =>
		$"DevotedServantRoleTaken: actor {ActingPlayerId}, target {VoteTargetId}";
}
