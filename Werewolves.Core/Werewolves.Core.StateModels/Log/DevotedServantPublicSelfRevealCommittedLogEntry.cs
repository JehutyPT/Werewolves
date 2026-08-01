using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// One accepted public Devoted Servant self-reveal. The entry atomically binds
/// any previously unknown physical ownership, establishes public identity, and
/// spends the concrete one-use Role Power resource.
/// </summary>
public sealed record DevotedServantPublicSelfRevealCommittedLogEntry
	: GameLogEntryBase,
	  IOneUseRolePowerCommittedLogEntry
{
	public required long RoleLockInVersion { get; init; }
	public required Guid ActingPlayerId { get; init; }
	public required Guid VoteTargetId { get; init; }
	public required Guid DevotedServantCardId { get; init; }
	public required bool BindsCardOwnership { get; init; }
	public required OneUseRolePowerResourceIdentity ResourceIdentity
		{ get; init; }

	internal override void EnforceValidity()
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(RoleLockInVersion);
		ResourceIdentity.EnforceValidity();
		if (CurrentPhase != GamePhase.Day ||
			ActingPlayerId == Guid.Empty ||
			VoteTargetId == Guid.Empty ||
			DevotedServantCardId == Guid.Empty ||
			ActingPlayerId == VoteTargetId ||
			ResourceIdentity.ActingPlayerId != ActingPlayerId ||
			ResourceIdentity.SourceRole != MainRoleType.DevotedServant)
		{
			throw new InvalidOperationException(
				"The Devoted Servant public self-reveal is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		if (BindsCardOwnership)
		{
			mutator.SetPhysicalCharacterCardOwnership(
				RoleLockInVersion,
				ActingPlayerId,
				DevotedServantCardId,
				MainRoleType.DevotedServant);
		}

		mutator.SetPlayerRole(ActingPlayerId, MainRoleType.DevotedServant);
		mutator.SetModeratorKnownRole(
			ActingPlayerId,
			MainRoleType.DevotedServant);
		mutator.SetPubliclyRevealedRole(
			ActingPlayerId,
			MainRoleType.DevotedServant);
		return this;
	}
}
