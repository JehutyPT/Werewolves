using FluentAssertions;
using Werewolves.Core.GameLogic;
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
using Werewolves.Core.StateModels.Serialization;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorRoleTests
{
	private sealed class TestExecutionCommitKey : IGameFlowManagerKey;
	private static readonly TestExecutionCommitKey ExecutionCommitKey = new();

	private static readonly PhysicalCharacterCard SeerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000141"),
		MainRoleType.Seer);
	private static readonly PhysicalCharacterCard CupidCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000142"),
		MainRoleType.Cupid);
	private static readonly PhysicalCharacterCard WitchCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000143"),
		MainRoleType.Witch);

	private static readonly PhaseManager<ListenerTestSubPhase> NightActionLoop = new(
		GamePhase.Night,
		ListenerTestSubPhase.ActionLoop,
		[
			new(
				ListenerTestSubPhase.ActionLoop,
				[
					HookSubPhaseStage.HookStage(GameHook.NightMainActionLoop),
					NavigationSubPhaseStage.NavigationEndStageSilent(GamePhase.Dawn)
				],
				possibleNextMainPhaseTransitions: [new(GamePhase.Dawn)])
		]);

	[Fact]
	public void KnownHolder_FirstOpening_WakesThenOffersRemainingSetupCardsPrivatelyAsSingleOptional()
	{
		var (session, start, actorId) = CreateActorSession(holderKnown: true);
		IGameHookListener listener = CreateActorRole();

		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().Equal(actorId);

		var choice = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		choice.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseActorSetupCard);
		choice.SelectionRange.Should().Be(NumberRangeConstraint.SingleOptional);
		choice.PublicAnnouncement.Should().BeNull();
		choice.PrivateInstruction.Should().Be(
			GameStrings.ActorSetupCardSelectionInstruction);
		choice.AffectedPlayerIds.Should().Equal(actorId);
		choice.Options.Select(option => (option.Id, option.Label)).Should().Equal(
			(SeerCard.Id.ToString("D"), MainRoleType.Seer.GetPublicName()),
			(CupidCard.Id.ToString("D"), MainRoleType.Cupid.GetPublicName()),
			(WitchCard.Id.ToString("D"), MainRoleType.Witch.GetPublicName()));
		session.GetModeratorRemainingActorSetupCards().Should().HaveCount(3);
		session.GetModeratorSpentActorSetupCards().Should().BeEmpty();
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
	}

	[Fact]
	public void KnownHolder_PendingSetupCardChoice_RoundTripRestoresExactPrivateContinuationWithoutCommittingAChoice()
	{
		var (session, start, actorId) = CreateActorSession(holderKnown: true);
		IGameHookListener listener = CreateActorRole();
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;

		var recovered = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(choice)
			.RehydrateGameSession();
		var recoveredChoice = RecoveryPayloadTestDriver.Capture(recovered)
			.PendingInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		IGameHookListener recoveredListener = CreateActorRole();

		recoveredChoice.InstructionId.Should().Be(choice.InstructionId);
		recoveredChoice.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseActorSetupCard);
		recoveredChoice.SelectionRange.Should().Be(
			NumberRangeConstraint.SingleOptional);
		recoveredChoice.PublicAnnouncement.Should().BeNull();
		recoveredChoice.PrivateInstruction.Should().Be(
			GameStrings.ActorSetupCardSelectionInstruction);
		recoveredChoice.AffectedPlayerIds.Should().Equal(actorId);
		recoveredChoice.Options.Select(option => option.Id).Should().Equal(
			SeerCard.Id.ToString("D"),
			CupidCard.Id.ToString("D"),
			WitchCard.Id.ToString("D"));
		var sleep = Advance(
			recoveredListener,
			recovered,
			recoveredChoice.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		recovered.GetModeratorSpentActorSetupCards().Should().BeEmpty();
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
	}

	[Theory]
	[InlineData(PendingChoiceTamper.RequiredSelection)]
	[InlineData(PendingChoiceTamper.ReplacedOptionId)]
	[InlineData(PendingChoiceTamper.ReorderedOptions)]
	[InlineData(PendingChoiceTamper.PublicAnnouncement)]
	[InlineData(PendingChoiceTamper.WrongPrivateInstruction)]
	public void PendingSetupCardChoice_TamperedCanonicalShapeIsRejected(
		PendingChoiceTamper tamper)
	{
		var (session, start, _) = CreateActorSession(holderKnown: true);
		IGameHookListener listener = CreateActorRole();
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var tampered = CreateTamperedPendingChoice(choice, tamper);
		Action rehydrateTamperedChoice = () =>
			RecoveryPayloadTestDriver.Capture(session)
				.WithPendingInstruction(tampered)
				.RehydrateGameSession();

		rehydrateTamperedChoice.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void SetupCardChoice_Declined_SleepsWithoutSpendingOrActivating()
	{
		var (session, start, actorId) = CreateActorSession(holderKnown: true);
		IGameHookListener listener = CreateActorRole();
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;

		var sleep = Advance(listener, session, choice.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		foreach (var option in choice.Options)
		{
			sleep.PublicAnnouncement.Should().NotContain(option.Label);
		}
		session.GetModeratorRemainingActorSetupCards().Should().HaveCount(3);
		session.GetModeratorSpentActorSetupCards().Should().BeEmpty();
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
		session.GameHistoryLog.OfType<ActorSetupCardSpendCommittedLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void SetupCardChoice_Selected_SpendsOnceAndActivatesFreshBorrowedLineageWithoutExecutingSourcePower()
	{
		var (session, start, actorId) = CreateActorSession(holderKnown: true);
		IGameHookListener listener = CreateActorRole();
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;

		var sleep = Advance(
			listener,
			session,
			choice.CreateResponse(SeerCard.Id.ToString("D"))).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		session.GetModeratorRemainingActorSetupCards().Should().Equal(
			CupidCard,
			WitchCard);
		session.GetModeratorSpentActorSetupCards().Should().Equal(SeerCard);
		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation();
		activation.Should().NotBeNull();
		var requiredActivation = activation!;
		requiredActivation.ActingPlayerId.Should().Be(actorId);
		requiredActivation.ActingRole.Should().Be(MainRoleType.Actor);
		requiredActivation.SelectedCardId.Should().Be(SeerCard.Id);
		requiredActivation.SourceRole.Should().Be(MainRoleType.Seer);
		requiredActivation.Origin.Should().Be(RolePowerInstanceOrigin.Borrowed);
		requiredActivation.ActivationId.Should().NotBeEmpty();
		requiredActivation.ActivationId.Should().NotBe(session.Id);
		session.GetPlayers().Select(player => player.Id).Should().NotContain(
			requiredActivation.ActivationId);
		session.GetModeratorActorSetupCards().Cards.Select(card => card.Id)
			.Should().NotContain(requiredActivation.ActivationId);
		session.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);
		session.GameHistoryLog.OfType<ActorSetupCardSpendCommittedLogEntry>()
			.Should().ContainSingle();
		session.GameHistoryLog.OfType<NightActionLogEntry>()
			.Should().NotContain(entry => entry.ActionType == NightActionType.SeerCheck);
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry => entry.Role == MainRoleType.Seer);
	}

	[Fact]
	public void BorrowedSeer_PrivateCheckSurvivesNextOpeningExpiryAndCannotReplay()
	{
		var (session, start, actorId) = CreateActorSession(holderKnown: true);
		var opening = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			SeerCard.Id);
		var activation = opening.Activation;
		var werewolf = session.GetPlayers().Single(player =>
			player.Name == "Werewolf");
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		IGameHookListener listener = new SeerRole(
			new RolePowerAvailabilityGateway(policy));
		var nightOrder = GameFlowManager.HookListeners[GameHook.NightMainActionLoop];

		nightOrder.IndexOf(ListenerIdentifier.Listener(MainRoleType.Actor)).Should()
			.BeLessThan(nightOrder.IndexOf(listener.Id));
		var wake = Advance(
			listener,
			session,
			opening.Sleep.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		ArrangeKnownWerewolfAgentGroup(session, werewolf.Id);
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName));
		wake.AffectedPlayerIds.Should().Equal(actorId);
		var targetSelection = Advance(listener, session, wake.CreateResponse())
			.Instruction.Should().BeOfType<SelectPlayersInstruction>().Subject;

		targetSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectSeerTarget);
		targetSelection.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		targetSelection.SelectablePlayerIds.Should().BeEquivalentTo(
			session.GetPlayers()
				.Where(player => player.Id != actorId)
				.Select(player => player.Id));
		targetSelection.AffectedPlayerIds.Should().Equal(actorId);
		targetSelection.PublicAnnouncement.Should().Contain(
			GameStrings.ActorRoleName);
		targetSelection.PublicAnnouncement.Should().NotContain(
			GameStrings.SeerRoleName);
		targetSelection.PrivateInstruction.Should().Be(
			GameStrings.SeerNightActionPrompt);
		var attempt = policy.ObservedAttempts.Should().ContainSingle().Subject;
		attempt.ActingPlayer.Id.Should().Be(actorId);
		attempt.ActingPlayer.State.CurrentRole.Should().Be(MainRoleType.Actor);
		attempt.SourceRole.Should().Be(MainRoleType.Seer);
		attempt.PowerInstance.Id.Should().Be(activation.ActivationId);
		attempt.PowerInstance.Origin.Should().Be(RolePowerInstanceOrigin.Borrowed);
		var logCountBeforeCheck = session.GameHistoryLog.Count();
		session = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(targetSelection)
			.RehydrateGameSession();

		var feedback = GameFlowManager.HandleInput(
				session,
				targetSelection.CreateResponse([werewolf.Id]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		feedback.Semantic.Should().Be(ModeratorInstructionSemantic.RevealSeerResult);
		feedback.PublicAnnouncement.Should().BeNull();
		feedback.PrivateInstruction.Should().Be(
			GameStrings.SeerResultWerewolfTeam.Format(werewolf.Name));
		feedback.AffectedPlayerIds.Should().Equal(actorId);
		var publicCommit = session.GameHistoryLog.Skip(logCountBeforeCheck)
			.Should().ContainSingle().Subject;
		publicCommit.Should().NotBeAssignableTo<NightActionLogEntry>();
		publicCommit.ToString().Should().NotContain(MainRoleType.Seer.ToString());
		publicCommit.ToString().Should().NotContain(activation.ActivationId.ToString());
		publicCommit.ToString().Should().NotContain(werewolf.Id.ToString());
		session.GameHistoryLog.OfType<TargetPrivateRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry => entry.Role == MainRoleType.Seer);
		session.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);

		var recovered = RecoveryPayloadTestDriver.Parse(
				session.SerializeRecoverySnapshot())
			.RehydrateGameSession();
		var recoveredFeedback = RecoveryPayloadTestDriver.Capture(recovered)
			.PendingInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recoveredFeedback.InstructionId.Should().Be(feedback.InstructionId);
		var sleep = GameFlowManager.HandleInput(
				recovered,
				recoveredFeedback.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		recovered.GameHistoryLog.Skip(logCountBeforeCheck).Should().ContainSingle();
		recovered.GameHistoryLog.OfType<TargetPrivateRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		recovered.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);
		var learnedCheck = recovered.GetActorBorrowedSeerCheckCommits()
			.Should().ContainSingle().Subject;
		Advance(listener, recovered, sleep.CreateResponse()).Outcome.Should()
			.Be(HookListenerOutcome.Complete);
		ArrangeKnownWerewolfAgentGroup(recovered, Guid.Empty);
		recovered.TransitionMainPhase(GamePhase.Day);
		recovered.TransitionMainPhase(GamePhase.Night);

		IGameHookListener nextActor = CreateActorRole();
		var nextActorWake = Advance(
			nextActor,
			recovered,
			start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>().Should()
			.ContainSingle();
		var nextActorChoice = Advance(
			nextActor,
			recovered,
			nextActorWake.CreateResponse()).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var nextActorSleep = Advance(
			nextActor,
			recovered,
			nextActorChoice.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var historyCountBeforeExpiredSource = recovered.GameHistoryLog.Count();
		var expiredSource = Advance(
			nextActor,
			recovered,
			nextActorSleep.CreateResponse());

		expiredSource.Outcome.Should().Be(HookListenerOutcome.Complete);
		expiredSource.Instruction.Should().BeNull();
		recovered.GameHistoryLog.Skip(historyCountBeforeExpiredSource).Should()
			.ContainSingle().Which.Should().BeOfType<PhaseTransitionLogEntry>();
		recovered.GetCurrentPhase().Should().Be(GamePhase.Dawn);
		recovered.GetActorBorrowedSeerCheckCommits().Should()
			.Equal(learnedCheck);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
		recovered.GetModeratorSpentActorSetupCards().Should().Equal(SeerCard);
		policy.ObservedAttempts.Should().ContainSingle();
	}

	[Fact]
	public void BorrowedSeer_AcknowledgedFeedback_RoundTripRestoresExactActorSleepWithoutReplayingCheck()
	{
		var (session, start, actorId) = CreateActorSession(holderKnown: true);
		var opening = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			SeerCard.Id);
		var werewolf = session.GetPlayers().Single(player =>
			player.Name == "Werewolf");
		IGameHookListener listener = new SeerRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var wake = Advance(
			listener,
			session,
			opening.Sleep.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		ArrangeKnownWerewolfAgentGroup(session, werewolf.Id);
		var targetSelection = Advance(listener, session, wake.CreateResponse())
			.Instruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
		session = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(targetSelection)
			.RehydrateGameSession();
		var feedback = GameFlowManager.HandleInput(
				session,
				targetSelection.CreateResponse([werewolf.Id]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var committedCheck = session.GetActorBorrowedSeerCheckCommits()
			.Should().ContainSingle().Subject;

		var sleep = GameFlowManager.HandleInput(
				session,
				feedback.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.InstructionId.Should().NotBe(feedback.InstructionId);
		var historyCountAtSleep = session.GameHistoryLog.Count();
		var recovered = RecoveryPayloadTestDriver.Parse(
				session.SerializeRecoverySnapshot())
			.RehydrateGameSession();
		var recoveredSleep = RecoveryPayloadTestDriver.Capture(recovered)
			.PendingInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredSleep.Should().BeEquivalentTo(sleep);
		recoveredSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		recoveredSleep.InstructionId.Should().NotBe(feedback.InstructionId);
		recovered.GameHistoryLog.Should().HaveCount(historyCountAtSleep);
		recovered.GetActorBorrowedSeerCheckCommits().Should()
			.Equal(committedCheck);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();

		GameFlowManager.HandleInput(
			recovered,
			recoveredSleep.CreateResponse(),
			SupportedRoleCatalog.Admissions);

		recovered.GetActorBorrowedSeerCheckCommits().Should()
			.Equal(committedCheck);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
	}

	[Fact]
	public void BorrowedSeer_NoOtherLivingTarget_OmitsSelectorAndCompletesThroughActorSleepWithoutCommit()
	{
		var (session, start, actorId) = CreateActorSession(holderKnown: true);
		var opening = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			SeerCard.Id);
		var activation = opening.Activation;
		foreach (var playerId in session.GetPlayers()
			         .Where(player => player.Id != actorId)
			         .Select(player => player.Id))
		{
			session.EliminatePlayer(
				playerId,
				EliminationReason.EventElimination);
		}

		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		IGameHookListener listener = new SeerRole(
			new RolePowerAvailabilityGateway(policy));
		var logCountBeforeSourceSlot = session.GameHistoryLog.Count();
		var wake = Advance(
			listener,
			session,
			opening.Sleep.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName));
		wake.AffectedPlayerIds.Should().Equal(actorId);
		var sleep = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		var attempt = policy.ObservedAttempts.Should().ContainSingle().Subject;
		attempt.ActingPlayer.Id.Should().Be(actorId);
		attempt.SourceRole.Should().Be(MainRoleType.Seer);
		attempt.PowerInstance.Id.Should().Be(activation.ActivationId);
		session.GetActorBorrowedSeerCheckCommits().Should().BeEmpty();
		session.GameHistoryLog.Skip(logCountBeforeSourceSlot)
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should().BeEmpty();
		session.GameHistoryLog.Skip(logCountBeforeSourceSlot)
			.OfType<NightActionLogEntry>().Should().NotContain(entry =>
				entry.ActionType == NightActionType.SeerCheck);

		var completion = Advance(listener, session, sleep.CreateResponse());

		completion.Outcome.Should().Be(HookListenerOutcome.Complete);
		completion.Instruction.Should().BeNull();
	}

	[Fact]
	public void CommittedSpend_RoundTripResolvesActorSleepWithoutReplayingTheSpend()
	{
		var (session, start, actorId) = CreateActorSession(holderKnown: true);
		IGameHookListener listener = CreateActorRole();
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var response = choice.CreateResponse(SeerCard.Id.ToString("D"));
		var sleep = Advance(listener, session, response).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var activation =
			session.GetModeratorActiveActorBorrowedRolePowerActivation();
		activation.Should().NotBeNull();
		var requiredActivation = activation!;
		var recovered = RecoveryPayloadTestDriver.Capture(session)
			.RecordActorSetupCardSpend(requiredActivation)
			.WithPendingInstruction(sleep)
			.WithRecoveryCursors(
				domainRecoveryCursor: new DomainRecoveryCursor
				{
					Version = DomainRecoveryCursor.CurrentVersion,
					Kind = DomainRecoveryCursorKind.ActorSetupCardSpendCommit,
					CommittedActionType = NightActionType.Unknown,
					ActingPlayerId = actorId,
					SourceRole = requiredActivation.SourceRole,
					ActorSetupCardId = requiredActivation.SelectedCardId,
					ActorBorrowedActivationId =
						requiredActivation.ActivationId,
					CommittedTargetIds = [],
					NextInstructionSemantic = sleep.Semantic,
					NextInstructionId = sleep.InstructionId
				})
			.RehydrateGameSession();
		var recoveredSleep = RecoveryPayloadTestDriver.Capture(recovered)
			.PendingInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		IGameHookListener recoveredListener = CreateActorRole();

		Advance(recoveredListener, recovered, recoveredSleep.CreateResponse())
			.Instruction.Should().BeOfType<ConfirmationInstruction>()
			.Which.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		recovered.GetModeratorSpentActorSetupCards().Should().Equal(SeerCard);
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(requiredActivation);
		recovered.GameHistoryLog.OfType<ActorSetupCardSpendCommittedLogEntry>()
			.Should().ContainSingle();
	}

	[Fact]
	public void DeclinedChoice_RoundTripResolvesActorSleepWithoutSpending()
	{
		var (session, start, _) = CreateActorSession(holderKnown: true);
		IGameHookListener listener = CreateActorRole();
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var sleep = Advance(listener, session, choice.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var recovered = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(sleep)
			.WithRecoveryCursors(
				acceptedObservationRecoveryCursor:
				new AcceptedObservationRecoveryCursor
				{
					Version = AcceptedObservationRecoveryCursor.CurrentVersion,
					AcceptedObservationSemantic =
						ModeratorInstructionSemantic.ChooseActorSetupCard,
					ObservedRole = MainRoleType.Actor,
					ContinuationRole = MainRoleType.Actor,
					NextInstructionSemantic = sleep.Semantic,
					NextInstructionId = sleep.InstructionId
				})
			.RehydrateGameSession();
		var recoveredSleep = RecoveryPayloadTestDriver.Capture(recovered)
			.PendingInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		IGameHookListener recoveredListener = CreateActorRole();

		Advance(recoveredListener, recovered, recoveredSleep.CreateResponse()).Outcome
			.Should().Be(HookListenerOutcome.Complete);
		recovered.GetModeratorRemainingActorSetupCards().Should().HaveCount(3);
		recovered.GetModeratorSpentActorSetupCards().Should().BeEmpty();
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
		recovered.GameHistoryLog.OfType<ActorSetupCardSpendCommittedLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void UnknownHolder_GenuineOpening_IdentifiesExactActorThenOffersPrivateChoice()
	{
		var (session, start, actorId) = CreateActorSession(holderKnown: false);
		IGameHookListener listener = CreateActorRole();

		var identification = Advance(
			listener,
			session,
			start.CreateResponse()).Instruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.Actor);
		identification.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		session.GetPlayerState(actorId).ModeratorKnownRole.Should().BeNull();

		var choice = Advance(
			listener,
			session,
			identification.CreateResponse([actorId])).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;

		choice.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseActorSetupCard);
		choice.AffectedPlayerIds.Should().Equal(actorId);
		choice.PublicAnnouncement.Should().BeNull();
		session.GetPlayerState(actorId).ModeratorKnownRole.Should()
			.Be(MainRoleType.Actor);
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.Actor &&
				entry.PlayerIds.SetEquals(new[] { actorId }));
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry =>
				entry.Role == SeerCard.PrintedRole ||
				entry.Role == CupidCard.PrintedRole ||
				entry.Role == WitchCard.PrintedRole);
	}

	[Fact]
	public void KnownEmptyHolder_OmitsEntireCallWithoutAvailabilityEvaluation()
	{
		var (session, start, actorId) = CreateActorSession(holderKnown: true);
		session.EliminatePlayer(actorId, EliminationReason.EventElimination);
		session.GetPlayerState(actorId).Health.Should().Be(PlayerHealth.Dead);
		session.GetPlayerState(actorId).ModeratorKnownRole.Should()
			.Be(MainRoleType.Actor);
		session.GetPlayers().Where(player =>
			player.State.Health == PlayerHealth.Alive &&
			player.State.CurrentRole == MainRoleType.Actor).Should().BeEmpty();
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		IGameHookListener listener = new ActorRole(
			new RolePowerAvailabilityGateway(policy));
		var identificationCountBeforeOpening = session.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>().Count();

		var result = Advance(listener, session, start.CreateResponse());

		result.Outcome.Should().Be(HookListenerOutcome.Complete);
		result.Instruction.Should().BeNull();
		policy.ObservedAttempts.Should().BeEmpty();
		session.GetModeratorRemainingActorSetupCards().Should().HaveCount(3);
		session.GetModeratorSpentActorSetupCards().Should().BeEmpty();
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
		session.GameHistoryLog.OfType<ActorSetupCardSpendCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.HaveCount(identificationCountBeforeOpening);
	}

	[Fact]
	public void NextOpening_ExpiresPreviousActivationBeforeWakeAndASecondSpendUsesFreshLineage()
	{
		var (session, start, actorId) = CreateActorSession(holderKnown: true);
		IGameHookListener listener = CreateActorRole();
		var firstWake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var firstChoice = Advance(
			listener,
			session,
			firstWake.CreateResponse()).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var firstSleep = Advance(
			listener,
			session,
			firstChoice.CreateResponse(SeerCard.Id.ToString("D"))).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var firstActivationId = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!
			.ActivationId;
		Advance(listener, session, firstSleep.CreateResponse()).Instruction.Should()
			.BeOfType<ConfirmationInstruction>()
			.Which.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		MoveToNextNight(session);

		var secondWake = Advance(
			listener,
			session,
			start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		secondWake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		secondWake.AffectedPlayerIds.Should().Equal(actorId);
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
		session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
			.Should().ContainSingle();
		var secondChoice = Advance(
			listener,
			session,
			secondWake.CreateResponse()).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		secondChoice.Options.Select(option => option.Id).Should().Equal(
			CupidCard.Id.ToString("D"),
			WitchCard.Id.ToString("D"));
		_ = Advance(
			listener,
			session,
			secondChoice.CreateResponse(CupidCard.Id.ToString("D")));
		var secondActivation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation();
		secondActivation.Should().NotBeNull();
		var requiredSecondActivation = secondActivation!;
		requiredSecondActivation.ActivationId.Should().NotBe(firstActivationId);
		requiredSecondActivation.SelectedCardId.Should().Be(CupidCard.Id);
	}

	[Fact]
	public void OpeningWithKnownEmptyInventory_ExpiresActivationWakesAndSleepsWithoutOfferingAChoice()
	{
		var (session, start, actorId) = CreateActorSession(holderKnown: true);
		IGameHookListener listener = CreateActorRole();
		var firstOpening = PerformSpendOpening(
			listener,
			session,
			start,
			SeerCard.Id);
		_ = Advance(listener, session, firstOpening.Sleep.CreateResponse());
		MoveToNextNight(session);
		var secondOpening = PerformSpendOpening(
			listener,
			session,
			start,
			CupidCard.Id);
		_ = Advance(listener, session, secondOpening.Sleep.CreateResponse());
		MoveToNextNight(session);
		var thirdOpening = PerformSpendOpening(
			listener,
			session,
			start,
			WitchCard.Id);
		_ = Advance(listener, session, thirdOpening.Sleep.CreateResponse());
		MoveToNextNight(session);
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.NotBeNull();

		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var sleep = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName));
		wake.AffectedPlayerIds.Should().Equal(actorId);
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		session.GetModeratorRemainingActorSetupCards().Should().BeEmpty();
		session.GetModeratorSpentActorSetupCards().Should().HaveCount(3);
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
		session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
			.Should().HaveCount(3);
	}

	[Fact]
	public void SuppressedOpening_WithPriorActivation_ExpiresThenSkipsWithoutWakeChoiceOrSleep()
	{
		var (session, start, _) = CreateActorSession(holderKnown: true);
		IGameHookListener listener = CreateActorRole();
		PerformSpendOpening(listener, session, start, SeerCard.Id);
		var spentBeforeSuppression = session
			.GetModeratorSpentActorSetupCards().ToArray();
		ActivateVillagerRolePowerSuppression(session);

		var result = Advance(listener, session, start.CreateResponse());

		result.Outcome.Should().Be(HookListenerOutcome.Complete);
		result.Instruction.Should().BeNull();
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
		session.GetModeratorSpentActorSetupCards().Should()
			.Equal(spentBeforeSuppression);
		session.GetModeratorRemainingActorSetupCards().Should().HaveCount(2);
		session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
			.Should().ContainSingle();
	}

	[Fact]
	public void UnknownHolder_SuppressedOpening_SkipsBeforeExactIdentification()
	{
		var (session, start, actorId) = CreateActorSession(
			holderKnown: false,
			suppressionActive: true);
		IGameHookListener listener = CreateActorRole();

		var result = Advance(listener, session, start.CreateResponse());

		result.Outcome.Should().Be(HookListenerOutcome.Complete);
		result.Instruction.Should().BeNull();
		session.GetPlayerState(actorId).ModeratorKnownRole.Should().BeNull();
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry => entry.Role == MainRoleType.Actor);
		session.GetModeratorSpentActorSetupCards().Should().BeEmpty();
	}

	[Fact]
	public void ProductionAdmission_UsesActorFactoryAndPublishesActorInSupportedCatalog()
	{
		var catalog = SupportedRoleCatalog.Admissions;
		var actorListenerId = ListenerIdentifier.Listener(MainRoleType.Actor);

		catalog.GetAdmission(actorListenerId).Should()
			.Be(RoleAdmissionKind.Active);
		catalog.TryGetListenerFactory(actorListenerId, out var factory).Should()
			.BeTrue();
		factory!().Should().BeOfType<ActorRole>();
		SupportedRoleCatalog.IsSupported(MainRoleType.Actor).Should().BeTrue();
		var nightOrder = GameFlowManager.HookListeners[GameHook.NightMainActionLoop];
		nightOrder.IndexOf(ListenerIdentifier.Listener(MainRoleType.Thief)).Should()
			.BeLessThan(nightOrder.IndexOf(actorListenerId));
		nightOrder.IndexOf(actorListenerId).Should().BeLessThan(
			nightOrder.IndexOf(ListenerIdentifier.Listener(MainRoleType.LittleGirl)));
	}

	private static ActorRole CreateActorRole() => new(
		new RolePowerAvailabilityGateway(
			new VillagerRolePowerSuppressionPolicy(
				AllowAllRolePowerAvailabilityPolicy.Instance)));

	private static SelectOptionsInstruction CreateTamperedPendingChoice(
		SelectOptionsInstruction choice,
		PendingChoiceTamper tamper)
	{
		var options = choice.Options.ToArray();
		var selectionRange = choice.SelectionRange;
		var publicAnnouncement = choice.PublicAnnouncement;
		var privateInstruction = choice.PrivateInstruction;
		switch (tamper)
		{
			case PendingChoiceTamper.RequiredSelection:
				selectionRange = NumberRangeConstraint.Single;
				break;
			case PendingChoiceTamper.ReplacedOptionId:
				options[0] = new ModeratorOption(
					"00000000-0000-0000-0000-000000000149",
					options[0].Label);
				break;
			case PendingChoiceTamper.ReorderedOptions:
				options = options.Reverse().ToArray();
				break;
			case PendingChoiceTamper.PublicAnnouncement:
				publicAnnouncement = GameStrings.NightStartsPrompt;
				break;
			case PendingChoiceTamper.WrongPrivateInstruction:
				privateInstruction = GameStrings.ThiefOfferSelectionInstruction;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(tamper), tamper, null);
		}

		return new SelectOptionsInstruction(
			choice.Semantic,
			options,
			selectionRange,
			publicAnnouncement,
			privateInstruction,
			choice.AffectedPlayerIds,
			choice.InstructionId);
	}

	private static ListenerAdvanceResult Advance(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response)
	{
		_ = session.GetOrCreateListener(listener.Id, () => listener);
		var consumedInstruction = session.Execution.PendingInstruction;
		var result = NightActionLoop.ProcessInputAndUpdatePhase(session, response);
		if (result is PhaseExecutionResult.InstructionReady { Instruction: var nextInstruction })
		{
			if (consumedInstruction != null)
			{
				PublishPendingInstruction(
					session, consumedInstruction, response, nextInstruction);
			}
			return new ListenerAdvanceResult(
				HookListenerOutcome.NeedInput, nextInstruction);
		}

		return new ListenerAdvanceResult(
			HookListenerOutcome.Complete,
			result is PhaseExecutionResult.PhaseExited exited
				? exited.TransitionInstruction
				: throw new InvalidOperationException("Unexpected phase execution outcome."));
	}

	private static void PublishPendingInstruction(
		GameSession session,
		ModeratorInstruction consumedInstruction,
		ModeratorResponse response,
		ModeratorInstruction nextInstruction)
	{
		var publicationResponse =
			response.InstructionId == consumedInstruction.InstructionId
				? response
				: new ModeratorResponse
				{
					InstructionId = consumedInstruction.InstructionId,
					Type = response.Type,
					SelectedPlayerIds = response.SelectedPlayerIds,
					AssignedPlayerRoles = response.AssignedPlayerRoles,
					SelectedOptionIds = response.SelectedOptionIds
				};
		session.CommitExecution(
			ExecutionCommitKey,
			ExecutionCommit.RetainRecoveryBoundary(
				session.Execution,
				consumedInstruction,
				publicationResponse,
				nextInstruction));
	}

	private static SpendOpeningResult PerformSpendOpening(
		IGameHookListener listener,
		GameSession session,
		StartGameConfirmationInstruction start,
		Guid selectedCardId)
	{
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var sleep = Advance(
			listener,
			session,
			choice.CreateResponse(selectedCardId.ToString("D"))).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		return new SpendOpeningResult(activation, sleep);
	}

	private static (GameSession Session, StartGameConfirmationInstruction Start, Guid ActorId)
		CreateActorSession(bool holderKnown, bool suppressionActive = false)
	{
		var setup = new ActorSetupCards(
			version: 7,
			new[] { WitchCard, SeerCard, CupidCard });
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
		var actorId = session.GetPlayers().First().Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		if (holderKnown)
		{
			RoleFactionKnowledge.CommitRoleIdentification(
				session,
				new HashSet<Guid> { actorId },
				MainRoleType.Actor);
		}
		if (suppressionActive)
		{
			ActivateVillagerRolePowerSuppression(session);
		}
		ArrangeKnownWerewolfAgentGroup(session, Guid.Empty);

		return (session, start, actorId);
	}

	private static void ActivateVillagerRolePowerSuppression(
		GameSession session)
	{
		session.TransitionMainPhase(GamePhase.Day);
		session.CommitGameFact(context =>
			new VillagerRolePowerSuppressionCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				AnnouncementInstructionId = Guid.NewGuid()
			});
		session.TransitionMainPhase(GamePhase.Night);
	}

	private static void MoveToNextNight(GameSession session)
	{
		session.TransitionMainPhase(GamePhase.Dawn);
		session.TransitionMainPhase(GamePhase.Day);
		session.TransitionMainPhase(GamePhase.Night);
	}

	private static void ArrangeKnownWerewolfAgentGroup(
		GameSession session,
		Guid werewolfId)
	{
		var hadCompleteAgentKnowledge = session.GetPlayers().All(player =>
			session.GetFactionAgentKnowledge(player.Id, Faction.Werewolf) !=
			FactionAgentKnowledge.Unknown);
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
				Source = hadCompleteAgentKnowledge
					? new FactionFactSource(
						FactionFactSourceKind.ExplicitTransition,
						"test-actor-role-werewolf-agent-group-update")
					: new FactionFactSource(
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

	private sealed class RecordingPolicy(RolePowerAvailabilityResult result)
		: IRolePowerAvailabilityPolicy
	{
		internal List<RolePowerAttempt> ObservedAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			ObservedAttempts.Add(attempt);
			return result;
		}
	}

	private sealed record ListenerAdvanceResult(
		HookListenerOutcome Outcome,
		ModeratorInstruction? Instruction);
	private sealed record SpendOpeningResult(
		ActorBorrowedRolePowerActivation Activation,
		ConfirmationInstruction Sleep);

	private enum ListenerTestSubPhase
	{
		ActionLoop
	}

	public enum PendingChoiceTamper
	{
		RequiredSelection,
		ReplacedOptionId,
		ReorderedOptions,
		PublicAnnouncement,
		WrongPrivateInstruction
	}
}
