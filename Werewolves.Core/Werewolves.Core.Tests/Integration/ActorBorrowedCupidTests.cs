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

public sealed class ActorBorrowedCupidTests
{
	private sealed class TestExecutionCommitKey : IGameFlowManagerKey;
	private static readonly TestExecutionCommitKey ExecutionCommitKey = new();

	private static readonly PhysicalCharacterCard CupidCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000145"),
		MainRoleType.Cupid);
	private static readonly PhysicalCharacterCard SeerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000146"),
		MainRoleType.Seer);
	private static readonly PhysicalCharacterCard DefenderCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000147"),
		MainRoleType.Defender);

	private static readonly SubPhaseManager<NightSubPhases> NightActionLoop = new(
		NightSubPhases.Start,
		[
			HookSubPhaseStage.HookStage(GameHook.NightMainActionLoop),
			NavigationSubPhaseStage.NavigationEndStageSilent(NightSubPhases.Start)
		]);

	[Fact]
	public void BorrowedCupid_NightOneRecoversDeferredRecognitionBeforeInitialBeneficiaryClosureWithoutDuplicate()
	{
		var (session, start, actorId) = CreateFirstNightActorSession();
		IGameHookListener listener = new CupidRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var (activation, wake) = PerformSpendOpening(
			CreateActorRole(),
			listener,
			session,
			start,
			CupidCard.Id);
		var players = session.GetPlayers().ToArray();
		var werewolfTargetId = players.Single(player =>
			player.Name == "Werewolf").Id;
		var villagerTargetId = players.Single(player =>
			player.Name == "Villager 1").Id;
		var lovers = new[] { villagerTargetId, werewolfTargetId };
		var selection = Advance(listener, session, wake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var logCountBeforeCommit = session.GameHistoryLog.Count();
		session = RehydrateAtPendingInstruction(session, selection);

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
			.ContainSingle(entry => entry.Source.Identifier ==
				FactionFactSource
					.RoleIdentificationWerewolfFactionAgencyEntailmentIdentifier);
		session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
		session.GameHistoryLog.OfType<LoversPairCommittedLogEntry>().Should()
			.BeEmpty();

		var recovered = RecoveryPayloadTestDriver.Parse(session.Serialize())
			.RehydrateGameSession();
		var recoveredRecognition = RecoveryPayloadTestDriver.Capture(recovered)
			.PendingInstruction
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
			.ContainSingle(entry => entry.Source.Identifier ==
				FactionFactSource
					.RoleIdentificationWerewolfFactionAgencyEntailmentIdentifier);
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

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void NightOneLoversClassification_NativeAndBorrowedPairsReachTheSameOutcomeInOneClosure(
		bool crossFaction)
	{
		var native = CreateNativeFirstNightCupidSession();
		var nativeLovers = SelectClassificationPair(
			native.Session,
			crossFaction);
		native.Session.CommitLoversPair(
			nativeLovers,
			new RolePowerInstanceIdentity(
				native.CupidId,
				MainRoleType.Cupid,
				CupidRole.LinkLoversPowerIdentifier.Value,
				native.CupidId,
				RolePowerInstanceOrigin.Native));
		var nativeBoundary = CommitCompleteWerewolfAgentObservation(
			native.Session,
			native.WerewolfId);
		var nativeHistoryCountBeforeClosure =
			native.Session.GameHistoryLog.Count();

		InitialBeneficiaryClosureRules.TryCommitCurrentSession(
				native.Session,
				nativeBoundary)
			.Should().Be(InitialBeneficiaryClosureResult.Committed);

		var (borrowedSession, start, _) = CreateFirstNightActorSession();
		IGameHookListener listener = new CupidRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var (_, wake) = PerformSpendOpening(
			CreateActorRole(),
			listener,
			borrowedSession,
			start,
			CupidCard.Id);
		var borrowedLovers = SelectClassificationPair(
			borrowedSession,
			crossFaction);
		var selection = Advance(
			listener,
			borrowedSession,
			wake.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		borrowedSession = RehydrateAtPendingInstruction(
			borrowedSession,
			selection);
		GameFlowManager.HandleInput(
			borrowedSession,
			selection.CreateResponse(borrowedLovers.ToHashSet()),
			SupportedRoleCatalog.Admissions);
		var borrowedBoundary = CommitCompleteWerewolfAgentObservation(
			borrowedSession,
			borrowedSession.GetPlayers().Single(player =>
				player.Name == "Werewolf").Id);
		var borrowedHistoryCountBeforeClosure =
			borrowedSession.GameHistoryLog.Count();

		InitialBeneficiaryClosureRules.TryCommitCurrentSession(
				borrowedSession,
				borrowedBoundary)
			.Should().Be(InitialBeneficiaryClosureResult.Committed);

		var expectedFaction = crossFaction
			? Faction.CrossFactionLovers
			: Faction.Villager;
		nativeLovers.Should().OnlyContain(playerId =>
			native.Session.RequireKnownFactionBeneficiary(playerId) ==
			expectedFaction);
		borrowedLovers.Should().OnlyContain(playerId =>
			borrowedSession.RequireKnownFactionBeneficiary(playerId) ==
			expectedFaction);
		native.Session.GameHistoryLog.Should().HaveCount(
			nativeHistoryCountBeforeClosure + 1);
		borrowedSession.GameHistoryLog.Should().HaveCount(
			borrowedHistoryCountBeforeClosure + 1);
		native.Session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>().Should()
			.ContainSingle(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		borrowedSession.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>().Should()
			.ContainSingle(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		borrowedSession.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
		borrowedSession.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Where(entry =>
				entry.Source.Kind !=
				FactionFactSourceKind.InitialBeneficiaryClosure)
			.SelectMany(entry => entry.Facts)
			.Should().NotContain(fact =>
				borrowedLovers.Contains(fact.PlayerId) &&
				fact.Faction == Faction.CrossFactionLovers);
	}

	[Fact]
	public void BorrowedCupid_LaterNightSourceSlotKeepsActorIdentityAndSelectsExactlyTwoLivingPlayers()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		var policy = new RecordingPolicy();
		IGameHookListener listener = new CupidRole(
			new RolePowerAvailabilityGateway(policy));
		var (activation, wake) = PerformSpendOpening(
			CreateActorRole(),
			listener,
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

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName));
		wake.PublicAnnouncement.Should().NotContain(GameStrings.CupidRoleName);
		wake.PrivateInstruction.Should().BeNull();
		wake.AffectedPlayerIds.Should().Equal(actorId);

		var selection = Advance(listener, session, wake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
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
		var policy = new RecordingPolicy();
		IGameHookListener listener = new CupidRole(
			new RolePowerAvailabilityGateway(policy));
		var (activation, wake) = PerformSpendOpening(
			CreateActorRole(),
			listener,
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

		var logCountBeforeSourceSlot = session.GameHistoryLog.Count();
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName));
		wake.AffectedPlayerIds.Should().Equal(actorId);
		var sleep = Advance(listener, session, wake.CreateResponse())
			.ModeratorInstruction
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

		CompleteCadence(listener, session, sleep.CreateResponse());
	}

	[Fact]
	public void BorrowedCupid_LaterNightSameFactionPairPreservesKnownBeneficiaries()
	{
		var (session, start, _) = CreateLaterNightActorSession();
		IGameHookListener listener = new CupidRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var (_, wake) = PerformSpendOpening(
			CreateActorRole(),
			listener,
			session,
			start,
			CupidCard.Id);
		var lovers = session.GetPlayers()
			.Where(player => player.Name.StartsWith(
				"Villager",
				StringComparison.Ordinal))
			.Take(2)
			.Select(player => player.Id)
			.ToArray();
		ArrangeKnownBeneficiaries(
			session,
			lovers.Select(playerId => (playerId, Faction.Villager)).ToArray());
		var selection = Advance(listener, session, wake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var historyCountBeforeCommit = session.GameHistoryLog.Count();
		session = RehydrateAtPendingInstruction(session, selection);

		var recognition = GameFlowManager.HandleInput(
				session,
				selection.CreateResponse(lovers.ToHashSet()),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeLovers);
		session.GetActorBorrowedCupidLoversCommits()
			.Should().ContainSingle()
			.Which.Disposition.Should().Be(
				ActorBorrowedCupidLoversDisposition.SameFaction);
		lovers.Should().OnlyContain(playerId =>
			session.RequireKnownFactionBeneficiary(playerId) == Faction.Villager);
		lovers.Should().OnlyContain(playerId =>
			session.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Lovers));
		session.GameHistoryLog.Skip(historyCountBeforeCommit).Should()
			.ContainSingle()
			.Which.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		session.GameHistoryLog.Skip(historyCountBeforeCommit)
			.OfType<FactionFactsCommittedLogEntry>().Should().BeEmpty();
	}

	[Fact]
	public void BorrowedCupid_LaterNightUnknownBeneficiaryRejectsSelectionAndStaleRetryWithoutMutation()
	{
		var (session, start, actorId) = CreateLaterNightActorSession();
		IGameHookListener listener = new CupidRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var (activation, wake) = PerformSpendOpening(
			CreateActorRole(),
			listener,
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

		var selection = Advance(listener, session, wake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		session = RehydrateAtPendingInstruction(session, selection);
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
			var pendingSelection = RecoveryPayloadTestDriver.Capture(session)
				.PendingInstruction.Should()
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
		IGameHookListener listener = new CupidRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var (activation, wake) = PerformSpendOpening(
			CreateActorRole(),
			listener,
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

		var selection = Advance(listener, session, wake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var logCountBeforeCommit = session.GameHistoryLog.Count();
		session = RehydrateAtPendingInstruction(session, selection);

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

		var recovered = RecoveryPayloadTestDriver.Parse(session.Serialize())
			.RehydrateGameSession();
		var recoveredRecognition = RecoveryPayloadTestDriver.Capture(recovered)
			.PendingInstruction
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
		CompleteCadence(listener, recovered, sleep.CreateResponse());
		recovered.TransitionMainPhase(GamePhase.Dawn);
		recovered.TransitionMainPhase(GamePhase.Day);
		recovered.TransitionMainPhase(GamePhase.Night);

		IGameHookListener nextActor = CreateActorRole();
		var nextActorWake = Advance(
			nextActor,
			recovered,
			start.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
		var nextActorChoice = Advance(
			nextActor,
			recovered,
			nextActorWake.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var nextActorSleep = Advance(
			nextActor,
			recovered,
			nextActorChoice.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		CompleteCadence(
			nextActor,
			recovered,
			nextActorSleep.CreateResponse());

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

	private static (
		ActorBorrowedRolePowerActivation Activation,
		ConfirmationInstruction SourceWake) PerformSpendOpening(
		IGameHookListener actorListener,
		IGameHookListener sourceListener,
		GameSession session,
		StartGameConfirmationInstruction start,
		Guid selectedCardId)
	{
		var wake = Advance(actorListener, session, start.CreateResponse())
			.ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(actorListener, session, wake.CreateResponse())
			.ModeratorInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var sleep = Advance(
			actorListener,
			session,
			choice.CreateResponse(selectedCardId.ToString("D")))
			.ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		session.GetOrCreateListener(sourceListener.Id, () => sourceListener);
		var sourceWake = AdvanceToBorrowedRoleWake(
			actorListener,
			session,
			sleep.CreateResponse(),
			activation.ActingPlayerId);
		return (activation, sourceWake);
	}

	private static ConfirmationInstruction AdvanceToBorrowedRoleWake(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response,
		Guid actorId)
	{
		var instruction = Advance(listener, session, response)
			.ModeratorInstruction;
		for (var step = 0; step < 20; step++)
		{
			if (instruction is ConfirmationInstruction
			    {
				    Semantic: ModeratorInstructionSemantic.WakeRole
			    } wake &&
			    wake.AffectedPlayerIds?.SequenceEqual([actorId]) == true)
			{
				return wake;
			}

			instruction = Advance(
				listener,
				session,
				CreateCadenceResponse(session, instruction))
					.ModeratorInstruction;
		}

		throw new InvalidOperationException(
			"The test cadence did not reach the borrowed Role wake within 20 steps.");
	}

	private static ModeratorResponse CreateCadenceResponse(
		GameSession session,
		ModeratorInstruction? instruction) => instruction switch
		{
			SelectPlayersInstruction
			{
				Semantic:
					ModeratorInstructionSemantic
						.ObserveWerewolfFactionAgentGroup
			} selection => CreateSingleSelectionResponse(
				session,
				selection,
				"Werewolf"),
			SelectPlayersInstruction
			{
				Semantic: ModeratorInstructionSemantic.SelectWerewolfVictim
			} selection => CreateSingleSelectionResponse(
				session,
				selection,
				"Villager 3"),
			ConfirmationInstruction confirmation =>
				confirmation.CreateResponse(),
			_ => throw new InvalidOperationException(
				$"The test cadence cannot answer '{instruction?.Semantic}'.")
		};

	private static ModeratorResponse CreateSingleSelectionResponse(
		GameSession session,
		SelectPlayersInstruction instruction,
		string preferredPlayerName)
	{
		var preferredPlayerId = session.GetPlayers()
			.Where(player => player.Name == preferredPlayerName)
			.Select(player => player.Id)
			.SingleOrDefault();
		var selectedPlayerId = instruction.SelectablePlayerIds.Contains(
			preferredPlayerId)
			? preferredPlayerId
			: instruction.SelectablePlayerIds.First();
		return instruction.CreateResponse([selectedPlayerId]);
	}

	private static void CompleteCadence(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response)
	{
		var instruction = Advance(listener, session, response)
			.ModeratorInstruction;
		for (var step = 0; step < 20; step++)
		{
			if (instruction == null)
			{
				return;
			}

			instruction = Advance(
				listener,
				session,
				CreateCadenceResponse(session, instruction))
				.ModeratorInstruction;
		}

		throw new InvalidOperationException(
			"The test cadence did not complete the Night hook within 20 steps.");
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
		return (session, start, actorId);
	}

	private static (
		GameSession Session,
		Guid CupidId,
		Guid WerewolfId) CreateNativeFirstNightCupidSession()
	{
		var config = new GameSessionConfig(
			["Werewolf", "Cupid", "Villager 1", "Villager 2", "Villager 3"],
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Cupid,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var sessionId = Guid.NewGuid();
		var session = new GameSession(
			sessionId,
			new StartGameConfirmationInstruction(sessionId),
			config);
		var players = session.GetPlayers().ToArray();
		var cupidId = players.Single(player => player.Name == "Cupid").Id;
		session.AssignRole(cupidId, MainRoleType.Cupid);
		session.IdentifyRole([cupidId], MainRoleType.Cupid);
		return (
			session,
			cupidId,
			players.Single(player => player.Name == "Werewolf").Id);
	}

	private static Guid[] SelectClassificationPair(
		GameSession session,
		bool crossFaction)
	{
		var villagers = session.GetPlayers()
			.Where(player => player.Name.StartsWith(
				"Villager",
				StringComparison.Ordinal))
			.Take(2)
			.Select(player => player.Id)
			.ToArray();
		return crossFaction
			? [
				session.GetPlayers().Single(player =>
					player.Name == "Werewolf").Id,
				villagers[0]
			]
			: villagers;
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

	private static PhaseHandlerResult Advance(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response)
	{
		var consumedInstruction = session.Execution.PendingInstruction
			?? throw new InvalidOperationException(
				"The Actor borrowed Cupid test workflow requires one Pending Instruction.");
		session.GetOrCreateListener(listener.Id, () => listener);
		var result = NightActionLoop.Execute(session, response);
		if (result.ModeratorInstruction is { } nextInstruction)
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

		return result;
	}

	private static GameSession RehydrateAtPendingInstruction(
		GameSession session,
		ModeratorInstruction instruction) =>
		RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(instruction)
			.RehydrateGameSession();

	private sealed class RecordingPolicy : IRolePowerAvailabilityPolicy
	{
		internal List<RolePowerAttempt> ObservedAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			ObservedAttempts.Add(attempt);
			return RolePowerAvailabilityResult.Allowed;
		}
	}

}
