using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal class WildChildRole : StandardNightRoleHookListener
{
    internal override string PublicName => GameStrings.WildChildRoleName;
    public override ListenerIdentifier Id => ListenerIdentifier.Listener(MainRoleType.WildChild);
    protected override bool HasNightPowers => false;

    protected override List<RoleStateMachineStage> DefineStateMachineStages()
    {
        var stages = base.DefineStateMachineStages();
        stages.Add(CreateStage(
            GameHook.PlayerRoleAssignedOnElimination,
            null,
            StandardNightRoleState.Asleep,
            TransformWildChildrenWhoseModelWasEliminated));

        return stages;
    }

    protected override ModeratorInstruction GenerateTargetSelectionInstruction(GameSession session, ModeratorResponse input)
    {
        var wildChildren = GetAliveRolePlayers(session);
        if (wildChildren == null || !wildChildren.Any())
        {
            throw new InvalidOperationException("No alive Wild Child found for model selection.");
        }

        return new SelectPlayersInstruction(
            selectablePlayerIds: GetPotentialTargets(session, canTargetSelf: false),
            countConstraint: NumberRangeConstraint.Single,
            publicAnnouncement: GameStrings.WildChildModelSelectionPrompt,
            affectedPlayerIds: wildChildren.Select(player => player.Id).ToList());
    }

    protected override void ProcessTargetSelectionNoFeedback(GameSession session, ModeratorResponse input)
    {
        var modelId = input.SelectedPlayerIds!.First();
        session.PerformNightAction(NightActionType.WildChildModel, modelId);
    }

    private HookListenerActionResult TransformWildChildrenWhoseModelWasEliminated(GameSession session, ModeratorResponse input)
    {
        var eliminatedPlayerIds = session.GameHistoryLog
            .OfType<PlayerEliminatedLogEntry>()
            .Where(entry =>
                entry.TurnNumber == session.TurnNumber &&
                entry.CurrentPhase == session.GetCurrentPhase())
            .Select(entry => entry.PlayerId)
            .ToHashSet();

        if (!eliminatedPlayerIds.Any())
        {
            return HookListenerActionResult.Skip();
        }

        var modelIds = session.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Where(entry => entry.ActionType == NightActionType.WildChildModel)
            .SelectMany(entry => entry.TargetIds ?? [])
            .ToHashSet();

        if (!modelIds.Overlaps(eliminatedPlayerIds))
        {
            return HookListenerActionResult.Skip();
        }

        var wildChildrenToTransform = session.GetPlayers()
            .WithRole(Id)
            .WithHealth(PlayerHealth.Alive)
            .Where(player => !player.State.HasStatusEffect(StatusEffectTypes.WildChildChanged))
            .ToList();

        foreach (var wildChild in wildChildrenToTransform)
        {
            session.ApplyStatusEffect(StatusEffectTypes.WildChildChanged, wildChild.Id);
            session.AssignRole(wildChild.Id, MainRoleType.SimpleWerewolf);
        }

        return wildChildrenToTransform.Count == 0
            ? HookListenerActionResult.Skip()
            : HookListenerActionResult.Complete(StandardNightRoleState.Asleep);
    }
}
