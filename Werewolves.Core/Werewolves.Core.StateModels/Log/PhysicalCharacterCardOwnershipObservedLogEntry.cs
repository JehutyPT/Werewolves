using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

public sealed record PhysicalCharacterCardOwnershipObservedLogEntry
	: GameLogEntryBase
{
	public required long RoleLockInVersion { get; init; }
	public required Guid PlayerId { get; init; }
	public required Guid CardId { get; init; }
	public required MainRoleType PrintedRole { get; init; }

	internal override void EnforceValidity()
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RoleLockInVersion);
		if (PlayerId == Guid.Empty || CardId == Guid.Empty)
		{
			throw new InvalidOperationException(
				"A Physical Character Card ownership observation requires Player and card identities.");
		}
		if (!Enum.IsDefined(PrintedRole))
		{
			throw new InvalidOperationException(
				"A Physical Character Card ownership observation has an unknown printed Role.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		mutator.SetPhysicalCharacterCardOwnership(
			RoleLockInVersion,
			PlayerId,
			CardId,
			PrintedRole);
		return this;
	}

	public override string ToString() =>
		$"PhysicalCharacterCardOwnershipObserved: {CardId} → {PlayerId}";
}
