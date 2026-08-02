using FluentAssertions;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
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
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorBorrowedCupidTests
{
	private static readonly PhysicalCharacterCard CupidCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000145"),
		MainRoleType.Cupid);
	private static readonly PhysicalCharacterCard SeerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000146"),
		MainRoleType.Seer);
	private static readonly PhysicalCharacterCard DefenderCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000147"),
		MainRoleType.Defender);

	private static readonly TestSubPhaseManagerKey SubPhaseKey = new();
	private static readonly TestHookSubPhaseKey HookKey = new();
	private static readonly TestGameFlowManagerKey RecoveryKey = new();

	[Fact]
	public void BorrowedCupid_NightOneRecoversDeferredRecognitionBeforeInitialBeneficiaryClosureWithoutDuplicate()
	{
		var (session, start, actorId) = CreateFirstNightActorSession();
		var activation = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			CupidCard.Id);
		var players = session.GetPlayers().ToArray();
		var werewolfTargetId = players.Single(player =>
			player.Name == "Werewolf").Id;
		var villagerTargetId = players.Single(player =>
			player.Name == "Villager 1").Id;
		var lovers = new[] { villagerTargetId, werewolfTargetId };
		IGameHookListener listener = new CupidRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var selection = Advance(listener, session, wake.CreateResponse())
			.Instruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var logCountBeforeCommit = session.GameHistoryLog.Count();
		session.SetPendingModeratorInstruction(RecoveryKey, selection);

		var recognition = GameFlowManager.HandleInput(
				session,
				selection.CreateResponse(lovers.ToHashSet()),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeLovers);
		var deferredCommit = session.GetActorBorrowedCupidLoversCommits()
			.Should().ContainSingle().Subject;
		deferredCommit.PowerIdentity.PowerInstanceId.Should().Be(
			activation.ActivationId);
		deferredCommit.Disposition.Should().Be(
			ActorBorrowedCupidLoversDisposition
				.DeferredToInitialBeneficiaryClosure);
		lovers.Should().OnlyContain(playerId =>
			session.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Lovers));
		lovers.Should().OnlyContain(playerId =>
			!session.GetFactionBeneficiaryKnowledge(playerId).IsKnown);
		session.GameHistoryLog.OfType<FactionFactsCommittedLogEntry>().Should()
			.BeEmpty();
		session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
		session.GameHistoryLog.OfType<LoversPairCommittedLogEntry>().Should()
			.BeEmpty();

		var recovered = new GameSession(session.Serialize());
		GameFlowManager.RestoreDurableContinuation(
			recovered,
			SupportedRoleCatalog.Admissions);
		var recoveredRecognition = recovered.PendingModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredRecognition.InstructionId.Should().Be(recognition.InstructionId);
		recoveredRecognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeLovers);
		recoveredRecognition.PublicAnnouncement.Should().BeNull();
		recoveredRecognition.PrivateInstruction.Should().Be(
			GameStrings.LoversRecognitionInstruction);
		recoveredRecognition.AffectedPlayerIds.Should().BeEquivalentTo(lovers);
		recovered.GetActorBorrowedCupidLoversCommits().Should()
			.Equal(deferredCommit);
		recovered.GetActorBorrowedCupidLoversCommits().Should()
			.OnlyContain(commit =>
				commit.Disposition ==
				ActorBorrowedCupidLoversDisposition
					.DeferredToInitialBeneficiaryClosure);
		lovers.Should().OnlyContain(playerId =>
			recovered.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Lovers));
		lovers.Should().OnlyContain(playerId =>
			!recovered.GetFactionBeneficiaryKnowledge(playerId).IsKnown);
		recovered.GameHistoryLog.Skip(logCountBeforeCommit).Should()
			.ContainSingle()
			.Which.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		recovered.GameHistoryLog.OfType<FactionFactsCommittedLogEntry>().Should()
			.BeEmpty();
		recovered.GameHistoryLog.OfType<LoversPairCommittedLogEntry>().Should()
			.BeEmpty();

		var sleep = GameFlowManager.HandleInput(
				recovered,
				recoveredRecognition.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		sleep.PrivateInstruction.Should().BeNull();
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		recovered.GetActorBorrowedCupidLoversCommits().Should()
			.Equal(deferredCommit);
		recovered.GameHistoryLog.Skip(logCountBeforeCommit)
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();

		var agentGroupBoundary = CommitCompleteWerewolfAgentObservation(
			recovered,
			werewolfTargetId);
		InitialBeneficiaryClosureRules.TryCommitCurrentSession(
				recovered,
				agentGroupBoundary)
			.Should().Be(InitialBeneficiaryClosureResult.Committed);

		var classifiedCommit = recovered.GetActorBorrowedCupidLoversCommits()
			.Should().ContainSingle().Subject;
		classifiedCommit.Disposition.Should().Be(
			ActorBorrowedCupidLoversDisposition.CrossFaction);
		lovers.Should().OnlyContain(playerId =>
			recovered.RequireKnownFactionBeneficiary(playerId) ==
				Faction.CrossFactionLovers);
		recovered.GetPlayers().Should().OnlyContain(player =>
			recovered.GetFactionBeneficiaryKnowledge(player.Id).IsKnown);
		recovered.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>().Should()
			.ContainSingle(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
		recovered.GameHistoryLog.OfType<LoversPairCommittedLogEntry>().Should()
			.BeEmpty();
	}

	[Fact]
	public void BorrowedCupid_LaterNightSourceSlotKeepsActorIdentityAndSelectsExactlyTwoLivingPlayers()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var activation = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			CupidCard.Id);

		session.TurnNumber.Should().Be(2);
		session.GetPlayerState(actorId).CurrentRole.Should().Be(
			MainRoleType.Actor);
		session.GetPlayers().Should().NotContain(player =>
			player.State.CurrentRole == MainRoleType.Cupid);
		session.GameHistoryLog.OfType<LoversPairCommittedLogEntry>().Should()
			.BeEmpty();
		session.GetPlayers().Should().OnlyContain(player =>
			!player.State.HasStatusEffect(StatusEffectTypes.Lovers));

		var policy = new RecordingPolicy();
		IGameHookListener listener = new CupidRole(
			new RolePowerAvailabilityGateway(policy));
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName));
		wake.PublicAnnouncement.Should().NotContain(GameStrings.CupidRoleName);
		wake.PrivateInstruction.Should().BeNull();
		wake.AffectedPlayerIds.Should().Equal(actorId);

		var selection = Advance(listener, session, wake.CreateResponse())
			.Instruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var livingPlayerIds = session.GetPlayers()
			.WithHealth(PlayerHealth.Alive)
			.Select(player => player.Id)
			.ToArray();

		selection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectCupidLovers);
		selection.CountConstraint.Should().Be(NumberRangeConstraint.Exact(2));
		selection.SelectablePlayerIds.Should().BeEquivalentTo(livingPlayerIds);
		selection.SelectablePlayerIds.Should().OnlyHaveUniqueItems();
		selection.PublicAnnouncement.Should().BeNull();
		selection.PrivateInstruction.Should().Be(
			GameStrings.CupidTargetSelectionInstruction);
		selection.AffectedPlayerIds.Should().Equal(actorId);

		var attempt = policy.ObservedAttempts.Should().ContainSingle().Subject;
		attempt.ActingPlayer.Id.Should().Be(actorId);
		attempt.ActingPlayer.State.CurrentRole.Should().Be(MainRoleType.Actor);
		attempt.SourceRole.Should().Be(MainRoleType.Cupid);
		attempt.PowerInstance.Id.Should().Be(activation.ActivationId);
		attempt.PowerInstance.Origin.Should().Be(RolePowerInstanceOrigin.Borrowed);
		session.GetPlayerState(actorId).CurrentRole.Should().Be(
			MainRoleType.Actor);
		session.GameHistoryLog.OfType<LoversPairCommittedLogEntry>().Should()
			.BeEmpty();
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.Cupid);
	}

	[Fact]
	public void BorrowedCupid_FewerThanTwoLivingCandidates_OmitsSelectorAndCompletesThroughActorSleepWithoutCommit()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var activation = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			CupidCard.Id);
		foreach (var playerId in session.GetPlayers()
			         .Where(player => player.Id != actorId)
			         .Select(player => player.Id))
		{
			session.EliminatePlayer(
				playerId,
				EliminationReason.EventElimination);
		}

		var policy = new RecordingPolicy();
		IGameHookListener listener = new CupidRole(
			new RolePowerAvailabilityGateway(policy));
		var logCountBeforeSourceSlot = session.GameHistoryLog.Count();
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
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
		attempt.SourceRole.Should().Be(MainRoleType.Cupid);
		attempt.PowerInstance.Id.Should().Be(activation.ActivationId);
		session.GetActorBorrowedCupidLoversCommits().Should().BeEmpty();
		session.GameHistoryLog.Skip(logCountBeforeSourceSlot)
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should().BeEmpty();
		session.GameHistoryLog.Skip(logCountBeforeSourceSlot)
			.OfType<LoversPairCommittedLogEntry>().Should().BeEmpty();
		session.GetPlayers().Should().OnlyContain(player =>
			!player.State.HasStatusEffect(StatusEffectTypes.Lovers));

		var completion = Advance(listener, session, sleep.CreateResponse());

		completion.Outcome.Should().Be(HookListenerOutcome.Complete);
		completion.Instruction.Should().BeNull();
	}

	[Fact]
	public void BorrowedCupid_LaterNightUnknownBeneficiaryRejectsSelectionAndStaleRetryWithoutMutation()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var activation = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			CupidCard.Id);
		var players = session.GetPlayers().ToArray();
		var knownTargetId = players.Single(player =>
			player.Name == "Werewolf").Id;
		var unknownTargetId = players.Single(player =>
			player.Name == "Villager 1").Id;
		var lovers = new[] { knownTargetId, unknownTargetId };
		ArrangeKnownBeneficiaries(
			session,
			(knownTargetId, Faction.Werewolf));
		session.GetFactionBeneficiaryKnowledge(unknownTargetId).Should().Be(
			FactionBeneficiaryKnowledge.Unknown);

		IGameHookListener listener = new CupidRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var selection = Advance(listener, session, wake.CreateResponse())
			.Instruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
		session.SetPendingModeratorInstruction(RecoveryKey, selection);
		var response = selection.CreateResponse(lovers.ToHashSet());
		var historyCountBefore = session.GameHistoryLog.Count();
		var markerCountBefore = session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Count();
		Action submitSelection = () => GameFlowManager.HandleInput(
			session,
			response,
			SupportedRoleCatalog.Admissions);

		submitSelection.Should().Throw<InvalidOperationException>()
			.WithMessage("*Required Faction facts are not ready*");
		AssertNoMutation();

		submitSelection.Should().Throw<InvalidOperationException>();
		AssertNoMutation();

		void AssertNoMutation()
		{
			session.GetActorBorrowedCupidLoversCommits().Should().BeEmpty();
			session.GameHistoryLog.Should().HaveCount(historyCountBefore);
			session.GameHistoryLog
				.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
				.Should().HaveCount(markerCountBefore);
			players.Should().OnlyContain(player =>
				!player.State.HasStatusEffect(StatusEffectTypes.Lovers));
			session.GetFactionBeneficiaryKnowledge(knownTargetId).Should().Be(
				FactionBeneficiaryKnowledge.Known(Faction.Werewolf));
			session.GetFactionBeneficiaryKnowledge(unknownTargetId).Should().Be(
				FactionBeneficiaryKnowledge.Unknown);
			session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
				.Be(activation);
			var pendingSelection = session.PendingModeratorInstruction.Should()
				.BeOfType<SelectPlayersInstruction>().Subject;
			pendingSelection.InstructionId.Should().Be(selection.InstructionId);
			pendingSelection.Semantic.Should().Be(
				ModeratorInstructionSemantic.SelectCupidLovers);
			pendingSelection.AffectedPlayerIds.Should().Equal(actorId);
		}
	}

	[Fact]
	public void BorrowedCupid_RelationshipSurvivesNextOpeningExpiryAndCannotReactivate()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var activation = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			CupidCard.Id);
		var players = session.GetPlayers().ToArray();
		var werewolfTargetId = players.Single(player =>
			player.Name == "Werewolf").Id;
		var villagerTargetId = players.Single(player =>
			player.Name == "Villager 1").Id;
		var lovers = new[] { villagerTargetId, werewolfTargetId };
		ArrangeKnownBeneficiaries(
			session,
			(werewolfTargetId, Faction.Werewolf),
			(villagerTargetId, Faction.Villager));
		session.RequireKnownFactionBeneficiary(werewolfTargetId).Should().Be(
			Faction.Werewolf);
		session.RequireKnownFactionBeneficiary(villagerTargetId).Should().Be(
			Faction.Villager);

		IGameHookListener listener = new CupidRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var selection = Advance(listener, session, wake.CreateResponse())
			.Instruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var logCountBeforeCommit = session.GameHistoryLog.Count();
		session.SetPendingModeratorInstruction(RecoveryKey, selection);

		var recognition = GameFlowManager.HandleInput(
				session,
				selection.CreateResponse(lovers.ToHashSet()),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeLovers);
		recognition.PublicAnnouncement.Should().BeNull();
		recognition.PrivateInstruction.Should().Be(
			GameStrings.LoversRecognitionInstruction);
		recognition.AffectedPlayerIds.Should().BeEquivalentTo(lovers);
		var privateCommit = session.GetActorBorrowedCupidLoversCommits()
			.Should().ContainSingle().Subject;
		var orderedLovers = lovers.Order().ToArray();
		privateCommit.PowerIdentity.ActingPlayerId.Should().Be(actorId);
		privateCommit.PowerIdentity.SourceRole.Should().Be(MainRoleType.Cupid);
		privateCommit.PowerIdentity.SourcePowerIdentifier.Should().Be(
			CupidRole.LinkLoversPowerIdentifier.Value);
		privateCommit.PowerIdentity.PowerInstanceId.Should().Be(
			activation.ActivationId);
		privateCommit.PowerIdentity.PowerInstanceOrigin.Should().Be(
			RolePowerInstanceOrigin.Borrowed);
		privateCommit.ActorSetupCardId.Should().Be(CupidCard.Id);
		privateCommit.FirstPlayerId.Should().Be(orderedLovers[0]);
		privateCommit.SecondPlayerId.Should().Be(orderedLovers[1]);
		privateCommit.Disposition.Should().Be(
			ActorBorrowedCupidLoversDisposition.CrossFaction);
		privateCommit.TurnNumber.Should().Be(2);
		privateCommit.CurrentPhase.Should().Be(GamePhase.Night);
		privateCommit.PublicMarkerLogIndex.Should().Be(logCountBeforeCommit);

		lovers.Should().OnlyContain(playerId =>
			session.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Lovers));
		session.RequireKnownFactionBeneficiary(werewolfTargetId).Should().Be(
			Faction.CrossFactionLovers);
		session.RequireKnownFactionBeneficiary(villagerTargetId).Should().Be(
			Faction.CrossFactionLovers);
		var publicCommit = session.GameHistoryLog.Skip(logCountBeforeCommit)
			.Should().ContainSingle().Subject;
		publicCommit.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		publicCommit.Should().NotBeAssignableTo<LoversPairCommittedLogEntry>();
		publicCommit.Should().NotBeAssignableTo<FactionFactsCommittedLogEntry>();
		publicCommit.Should().NotBeAssignableTo<NightActionLogEntry>();
		var publicCommitText = publicCommit.ToString();
		publicCommitText.Should().NotContain(MainRoleType.Cupid.ToString());
		publicCommitText.Should().NotContain(
			CupidRole.LinkLoversPowerIdentifier.Value);
		publicCommitText.Should().NotContain(activation.ActivationId.ToString());
		publicCommitText.Should().NotContain(CupidCard.Id.ToString());
		publicCommitText.Should().NotContain(actorId.ToString());
		lovers.Should().OnlyContain(playerId =>
			!publicCommitText.Contains(
				playerId.ToString(),
				StringComparison.Ordinal));
		session.GameHistoryLog.OfType<LoversPairCommittedLogEntry>().Should()
			.BeEmpty();
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.Cupid);

		var recovered = new GameSession(session.Serialize());
		GameFlowManager.RestoreDurableContinuation(
			recovered,
			SupportedRoleCatalog.Admissions);
		var recoveredRecognition = recovered.PendingModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredRecognition.InstructionId.Should().Be(recognition.InstructionId);
		recoveredRecognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeLovers);
		recoveredRecognition.PublicAnnouncement.Should().BeNull();
		recoveredRecognition.PrivateInstruction.Should().Be(
			GameStrings.LoversRecognitionInstruction);
		recoveredRecognition.AffectedPlayerIds.Should().BeEquivalentTo(lovers);
		recovered.GetActorBorrowedCupidLoversCommits().Should()
			.Equal(privateCommit);
		recovered.GameHistoryLog.Skip(logCountBeforeCommit).Should()
			.ContainSingle()
			.Which.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		recovered.GameHistoryLog.OfType<LoversPairCommittedLogEntry>().Should()
			.BeEmpty();
		lovers.Should().OnlyContain(playerId =>
			recovered.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Lovers));
		recovered.RequireKnownFactionBeneficiary(werewolfTargetId).Should().Be(
			Faction.CrossFactionLovers);
		recovered.RequireKnownFactionBeneficiary(villagerTargetId).Should().Be(
			Faction.CrossFactionLovers);

		var sleep = GameFlowManager.HandleInput(
				recovered,
				recoveredRecognition.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		sleep.PrivateInstruction.Should().BeNull();
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		recovered.GetActorBorrowedCupidLoversCommits().Should()
			.Equal(privateCommit);
		recovered.GameHistoryLog.Skip(logCountBeforeCommit).Should()
			.ContainSingle();
		recovered.GetPlayerState(actorId).CurrentRole.Should().Be(
			MainRoleType.Actor);
		recovered.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.Cupid);
		Advance(listener, recovered, sleep.CreateResponse()).Outcome.Should()
			.Be(HookListenerOutcome.Complete);
		recovered.ClearCurrentListenerCache(HookKey);
		recovered.TransitionMainPhase(GamePhase.Dawn);
		recovered.TransitionMainPhase(GamePhase.Day);
		recovered.TransitionMainPhase(GamePhase.Night);
		recovered.TryEnterSubPhaseStage(
			SubPhaseKey,
			GameHook.NightMainActionLoop.ToString()).Should().BeTrue();

		IGameHookListener nextActor = CreateActorRole();
		var nextActorWake = Advance(
			nextActor,
			recovered,
			start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
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
		Advance(nextActor, recovered, nextActorSleep.CreateResponse()).Outcome
			.Should().Be(HookListenerOutcome.Complete);
		recovered.ClearCurrentListenerCache(HookKey);
		var historyCountBeforeExpiredSource = recovered.GameHistoryLog.Count();

		var expiredSource = Advance(
			listener,
			recovered,
			start.CreateResponse());

		expiredSource.Outcome.Should().Be(HookListenerOutcome.Skip);
		expiredSource.Instruction.Should().BeNull();
		recovered.GameHistoryLog.Should().HaveCount(
			historyCountBeforeExpiredSource);
		recovered.GetActorBorrowedCupidLoversCommits().Should()
			.Equal(privateCommit);
		lovers.Should().OnlyContain(playerId =>
			recovered.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Lovers));
		recovered.RequireKnownFactionBeneficiary(werewolfTargetId).Should().Be(
			Faction.CrossFactionLovers);
		recovered.RequireKnownFactionBeneficiary(villagerTargetId).Should().Be(
			Faction.CrossFactionLovers);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
		recovered.GetModeratorSpentActorSetupCards().Should().Equal(CupidCard);
	}

	private static ActorRole CreateActorRole() => new(
		new RolePowerAvailabilityGateway(
			new VillagerRolePowerSuppressionPolicy(
				AllowAllRolePowerAvailabilityPolicy.Instance)));

	private static ActorBorrowedRolePowerActivation PerformSpendOpening(
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
		Advance(listener, session, sleep.CreateResponse()).Outcome.Should()
			.Be(HookListenerOutcome.Complete);
		session.ClearCurrentListenerCache(HookKey);
		return activation;
	}

	private static (
		GameSession Session,
		StartGameConfirmationInstruction Start,
		Guid ActorId) CreateFirstNightActorSession()
	{
		var setup = new ActorSetupCards(
			version: 7,
			new[] { CupidCard, SeerCard, DefenderCard });
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
		session.IdentifyRole([actorId], MainRoleType.Actor);
		session.TryEnterSubPhaseStage(
			SubPhaseKey,
			GameHook.NightMainActionLoop.ToString()).Should().BeTrue();
		return (session, start, actorId);
	}

	private static (
		GameSession Session,
		StartGameConfirmationInstruction Start,
		Guid ActorId) CreateLaterNightActorSession()
	{
		var setup = new ActorSetupCards(
			version: 7,
			new[] { CupidCard, SeerCard, DefenderCard });
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
		session.IdentifyRole([actorId], MainRoleType.Actor);
		session.TransitionMainPhase(GamePhase.Day);
		session.TransitionMainPhase(GamePhase.Night);
		session.TryEnterSubPhaseStage(
			SubPhaseKey,
			GameHook.NightMainActionLoop.ToString()).Should().BeTrue();
		return (session, start, actorId);
	}

	private static FactionFactEffectiveBoundary
		CommitCompleteWerewolfAgentObservation(
			GameSession session,
			Guid werewolfPlayerId)
	{
		FactionFactEffectiveBoundary? observationBoundary = null;
		session.CommitFactionFactBatch(context =>
		{
			var boundary = new FactionFactEffectiveBoundary(
				context.TurnNumber,
				context.CurrentPhase,
				session.GameHistoryLog.Count());
			observationBoundary = boundary;
			return new FactionFactsCommittedLogEntry
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
					.. session.GetPlayers().Select(player =>
						FactionFact.Agent(
							player.Id,
							Faction.Werewolf,
							player.Id == werewolfPlayerId
								? FactionAgentKnowledge.KnownAgent
								: FactionAgentKnowledge.KnownNonAgent,
							boundary))
				]
			};
		});

		return observationBoundary!;
	}

	private static void ArrangeKnownBeneficiaries(
		GameSession session,
		params (Guid PlayerId, Faction Faction)[] beneficiaries)
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
					FactionFactSourceKind.ExplicitTransition,
					"test-actor-borrowed-cupid-known-beneficiaries"),
				Facts =
				[
					.. beneficiaries.Select(beneficiary =>
						FactionFact.Beneficiary(
							beneficiary.PlayerId,
							beneficiary.Faction,
							boundary))
				]
			});
	}

	private static HookListenerActionResult Advance(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response)
	{
		var result = listener.Execute(session, response);
		if (result.Outcome != HookListenerOutcome.Skip)
		{
			session.TransitionListenerStateCache(
				HookKey,
				listener.Id,
				result.NextListenerPhase!);
		}

		return result;
	}

	private sealed class RecordingPolicy : IRolePowerAvailabilityPolicy
	{
		internal List<RolePowerAttempt> ObservedAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			ObservedAttempts.Add(attempt);
			return RolePowerAvailabilityResult.Allowed;
		}
	}

	private sealed class TestSubPhaseManagerKey : ISubPhaseManagerKey;
	private sealed class TestHookSubPhaseKey : IHookSubPhaseKey;
	private sealed class TestGameFlowManagerKey : IGameFlowManagerKey;
}
