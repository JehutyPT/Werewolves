using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records delivery acknowledgment for the public Villager Role Power
/// Suppression announcement without persisting holder or localized data.
/// </summary>
public sealed record VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry
	: GameLogEntryBase,
		IGameFactLogEntry
{
	public required Guid AnnouncementInstructionId { get; init; }

	internal override void EnforceValidity()
	{
		if (AnnouncementInstructionId == Guid.Empty ||
		    CurrentPhase != GamePhase.Day)
		{
			throw new InvalidOperationException(
				"The Villager Role Power Suppression announcement acknowledgment is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;

	public override string ToString() =>
		$"VillagerRolePowerSuppressionAnnouncementAcknowledged: {AnnouncementInstructionId}";
}
