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
	private readonly SharedVictoryGameResult[] _sharedVictoryCapabilities;

	public static SimulatorProfile Active { get; } = new(
		new SimulatorProfileIdentity("core-simulator", "1"),
		ActiveRoleDescriptors,
		sharedVictoryCapabilities: []);

	public SimulatorProfileIdentity Identity { get; }

	public IReadOnlyList<MainRoleType> SupportedRoles { get; }
	public IReadOnlyList<SharedVictoryGameResult> SharedVictoryCapabilities { get; }

	public bool SupportsActorSetupCards => false;

	internal SimulatorProfile(
		SimulatorProfileIdentity identity,
		IEnumerable<SimulatorProfileRoleDescriptor> roleDescriptors,
		IEnumerable<SharedVictoryGameResult>? sharedVictoryCapabilities = null)
	{
		ArgumentNullException.ThrowIfNull(identity);
		ArgumentNullException.ThrowIfNull(roleDescriptors);
		Identity = identity;
		var snapshot = roleDescriptors.ToArray();
		_beneficiaryFactions = snapshot.ToDictionary(
			descriptor => descriptor.Role,
			descriptor => descriptor.BeneficiaryFaction);
		SupportedRoles = Array.AsReadOnly(snapshot.Select(descriptor => descriptor.Role).ToArray());
		_sharedVictoryCapabilities = (sharedVictoryCapabilities ?? [])
			.Distinct()
			.OrderBy(result => string.Join(',', result.Factions))
			.ToArray();
		SharedVictoryCapabilities = Array.AsReadOnly(_sharedVictoryCapabilities);
	}

	public bool SupportsRole(MainRoleType role) => _beneficiaryFactions.ContainsKey(role);

	internal bool TryGetBeneficiaryFaction(MainRoleType role, out Faction faction) =>
		_beneficiaryFactions.TryGetValue(role, out faction);

	internal GameResult[] CreatePossibleGameResults(IEnumerable<Faction> possibleFactions)
	{
		ArgumentNullException.ThrowIfNull(possibleFactions);
		var factions = possibleFactions.ToArray();
		return factions
			.Select(faction => (GameResult)new SingleFactionGameResult(faction))
			.Concat(_sharedVictoryCapabilities
				.Where(result => result.Factions.All(factions.Contains)))
			.Append(new NoWinnerGameResult())
			.ToArray();
	}

	public bool SupportsRuleState(SimulationRuleState ruleState) =>
		ruleState == SimulationRuleState.Default;
}

internal sealed record SimulatorProfileRoleDescriptor(
	MainRoleType Role,
	Faction BeneficiaryFaction);
