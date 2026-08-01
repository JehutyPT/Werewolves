using System.Collections.Immutable;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// One append-only Permanent Role Swap. The entry carries the caller's complete
/// category policy, physical-card exchange, Faction facts, and fresh power lineage.
/// </summary>
public sealed record PermanentRoleSwapCommittedLogEntry
	: GameLogEntryBase, IFactionFactBatchLogEntry
{
	public required long RoleLockInVersion { get; init; }
	public required Guid PlayerId { get; init; }
	public required MainRoleType ExpectedCurrentRole { get; init; }
	public required MainRoleType NewCurrentRole { get; init; }
	public required PermanentRoleSwapCardMovement PhysicalCards { get; init; }
	public required PermanentRoleSwapPolicy Policy { get; init; }
	public required PermanentRoleSwapStateChanges StateChanges { get; init; }
	public required FactionFactSource Source { get; init; }
	public required ImmutableArray<FactionFact> Facts { get; init; }
	public required Guid NewPowerInstanceId { get; init; }
	public required RolePowerInstanceOrigin PowerInstanceOrigin { get; init; }

	internal override void EnforceValidity()
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RoleLockInVersion);
		if (PlayerId == Guid.Empty || NewPowerInstanceId == Guid.Empty)
		{
			throw new InvalidOperationException(
				"Permanent Role Swap requires stable Player and power-instance identities.");
		}
		if (!Enum.IsDefined(ExpectedCurrentRole) ||
			!Enum.IsDefined(NewCurrentRole) ||
			ExpectedCurrentRole == NewCurrentRole ||
			PowerInstanceOrigin != RolePowerInstanceOrigin.Swapped)
		{
			throw new InvalidOperationException(
				"Permanent Role Swap Role or power lineage is invalid.");
		}
		ArgumentNullException.ThrowIfNull(PhysicalCards);
		ArgumentNullException.ThrowIfNull(Policy);
		ArgumentNullException.ThrowIfNull(StateChanges);
		ArgumentNullException.ThrowIfNull(Source);
		if (!Policy.IsExplicit ||
			!StateChanges.IsCoherentWith(Policy) ||
			Policy.PublicRevealHistory != PermanentRoleSwapDisposition.Preserve ||
			Policy.RolePowerState != PermanentRoleSwapDisposition.Change ||
			Facts.IsDefault ||
			Facts.Any(fact => fact is null))
		{
			throw new InvalidOperationException(
				"Permanent Role Swap requires an explicit category policy and valid Faction facts.");
		}
		if (!PermanentRoleSwapFactionFacts.IsCanonicalSource(
				Source,
				PlayerId,
				NewPowerInstanceId) ||
			!PermanentRoleSwapFactionFacts.IsValidCommittedBatch(
				PlayerId,
				Policy,
				Facts,
				TurnNumber,
				CurrentPhase,
				expectedOrder: null))
		{
			throw new InvalidOperationException(
				"Permanent Role Swap Faction facts are invalid.");
		}
		if (Facts.Distinct().Count() != Facts.Length ||
			Facts.GroupBy(FactionFactProjection.FactBoundaryKey)
				.Any(group => group.Count() > 1) ||
			Facts.Any(fact => fact.PlayerId != PlayerId))
		{
			throw new InvalidOperationException(
				"Permanent Role Swap Faction facts are contradictory.");
		}
		foreach (var fact in Facts)
		{
			var effective = fact.EffectiveBoundary;
			if (effective.TurnNumber > TurnNumber ||
				effective.TurnNumber == TurnNumber &&
				FactionFactProjection.PhaseOrder(effective.Phase) >
				FactionFactProjection.PhaseOrder(CurrentPhase))
			{
				throw new InvalidOperationException(
					"A Permanent Role Swap Faction fact cannot be effective after its commit boundary.");
			}
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		mutator.ApplyPermanentRoleSwap(this);
		return this;
	}

	public override string ToString() =>
		$"PermanentRoleSwapCommitted: {PlayerId} {ExpectedCurrentRole}->{NewCurrentRole}";
}
