using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;


/// <summary>
/// Logs the assignment of a specific main role to a player, either during setup (Night 1)
/// or due to player elimination. This represents moderator knowledge, not necessarily public knowledge
/// (i.e. the executioner or the devoted servant may be the only players that knows
/// which role belonged to the lynched player, but the moderator will always know regardless)
/// </summary>
public record AssignRoleLogEntry : GameLogEntryBase
{
	/// <summary>
	/// The ID of the player assigned the role.
	/// </summary>
	public required HashSet<Guid> PlayerIds { get; init; }

	/// <summary>
	/// The specific role type assigned.
	/// </summary>
	public required MainRoleType AssignedMainRole { get; init; }

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		foreach (var player in PlayerIds)
		{
			mutator.SetPlayerRole(player, AssignedMainRole);
		}

		return this;
	}

	public override string ToString() =>
		$"AssignRole: {AssignedMainRole} → [{string.Join(", ", PlayerIds)}]";
}
