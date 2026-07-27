using Werewolves.Core.StateModels.Core;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records delivery acknowledgment for the fixed permitted-voter announcement.
/// </summary>
public sealed record ScapegoatVoterRestrictionAnnouncementAcknowledgedLogEntry
	: GameLogEntryBase
{
	public required string ScopeId { get; init; }
	public required Guid AnnouncementInstructionId { get; init; }

	internal void EnforceValidity()
	{
		if (string.IsNullOrWhiteSpace(ScopeId) ||
		    AnnouncementInstructionId == Guid.Empty)
		{
			throw new InvalidOperationException(
				"The Scapegoat voter restriction announcement acknowledgment is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;
}
