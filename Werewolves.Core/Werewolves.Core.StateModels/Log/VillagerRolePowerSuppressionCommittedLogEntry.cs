using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records the continuing Game Session-wide suppression of new Villager
/// Role Power effects without identifying the triggering Role holder.
/// </summary>
public sealed record VillagerRolePowerSuppressionCommittedLogEntry
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
				"The Villager Role Power Suppression fact is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;

	public override string ToString() =>
		$"VillagerRolePowerSuppressionCommitted: {AnnouncementInstructionId}";
}
