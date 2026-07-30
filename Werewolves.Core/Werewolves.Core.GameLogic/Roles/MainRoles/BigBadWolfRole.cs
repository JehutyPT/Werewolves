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
    : NightRoleHookListener<BigBadWolfRoleState>
{
    private readonly RolePowerAvailabilityGateway _availabilityGateway;

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
    }

    internal override string PublicName => GameStrings.BigBadWolfRoleName;

    public override ListenerIdentifier Id =>
        ListenerIdentifier.Listener(MainRoleType.BigBadWolf);

    protected override BigBadWolfRoleState WokenUpStateEnum =>
        BigBadWolfRoleState.Awake;

    protected override BigBadWolfRoleState ReadyToSleepStateEnum =>
        BigBadWolfRoleState.ReadyToSleep;

    protected override BigBadWolfRoleState AsleepStateEnum =>
        BigBadWolfRoleState.Asleep;

    protected override bool HasNightPowers => true;

    public override HookListenerActionResult Execute(
        GameSession session,
        ModeratorResponse input)
    {
        if (GetCurrentListenerState(session) == null &&
            (!TryGetRetainedVictimId(session, out _) ||
             GameSessionQueries.HasEliminatedKnownWerewolfFactionAgent(
                 session)))
        {
            return HookListenerActionResult.Skip();
        }

        return base.Execute(session, input);
    }

    public override bool TryResolvePendingInstructionContinuation(
        GameHook hook,
        GameSession session,
        ModeratorInstruction pendingInstruction,
        out string listenerState)
    {
        listenerState = string.Empty;
        if (hook == GameHook.NightMainActionLoop &&
            pendingInstruction is SelectPlayersInstruction
            {
                Semantic:
                    ModeratorInstructionSemantic.SelectBigBadWolfTarget
            } &&
            HasExpectedAffectedRoleHolders(session, pendingInstruction))
        {
            listenerState =
                BigBadWolfRoleState.AwaitingTargetSelection.ToString();
            return true;
        }

        if (hook != GameHook.NightMainActionLoop ||
            pendingInstruction is not ConfirmationInstruction
            {
                Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
                AffectedPlayerIds: { Count: 1 } affectedPlayerIds
            })
        {
            return base.TryResolvePendingInstructionContinuation(
                hook,
                session,
                pendingInstruction,
                out listenerState);
        }

        var holder = GetAliveRolePlayers(session)?.SingleOrDefault();
        if (holder == null || affectedPlayerIds.Single() != holder.Id)
        {
            return false;
        }

        var committedActions =
            GameSessionQueries.GetOrderedNightActionsThisNight(
                    session,
                    [NightActionType.BigBadWolfVictimSelection])
                .Where(entry =>
                    entry.GetType() == typeof(NightActionLogEntry))
                .ToArray();
        if (committedActions.Length == 0)
        {
            listenerState = BigBadWolfRoleState.ReadyToSleep.ToString();
            return true;
        }

        if (committedActions is not [var committedAction])
        {
            throw new InvalidOperationException(
                "The pending Big Bad Wolf sleep instruction has multiple additional-victim commits.");
        }

        ValidateCommittedAdditionalVictim(session, committedAction);
        listenerState = BigBadWolfRoleState.ReadyToSleep.ToString();
        return true;
    }

    internal static bool TryValidateCommittedRecoveryBoundary(
        GameSession session,
        ModeratorInstruction? startingInstruction,
        ModeratorResponse input,
        NightActionLogEntry committedEntry,
        ModeratorInstruction nextInstruction,
        out Guid actingPlayerId)
    {
        actingPlayerId = Guid.Empty;
        if (committedEntry.GetType() != typeof(NightActionLogEntry) ||
            committedEntry.ActionType !=
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
            nextInstruction is not ConfirmationInstruction
            {
                Semantic: ModeratorInstructionSemantic.PutRoleToSleep,
                AffectedPlayerIds: { Count: 1 } sleepAffectedPlayerIds
            } ||
            sleepAffectedPlayerIds.Single() != affectedPlayerIds.Single())
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf commit must correlate to its accepted target and exact sleep continuation.");
        }

        actingPlayerId = affectedPlayerIds.Single();
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

    internal static void ValidateRecurringRecoveryCursorIdentity(
        DomainRecoveryCursor cursor)
    {
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
            cursor.PowerInstanceId != cursor.ActingPlayerId ||
            cursor.PowerInstanceOrigin != RolePowerInstanceOrigin.Native ||
            cursor.OneUseResourceId != Guid.Empty)
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf recovery cursor has an invalid recurring Role Power identity.");
        }
    }

    protected override HookListenerActionResult HandleRoleWakeupAndId(
        GameSession session,
        ModeratorResponse input)
    {
        var result = base.HandleRoleWakeupAndId(session, input);
        if (result.Instruction is not ConfirmationInstruction
            {
                Semantic: ModeratorInstructionSemantic.WakeRole
            })
        {
            return result;
        }

        var holder = GetHolder(session);
        return HookListenerActionResult.NeedInput(
            new ConfirmationInstruction(
                ModeratorInstructionSemantic.WakeRole,
                GameStrings.RoleWakesUp.Format(PublicName),
                affectedPlayerIds: [holder.Id]),
            BigBadWolfRoleState.Awake);
    }

    protected override List<RoleStateMachineStage> DefineStateMachineStages() =>
    [
        CreateStage(
            GameHook.NightMainActionLoop,
            null,
            [
                BigBadWolfRoleState.Awake,
                BigBadWolfRoleState.Asleep
            ],
            HandleRoleWakeupAndId),
        CreateStage(
            GameHook.NightMainActionLoop,
            BigBadWolfRoleState.Awake,
            [
                BigBadWolfRoleState.AwaitingTargetSelection,
                BigBadWolfRoleState.ReadyToSleep
            ],
            HandleNightPowerUse_AndId),
        CreateStage(
            GameHook.NightMainActionLoop,
            BigBadWolfRoleState.AwaitingTargetSelection,
            BigBadWolfRoleState.ReadyToSleep,
            CommitTargetSelection),
        CreateStage(
            GameHook.NightMainActionLoop,
            BigBadWolfRoleState.ReadyToSleep,
            BigBadWolfRoleState.Asleep,
            HandleAsleepConfirmation),
        CreateEndStage(
            GameHook.NightMainActionLoop,
            BigBadWolfRoleState.Asleep,
            (_, _) => HookListenerActionResult.Complete(
                BigBadWolfRoleState.Asleep))
    ];

    protected override HookListenerActionResult HandleNightPowerUse(
        GameSession session,
        ModeratorResponse input)
    {
        var holder = GetHolder(session);
        if (!TryGetRetainedVictimId(session, out var retainedVictimId))
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf requires one retained collective victim.");
        }

        if (GameSessionQueries.HasEliminatedKnownWerewolfFactionAgent(session))
        {
            return PrepareSleepInstruction(session);
        }

        var availability = _availabilityGateway.Evaluate(
            new RolePowerAttempt(
                holder,
                MainRoleType.BigBadWolf,
                AdditionalVictimPower,
                RolePowerInstance.CreateNative(
                    holder,
                    MainRoleType.BigBadWolf,
                    AdditionalVictimPower)));
        if (!availability.AvailabilityResult.IsAvailable)
        {
            return PrepareSleepInstruction(session);
        }

        var eligibleTargets = GetEligibleTargets(session, retainedVictimId);
        if (eligibleTargets.Count == 0)
        {
            return PrepareSleepInstruction(session);
        }

        return HookListenerActionResult.NeedInput(
            new SelectPlayersInstruction(
                ModeratorInstructionSemantic.SelectBigBadWolfTarget,
                privateInstruction:
                    GameStrings.BigBadWolfTargetSelectionInstruction,
                selectablePlayerIds: eligibleTargets,
                affectedPlayerIds: [holder.Id],
                countConstraint: NumberRangeConstraint.Single),
            BigBadWolfRoleState.AwaitingTargetSelection);
    }

    private HookListenerActionResult CommitTargetSelection(
        GameSession session,
        ModeratorResponse input)
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

        session.PerformNightAction(
            NightActionType.BigBadWolfVictimSelection,
            targetId);
        return PrepareSleepInstruction(session);
    }

    protected override HookListenerActionResult PrepareSleepInstruction(
        GameSession session)
    {
        var holder = GetHolder(session);
        return HookListenerActionResult.NeedInput(
            new ConfirmationInstruction(
                ModeratorInstructionSemantic.PutRoleToSleep,
                GameStrings.RoleGoesToSleepSingle.Format(PublicName),
                affectedPlayerIds: [holder.Id]),
            BigBadWolfRoleState.ReadyToSleep);
    }

    private IPlayer GetHolder(GameSession session) =>
        GetAliveRolePlayers(session)?.SingleOrDefault()
        ?? throw new InvalidOperationException(
            "No living Big Bad Wolf is available.");

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

    private static void ValidateCommittedAdditionalVictim(
        GameSession session,
        NightActionLogEntry committedEntry)
    {
        if (committedEntry.GetType() != typeof(NightActionLogEntry) ||
            committedEntry.ActionType !=
            NightActionType.BigBadWolfVictimSelection ||
            committedEntry.TargetIds is not [var committedTargetId])
        {
            throw new InvalidOperationException(
                "The Big Bad Wolf recovery boundary requires one generic additional-victim action.");
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
}
