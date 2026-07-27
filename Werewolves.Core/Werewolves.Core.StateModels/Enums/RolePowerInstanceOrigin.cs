namespace Werewolves.Core.StateModels.Enums;

/// <summary>
/// Durable origin of a concrete Role Power instance.
/// It is part of the owner-qualified identity of One-Use Resources.
/// </summary>
public enum RolePowerInstanceOrigin
{
	Native,
	Swapped,
	Borrowed
}
