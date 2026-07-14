using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public sealed class SimulatorProfile
{
	private static readonly SimulatorProfileRoleDescriptor[] ActiveRoleDescriptors =
	[
		new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
		new(MainRoleType.Seer, Faction.Villager),
		new(MainRoleType.WildChild, Faction.Villager),
		new(MainRoleType.SimpleVillager, Faction.Villager)
	];

	private readonly IReadOnlyDictionary<MainRoleType, Faction> _beneficiaryFactions;

	public static SimulatorProfile Active { get; } = new(
		new SimulatorProfileIdentity("core-simulator", "1"),
		ActiveRoleDescriptors);

	public SimulatorProfileIdentity Identity { get; }

	public IReadOnlyList<MainRoleType> SupportedRoles { get; }

	public bool SupportsActorSetupCards => false;

	public SimulatorProfile(
		SimulatorProfileIdentity identity,
		IEnumerable<SimulatorProfileRoleDescriptor> roleDescriptors)
	{
		ArgumentNullException.ThrowIfNull(identity);
		ArgumentNullException.ThrowIfNull(roleDescriptors);
		Identity = identity;
		var snapshot = roleDescriptors.ToArray();
		_beneficiaryFactions = snapshot.ToDictionary(
			descriptor => descriptor.Role,
			descriptor => descriptor.BeneficiaryFaction);
		SupportedRoles = Array.AsReadOnly(snapshot.Select(descriptor => descriptor.Role).ToArray());
	}

	public bool SupportsRole(MainRoleType role) => _beneficiaryFactions.ContainsKey(role);

	public bool TryGetBeneficiaryFaction(MainRoleType role, out Faction faction) =>
		_beneficiaryFactions.TryGetValue(role, out faction);

	public bool SupportsRuleState(SimulationRuleState ruleState) =>
		ruleState == SimulationRuleState.Default;
}

public sealed record SimulatorProfileRoleDescriptor(
	MainRoleType Role,
	Faction BeneficiaryFaction);
