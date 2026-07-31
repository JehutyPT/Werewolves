using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records that the Bear Tamer growl was performed at the table.
/// </summary>
public sealed record BearTamerGrowlOccurredLogEntry
	: GameLogEntryBase,
		IGameFactLogEntry
{
	internal override void EnforceValidity()
	{
		if (CurrentPhase != GamePhase.Dawn)
		{
			throw new InvalidOperationException(
				"The Bear Tamer growl fact is structurally invalid.");
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator) => this;

	public override string ToString() => "BearTamerGrowlOccurred";
}
