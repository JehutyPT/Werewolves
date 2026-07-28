using Werewolves.Core.StateModels.Core;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records delivery acknowledgment for a fixed voter-eligibility announcement.
/// </summary>
public sealed record VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry
	: GameLogEntryBase,
		IGameFactLogEntry
{
	public required string ScopeId { get; init; }
	public required Guid AnnouncementInstructionId { get; init; }

	internal override void EnforceValidity()
	{
		if (string.IsNullOrWhiteSpace(ScopeId) ||
		    AnnouncementInstructionId == Guid.Empty ||
		    CurrentPhase != Enums.GamePhase.Day)
		{
			throw new InvalidOperationException(
				"The voter-eligibility restriction acknowledgment is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;

	public override string ToString() =>
		$"VoterEligibilityRestrictionAcknowledged: scope {ScopeId}";
}
