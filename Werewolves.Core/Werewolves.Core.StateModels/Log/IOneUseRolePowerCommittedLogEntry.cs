using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Shared owner-qualified identity surface for every atomic One-Use Resource
/// commit, independent of whether the action occurs at Night or during Day.
/// </summary>
public interface IOneUseRolePowerCommittedLogEntry
{
	OneUseRolePowerResourceIdentity ResourceIdentity { get; }
}
