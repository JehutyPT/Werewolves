using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

/// <summary>
/// Simple Werewolf role implementation using the polymorphic hook listener pattern.
/// Inherits from StandardNightRoleHookListener for standard target selection workflow.
/// </summary>
internal class SimpleWerewolfRole : StandardNightRoleHookListener
{
	private readonly RolePowerAvailabilityGateway _availabilityGateway;
	private bool? _littleGirlGuidanceAllowed;

	internal SimpleWerewolfRole(
		RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

    internal override string PublicName => GameStrings.SimpleWerewolfRoleName;
    public override ListenerIdentifier Id => ListenerIdentifier.Listener(MainRoleType.SimpleWerewolf);
    protected override bool HasNightPowers => true;

    public override HookListenerActionResult Execute(
        GameSession session,
        ModeratorResponse input)
    {
        if (GetCurrentListenerState(session) == null &&
            TryGetKnownLivingWerewolfAgents(session, out var agents) &&
            agents.Count == 0)
        {
            TryCommitInitialBeneficiaryClosure(session);
            return HookListenerActionResult.Skip();
        }

        // This listener is the established Werewolf-faction anchor. Its execution
        // depends on current Faction Agent facts, not exact Simple Werewolf holders.
        return ExecuteCore(session, input);
    }

    public override bool TryResolvePendingInstructionContinuation(
        GameHook hook,
        GameSession session,
        ModeratorInstruction pendingInstruction,
        out string listenerState)
    {
        listenerState = string.Empty;
        if (hook != GameHook.NightMainActionLoop)
        {
            return false;
        }

        switch (pendingInstruction)
        {
            case SelectPlayersInstruction
            {
                Semantic:
                    ModeratorInstructionSemantic
                        .ObserveWerewolfFactionAgentGroup,
                RoleIdentification: null
            } when !TryGetKnownLivingWerewolfAgents(session, out _):
            case ConfirmationInstruction
            {
                Semantic: ModeratorInstructionSemantic.WakeRole
            } when HasExpectedAffectedWerewolfAgents(
                session,
                pendingInstruction):
                listenerState = WokenUpStateEnum.ToString();
                return true;
            case SelectPlayersInstruction
            {
                Semantic: ModeratorInstructionSemantic.SelectWerewolfVictim
            } when HasExpectedAffectedWerewolfAgents(
                session,
                pendingInstruction):
                listenerState = AwaitingTargetSelectionEnum.ToString();
                return true;
            case ConfirmationInstruction
            {
                Semantic: ModeratorInstructionSemantic.PutRoleToSleep
            } when HasExpectedAffectedWerewolfAgents(
                session,
                pendingInstruction):
                listenerState = ReadyToSleepStateEnum.ToString();
                return true;
        }

        if (pendingInstruction.Semantic is
            ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup or
            ModeratorInstructionSemantic.WakeRole or
            ModeratorInstructionSemantic.SelectWerewolfVictim or
            ModeratorInstructionSemantic.PutRoleToSleep)
        {
            return false;
        }

        return base.TryResolvePendingInstructionContinuation(
            hook,
            session,
            pendingInstruction,
            out listenerState);
    }

    protected override List<RoleStateMachineStage> DefineStateMachineStages()
    {
        var stages = base.DefineStateMachineStages();
        stages.Add(CreateStage(
            GameHook.NightMainActionLoop,
            WokenUpStateEnum,
            [AwaitingTargetSelectionEnum, ReadyToSleepStateEnum],
            HandleNightPowerUse_AndId,
            shouldOverwriteStartStage: true));
        return stages;
    }

    protected override HookListenerActionResult HandleRoleWakeupAndId(
        GameSession session,
        ModeratorResponse input)
    {
		_littleGirlGuidanceAllowed =
		    EvaluateLittleGirlGuidanceAvailability(session);

        if (!TryGetKnownLivingWerewolfAgents(session, out var agents))
        {
            var livingPlayerIds = GetLivingPlayers(session)
                .Select(player => player.Id)
                .ToHashSet();
		    var privateInstruction =
		        _littleGirlGuidanceAllowed == true
		            ? string.Join(
		                Environment.NewLine + Environment.NewLine,
		                GameStrings.WerewolfFactionAgentObservationPrompt,
		                GameStrings.LittleGirlOpeningGuidance)
		            : GameStrings.WerewolfFactionAgentObservationPrompt;
            return HookListenerActionResult.NeedInput(
                new SelectPlayersInstruction(
                    ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup,
                    selectablePlayerIds: livingPlayerIds,
                    countConstraint: NumberRangeConstraint.AtLeast(1),
                    publicAnnouncement: GameStrings.RoleHoldersWakeUp.Format(
                        GameStrings.WerewolvesGroupName),
		            privateInstruction: privateInstruction),
                WokenUpStateEnum);
        }

        if (agents.Count == 0)
        {
            return HookListenerActionResult.Complete(AsleepStateEnum);
        }

        return HookListenerActionResult.NeedInput(
            new ConfirmationInstruction(
                ModeratorInstructionSemantic.WakeRole,
                GameStrings.RoleHoldersWakeUp.Format(
                    GameStrings.WerewolvesGroupName),
		        privateInstruction: _littleGirlGuidanceAllowed == true
		            ? GameStrings.LittleGirlOpeningGuidance
		            : null,
                affectedPlayerIds: agents.Select(player => player.Id).ToArray()),
            WokenUpStateEnum);
    }

    protected override HookListenerActionResult HandleNightPowerUse_AndId(
        GameSession session,
        ModeratorResponse input)
    {
        FactionFactEffectiveBoundary? observationBoundary = null;
        if (!TryGetKnownLivingWerewolfAgents(session, out _))
        {
            observationBoundary =
                CommitWerewolfAgentGroupObservation(session, input);
        }

        var result = HandleNightPowerUse(session, input);
        TryCommitInitialBeneficiaryClosure(session, observationBoundary);
        return result;
    }

    protected override HookListenerActionResult HandleNightPowerUse(
        GameSession session,
        ModeratorResponse input)
    {
        if (GetLivingKnownNonAgents(session).Count == 0)
        {
            return PrepareSleepInstruction(session);
        }

        return base.HandleNightPowerUse(session, input);
    }

    protected override ModeratorInstruction GenerateTargetSelectionInstruction(GameSession session, ModeratorResponse input)
    {
        if (!TryGetKnownLivingWerewolfAgents(session, out var werewolves) ||
            werewolves.Count == 0)
        {
            throw new InvalidOperationException(
                "Werewolf victim selection requires a known, nonempty living Agent group.");
        }

        var potentialTargets = GetLivingKnownNonAgents(session);
        if (potentialTargets.Count == 0)
        {
            throw new InvalidOperationException(
                "Werewolf victim selection requires a living known non-Agent.");
        }

        return new SelectPlayersInstruction(
            ModeratorInstructionSemantic.SelectWerewolfVictim,
            publicAnnouncement: GameStrings.WerewolvesChooseVictimPrompt,
            selectablePlayerIds: potentialTargets,
            affectedPlayerIds: werewolves.Select(w => w.Id).ToList(),
            countConstraint: NumberRangeConstraint.Single
        );
    }

    protected override void ProcessTargetSelectionNoFeedback(GameSession session, ModeratorResponse input)
    {
        if (input.SelectedPlayerIds is not { Count: 1 })
        {
            throw new InvalidOperationException(
                "Werewolf victim selection requires exactly one Player.");
        }

        var victimId = input.SelectedPlayerIds.Single();
        if (!GetLivingKnownNonAgents(session).Contains(victimId))
        {
            throw new InvalidOperationException(
                session.GetModeratorActiveActorBorrowedRolePowerActivation()
                    ?.SourceRole == MainRoleType.LittleGirl
                    ? "The borrowed Role Power response is invalid or no longer available."
                    : "The Werewolf victim must be a living known non-Agent.");
        }

        session.PerformNightAction(NightActionType.WerewolfVictimSelection, victimId);
    }

    protected override HookListenerActionResult PrepareSleepInstruction(
        GameSession session)
    {
        if (!TryGetKnownLivingWerewolfAgents(session, out var werewolves) ||
            werewolves.Count == 0)
        {
            throw new InvalidOperationException(
                "Werewolf sleep requires a known, nonempty living Agent group.");
        }

        return HookListenerActionResult.NeedInput(
            new ConfirmationInstruction(
                ModeratorInstructionSemantic.PutRoleToSleep,
                GameStrings.RoleHoldersGoToSleep.Format(
                    GameStrings.WerewolvesGroupName),
		        privateInstruction: _littleGirlGuidanceAllowed == true
		            ? GameStrings.LittleGirlClosingGuidance
		            : null,
                affectedPlayerIds: werewolves
                    .Select(player => player.Id)
                    .ToArray()),
            ReadyToSleepStateEnum);
    }

	internal bool? LittleGirlGuidanceDecision =>
		_littleGirlGuidanceAllowed;

	internal void RestoreLittleGirlGuidanceDecision(bool? isAllowed) =>
		_littleGirlGuidanceAllowed = isAllowed;

	protected override HookListenerActionResult HandleAsleepConfirmation(
		GameSession session,
		ModeratorResponse input)
	{
		var result = base.HandleAsleepConfirmation(session, input);
		_littleGirlGuidanceAllowed = null;
		return result;
	}

	private bool? EvaluateLittleGirlGuidanceAvailability(GameSession session)
	{
		var livingHolders = session.GetPlayers()
		    .WithHealth(PlayerHealth.Alive)
		    .Where(player =>
		        player.State.CurrentRole == MainRoleType.LittleGirl)
		    .ToArray();
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		var hasBorrowedPower =
			activation?.SourceRole == MainRoleType.LittleGirl;
		var executionCount =
			livingHolders.Length + (hasBorrowedPower ? 1 : 0);
		if (executionCount == 0)
		{
		    return null;
		}

		if (executionCount != 1)
		{
		    throw new InvalidOperationException(
		        "Little Girl spying requires exactly one active execution.");
		}

		var actingPlayer = hasBorrowedPower
			? session.GetPlayer(activation!.ActingPlayerId)
			: livingHolders.Single();
		var instance = hasBorrowedPower
			? RolePowerInstance.CreateBorrowed(
				session,
				actingPlayer,
				MainRoleType.LittleGirl,
				LittleGirlRole.SpyingPower)
			: RolePowerInstance.CreateCurrent(
				session,
				actingPlayer,
				MainRoleType.LittleGirl,
				LittleGirlRole.SpyingPower);
		return _availabilityGateway.Evaluate(
		        new RolePowerAttempt(
		            session,
		            actingPlayer,
		            MainRoleType.LittleGirl,
		            LittleGirlRole.SpyingPower,
		            instance))
		    .AvailabilityResult.IsAvailable;
	}

    private static IReadOnlyList<IPlayer> GetLivingPlayers(GameSession session) =>
        session.GetPlayers()
            .WithHealth(PlayerHealth.Alive)
            .ToArray();

    private static bool TryGetKnownLivingWerewolfAgents(
        GameSession session,
        out IReadOnlyList<IPlayer> agents)
    {
        var livingPlayers = GetLivingPlayers(session);
        if (livingPlayers.Any(player =>
                session.GetFactionAgentKnowledge(
                    player.Id,
                    Faction.Werewolf) == FactionAgentKnowledge.Unknown))
        {
            agents = [];
            return false;
        }

        agents = livingPlayers
            .Where(player =>
                session.GetFactionAgentKnowledge(
                    player.Id,
                    Faction.Werewolf) == FactionAgentKnowledge.KnownAgent)
            .ToArray();
        return true;
    }

    private static bool HasExpectedAffectedWerewolfAgents(
        GameSession session,
        ModeratorInstruction pendingInstruction) =>
        TryGetKnownLivingWerewolfAgents(session, out var agents) &&
        agents.Count > 0 &&
        pendingInstruction.AffectedPlayerIds is { } affectedPlayerIds &&
        affectedPlayerIds.ToHashSet().SetEquals(
            agents.Select(player => player.Id));

    private static HashSet<Guid> GetLivingKnownNonAgents(GameSession session) =>
        GetLivingPlayers(session)
            .Where(player =>
                session.GetFactionAgentKnowledge(
                    player.Id,
                    Faction.Werewolf) ==
                FactionAgentKnowledge.KnownNonAgent)
            .Select(player => player.Id)
            .ToHashSet();

    private static FactionFactEffectiveBoundary
        CommitWerewolfAgentGroupObservation(
            GameSession session,
            ModeratorResponse input)
    {
        if (input.SelectedPlayerIds is not { Count: > 0 } selectedPlayerIds)
        {
            throw new InvalidOperationException(
                "Werewolf Agent-group observation requires a nonempty Player selection.");
        }

        var livingPlayers = GetLivingPlayers(session);
        var livingPlayerIds = livingPlayers
            .Select(player => player.Id)
            .ToHashSet();
        var observedAgentIds = selectedPlayerIds.ToHashSet();
        if (!observedAgentIds.IsSubsetOf(livingPlayerIds))
        {
            throw new InvalidOperationException(
                "Werewolf Agent-group observation may select only living Players.");
        }

        var contradictedKnownFact = livingPlayers.Any(player =>
        {
            var knowledge = session.GetFactionAgentKnowledge(
                player.Id,
                Faction.Werewolf);
            return knowledge == FactionAgentKnowledge.KnownAgent &&
                   !observedAgentIds.Contains(player.Id) ||
                   knowledge == FactionAgentKnowledge.KnownNonAgent &&
                   observedAgentIds.Contains(player.Id);
        });
        if (contradictedKnownFact)
        {
            throw new InvalidOperationException(
                "Werewolf Agent-group observation contradicts committed Faction facts.");
        }

        FactionFactEffectiveBoundary? committedBoundary = null;
        session.CommitFactionFactBatch(context =>
        {
            committedBoundary = new FactionFactEffectiveBoundary(
                context.TurnNumber,
                context.CurrentPhase,
                session.GameHistoryLog.Count());
            var facts = livingPlayers
                .Select(player => FactionFact.Agent(
                    player.Id,
                    Faction.Werewolf,
                    observedAgentIds.Contains(player.Id)
                        ? FactionAgentKnowledge.KnownAgent
                        : FactionAgentKnowledge.KnownNonAgent,
                    committedBoundary))
                .ToImmutableArray();
            return new FactionFactsCommittedLogEntry
            {
                Timestamp = context.Timestamp,
                TurnNumber = context.TurnNumber,
                CurrentPhase = context.CurrentPhase,
                Source = new FactionFactSource(
                    FactionFactSourceKind.ScheduledObservation,
                    FactionFactSource
                        .WerewolfFactionAgentGroupObservationIdentifier),
                Facts = facts
            };
        });

        return committedBoundary ??
               throw new InvalidOperationException(
                   "Werewolf Agent-group observation did not establish a boundary.");
    }

    private static void TryCommitInitialBeneficiaryClosure(
        GameSession session,
        FactionFactEffectiveBoundary? initialAgentGroupBoundary = null)
    {
        if (session.TurnNumber != 1)
        {
            return;
        }

        _ = InitialBeneficiaryClosureRules.TryCommitCurrentSession(
            session,
            initialAgentGroupBoundary);
    }
}
