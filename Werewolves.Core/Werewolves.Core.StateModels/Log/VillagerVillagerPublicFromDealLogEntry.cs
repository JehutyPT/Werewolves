using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// Records the public fact already established when the two-sided
/// Villager-Villager Character Card was physically dealt.
/// </summary>
public sealed record VillagerVillagerPublicFromDealLogEntry : GameLogEntryBase
{
    public required Guid PlayerId { get; init; }
	public required long RoleLockInVersion { get; init; }
	public required Guid CardId { get; init; }

    protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
    {
		mutator.SetPhysicalCharacterCardOwnership(
			RoleLockInVersion,
			PlayerId,
			CardId,
			MainRoleType.VillagerVillager);
        mutator.SetPlayerRole(PlayerId, MainRoleType.VillagerVillager);
        mutator.SetModeratorKnownRole(PlayerId, MainRoleType.VillagerVillager);
        mutator.SetPubliclyRevealedRole(PlayerId, MainRoleType.VillagerVillager);
        return this;
    }
}
