using System.Collections.Immutable;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;

namespace Werewolves.Core.StateModels.Models;

internal enum ActorBorrowedCupidLoversDisposition
{
	DeferredToInitialBeneficiaryClosure = 0,
	SameFaction = 1,
	CrossFaction = 2
}

/// <summary>
/// Private durable projection of one Actor-borrowed Cupid Lovers pair.
/// Source lineage, pair endpoints, and classification remain outside public
/// Game History.
/// </summary>
internal sealed record ActorBorrowedCupidLoversCommit(
	RolePowerInstanceIdentity PowerIdentity,
	Guid ActorSetupCardId,
	Guid FirstPlayerId,
	Guid SecondPlayerId,
	ActorBorrowedCupidLoversDisposition Disposition,
	DateTimeOffset Timestamp,
	int TurnNumber,
	GamePhase CurrentPhase,
	int PublicMarkerLogIndex)
	: IActorBorrowedRolePowerCommit,
	  IFactionFactBatchLogEntry
{
	internal const string ExpectedSourcePowerIdentifier = "cupid-link-lovers";
	private const string FactionFactSourceIdentifier =
		"actor-borrowed-cupid-lovers";
	private const int CrossFactionLoversBeneficiaryPrecedence = 1;

	internal IReadOnlyList<Guid> PlayerIds => [FirstPlayerId, SecondPlayerId];

	internal FactionFactEffectiveBoundary LinkBoundary => new(
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);

	ActorBorrowedRolePowerCommitCoordinate
		IActorBorrowedRolePowerCommit.Coordinate => new(
			PowerIdentity,
			ActorSetupCardId,
			Timestamp,
			TurnNumber,
			CurrentPhase,
			PublicMarkerLogIndex);

	FactionFactSource IFactionFactBatchLogEntry.Source => new(
		FactionFactSourceKind.ExplicitTransition,
		FactionFactSourceIdentifier);

	ImmutableArray<FactionFact> IFactionFactBatchLogEntry.Facts =>
		Disposition == ActorBorrowedCupidLoversDisposition.CrossFaction
			? PlayerIds
				.Select(playerId => FactionFact.Beneficiary(
					playerId,
					Faction.CrossFactionLovers,
					LinkBoundary,
					CrossFactionLoversBeneficiaryPrecedence))
				.ToImmutableArray()
			: [];

	internal void EnforceValidity()
	{
		((IActorBorrowedRolePowerCommit)this).Coordinate.EnforceValidity();
		if (CurrentPhase != GamePhase.Night ||
			PowerIdentity.SourceRole != MainRoleType.Cupid ||
			!StringComparer.Ordinal.Equals(
				PowerIdentity.SourcePowerIdentifier,
				ExpectedSourcePowerIdentifier) ||
			FirstPlayerId == Guid.Empty ||
			SecondPlayerId == Guid.Empty ||
			FirstPlayerId.CompareTo(SecondPlayerId) >= 0 ||
			!Enum.IsDefined(Disposition) ||
			TurnNumber > 1 &&
			Disposition == ActorBorrowedCupidLoversDisposition
				.DeferredToInitialBeneficiaryClosure)
		{
			throw new InvalidOperationException(
				"The private Actor borrowed Cupid Lovers commit is structurally invalid.");
		}
	}

}
