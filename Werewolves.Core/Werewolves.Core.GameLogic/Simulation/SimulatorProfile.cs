using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public sealed class SimulatorProfile
{
	private static readonly MainRoleType[] ActiveSupportedRoles =
	[
		MainRoleType.SimpleWerewolf,
		MainRoleType.Seer,
		MainRoleType.WildChild,
		MainRoleType.SimpleVillager
	];

	private readonly HashSet<MainRoleType> _supportedRoleSet;

	public static SimulatorProfile Active { get; } = new(
		new SimulatorProfileIdentity("core-simulator", "1"),
		ActiveSupportedRoles);

	public SimulatorProfileIdentity Identity { get; }

	public IReadOnlyList<MainRoleType> SupportedRoles { get; }

	public bool SupportsActorSetupCards => false;

	private SimulatorProfile(
		SimulatorProfileIdentity identity,
		IEnumerable<MainRoleType> supportedRoles)
	{
		Identity = identity;
		var snapshot = supportedRoles.ToArray();
		SupportedRoles = Array.AsReadOnly(snapshot);
		_supportedRoleSet = snapshot.ToHashSet();
	}

	public bool SupportsRole(MainRoleType role) => _supportedRoleSet.Contains(role);

	public bool SupportsRuleState(SimulationRuleState ruleState) =>
		ruleState == SimulationRuleState.Default;
}
