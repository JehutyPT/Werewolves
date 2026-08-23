using FluentAssertions;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorBorrowedLittleGirlTests
{
	private sealed class TestExecutionCommitKey : IGameFlowManagerKey;
	private static readonly TestExecutionCommitKey ExecutionCommitKey = new();

	private static readonly PhysicalCharacterCard LittleGirlCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000142"),
		MainRoleType.LittleGirl);
	private static readonly PhysicalCharacterCard SeerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000143"),
		MainRoleType.Seer);
	private static readonly PhysicalCharacterCard CupidCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000144"),
		MainRoleType.Cupid);
	private static readonly SubPhaseManager<NightSubPhases> NightActionLoop =
		new(
			NightSubPhases.Start,
			[
				HookSubPhaseStage.HookStage(GameHook.NightMainActionLoop),
				NavigationSubPhaseStage.NavigationEndStageSilent(GamePhase.Dawn)
			]);

	[Fact]
	public void BorrowedLittleGirl_UnknownWerewolfMembership_DecoratesExistingCollectiveIntervalOnceWithoutIdentityLeak()
	{
		var (session, start, actorId, werewolfId) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			LittleGirlCard.Id);
		var policy = new RecordingPolicy();
		IGameHookListener listener = new SimpleWerewolfRole(
			new RolePowerAvailabilityGateway(policy));

		var observation = Advance(
				listener,
				session,
				actorSleep.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		activation.SourceRole.Should().Be(MainRoleType.LittleGirl);
		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		observation.PrivateInstruction.Should().Be(string.Join(
			Environment.NewLine + Environment.NewLine,
			GameStrings.WerewolfFactionAgentObservationPrompt.Format(1),
			GameStrings.LittleGirlOpeningGuidance));
		observation.AffectedPlayerIds.Should().BeNull();
		var attempt = policy.ObservedAttempts.Should().ContainSingle().Subject;
		attempt.ActingPlayer.Id.Should().Be(actorId);
		attempt.ActingPlayer.State.CurrentRole.Should().Be(MainRoleType.Actor);
		attempt.SourceRole.Should().Be(MainRoleType.LittleGirl);
		attempt.SourcePower.Identifier.Value.Should().Be("little-girl-spying");
		attempt.PowerInstance.Id.Should().Be(activation.ActivationId);
		attempt.PowerInstance.Origin.Should().Be(RolePowerInstanceOrigin.Borrowed);
		attempt.OneUseResource.Should().BeNull();

		var victimSelection = Advance(
			listener,
			session,
			observation.CreateResponse([werewolfId]))
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		var victimId = victimSelection.SelectablePlayerIds.First();
		var sleep = Advance(
			listener,
			session,
			victimSelection.CreateResponse([victimId]))
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PrivateInstruction.Should().Be(GameStrings.LittleGirlClosingGuidance);
		policy.ObservedAttempts.Should().ContainSingle();
		session.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.LittleGirl);
		foreach (var announcement in new[]
			         {
				         observation.PublicAnnouncement,
				         victimSelection.PublicAnnouncement,
				         sleep.PublicAnnouncement
			         }.Where(announcement => announcement is not null))
		{
			announcement.Should().NotContain(GameStrings.LittleGirlRoleName);
			announcement.Should().NotContain(LittleGirlCard.Id.ToString());
			announcement.Should().NotContain(activation.ActivationId.ToString());
		}
	}

	[Fact]
	public void BorrowedLittleGirl_InvalidObservationResponsesUseGenericErrorWithoutMutation()
	{
		var (session, start, actorId, _) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			LittleGirlCard.Id);
		IGameHookListener listener = new SimpleWerewolfRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var observation = Advance(
				listener,
				session,
				actorSleep.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var historyCount = session.GameHistoryLog.Count();
		var werewolfFactionAgencyBefore = session.GetPlayers().ToDictionary(
			player => player.Id,
			player => session.GetFactionAgentKnowledge(
				player.Id,
				Faction.Werewolf));
		var invalidResponses = new[]
		{
			new ModeratorResponse
			{
				InstructionId = observation.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds = new HashSet<Guid>()
			},
			new ModeratorResponse
			{
				InstructionId = observation.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds = new HashSet<Guid> { Guid.NewGuid() }
			}
		};

		foreach (var invalidResponse in invalidResponses)
		{
			var act = () => Advance(listener, session, invalidResponse);

			act.Should().Throw<InvalidOperationException>().WithMessage(
				GameStrings.ActorBorrowedRolePowerInvalidResponse);
			session.GameHistoryLog.Should().HaveCount(historyCount);
			session.GetPlayers().ToDictionary(
				player => player.Id,
				player => session.GetFactionAgentKnowledge(
					player.Id,
					Faction.Werewolf)).Should().Equal(werewolfFactionAgencyBefore);
			session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
				.Be(activation);
			session.GetPlayerState(actorId).CurrentRole.Should().Be(
				MainRoleType.Actor);
		}
	}

	[Fact]
	public void BorrowedLittleGirl_MalformedVictimSelectionUsesGenericErrorWithoutMutation()
	{
		var (session, start, actorId, werewolfId) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			LittleGirlCard.Id);
		IGameHookListener listener = new SimpleWerewolfRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var observation = Advance(
				listener,
				session,
				actorSleep.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var victimSelection = Advance(
			listener,
			session,
			observation.CreateResponse([werewolfId]))
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var historyCount = session.GameHistoryLog.Count();
		var malformed = new ModeratorResponse
		{
			InstructionId = victimSelection.InstructionId,
			Type = ExpectedInputType.PlayerSelection,
			SelectedPlayerIds = victimSelection.SelectablePlayerIds
				.Take(2)
				.ToHashSet()
		};

		var act = () => Advance(listener, session, malformed);

		act.Should().Throw<InvalidOperationException>().WithMessage(
			GameStrings.ActorBorrowedRolePowerInvalidResponse);
		session.GameHistoryLog.Should().HaveCount(historyCount);
		session.GameHistoryLog.OfType<NightActionLogEntry>().Should()
			.NotContain(entry =>
				entry.ActionType == NightActionType.WerewolfVictimSelection);
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
		session.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);
	}

	[Fact]
	public void BorrowedLittleGirl_KnownWerewolfMembership_DecoratesWholeCollectiveIntervalWithOneDecision()
	{
		var (session, start, actorId, werewolfId) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			LittleGirlCard.Id);
		ArrangeKnownWerewolfAgentGroup(session, werewolfId);
		var policy = new RecordingPolicy(
			RolePowerAvailabilityResult.Allowed,
			RolePowerAvailabilityResult.Denied);
		IGameHookListener listener = new SimpleWerewolfRole(
			new RolePowerAvailabilityGateway(policy));

		var wake = Advance(listener, session, actorSleep.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().Equal(werewolfId);
		wake.PrivateInstruction.Should().Be(GameStrings.LittleGirlOpeningGuidance);
		var attempt = policy.ObservedAttempts.Should().ContainSingle().Subject;
		attempt.ActingPlayer.Id.Should().Be(actorId);
		attempt.ActingPlayer.State.CurrentRole.Should().Be(MainRoleType.Actor);
		attempt.SourceRole.Should().Be(MainRoleType.LittleGirl);
		attempt.SourcePower.Identifier.Value.Should().Be("little-girl-spying");
		attempt.PowerInstance.Id.Should().Be(activation.ActivationId);
		attempt.PowerInstance.Origin.Should().Be(RolePowerInstanceOrigin.Borrowed);

		var victimSelection = Advance(
			listener,
			session,
			wake.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var sleep = Advance(
			listener,
			session,
			victimSelection.CreateResponse(
				[victimSelection.SelectablePlayerIds.First()]))
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PrivateInstruction.Should().Be(GameStrings.LittleGirlClosingGuidance);
		policy.ObservedAttempts.Should().ContainSingle();
		session.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);
	}

	[Fact]
	public void BorrowedLittleGirl_KnownEmptyWerewolfMembership_OmitsCollectiveBeforeEvaluation()
	{
		var (session, start, actorId, _) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			LittleGirlCard.Id);
		ArrangeKnownWerewolfAgentGroup(session, werewolfId: null);
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		IGameHookListener listener = new SimpleWerewolfRole(
			new RolePowerAvailabilityGateway(policy));

		var result = Advance(listener, session, actorSleep.CreateResponse());

		activation.SourceRole.Should().Be(MainRoleType.LittleGirl);
		result.Should().BeNull();
		policy.ObservedAttempts.Should().BeEmpty();
		session.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.LittleGirl);
	}

	[Fact]
	public void BorrowedLittleGirl_AcceptedObservationRecoveryRetainsDecisionWhileVictimSelectionTailReplays()
	{
		var (session, start, actorId, werewolfId) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			LittleGirlCard.Id);
		var policy = new RecordingPolicy(
			RolePowerAvailabilityResult.Allowed,
			RolePowerAvailabilityResult.Denied);
		IGameHookListener listener = session.GetOrCreateListener(
			ListenerIdentifier.Listener(MainRoleType.SimpleWerewolf),
			() => new SimpleWerewolfRole(
				new RolePowerAvailabilityGateway(policy)));
		var observation = Advance(
				listener,
				session,
				actorSleep.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		observation.PrivateInstruction.Should().Contain(
			GameStrings.LittleGirlOpeningGuidance);
		activation.SourceRole.Should().Be(MainRoleType.LittleGirl);
		policy.ObservedAttempts.Should().ContainSingle();
		session = RestorePendingInstruction(session, listener, observation);

		var victimSelection = GameFlowManager.HandleInput(
				session,
				observation.CreateResponse([werewolfId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		var recovered = new GameSession(session.Serialize());
		recovered.GetOrCreateListener(
			ListenerIdentifier.Listener(MainRoleType.SimpleWerewolf),
			() => new SimpleWerewolfRole(
				new RolePowerAvailabilityGateway(policy)));
		GameFlowManager.RestoreDurableContinuation(
			recovered,
			SupportedRoleCatalog.Admissions);
		var recoveredVictimSelection = RecoveryPayloadTestDriver.Capture(recovered)
			.PendingInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var victimId = recoveredVictimSelection.SelectablePlayerIds.First();

		var sleep = GameFlowManager.HandleInput(
				recovered,
				recoveredVictimSelection.CreateResponse([victimId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredVictimSelection.InstructionId.Should().Be(
			victimSelection.InstructionId);
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PrivateInstruction.Should().Be(GameStrings.LittleGirlClosingGuidance);
		policy.ObservedAttempts.Should().ContainSingle();
		var replayRecovered = new GameSession(recovered.Serialize());
		replayRecovered.GetOrCreateListener(
			ListenerIdentifier.Listener(MainRoleType.SimpleWerewolf),
			() => new SimpleWerewolfRole(
				new RolePowerAvailabilityGateway(policy)));

		GameFlowManager.RestoreDurableContinuation(
			replayRecovered,
			SupportedRoleCatalog.Admissions);
		var replayedVictimSelection = RecoveryPayloadTestDriver
			.Capture(replayRecovered)
			.PendingInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		replayedVictimSelection.InstructionId.Should().Be(
			recoveredVictimSelection.InstructionId);
		replayedVictimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		policy.ObservedAttempts.Should().ContainSingle();
		var replayedSleep = GameFlowManager.HandleInput(
				replayRecovered,
				replayedVictimSelection.CreateResponse([victimId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		replayedSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		replayedSleep.PrivateInstruction.Should().Be(
			GameStrings.LittleGirlClosingGuidance);
		policy.ObservedAttempts.Should().ContainSingle();
		replayRecovered.GetPlayerState(actorId).CurrentRole.Should().Be(
			MainRoleType.Actor);
		replayRecovered.GameHistoryLog.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		replayRecovered.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.LittleGirl);
		foreach (var announcement in new[]
			         {
				         recoveredVictimSelection.PublicAnnouncement,
				         sleep.PublicAnnouncement,
				         replayedVictimSelection.PublicAnnouncement,
				         replayedSleep.PublicAnnouncement
			         }.Where(announcement => announcement is not null))
		{
			announcement.Should().NotContain(GameStrings.LittleGirlRoleName);
			announcement.Should().NotContain(LittleGirlCard.Id.ToString());
			announcement.Should().NotContain(activation.ActivationId.ToString());
		}
		foreach (var entry in replayRecovered.GameHistoryLog)
		{
			entry.ToString().Should().NotContain(MainRoleType.LittleGirl.ToString());
			entry.ToString().Should().NotContain(LittleGirlCard.Id.ToString());
			entry.ToString().Should().NotContain(activation.ActivationId.ToString());
		}
	}

	[Fact]
	public void BorrowedLittleGirl_DeniedGuidanceRecoveryRetainsDecisionWithoutReevaluation()
	{
		var (session, start, actorId, werewolfId) = CreateActorSession();
		var (activation, actorSleep) = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			LittleGirlCard.Id);
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Denied);
		IGameHookListener listener = session.GetOrCreateListener(
			ListenerIdentifier.Listener(MainRoleType.SimpleWerewolf),
			() => new SimpleWerewolfRole(
				new RolePowerAvailabilityGateway(policy)));
		var observation = Advance(
				listener,
				session,
				actorSleep.CreateResponse())
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		observation.PrivateInstruction.Should().Be(
			GameStrings.WerewolfFactionAgentObservationPrompt.Format(1));
		policy.ObservedAttempts.Should().ContainSingle();
		session = RestorePendingInstruction(session, listener, observation);
		var victimSelection = GameFlowManager.HandleInput(
				session,
				observation.CreateResponse([werewolfId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		var recovered = new GameSession(session.Serialize());
		recovered.GetOrCreateListener(
			ListenerIdentifier.Listener(MainRoleType.SimpleWerewolf),
			() => new SimpleWerewolfRole(
				new RolePowerAvailabilityGateway(policy)));
		GameFlowManager.RestoreDurableContinuation(
			recovered,
			SupportedRoleCatalog.Admissions);
		var recoveredVictimSelection = RecoveryPayloadTestDriver.Capture(recovered)
			.PendingInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var victimId = recoveredVictimSelection.SelectablePlayerIds.First();
		var sleep = GameFlowManager.HandleInput(
				recovered,
				recoveredVictimSelection.CreateResponse([victimId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredVictimSelection.InstructionId.Should().Be(
			victimSelection.InstructionId);
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PrivateInstruction.Should().BeNull();
		policy.ObservedAttempts.Should().ContainSingle();
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
		recovered.GetPlayerState(actorId).CurrentRole.Should().Be(
			MainRoleType.Actor);
	}

	private static ActorRole CreateActorRole() => new(
		new RolePowerAvailabilityGateway(
			new VillagerRolePowerSuppressionPolicy(
				AllowAllRolePowerAvailabilityPolicy.Instance)));

	private static (
		ActorBorrowedRolePowerActivation Activation,
		ConfirmationInstruction CompletionInstruction) PerformSpendOpening(
		IGameHookListener listener,
		GameSession session,
		StartGameConfirmationInstruction start,
		Guid selectedCardId)
	{
		var wake = Advance(listener, session, start.CreateResponse())
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(listener, session, wake.CreateResponse())
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var sleep = Advance(
			listener,
			session,
			choice.CreateResponse(selectedCardId.ToString("D")))
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		return (activation, sleep);
	}

	private static (
		GameSession Session,
		StartGameConfirmationInstruction Start,
		Guid ActorId,
		Guid WerewolfId) CreateActorSession()
	{
		var setup = new ActorSetupCards(
			version: 7,
			new[] { LittleGirlCard, SeerCard, CupidCard });
		var config = new GameSessionConfig(
			[GameStrings.ActorRoleName, "Werewolf", "Villager 1", "Villager 2", "Villager 3"],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var werewolfId = players[1].Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		session.IdentifyRole([actorId], MainRoleType.Actor);
		return (session, start, actorId, werewolfId);
	}

	private static void ArrangeKnownWerewolfAgentGroup(
		GameSession session,
		Guid? werewolfId)
	{
		var boundary = new FactionFactEffectiveBoundary(
			session.TurnNumber,
			session.GetCurrentPhase(),
			session.GameHistoryLog.Count());
		session.CommitFactionFactBatch(context =>
			new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ScheduledObservation,
					FactionFactSource
						.WerewolfFactionAgentGroupObservationIdentifier),
				Facts =
				[
					.. session.GetPlayers().Select(player => FactionFact.Agent(
						player.Id,
						Faction.Werewolf,
						player.Id == werewolfId
							? FactionAgentKnowledge.KnownAgent
							: FactionAgentKnowledge.KnownNonAgent,
						boundary))
				]
			});
	}

	private static ModeratorInstruction? Advance(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response)
	{
		var startingExecution = session.Execution;
		var consumedInstruction = startingExecution.PendingInstruction
			?? throw new InvalidOperationException(
				"The Actor borrowed test workflow requires one Pending Instruction.");
		session.GetOrCreateListener(listener.Id, () => listener);
		var nextInstruction = NightActionLoop.Execute(
			session,
			response).ModeratorInstruction;
		if (nextInstruction != null)
		{
			session.CommitExecution(
				ExecutionCommitKey,
				ExecutionCommit.RetainRecoveryBoundary(
					session.Execution,
					consumedInstruction,
					response,
					nextInstruction));
		}

		return nextInstruction;
	}

	private static GameSession RestorePendingInstruction(
		GameSession session,
		IGameHookListener listener,
		ModeratorInstruction instruction)
	{
		var recovered = new GameSession(
			RecoveryPayloadTestDriver.Capture(session)
				.WithPendingInstruction(instruction)
				.Serialize());
		recovered.GetOrCreateListener(listener.Id, () => listener);
		GameFlowManager.RestoreDurableContinuation(
			recovered,
			SupportedRoleCatalog.Admissions);
		return recovered;
	}

	private sealed class RecordingPolicy(
		params RolePowerAvailabilityResult[] results)
		: IRolePowerAvailabilityPolicy
	{
		private readonly Queue<RolePowerAvailabilityResult> _results =
			new(results);

		internal List<RolePowerAttempt> ObservedAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			ObservedAttempts.Add(attempt);
			return _results.Count == 0
				? RolePowerAvailabilityResult.Allowed
				: _results.Dequeue();
		}
	}

}
