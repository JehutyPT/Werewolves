using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal class WildChildRole :
    StandardNightRoleHookListener,
    IEliminationCascadeReaction
{
    internal override string PublicName => GameStrings.WildChildRoleName;
    public override ListenerIdentifier Id => ListenerIdentifier.Listener(MainRoleType.WildChild);
    protected override bool HasNightPowers => false;

    protected override List<RoleStateMachineStage> DefineStateMachineStages()
        => base.DefineStateMachineStages();

    public string ReactionId =>
        EliminationCascadeReactionIds.WildChildModelEliminated;

    public EliminationCascadeReactionResult Advance(
        GameSession session,
        IReadOnlyCollection<Guid> eliminatedPlayerIds,
        ModeratorResponse input)
    {
        TransformWildChildrenWhoseModelWasEliminated(
            session,
            eliminatedPlayerIds);
        return EliminationCascadeReactionResult.Complete();
    }

    protected override ModeratorInstruction GenerateTargetSelectionInstruction(GameSession session, ModeratorResponse input)
    {
        var wildChildren = GetAliveRolePlayers(session);
        if (wildChildren == null || !wildChildren.Any())
        {
            throw new InvalidOperationException("No alive Wild Child found for model selection.");
        }

        return new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectWildChildModel,
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

    private void TransformWildChildrenWhoseModelWasEliminated(
        GameSession session,
        IReadOnlyCollection<Guid> eliminatedPlayerIds)
    {
        if (eliminatedPlayerIds.Count == 0)
        {
            return;
        }

        var modelIds = session.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Where(entry => entry.ActionType == NightActionType.WildChildModel)
            .SelectMany(entry => entry.TargetIds ?? [])
            .ToHashSet();

        if (!modelIds.Overlaps(eliminatedPlayerIds))
        {
            return;
        }

        var wildChildrenToTransform = session.GetPlayers()
            .WithRole(Id)
            .WithHealth(PlayerHealth.Alive)
            .Where(player => !player.State.HasStatusEffect(StatusEffectTypes.WildChildChanged))
            .ToList();
        if (wildChildrenToTransform.Count == 0)
        {
            return;
        }

        CommitFactionTransition(
            session,
            wildChildrenToTransform.Select(player => player.Id).ToArray());
        foreach (var wildChild in wildChildrenToTransform)
        {
            session.ApplyStatusEffect(StatusEffectTypes.WildChildChanged, wildChild.Id);
        }

    }

    private static void CommitFactionTransition(
        GameSession session,
        IReadOnlyCollection<Guid> wildChildPlayerIds)
    {
        ArgumentNullException.ThrowIfNull(wildChildPlayerIds);
        var playerIds = wildChildPlayerIds.ToArray();
        if (playerIds.Length == 0
            || playerIds.Any(playerId => playerId == Guid.Empty)
            || playerIds.Distinct().Count() != playerIds.Length)
        {
            throw new ArgumentException(
                "Wild Child Faction transitions require unique Players.",
                nameof(wildChildPlayerIds));
        }

        session.CommitFactionFactBatch(context =>
        {
            var boundary = new FactionFactEffectiveBoundary(
                context.TurnNumber,
                context.CurrentPhase,
                session.GameHistoryLog.Count());
            var facts = playerIds
                .SelectMany(playerId => new[]
                {
                    FactionFact.Beneficiary(
                        playerId,
                        Faction.Werewolf,
                        boundary),
                    FactionFact.Agent(
                        playerId,
                        Faction.Werewolf,
                        FactionAgentKnowledge.KnownAgent,
                        boundary)
                })
                .ToImmutableArray();
            return new FactionFactsCommittedLogEntry
            {
                Timestamp = context.Timestamp,
                TurnNumber = context.TurnNumber,
                CurrentPhase = context.CurrentPhase,
                Source = new FactionFactSource(
                    FactionFactSourceKind.ExplicitTransition,
                    EliminationCascadeReactionIds.WildChildModelEliminated),
                Facts = facts
            };
        });
    }
}
