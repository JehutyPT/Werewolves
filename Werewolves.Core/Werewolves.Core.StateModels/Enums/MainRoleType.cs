namespace Werewolves.Core.StateModels.Enums;

/// <summary>
/// Represents the specific main character roles in the game.
/// </summary>
public enum MainRoleType
{
    // Werewolves
    SimpleWerewolf,
    BigBadWolf, // Phase 5+
    AccursedWolfFather, // Phase 5+
    WhiteWerewolf, // Phase 6+

    // Villagers
    SimpleVillager,
    VillagerVillager, // Phase 9+
    Seer, // Phase 2
    Cupid, // Phase 3+
    Witch, // Phase 2+
    Hunter, // Phase 3+
    LittleGirl, // Phase 3+
    Defender, // Phase 2+
    Elder, // Phase 3+
    Scapegoat, // Phase 9+
    VillageIdiot, // Phase 9+
    TwoSisters, // Phase 9+
    ThreeBrothers, // Phase 9+
    Fox, // Phase 4+
    BearTamer, // Phase 4+
    StutteringJudge, // Phase 9+
    KnightWithRustySword, // Phase 4+

    // Ambiguous
    Thief, // Phase 5+
    DevotedServant, // Phase 5+
    Actor, // Phase 5+
    WildChild, // Phase 5+
    WolfHound, // Phase 5+

    // Loners
    Angel, // Phase 6+
    Piper, // Phase 6+
    PrejudicedManipulator, // Phase 6+

    // New Moon Roles
    Gypsy, // Phase 8+
}