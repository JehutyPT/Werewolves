using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal enum BigBadWolfRoleState
{
    Awake,
    AwaitingTargetSelection,
    ReadyToSleep,
    Asleep
}

internal sealed class BigBadWolfRole
    : RoleHookListener,
        IDeclaredRoleWorkflow
{
    private readonly RolePowerAvailabilityGateway _availabilityGateway;
    private readonly RoleWorkflowRuntime _workflowRuntime;

    private static readonly RolePowerDefinition AdditionalVictimPower = new(
        new RolePowerIdentifier("big-bad-wolf-additional-victim"),
        RolePowerCategory.Chosen);

    internal static RolePowerIdentifier AdditionalVictimPowerIdentifier =>
        AdditionalVictimPower.Identifier;

    internal BigBadWolfRole(
        RolePowerAvailabilityGateway availabilityGateway)
    {
        ArgumentNullException.ThrowIfNull(availabilityGateway);
        _availabilityGateway = availabilityGateway;

        var identificationWait = RecoverableWait<
                BigBadWolfRoleState,
                SelectPlayersInstruction>
            .ReplayableWithAcceptedObservationHandoff(
                Id,
                GameHook.NightMainActionLoop,
                startState: null,
                BigBadWolfRoleState.Awake,
                ModeratorInstructionSemantic.IdentifyRoleHolders,
                ExpectedInputType.PlayerSelection,
                static _ => false,
                static (_, _) => { },
                CreateIdentificationInstruction,
                static (_, instruction) =>
                    instruction is SelectPlayersInstruction
                    {
                        RoleIdentification: MainRoleType.BigBadWolf
                    },
                ValidateIdentificationInstruction,
                (_, _, cursor) => ValidateCallHandoff(cursor),
                static _ => BigBadWolfRoleState.Awake);
        var wakeWait = RecoverableWait<
                BigBadWolfRoleState,
                ConfirmationInstruction>
            .ReplayableWithAcceptedObservationHandoff(
                Id,
                GameHook.NightMainActionLoop,
                startState: null,
                BigBadWolfRoleState.Awake,
                ModeratorInstructionSemantic.WakeRole,
                ExpectedInputType.Continue,
                static _ => false,
                static (_, _) => { },
                CreateWakeInstruction,
                (session, instruction) =>
                    instruction.Semantic ==
                    ModeratorInstructionSemantic.WakeRole &&
                    HasExpectedAffectedHolder(session, instruction),
                ValidateWakeInstruction,
                (_, _, cursor) => ValidateCallHandoff(cursor),
                static _ => BigBadWolfRoleState.Awake);
        var targetSelectionWait = RecoverableWait<
                BigBadWolfRoleState,
                SelectPlayersInstruction>
            .ReplayableWithAcceptedObservationHandoff(
                Id,
                GameHook.NightMainActionLoop,
                BigBadWolfRoleState.Awake,
                BigBadWolfRoleState.AwaitingTargetSelection,
                ModeratorInstructionSemantic.SelectBigBadWolfTarget,
                ExpectedInputType.PlayerSelection,
                static _ => false,
                static (_, _) => { },
                CreateTargetSelectionInstruction,
                (session, instruction) =>
                    instruction.Semantic ==
                    ModeratorInstructionSemantic.SelectBigBadWolfTarget &&
                    HasExpectedAffectedHolder(session, instruction),
                ValidateTargetSelectionInstruction,
                (session, _, cursor) =>
                    ValidateIdentificationHandoff(session, cursor),
                static _ => BigBadWolfRoleState.AwaitingTargetSelection);
        var replayableSleepWait = RecoverableWait<
                BigBadWolfRoleState,
                ConfirmationInstruction>
            .ReplayableWithAcceptedObservationHandoff(
                Id,
                GameHook.NightMainActionLoop,
                BigBadWolfRoleState.Awake,
                BigBadWolfRoleState.ReadyToSleep,
                ModeratorInstructionSemantic.PutRoleToSleep,
                ExpectedInputType.Continue,
                static _ => false,
                static (_, _) => { },
                CreateSleepInstruction,
                (session, instruction) =>
                    instruction.Semantic ==
                    ModeratorInstructionSemantic.PutRoleToSleep &&
                    !GetAdditionalVictimCommitsThisNight(session).Any() &&
                    HasExpectedAffectedHolder(session, instruction),
                ValidateReplayableSleepInstruction,
                (session, _, cursor) =>
                    ValidateIdentificationHandoff(session, cursor),
                static _ => BigBadWolfRoleState.ReadyToSleep);
        var committedSleepWait = RecoverableWait<
                BigBadWolfRoleState,
                ConfirmationInstruction>
            .RecurringDomainDurable(
                Id,
                GameHook.NightMainActionLoop,
                BigBadWolfRoleState.AwaitingTargetSelection,
                BigBadWolfRoleState.ReadyToSleep,
                ModeratorInstructionSemantic.PutRoleToSleep,
                ExpectedInputType.Continue,
                static _ => false,
                static (_, _) => { },
                CreateSleepInstruction,
                (session, instruction) =>
                    instruction.Semantic ==
                    ModeratorInstructionSemantic.PutRoleToSleep &&
                    GetAdditionalVictimCommitsThisNight(session)
                        .Take(2).Count() == 1 &&
                    HasExpectedAffectedHolder(session, instruction),
                ValidateCommittedSleepInstruction,
                ValidateRecurringRecoveryCursor,
                static _ => BigBadWolfRoleState.ReadyToSleep,
                TryValidateCommittedRecoveryBoundary);

        _workflowRuntime = new RoleWorkflowRuntime(
            Id,
            GameHook.NightMainActionLoop,
            [
                identificationWait,
                wakeWait,
                targetSelectionWait,
                replayableSleepWait,
                committedSleepWait,
                new RoleWorkflowDecisionStep<BigBadWolfRoleState>(
                    Id,
                    GameHook.NightMainActionLoop,
                    startState: null,
                    static _ => true,
                    (session, input) => BeginCall(
                        session,
                        input,
                        identificationWait,
                        wakeWait)),
                new RoleWorkflowDecisionStep<BigBadWolfRoleState>(
                    Id,
                    GameHook.NightMainActionLoop,
                    BigBadWolfRoleState.Awake,
                    static _ => true,
                    (session, input) => PrepareNightPower(
                        session,
                        input,
                        targetSelectionWait,
                        replayableSleepWait)),
                new RoleWorkflowDecisionStep<BigBadWolfRoleState>(
                    Id,
                    GameHook.NightMainActionLoop,
                    BigBadWolfRoleState.AwaitingTargetSelection,
                    static _ => true,
                    (session, input) => CommitTargetSelection(
                        session,
                        input,
                        committedSleepWait)),
                new RoleWorkflowCompletionStep<BigBadWolfRoleState>(
                    Id,
                    GameHook.NightMainActionLoop,
                    BigBadWolfRoleState.ReadyToSleep,
                    BigBadWolfRoleState.Asleep,
                    static _ => true),
                new RoleWorkflowCompletionStep<BigBadWolfRoleState>(
                    Id,
                    GameHook.NightMainActionLoop,
                    BigBadWolfRoleState.Asleep,
                    BigBadWolfRoleState.Asleep,
                    static _ => true)
            ]);
    }

    internal override string PublicName => GameStrings.BigBadWolfRoleName;

    public override ListenerIdentifier Id =>
        ListenerIdentifier.Listener(MainRoleType.BigBadWolf);

    RoleWorkflowRuntime IDeclaredRoleWorkflow.WorkflowRuntime =>
        _workflowRuntime;

    public override HookListenerActionResult Execute(
        GameSession session,
        ModeratorResponse input)
    {
        if (session.Execution
                .GetCurrentListenerState<BigBadWolfRoleState>(Id) == null &&
            (!TryGetRetainedVictimId(session, out _) ||
             GameSessionQueries.HasEliminatedKnownWerewolfFactionAgent(
                 session)))
        {
            return HookListenerActionResult.Skip();
        }

        return base.Execute(session, input);
    }

    protected override HookListenerActionResult ExecuteCore(
        GameSession session,
        ModeratorResponse input) =>
        _workflowRuntime.Execute(
            session,
            input,
            session.Execution.GetCurrentListenerState<BigBadWolfRoleState>(Id));

    private HookListenerActionResult BeginCall(
        GameSession session,
        ModeratorResponse input,
        RecoverableWait<BigBadWolfRoleState, SelectPlayersInstruction>
            identificationWait,
        RecoverableWait<BigBadWolfRoleState, ConfirmationInstruction>
            wakeWait) =>
        IsCompleteHolderSetKnown(session)
            ? wakeWait.Execute(session, input)
            : identificationWait.Execute(session, input);

    private HookListenerActionResult PrepareNightPower(
        GameSession session,
        ModeratorResponse input,
        RecoverableWait<BigBadWolfRoleState, SelectPlayersInstruction>
            targetSelectionWait,
        RecoverableWait<BigBadWolfRoleState, ConfirmationInstruction>
            sleepWait)
    {
        if (!IsCompleteHolderSetKnown(session))
        {
            IdentifyCompleteLivingRoleHolderSet(
                session,
                input.SelectedPlayerIds?.ToHashSet()
                ?? throw new InvalidOperationException(
                    "Big Bad Wolf identification requires one Player selection."));
        }

        var holder = GetHolder(session);
        if (!TryGetRetainedVictimId(session, out var retainedVictimId))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf requires one retained collective victim.");
        }

        if (GameSessionQueries.HasEliminatedKnownWerewolfFactionAgent(session))
        {
            return sleepWait.Execute(session, input);
        }

        var availability = _availabilityGateway.Evaluate(
            new RolePowerAttempt(
                session,
                holder,
                MainRoleType.BigBadWolf,
                AdditionalVictimPower,
                RolePowerInstance.CreateCurrent(
                    session,
                    holder,
                    MainRoleType.BigBadWolf,
                    AdditionalVictimPower)));
        return availability.AvailabilityResult.IsAvailable &&
               GetEligibleTargets(session, retainedVictimId).Count > 0
            ? targetSelectionWait.Execute(session, input)
            : sleepWait.Execute(session, input);
    }

    private HookListenerActionResult CommitTargetSelection(
        GameSession session,
        ModeratorResponse input,
        RecoverableWait<BigBadWolfRoleState, ConfirmationInstruction>
            committedSleepWait)
    {
        if (input.SelectedPlayerIds is not { Count: 1 } selectedPlayerIds)
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf must select exactly one additional victim.");
        }

        if (!TryGetRetainedVictimId(session, out var retainedVictimId))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf requires one retained collective victim.");
        }

        if (GameSessionQueries.HasEliminatedKnownWerewolfFactionAgent(session))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf power is disabled after a Werewolf faction Agent is eliminated.");
        }

        if (HasAdditionalVictimIntentThisNight(session))
        {
            throw new InvalidOperationException(
                "Only one Big Bad Wolf additional victim may be committed per Night.");
        }

        var targetId = selectedPlayerIds.Single();
        if (!GetEligibleTargets(session, retainedVictimId).Contains(targetId))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf target must be a living known non-Agent other than the collective victim.");
        }

        var holder = GetHolder(session);
        var powerInstance = RolePowerInstance.CreateCurrent(
            session,
            holder,
            MainRoleType.BigBadWolf,
            AdditionalVictimPower);
        session.CommitRecurringRolePowerNightAction(
            NightActionType.BigBadWolfVictimSelection,
            targetId,
            new RolePowerInstanceIdentity(
                holder.Id,
                MainRoleType.BigBadWolf,
                AdditionalVictimPower.Identifier.Value,
                powerInstance.Id,
                powerInstance.Origin));
        return committedSleepWait.Execute(session, input);
    }

    private SelectPlayersInstruction CreateIdentificationInstruction(
        GameSession session)
    {
        var selectablePlayerIds = GetIdentificationCandidates(session);
        var roleCount = GetExpectedLivingRoleHolderCount(session);
        var committedLivingRoleHolderCount =
            GetCommittedLivingRoleHolderIds(session).Count;
        if (roleCount <= 0 ||
            committedLivingRoleHolderCount > roleCount ||
            selectablePlayerIds.Count < roleCount)
        {
            throw new InvalidOperationException(
                "Confirmed Role knowledge contradicts the required Living Role Holder count.");
        }

        return new SelectPlayersInstruction(
            ModeratorInstructionSemantic.IdentifyRoleHolders,
            selectablePlayerIds: selectablePlayerIds,
            countConstraint: NumberRangeConstraint.Exact(roleCount),
            publicAnnouncement: GameStrings.RoleWakesUp.Format(PublicName),
            privateInstruction: roleCount == 1
                ? GameStrings.RoleSingleIdentificationPrompt.Format(PublicName)
                : GameStrings.RoleMultipleIdentificationPrompt.Format(
                    PublicName),
            affectedPlayerIds: null,
            roleIdentification: MainRoleType.BigBadWolf);
    }

    private ConfirmationInstruction CreateWakeInstruction(
        GameSession session) =>
        new(
            ModeratorInstructionSemantic.WakeRole,
            GameStrings.RoleWakesUp.Format(PublicName),
            affectedPlayerIds: [GetHolder(session).Id]);

    private SelectPlayersInstruction CreateTargetSelectionInstruction(
        GameSession session)
    {
        var holder = GetHolder(session);
        if (!TryGetRetainedVictimId(session, out var retainedVictimId))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf requires one retained collective victim.");
        }

        var eligibleTargets = GetEligibleTargets(session, retainedVictimId);
        if (eligibleTargets.Count == 0)
        {
            throw new InvalidOperationException(
                "Big Bad Wolf target selection requires one eligible living Player.");
        }

        return new SelectPlayersInstruction(
            ModeratorInstructionSemantic.SelectBigBadWolfTarget,
            privateInstruction:
                GameStrings.BigBadWolfTargetSelectionInstruction,
            selectablePlayerIds: eligibleTargets,
            affectedPlayerIds: [holder.Id],
            countConstraint: NumberRangeConstraint.Single);
    }

    private ConfirmationInstruction CreateSleepInstruction(
        GameSession session) =>
        new(
            ModeratorInstructionSemantic.PutRoleToSleep,
            GameStrings.RoleGoesToSleepSingle.Format(PublicName),
            affectedPlayerIds: [GetHolder(session).Id]);

    private void ValidateIdentificationInstruction(
        GameSession session,
        SelectPlayersInstruction instruction)
    {
        var roleCount = GetExpectedLivingRoleHolderCount(session);
        if (instruction.RoleIdentification != MainRoleType.BigBadWolf ||
            instruction.AffectedPlayerIds != null ||
            roleCount <= 0 ||
            instruction.CountConstraint !=
                NumberRangeConstraint.Exact(roleCount) ||
            !instruction.SelectablePlayerIds.SetEquals(
                GetIdentificationCandidates(session)))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf identification instruction has invalid workflow context.");
        }
    }

    private void ValidateWakeInstruction(
        GameSession session,
        ConfirmationInstruction instruction)
    {
        if (!HasExpectedAffectedHolder(session, instruction))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf wake instruction has invalid workflow context.");
        }
    }

    private void ValidateTargetSelectionInstruction(
        GameSession session,
        SelectPlayersInstruction instruction)
    {
        if (HasAdditionalVictimIntentThisNight(session) ||
            GameSessionQueries.HasEliminatedKnownWerewolfFactionAgent(
                session) ||
            instruction.RoleIdentification != null ||
            instruction.CountConstraint != NumberRangeConstraint.Single ||
            !TryGetRetainedVictimId(session, out var retainedVictimId) ||
            !instruction.SelectablePlayerIds.SetEquals(
                GetEligibleTargets(session, retainedVictimId)) ||
            instruction.SelectablePlayerIds.Count == 0 ||
            !HasExpectedAffectedHolder(session, instruction))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf target selection has invalid workflow context.");
        }
    }

    private void ValidateReplayableSleepInstruction(
        GameSession session,
        ConfirmationInstruction instruction)
    {
        if (GetAdditionalVictimCommitsThisNight(session).Any() ||
            !HasExpectedAffectedHolder(session, instruction))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf replayable sleep has invalid workflow context.");
        }
    }

    private void ValidateCommittedSleepInstruction(
        GameSession session,
        ConfirmationInstruction instruction)
    {
        var commits = GetAdditionalVictimCommitsThisNight(session).ToArray();
        if (commits is not [var commit] ||
            !HasExpectedAffectedHolder(session, instruction))
        {
            throw new InvalidOperationException(
                "The pending Big Bad Wolf sleep instruction has invalid additional-victim workflow context.");
        }

        ValidateCommittedAdditionalVictim(session, commit);
    }

    private static void ValidateCallHandoff(
        AcceptedObservationRecoveryCursor cursor)
    {
        if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
            cursor.ContinuationRole != MainRoleType.BigBadWolf)
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf call has invalid accepted-observation handoff context.");
        }
    }

    private static void ValidateIdentificationHandoff(
        GameSession session,
        AcceptedObservationRecoveryCursor cursor)
    {
        if (cursor.Version != AcceptedObservationRecoveryCursor.CurrentVersion ||
            cursor.ContinuationRole != MainRoleType.BigBadWolf ||
            cursor.ObservedRole != MainRoleType.BigBadWolf ||
            cursor.AcceptedObservationSemantic !=
                ModeratorInstructionSemantic.IdentifyRoleHolders ||
            cursor.RetainedLittleGirlGuidanceDecision != null)
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf continuation has invalid accepted-observation handoff context.");
        }

        var livingHolderIds = session.GetPlayers()
            .WithHealth(PlayerHealth.Alive)
            .Where(player =>
                player.State.CurrentRole == MainRoleType.BigBadWolf)
            .Select(player => player.Id)
            .ToHashSet();
        if (livingHolderIds.Count == 0 ||
            !session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
                .Any(entry =>
                    entry.TurnNumber == session.TurnNumber &&
                    entry.CurrentPhase == GamePhase.Night &&
                    entry.Role == MainRoleType.BigBadWolf &&
                    entry.PlayerIds.SetEquals(livingHolderIds)))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf identification continuation has invalid durable context.");
        }
    }

    private void ValidateRecurringRecoveryCursor(
        GameSession session,
        ConfirmationInstruction instruction,
        DomainRecoveryCursor cursor)
    {
        ValidateRecurringRecoveryCursorIdentity(session, cursor);
        var commits = GetAdditionalVictimCommitsThisNight(session)
            .Where(commit =>
                commit.PowerIdentity == cursor.PowerIdentity &&
                commit.TargetIds is { Count: 1 } targetIds &&
                cursor.CommittedTargetIds.SequenceEqual(targetIds))
            .ToArray();
        if (commits is not [var committedAction])
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf recovery cursor does not match one recurring additional-victim action.");
        }

        ValidateCommittedAdditionalVictim(session, committedAction);
    }

    private static void ValidateRecurringRecoveryCursorIdentity(
        GameSession session,
        DomainRecoveryCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(cursor);
        if (cursor.Kind !=
            DomainRecoveryCursorKind.RecurringNativeRolePowerCommit ||
            cursor.SourceRole != MainRoleType.BigBadWolf ||
            cursor.CommittedActionType !=
            NightActionType.BigBadWolfVictimSelection ||
            cursor.ActingPlayerId == Guid.Empty ||
            !StringComparer.Ordinal.Equals(
                cursor.SourcePowerIdentifier,
                AdditionalVictimPowerIdentifier.Value) ||
            cursor.PowerIdentity != CreateCurrentPowerIdentity(
                session,
                session.GetPlayer(cursor.ActingPlayerId)) ||
            cursor.OneUseResourceId != Guid.Empty)
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf recovery cursor has an invalid recurring Role Power identity.");
        }
    }

    private static bool TryValidateCommittedRecoveryBoundary(
        GameSession session,
        ModeratorInstruction? startingInstruction,
        ModeratorResponse input,
        RecurringRolePowerCommittedLogEntry committedEntry,
        ConfirmationInstruction nextInstruction)
    {
        if (committedEntry.ActionType !=
            NightActionType.BigBadWolfVictimSelection)
        {
            return false;
        }

        if (committedEntry.TargetIds is not [var committedTargetId] ||
            startingInstruction is not SelectPlayersInstruction
            {
                Semantic:
                    ModeratorInstructionSemantic.SelectBigBadWolfTarget,
                CountConstraint: var countConstraint,
                AffectedPlayerIds: { Count: 1 } affectedPlayerIds,
                RoleIdentification: null
            } targetSelection ||
            countConstraint != NumberRangeConstraint.Single ||
            input.SelectedPlayerIds is not
            { Count: 1 } selectedPlayerIds ||
            selectedPlayerIds.Single() != committedTargetId ||
            !targetSelection.SelectablePlayerIds.Contains(committedTargetId) ||
            nextInstruction.AffectedPlayerIds is not
                { Count: 1 } sleepAffectedPlayerIds ||
            sleepAffectedPlayerIds.Single() != affectedPlayerIds.Single())
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf commit must correlate to its accepted target and exact sleep continuation.");
        }

        var actingPlayerId = committedEntry.ActingPlayerId;
        if (actingPlayerId != affectedPlayerIds.Single())
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf commit does not belong to the instructed Role holder.");
        }

        var holder = session.GetPlayer(actingPlayerId);
        if (holder.State.Health != PlayerHealth.Alive ||
            holder.State.MainRole != MainRoleType.BigBadWolf)
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf commit does not belong to the living Role holder.");
        }

        ValidateCommittedAdditionalVictim(session, committedEntry);
        return true;
    }

    private bool IsCompleteHolderSetKnown(GameSession session) =>
        GameSessionQueries.IsCompleteLivingRoleHolderSetKnown(
            session,
            MainRoleType.BigBadWolf);

    private HashSet<Guid> GetIdentificationCandidates(GameSession session) =>
        session.GetPlayers()
            .WithHealth(PlayerHealth.Alive)
            .Where(player =>
                player.State.CurrentRole == MainRoleType.BigBadWolf ||
                (player.State.CurrentRole == null &&
                 (player.State.ModeratorKnownRole == null ||
                  player.State.ModeratorKnownRole ==
                      MainRoleType.BigBadWolf)))
            .ToIdSet();

    private IPlayer GetHolder(GameSession session) =>
        TryGetHolder(session)
        ?? throw new InvalidOperationException(
            "No living Big Bad Wolf is available.");

    private IPlayer? TryGetHolder(GameSession session) =>
        GetAliveRolePlayers(session)?.SingleOrDefault();

    private bool HasExpectedAffectedHolder(
        GameSession session,
        ModeratorInstruction instruction) =>
        TryGetHolder(session) is { } holder &&
        instruction.AffectedPlayerIds is [var affectedPlayerId] &&
        affectedPlayerId == holder.Id;

    private static bool TryGetRetainedVictimId(
        GameSession session,
        out Guid victimId) =>
        GameSessionQueries.TryGetRetainedWerewolfVictimThisNight(
            session,
            out victimId);

    private static HashSet<Guid> GetEligibleTargets(
        GameSession session,
        Guid retainedVictimId) =>
        session.GetPlayers()
            .WithHealth(PlayerHealth.Alive)
            .Where(player =>
                player.Id != retainedVictimId &&
                player.State.GetFactionAgentKnowledge(Faction.Werewolf) ==
                FactionAgentKnowledge.KnownNonAgent)
            .Select(player => player.Id)
            .ToHashSet();

    private static bool HasAdditionalVictimIntentThisNight(
        GameSession session) =>
        GameSessionQueries.GetOrderedNightActionsThisNight(
                session,
                [NightActionType.BigBadWolfVictimSelection])
            .Any();

    private static IEnumerable<RecurringRolePowerCommittedLogEntry>
        GetAdditionalVictimCommitsThisNight(GameSession session) =>
        GameSessionQueries.GetOrderedNightActionsThisNight(
                session,
                [NightActionType.BigBadWolfVictimSelection])
            .OfType<RecurringRolePowerCommittedLogEntry>();

    private static void ValidateCommittedAdditionalVictim(
        GameSession session,
        RecurringRolePowerCommittedLogEntry committedEntry)
    {
        if (committedEntry.ActionType !=
                NightActionType.BigBadWolfVictimSelection ||
            committedEntry.TargetIds is not [var committedTargetId] ||
            committedEntry.SourceRole != MainRoleType.BigBadWolf ||
            !StringComparer.Ordinal.Equals(
                committedEntry.SourcePowerIdentifier,
                AdditionalVictimPowerIdentifier.Value) ||
            committedEntry.PowerIdentity != CreateCurrentPowerIdentity(
                session,
                session.GetPlayer(committedEntry.ActingPlayerId)))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf recovery boundary requires one owned recurring additional-victim action.");
        }

        if (!TryGetRetainedVictimId(session, out var retainedVictimId))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf recovery boundary requires one retained collective victim.");
        }

        if (committedTargetId == retainedVictimId ||
            !GetEligibleTargets(session, retainedVictimId)
                .Contains(committedTargetId))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf additional victim must be a living known non-Agent other than the collective victim.");
        }
    }

    private static RolePowerInstanceIdentity CreateCurrentPowerIdentity(
        GameSession session,
        IPlayer holder) =>
        RolePowerInstance.CreateCurrentIdentity(
            session,
            holder,
            MainRoleType.BigBadWolf,
            AdditionalVictimPower);
}
