using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal class WildChildRole :
    RoleHookListener,
    IDeclaredRoleWorkflow,
    IEliminationCascadeReaction
{
    private readonly RoleWorkflowRuntime _workflowRuntime;

    internal WildChildRole()
    {
        _workflowRuntime = new RoleWorkflowRuntime(
            Id,
            GameHook.NightMainActionLoop,
            [
                RecoverableWait<StandardNightRoleState, SelectPlayersInstruction>
                    .Replayable(
                        Id,
                        GameHook.NightMainActionLoop,
                        startState: null,
                        StandardNightRoleState.AwaitingAwakeConfirmation,
                        ModeratorInstructionSemantic.IdentifyRoleHolders,
                        ExpectedInputType.PlayerSelection,
                        ShouldIdentifyHolder,
                        static (_, _) => { },
                        CreateIdentificationInstruction,
                        static (_, instruction) =>
                            instruction is SelectPlayersInstruction
                            {
                                RoleIdentification: MainRoleType.WildChild
                            },
                        ValidateIdentificationInstruction),
                RecoverableWait<StandardNightRoleState, ConfirmationInstruction>
                    .Replayable(
                        Id,
                        GameHook.NightMainActionLoop,
                        startState: null,
                        StandardNightRoleState.AwaitingAwakeConfirmation,
                        ModeratorInstructionSemantic.WakeRole,
                        ExpectedInputType.Continue,
                        ShouldWakeKnownHolder,
                        static (_, _) => { },
                        CreateWakeInstruction,
                        static (_, _) => false,
                        ValidateWakeInstruction),
                new RoleWorkflowCompletionStep<StandardNightRoleState>(
                    Id,
                    GameHook.NightMainActionLoop,
                    startState: null,
                    StandardNightRoleState.Asleep,
                    static session => session.TurnNumber != 1),
                RecoverableWait<StandardNightRoleState, SelectPlayersInstruction>
                    .Durable(
                        Id,
                        GameHook.NightMainActionLoop,
                        StandardNightRoleState.AwaitingAwakeConfirmation,
                        StandardNightRoleState.AwaitingTargetSelection,
                        ModeratorInstructionSemantic.SelectWildChildModel,
                        ExpectedInputType.PlayerSelection,
                        static session => session.TurnNumber == 1,
                        AcceptIdentificationIfNeeded,
                        CreateModelSelectionInstruction,
                        static (_, _) => false,
                        ValidateModelSelectionInstruction,
                        ValidateIdentificationRecoveryBoundary,
                        static _ =>
                            StandardNightRoleState.AwaitingTargetSelection),
                RecoverableWait<StandardNightRoleState, ConfirmationInstruction>
                    .Durable(
                        Id,
                        GameHook.NightMainActionLoop,
                        StandardNightRoleState.AwaitingTargetSelection,
                        StandardNightRoleState.AwaitingSleepConfirmation,
                        ModeratorInstructionSemantic.PutRoleToSleep,
                        ExpectedInputType.Continue,
                        static _ => true,
                        CommitModelSelection,
                        CreateSleepInstruction,
                        static (_, _) => false,
                        ValidateSleepInstruction,
                        ValidateModelRecoveryBoundary,
                        static _ =>
                            StandardNightRoleState.AwaitingSleepConfirmation),
                new RoleWorkflowCompletionStep<StandardNightRoleState>(
                    Id,
                    GameHook.NightMainActionLoop,
                    StandardNightRoleState.AwaitingSleepConfirmation,
                    StandardNightRoleState.Asleep,
                    static _ => true)
            ]);
    }

    internal override string PublicName => GameStrings.WildChildRoleName;
    public override ListenerIdentifier Id => ListenerIdentifier.Listener(MainRoleType.WildChild);
    RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
        _workflowRuntime;

    protected override HookListenerActionResult ExecuteCore(
        GameSession session,
        ModeratorResponse input) =>
        _workflowRuntime.Execute(
            session,
            input,
            session.Execution.GetCurrentListenerState<StandardNightRoleState>(Id));

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

    private bool ShouldIdentifyHolder(GameSession session) =>
        session.TurnNumber == 1 && !IsCompleteHolderSetKnown(session);

    private bool ShouldWakeKnownHolder(GameSession session) =>
        session.TurnNumber == 1 && IsCompleteHolderSetKnown(session);

    private bool IsCompleteHolderSetKnown(GameSession session) =>
        GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
            session,
            MainRoleType.WildChild);

    private SelectPlayersInstruction CreateIdentificationInstruction(
        GameSession session)
    {
        var roleCount = GetExpectedLivingRoleHolderCount(session);
        var committedHolderCount = GetCommittedLivingRoleHolderIds(session).Count;
        var selectablePlayerIds = GetIdentificationCandidates(session);
        if (roleCount <= 0 ||
            committedHolderCount > roleCount ||
            selectablePlayerIds.Count < roleCount)
        {
            throw new InvalidOperationException(
                "Confirmed Role knowledge contradicts the required Wild Child holder count.");
        }

        var privateInstruction = roleCount == 1
            ? GameStrings.RoleSingleIdentificationPrompt.Format(PublicName)
            : GameStrings.RoleMultipleIdentificationPrompt.Format(PublicName);
        return new SelectPlayersInstruction(
            ModeratorInstructionSemantic.IdentifyRoleHolders,
            selectablePlayerIds,
            NumberRangeConstraint.Exact(roleCount),
            publicAnnouncement: GameStrings.RoleWakesUp.Format(PublicName),
            privateInstruction: privateInstruction,
            affectedPlayerIds: null,
            roleIdentification: MainRoleType.WildChild);
    }

    private ConfirmationInstruction CreateWakeInstruction(GameSession _) =>
        new(
            ModeratorInstructionSemantic.WakeRole,
            GameStrings.RoleWakesUp.Format(PublicName));

    private void AcceptIdentificationIfNeeded(
        GameSession session,
        ModeratorResponse input)
    {
        if (IsCompleteHolderSetKnown(session))
        {
            return;
        }

        IdentifyCompleteLivingRoleHolderSet(
            session,
            input.SelectedPlayerIds?.ToHashSet()
            ?? throw new InvalidOperationException(
                "Wild Child identification requires a Player selection."));
    }

    private SelectPlayersInstruction CreateModelSelectionInstruction(
        GameSession session)
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

    private static void CommitModelSelection(
        GameSession session,
        ModeratorResponse input)
    {
        var modelId = input.SelectedPlayerIds?.SingleOrDefault() ?? Guid.Empty;
        if (modelId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Wild Child model selection requires exactly one Player.");
        }

        session.PerformNightAction(NightActionType.WildChildModel, modelId);
    }

    private ConfirmationInstruction CreateSleepInstruction(GameSession _) =>
        new ConfirmationInstruction(
            ModeratorInstructionSemantic.PutRoleToSleep,
            GameStrings.RoleGoesToSleepSingle.Format(PublicName));

    private void ValidateIdentificationInstruction(
        GameSession session,
        SelectPlayersInstruction instruction)
    {
        if (instruction.RoleIdentification != MainRoleType.WildChild ||
            instruction.AffectedPlayerIds != null ||
            !instruction.SelectablePlayerIds.SetEquals(
                GetIdentificationCandidates(session)) ||
            instruction.CountConstraint != NumberRangeConstraint.Exact(
                GetExpectedLivingRoleHolderCount(session)))
        {
            throw new InvalidOperationException(
                "The Wild Child identification instruction has invalid workflow context.");
        }
    }

    private static void ValidateWakeInstruction(
        GameSession _,
        ConfirmationInstruction instruction)
    {
        if (instruction.AffectedPlayerIds != null)
        {
            throw new InvalidOperationException(
                "The Wild Child wake instruction has invalid workflow context.");
        }
    }

    private void ValidateModelSelectionInstruction(
        GameSession session,
        SelectPlayersInstruction instruction)
    {
        if (instruction.RoleIdentification != null ||
            instruction.AffectedPlayerIds is not { } affectedPlayerIds ||
            !affectedPlayerIds.ToHashSet().SetEquals(GetLivingHolderIds(session)) ||
            !instruction.SelectablePlayerIds.SetEquals(
                GetPotentialTargets(session, canTargetSelf: false)) ||
            instruction.CountConstraint != NumberRangeConstraint.Single)
        {
            throw new InvalidOperationException(
                "The Wild Child model instruction has invalid workflow context.");
        }
    }

    private void ValidateSleepInstruction(
        GameSession session,
        ConfirmationInstruction instruction)
    {
        if (instruction.AffectedPlayerIds != null ||
            GetLivingHolderIds(session).Count == 0)
        {
            throw new InvalidOperationException(
                "The Wild Child sleep instruction has invalid holder context.");
        }
    }

    private void ValidateIdentificationRecoveryBoundary(
        GameSession session,
        SelectPlayersInstruction instruction,
        AcceptedObservationRecoveryCursor cursor)
    {
        ValidateWildChildCursor(
            cursor,
            ModeratorInstructionSemantic.IdentifyRoleHolders);
        var livingHolderIds = GetLivingHolderIds(session);
        if (livingHolderIds.Count == 0 ||
            !session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Any(entry =>
                entry.TurnNumber == session.TurnNumber &&
                entry.CurrentPhase == GamePhase.Night &&
                entry.Role == MainRoleType.WildChild &&
                entry.PlayerIds.SetEquals(livingHolderIds)))
        {
            throw new InvalidOperationException(
                "The Wild Child model wait has no committed identification.");
        }
    }

    private void ValidateModelRecoveryBoundary(
        GameSession session,
        ConfirmationInstruction instruction,
        AcceptedObservationRecoveryCursor cursor)
    {
        ValidateWildChildCursor(
            cursor,
            ModeratorInstructionSemantic.SelectWildChildModel);
        var holderIds = GetLivingHolderIds(session);
        var modelEntries = session.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .Where(entry =>
                entry.TurnNumber == session.TurnNumber &&
                entry.CurrentPhase == GamePhase.Night &&
                entry.ActionType == NightActionType.WildChildModel)
            .ToArray();
        if (modelEntries is not [{ TargetIds: [var modelId] }] ||
            holderIds.Contains(modelId) ||
            !session.GetPlayers().Any(player =>
                player.Id == modelId && player.State.Health == PlayerHealth.Alive))
        {
            throw new InvalidOperationException(
                "The Wild Child sleep continuation has no valid durable model fact.");
        }
    }

    private static void ValidateWildChildCursor(
        AcceptedObservationRecoveryCursor cursor,
        ModeratorInstructionSemantic acceptedSemantic)
    {
        if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
            cursor.AcceptedObservationSemantic != acceptedSemantic ||
            cursor.ObservedRole != MainRoleType.WildChild ||
            cursor.ContinuationRole != MainRoleType.WildChild ||
            cursor.RetainedLittleGirlGuidanceDecision != null)
        {
            throw new InvalidOperationException(
                "The Wild Child continuation cursor has invalid workflow context.");
        }
    }

    private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
        session.GetPlayers()
            .WithHealth(PlayerHealth.Alive)
            .Where(player =>
                player.State.CurrentRole == MainRoleType.WildChild ||
                (player.State.CurrentRole == null &&
                 (player.State.ModeratorKnownRole == MainRoleType.WildChild ||
                  player.State.ModeratorKnownRole == null &&
                  RoleFactionKnowledge.GetPossibleRoles(session, player.Id)
                      .Contains(MainRoleType.WildChild))))
            .ToIdSet();

    private HashSet<Guid> GetLivingHolderIds(GameSession session) =>
        GetAliveRolePlayers(session)?.Select(player => player.Id).ToHashSet()
        ?? [];

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
