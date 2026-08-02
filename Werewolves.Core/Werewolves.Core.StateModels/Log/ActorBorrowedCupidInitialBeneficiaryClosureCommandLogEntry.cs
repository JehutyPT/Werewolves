using System.Collections.Immutable;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Private transaction envelope that resolves a deferred Actor-borrowed Cupid
/// pair while returning only the ordinary Initial Beneficiary Closure entry to
/// public Game History.
/// </summary>
internal sealed record ActorBorrowedCupidInitialBeneficiaryClosureCommandLogEntry
	: GameLogEntryBase, IFactionFactBatchLogEntry
{
	internal required FactionFactsCommittedLogEntry PublicClosureEntry
		{ get; init; }
	internal required ActorBorrowedCupidLoversCommit ExpectedDeferredCommit
		{ get; init; }
	internal required ActorBorrowedCupidLoversDisposition ResolvedDisposition
		{ get; init; }

	FactionFactSource IFactionFactBatchLogEntry.Source =>
		PublicClosureEntry.Source;

	ImmutableArray<FactionFact> IFactionFactBatchLogEntry.Facts =>
		PublicClosureEntry.Facts;

	internal override void EnforceValidity()
	{
		ArgumentNullException.ThrowIfNull(PublicClosureEntry);
		ArgumentNullException.ThrowIfNull(ExpectedDeferredCommit);
		PublicClosureEntry.EnforceValidity();
		ExpectedDeferredCommit.EnforceValidity();
		if (Timestamp != PublicClosureEntry.Timestamp ||
			TurnNumber != PublicClosureEntry.TurnNumber ||
			CurrentPhase != PublicClosureEntry.CurrentPhase ||
			PublicClosureEntry.Source.Kind !=
				FactionFactSourceKind.InitialBeneficiaryClosure ||
			ExpectedDeferredCommit.TurnNumber != 1 ||
			ExpectedDeferredCommit.CurrentPhase != GamePhase.Night ||
			ExpectedDeferredCommit.Disposition !=
				ActorBorrowedCupidLoversDisposition
					.DeferredToInitialBeneficiaryClosure ||
			ResolvedDisposition is not
				(ActorBorrowedCupidLoversDisposition.SameFaction or
				 ActorBorrowedCupidLoversDisposition.CrossFaction))
		{
			throw new InvalidOperationException(
				"The Actor borrowed Cupid Initial Beneficiary Closure transaction is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		if (mutator is not IActorSessionMutator actorMutator)
		{
			throw new NotSupportedException(
				"This Session Mutator does not resolve Actor borrowed Cupid Initial Beneficiary Closure state.");
		}

		actorMutator.ApplyActorBorrowedCupidInitialBeneficiaryClosure(this);
		return PublicClosureEntry;
	}

	public override string ToString() =>
		"ActorBorrowedCupidInitialBeneficiaryClosureCommand";
}
