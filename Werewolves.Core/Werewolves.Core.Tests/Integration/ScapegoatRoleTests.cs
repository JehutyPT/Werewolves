using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class ScapegoatRoleTests : DiagnosticTestBase
{
	private static readonly JsonSerializerOptions RecoverySerializationOptions =
		new()
		{
			Converters =
			{
				new GameResultConverter(),
				new GameLogEntryConverter(),
				new ModeratorInstructionConverter(),
				new JsonStringEnumConverter()
			}
		};

	public ScapegoatRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void KnownScapegoat_TiedVote_RevealsAndStartsSacrificeCascadeBeforeVoterChoice()
	{
		var policy = new RecordingAvailabilityPolicy(
			RolePowerAvailabilityResult.Allowed);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var scapegoat = players[1];
		var dawnVictim = players[4];
		builder.ArrangeKnownPhysicalRole(scapegoat.Id, MainRoleType.Scapegoat);
		builder.CompleteNightPhase([werewolf.Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));

		var afterTie = builder.Process(vote.CreateResponse([]));

		var reveal = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			afterTie);
		reveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealScapegoatForTie);
		reveal.AffectedPlayerIds.Should().Equal(scapegoat.Id);
		var beforeReveal = builder.GetGameState()!;
		beforeReveal.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
			.Where(entry => entry.CurrentPhase == GamePhase.Day)
			.Should().ContainSingle()
			.Which.ReportedOutcomePlayerId.Should().Be(Guid.Empty);
		beforeReveal.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry =>
				entry.Reason == EliminationReason.ScapegoatSacrifice);

		var afterReveal = builder.Process(reveal.CreateResponse());

		var voterChoice =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				afterReveal);
		voterChoice.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectScapegoatPermittedVoters);
		voterChoice.CountConstraint.Should().Be(
			NumberRangeConstraint.AtLeast(1));
		voterChoice.SelectablePlayerIds.Should().BeEquivalentTo(
			players
				.Where(player =>
					player.Id != scapegoat.Id &&
					player.Id != dawnVictim.Id)
				.Select(player => player.Id));
		var state = builder.GetGameState()!;
		state.GetPlayerState(scapegoat.Id).Health.Should().Be(PlayerHealth.Dead);
		state.GetPlayerState(scapegoat.Id).PubliclyRevealedRole.Should()
			.Be(MainRoleType.Scapegoat);
		state.GameHistoryLog.OfType<ScapegoatTieReplacementLogEntry>()
			.Should().ContainSingle()
			.Which.ScapegoatPlayerId.Should().Be(scapegoat.Id);
		state.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Where(entry =>
				entry.PlayerId == scapegoat.Id &&
				entry.Reason == EliminationReason.ScapegoatSacrifice)
			.Should().ContainSingle();
		state.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
			.Where(entry => entry.CurrentPhase == GamePhase.Day)
			.Should().ContainSingle();
		policy.ObservedAttempts.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void PermittedVoterChoice_CommitsFixedSnapshotAndWaitsForPublicAnnouncement()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var scapegoat = players[1];
		var dawnVictim = players[4];
		builder.ArrangeKnownPhysicalRole(scapegoat.Id, MainRoleType.Scapegoat);
		builder.CompleteNightPhase([players[0].Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		var reveal =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(vote.CreateResponse([])));
		var voterChoice =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(reveal.CreateResponse()));
		var selected = voterChoice.SelectablePlayerIds.Take(2).ToHashSet();

		var afterSelection = builder.Process(
			voterChoice.CreateResponse(selected));

		var announcement =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterSelection);
		announcement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters);
		announcement.AffectedPlayerIds.Should().BeEquivalentTo(selected);
		var state = builder.GetGameState()!;
		var restriction = state.GameHistoryLog
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.Should().ContainSingle().Which;
		restriction.CandidatePlayerIds.Should().BeEquivalentTo(
			voterChoice.SelectablePlayerIds);
		restriction.PermittedVoterIds.Should().BeEquivalentTo(selected);
		Action mutateCandidates = () =>
			((IList<Guid>)restriction.CandidatePlayerIds).Clear();
		Action mutatePermittedVoters = () =>
			((IList<Guid>)restriction.PermittedVoterIds).Clear();
		mutateCandidates.Should().Throw<NotSupportedException>();
		mutatePermittedVoters.Should().Throw<NotSupportedException>();
		restriction.CandidatePlayerIds.Should().BeEquivalentTo(
			voterChoice.SelectablePlayerIds);
		restriction.PermittedVoterIds.Should().BeEquivalentTo(selected);
		restriction.AppliesOnTurnNumber.Should().Be(state.TurnNumber + 1);
		restriction.AnnouncementInstructionId.Should()
			.Be(announcement.InstructionId);
		state.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().NotContain(entry => entry.ScopeId == restriction.ScopeId);

		builder.Process(announcement.CreateResponse()).IsSuccess.Should().BeTrue();

		var completed = builder.GetGameState()!;
		completed.GameHistoryLog
			.OfType<VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry>()
			.Should().ContainSingle()
			.Which.AnnouncementInstructionId.Should()
			.Be(announcement.InstructionId);
		completed.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().ContainSingle(entry => entry.ScopeId == restriction.ScopeId);
		MarkTestCompleted();
	}

	[Fact]
	public void InvalidPermittedVoterSelection_IsSideEffectFree()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var scapegoat = players[1];
		var dawnVictim = players[4];
		builder.ArrangeKnownPhysicalRole(scapegoat.Id, MainRoleType.Scapegoat);
		builder.CompleteNightPhase([players[0].Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		var reveal =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(vote.CreateResponse([])));
		var choice =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(reveal.CreateResponse()));
		var before = builder.SerializeSession();
		var invalid = new ModeratorResponse
		{
			InstructionId = choice.InstructionId,
			Type = ExpectedInputType.PlayerSelection,
			SelectedPlayerIds = new HashSet<Guid> { scapegoat.Id }
		};

		var act = () => builder.Process(invalid);

		act.Should().Throw<InvalidOperationException>();
		builder.SerializeSession().Should().Be(before);
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			choice.InstructionId);
		builder.GetGameState()!.GameHistoryLog
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void VoterChoiceAndAnnouncement_PrecedeForcedReactionAndRecoveryResumesIt()
	{
		var reaction = new ScapegoatTriggeredReaction();
		var builder = CreateBuilder()
			.WithEliminationCascadeReaction(reaction)
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var scapegoat = players[1];
		var reactionVictim = players[2];
		var dawnVictim = players[5];
		reaction.Configure(scapegoat.Id, reactionVictim.Id);
		builder.ArrangeKnownPhysicalRole(scapegoat.Id, MainRoleType.Scapegoat);
		builder.CompleteNightPhase([players[0].Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		var reveal =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(vote.CreateResponse([])));
		var choice =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(reveal.CreateResponse()));

		choice.SelectablePlayerIds.Should().Contain(reactionVictim.Id);
		reaction.InvocationCount.Should().Be(0);
		var announcement =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(choice.CreateResponse([reactionVictim.Id])));

		reaction.InvocationCount.Should().Be(0);
		builder.GetGameState()!.GetPlayerState(reactionVictim.Id).Health
			.Should().Be(PlayerHealth.Alive);
		var reactionReveal =
			InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
				builder.Process(announcement.CreateResponse()));

		reaction.InvocationCount.Should().Be(1);
		reactionReveal.SelectableRolesForPlayers.Keys.Should().Equal(reactionVictim.Id);
		var interrupted = builder.GetGameState()!;
		var restriction = interrupted.GameHistoryLog
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.Should().ContainSingle().Which;
		restriction.PermittedVoterIds.Should().Equal(reactionVictim.Id);
		var recoveredReaction = new ScapegoatTriggeredReaction();
		recoveredReaction.Configure(scapegoat.Id, reactionVictim.Id);
		var service = new GameService(
			AllowAllRolePowerAvailabilityPolicy.Instance,
			[
				new EliminationCascadeReactionBinding(
					recoveredReaction,
					EliminationCascadeReactionBoundary.Forced)
			]);
		var gameId = service.RehydrateSession(builder.SerializeSession());
		var recoveredReveal = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<AssignRolesInstruction>().Subject;

		recoveredReveal.InstructionId.Should().Be(reactionReveal.InstructionId);
		service.ProcessInstruction(
			gameId,
			recoveredReveal.CreateObservedRoleResponse(new Dictionary<Guid, MainRoleType>
			{
				[reactionVictim.Id] = MainRoleType.SimpleVillager
			}));

		var recovered = service.GetGameStateView(gameId)!;
		recovered.GetPlayerState(reactionVictim.Id).Health.Should().Be(
			PlayerHealth.Dead);
		recovered.GameHistoryLog
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.Should().ContainSingle()
			.Which.PermittedVoterIds.Should().Equal(reactionVictim.Id);
		recovered.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == reactionVictim.Id &&
				entry.Reason == EliminationReason.EventElimination);
		MarkTestCompleted();
	}

	[Fact]
	public void UnknownScapegoat_TiedVote_UsesOptionalPublicHolderObservation()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var scapegoat = players[1];
		var dawnVictim = players[4];
		builder.CompleteNightPhase([players[0].Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));

		var afterTie = builder.Process(vote.CreateResponse([]));

		var observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				afterTie);
		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveScapegoatHolderForTie);
		observation.CountConstraint.Should().Be(
			NumberRangeConstraint.SingleOptional);
		observation.SelectablePlayerIds.Should().Contain(scapegoat.Id);

		var afterObservation = builder.Process(
			observation.CreateResponse([scapegoat.Id]));

		InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				afterObservation)
			.Semantic.Should().Be(
				ModeratorInstructionSemantic.SelectScapegoatPermittedVoters);
		var state = builder.GetGameState()!;
		var scapegoatState = state.GetPlayerState(scapegoat.Id);
		scapegoatState.CurrentRole.Should().Be(MainRoleType.Scapegoat);
		scapegoatState.ModeratorKnownRole.Should().Be(MainRoleType.Scapegoat);
		scapegoatState.PubliclyRevealedRole.Should().Be(MainRoleType.Scapegoat);
		scapegoatState.PhysicalCharacterCardRole.Should().BeNull();
		state.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.Scapegoat &&
				entry.PlayerIds.SetEquals(new[] { scapegoat.Id }));
		state.GameHistoryLog.OfType<ScapegoatTieReplacementLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void UnknownScapegoat_EmptyObservation_LeavesOrdinaryTieUnchanged()
	{
		var policy = new RecordingAvailabilityPolicy(
			RolePowerAvailabilityResult.Allowed);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var dawnVictim = players[4];
		builder.CompleteNightPhase([players[0].Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		var observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(vote.CreateResponse([])));

		var afterObservation = builder.Process(
			observation.CreateResponse([]));

		afterObservation.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		policy.ObservedAttempts.Should().BeEmpty();
		var state = builder.GetGameState()!;
		state.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
			.Should().ContainSingle()
			.Which.ReportedOutcomePlayerId.Should().Be(Guid.Empty);
		state.GameHistoryLog.OfType<ScapegoatTieReplacementLogEntry>()
			.Should().BeEmpty();
		state.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry =>
				entry.Reason == EliminationReason.ScapegoatSacrifice);
		MarkTestCompleted();
	}

	[Fact]
	public void UnknownScapegoat_UnavailableReveal_IsRejectedWithoutMutation()
	{
		var policy = new RecordingAvailabilityPolicy(
			RolePowerAvailabilityResult.Denied);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var scapegoat = players[1];
		var dawnVictim = players[4];
		builder.CompleteNightPhase([players[0].Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		var observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(vote.CreateResponse([])));
		var before = builder.SerializeSession();

		var act = () => builder.Process(
			observation.CreateResponse([scapegoat.Id]));

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*unavailable*");
		builder.SerializeSession().Should().Be(before);
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			observation.InstructionId);
		policy.ObservedAttempts.Should().ContainSingle();
		var attempt = policy.ObservedAttempts.Single();
		attempt.ActingPlayer.Id.Should().Be(scapegoat.Id);
		attempt.SourceRole.Should().Be(MainRoleType.Scapegoat);
		attempt.SourcePower.Identifier.Should().Be(
			new RolePowerIdentifier("scapegoat-tie-replacement"));
		attempt.PowerInstance.Origin.Should().Be(
			RolePowerInstanceOrigin.Native);
		attempt.OneUseResource.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void KnownScapegoat_UnavailablePower_LeavesOrdinaryTieWithoutPrompt()
	{
		var policy = new RecordingAvailabilityPolicy(
			RolePowerAvailabilityResult.Denied);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var scapegoat = players[1];
		var dawnVictim = players[4];
		builder.ArrangeKnownPhysicalRole(scapegoat.Id, MainRoleType.Scapegoat);
		builder.CompleteNightPhase([players[0].Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));

		var afterTie = builder.Process(vote.CreateResponse([]));

		afterTie.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		policy.ObservedAttempts.Should().ContainSingle();
		var state = builder.GetGameState()!;
		state.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
			.Should().ContainSingle()
			.Which.ReportedOutcomePlayerId.Should().Be(Guid.Empty);
		state.GameHistoryLog.OfType<ScapegoatTieReplacementLogEntry>()
			.Should().BeEmpty();
		state.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry =>
				entry.Reason == EliminationReason.ScapegoatSacrifice);
		state.GameHistoryLog
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void RecoveryBeforeAndAfterVoterChoice_PreservesExactPendingInteraction()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var scapegoat = players[1];
		var dawnVictim = players[4];
		builder.ArrangeKnownPhysicalRole(scapegoat.Id, MainRoleType.Scapegoat);
		builder.CompleteNightPhase([players[0].Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		var reveal =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(vote.CreateResponse([])));
		var voterChoice =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(reveal.CreateResponse()));
		var selected = voterChoice.SelectablePlayerIds.Take(2).ToHashSet();
		var beforeChoiceService = new GameService();
		var gameId = beforeChoiceService.RehydrateSession(
			builder.SerializeSession());
		var restoredChoice = beforeChoiceService.GetCurrentInstruction(gameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		restoredChoice.InstructionId.Should().Be(voterChoice.InstructionId);

		var afterChoice = beforeChoiceService.ProcessInstruction(
			gameId,
			restoredChoice.CreateResponse(selected));

		var announcement = afterChoice.ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		announcement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters);
		var afterChoiceState = beforeChoiceService.GetGameStateView(gameId)!;
		afterChoiceState.GameHistoryLog
			.OfType<ScapegoatTieReplacementLogEntry>()
			.Should().ContainSingle();
		afterChoiceState.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Reason == EliminationReason.ScapegoatSacrifice);
		afterChoiceState.GameHistoryLog
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.Should().ContainSingle();

		var afterChoiceService = new GameService();
		var restoredGameId = afterChoiceService.RehydrateSession(
			beforeChoiceService.SerializeSession(gameId));
		var restoredAnnouncement =
			afterChoiceService.GetCurrentInstruction(restoredGameId)
				.Should().BeOfType<ConfirmationInstruction>().Subject;
		restoredAnnouncement.InstructionId.Should()
			.Be(announcement.InstructionId);
		restoredAnnouncement.AffectedPlayerIds.Should()
			.BeEquivalentTo(selected);

		afterChoiceService.ProcessInstruction(
			restoredGameId,
			restoredAnnouncement.CreateResponse());

		var completed = afterChoiceService.GetGameStateView(restoredGameId)!;
		completed.GameHistoryLog
			.OfType<ScapegoatTieReplacementLogEntry>()
			.Should().ContainSingle();
		completed.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Reason == EliminationReason.ScapegoatSacrifice);
		completed.GameHistoryLog
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.Should().ContainSingle();
		completed.GameHistoryLog
			.OfType<
				VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void Recovery_WithStructurallyInvalidVoterRestriction_IsRejected()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var scapegoat = players[1];
		var dawnVictim = players[4];
		builder.ArrangeKnownPhysicalRole(scapegoat.Id, MainRoleType.Scapegoat);
		builder.CompleteNightPhase([players[0].Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		var reveal =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(vote.CreateResponse([])));
		var voterChoice =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(reveal.CreateResponse()));
		builder.Process(voterChoice.CreateResponse(
			[voterChoice.SelectablePlayerIds.First()]));
		var payload = RecoveryPayloadTestDriver.Parse(
			builder.SerializeSession());
		var malformed = payload
			.InvalidateLatestVoterEligibilityRestrictionTurn()
			.Serialize();

		var act = () => new GameService().RehydrateSession(malformed);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage(
				"*voter-eligibility restriction*structurally invalid*");
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Recovery_NativeRestrictionLoggedAfterAcknowledgmentOrExpiry_IsRejected(
		bool moveAfterExpiry)
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = (GameSession)builder.GetGameState()!;
		session.TransitionMainPhase(GamePhase.Day);
		var candidatePlayerIds = session.GetPlayers()
			.Select(player => player.Id)
			.ToArray();
		const string scopeId = "chronology-native-restriction";
		var announcementInstructionId = Guid.Parse(
			"00000000-0000-0000-0000-000000000143");
		DayVoteRules.CommitVoterEligibilityRestriction(
			session,
			scopeId,
			MainRoleType.Scapegoat,
			candidatePlayerIds,
			[candidatePlayerIds[0]],
			session.TurnNumber + 1,
			announcementInstructionId);
		DayVoteRules.AcknowledgeVoterEligibilityRestrictionAnnouncement(
			session,
			scopeId,
			announcementInstructionId);
		if (moveAfterExpiry)
		{
			session.TransitionMainPhase(GamePhase.Night);
			session.TransitionMainPhase(GamePhase.Day);
			DayVoteRules.ExpireActiveVoterEligibilityRestriction(session);
		}

		var malformed = MoveNativeRestrictionAfterEvent(
			RecoveryPayloadTestDriver.Capture(session).Serialize(),
			moveAfterExpiry);

		var act = () => new GameService().RehydrateSession(malformed);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage(
				"*voter-eligibility restriction acknowledgment*stale*");
		MarkTestCompleted();
	}

	[Fact]
	public void Recovery_BorrowedRestrictionOpaqueMarkerAfterAcknowledgment_IsRejected()
	{
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedScapegoatPendingSnapshot(
				ActorBorrowedScapegoatRecoveryStep.PermittedVoterSelection,
				new DiagnosticStateObserver());
		var fixtureService = new GameService();
		var fixtureGameId = fixtureService.RehydrateSession(
			snapshot.SerializedSession);
		var session = fixtureService.GetGameStateView(fixtureGameId)
			.Should().BeOfType<GameSession>().Subject;
		var tieReplacement = session
			.GetActorBorrowedScapegoatTieReplacementCommits()
			.Should().ContainSingle().Subject;
		var candidatePlayerIds = session.GetPlayers()
			.Where(player => player.State.Health == PlayerHealth.Alive)
			.Select(player => player.Id)
			.ToArray();
		var announcementInstructionId = Guid.Parse(
			"00000000-0000-0000-0000-000000000144");
		var acknowledgmentLogIndex = session.GameHistoryLog.Count();
		DayVoteRules.AcknowledgeVoterEligibilityRestrictionAnnouncement(
			session,
			tieReplacement.CascadeScopeId,
			announcementInstructionId);
		session.CommitActorBorrowedScapegoatVoterRestriction(
			tieReplacement.PowerIdentity,
			tieReplacement.PublicMarkerLogIndex,
			tieReplacement.CascadeScopeId,
			candidatePlayerIds,
			[candidatePlayerIds[0]],
			session.TurnNumber + 1,
			announcementInstructionId);
		var restriction = session
			.GetActorBorrowedScapegoatVoterRestrictionCommits()
			.Should().ContainSingle().Subject;
		restriction.PublicMarkerLogIndex.Should()
			.BeGreaterThan(acknowledgmentLogIndex);
		session.GameHistoryLog.ElementAt(restriction.PublicMarkerLogIndex)
			.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		var serialized = RecoveryPayloadTestDriver.Capture(session).Serialize();

		var act = () => new GameService().RehydrateSession(serialized);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage(
				"*voter-eligibility restriction acknowledgment*stale*");
		MarkTestCompleted();
	}

	[Fact]
	public void SameDayConsecutiveVote_IgnoresFollowingDayRestriction()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var judge = players[0];
		var werewolf = players[1];
		var scapegoat = players[2];
		var unselectedTarget = players[3];
		var dawnVictim = players[6];
		builder.ArrangeKnownRole(judge.Id, MainRoleType.StutteringJudge);
		builder.ArrangeKnownPhysicalRole(scapegoat.Id, MainRoleType.Scapegoat);
		builder.ConfirmGameStart();
		var judgeWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var judgeSetup =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(judgeWake.CreateResponse()));
		builder.Process(judgeSetup.CreateResponse());
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					dawnVictim.Id));
		builder.Process(finishNight.CreateResponse());
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var conductVote =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(debate.CreateResponse()));
		var signal =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				builder.Process(conductVote.CreateResponse()));
		var vote =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(signal.CreateResponse(
					StutteringJudgeSignalOptionIds.Occurred)));
		var reveal =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(vote.CreateResponse([])));
		var choice =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(reveal.CreateResponse()));
		var announcement =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(choice.CreateResponse([werewolf.Id])));

		var consecutiveVote =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(announcement.CreateResponse()));

		consecutiveVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		consecutiveVote.PrivateInstruction.Should().Be(
			GameStrings.VoteStartsModeratorInstruction);
		consecutiveVote.SelectablePlayerIds.Should().Contain(
			[werewolf.Id, judge.Id, unselectedTarget.Id]);
		var voteTurn = builder.GetGameState()!.TurnNumber;
		builder.Process(consecutiveVote.CreateResponse([]));
		var state = builder.GetGameState()!;
		state.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
			.Where(entry =>
				entry.TurnNumber == voteTurn &&
				entry.CurrentPhase == GamePhase.Day)
			.Should().HaveCount(2)
			.And.OnlyContain(entry =>
				entry.ReportedOutcomePlayerId == Guid.Empty);
		state.GameHistoryLog
			.OfType<OneUseRolePowerDayActionCommittedLogEntry>()
			.Should().ContainSingle();
		state.GameHistoryLog.OfType<ScapegoatTieReplacementLogEntry>()
			.Should().ContainSingle();
		state.GameHistoryLog
			.OfType<VoterEligibilityRestrictionExpiredLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void FollowingDay_IntersectsAllowlistWithVotingRight_ButKeepsEveryLivingTarget()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var scapegoat = players[1];
		var permittedWithoutVotingRight = players[2];
		var unselectedTarget = players[3];
		var secondNightVictim = players[5];
		var firstDawnVictim = players[6];
		builder.ArrangeKnownPhysicalRole(scapegoat.Id, MainRoleType.Scapegoat);
		builder.ArrangeVotingRight(
			permittedWithoutVotingRight.Id,
			hasVotingRight: false);
		builder.CompleteNightPhase([werewolf.Id], firstDawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[firstDawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		var reveal =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(vote.CreateResponse([])));
		var choice =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(reveal.CreateResponse()));
		var announcement =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(choice.CreateResponse(
				[
					werewolf.Id,
					permittedWithoutVotingRight.Id
				])));
		builder.Process(announcement.CreateResponse());
		CompleteSubsequentNight(builder, secondNightVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[secondNightVictim.Id] = MainRoleType.SimpleVillager
		});
		builder.ArrangeCurrentRole(werewolf.Id, MainRoleType.Seer);
		var followingDayDebate =
			InstructionAssert.ExpectType<ConfirmationInstruction>(
				builder.GetCurrentInstruction());

		var followingDayVote =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(followingDayDebate.CreateResponse()));

		followingDayVote.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDayVote);
		followingDayVote.PrivateInstruction.Should().Contain(werewolf.Name);
		followingDayVote.PrivateInstruction.Should()
			.NotContain(permittedWithoutVotingRight.Name);
		followingDayVote.SelectablePlayerIds.Should().Contain(
			[
				werewolf.Id,
				permittedWithoutVotingRight.Id,
				unselectedTarget.Id
			]);

		builder.Process(followingDayVote.CreateResponse([]));

		var state = builder.GetGameState()!;
		state.GameHistoryLog
			.OfType<VoterEligibilityRestrictionExpiredLogEntry>()
			.Should().ContainSingle();
		state.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.TurnNumber == 2 &&
				entry.ReportedOutcomePlayerId == Guid.Empty);
		MarkTestCompleted();
	}

	[Fact]
	public void FollowingDay_WithNoEffectiveVoter_SkipsVoteAndExpiresRestriction()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var scapegoat = players[1];
		var permittedWithoutVotingRight = players[2];
		var secondNightVictim = players[5];
		var firstDawnVictim = players[6];
		builder.ArrangeKnownPhysicalRole(scapegoat.Id, MainRoleType.Scapegoat);
		builder.ArrangeVotingRight(
			permittedWithoutVotingRight.Id,
			hasVotingRight: false);
		builder.CompleteNightPhase([players[0].Id], firstDawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[firstDawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		var reveal =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(vote.CreateResponse([])));
		var choice =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(reveal.CreateResponse()));
		var announcement =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(choice.CreateResponse(
					[permittedWithoutVotingRight.Id])));
		builder.Process(announcement.CreateResponse());
		CompleteSubsequentNight(builder, secondNightVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[secondNightVictim.Id] = MainRoleType.SimpleVillager
		});
		var followingDayDebate =
			InstructionAssert.ExpectType<ConfirmationInstruction>(
				builder.GetCurrentInstruction());

		var afterDebate = builder.Process(
			followingDayDebate.CreateResponse());

		afterDebate.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		var state = builder.GetGameState()!;
		state.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
			.Should().NotContain(entry => entry.TurnNumber == 2);
		state.GameHistoryLog
			.OfType<VoterEligibilityRestrictionExpiredLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void PendingHolderObservation_WithForgedSelectableRoster_IsRejectedBeforeAUsableSession()
	{
		var builder = ArrangePendingHolderObservation(out var observation);

		var forged = RecoveryPayloadTestDriver
			.Capture((GameSession)builder.GetGameState()!)
			.RewritePendingPlayerSelectionSelectablePlayerIds(
				observation.SelectablePlayerIds.Take(1))
			.Serialize();
		var act = () => new GameService().RehydrateSession(forged);

		act.Should().Throw<InvalidOperationException>();
		MarkTestCompleted();
	}

	[Fact]
	public void PendingHolderObservation_WithForgedCountConstraint_IsRejectedBeforeAUsableSession()
	{
		var builder = ArrangePendingHolderObservation(out _);

		var forged = RecoveryPayloadTestDriver
			.Capture((GameSession)builder.GetGameState()!)
			.RewritePendingPlayerSelectionCountConstraint(
				NumberRangeConstraint.Exact(1))
			.Serialize();
		var act = () => new GameService().RehydrateSession(forged);

		act.Should().Throw<InvalidOperationException>();
		MarkTestCompleted();
	}

	[Fact]
	public void PendingTieReveal_WithForgedRevealedHolder_IsRejectedBeforeAUsableSession()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var scapegoat = players[1];
		var dawnVictim = players[4];
		builder.ArrangeKnownPhysicalRole(scapegoat.Id, MainRoleType.Scapegoat);
		builder.CompleteNightPhase([players[0].Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		var reveal =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(vote.CreateResponse([])));
		reveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealScapegoatForTie);

		var forged = RecoveryPayloadTestDriver
			.Capture((GameSession)builder.GetGameState()!)
			.RewritePendingConfirmationAffectedPlayer(players[2].Id)
			.Serialize();
		var act = () => new GameService().RehydrateSession(forged);

		act.Should().Throw<InvalidOperationException>();
		MarkTestCompleted();
	}

	private GameTestBuilder ArrangePendingHolderObservation(
		out SelectPlayersInstruction observation)
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var dawnVictim = players[4];
		builder.CompleteNightPhase([players[0].Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var vote = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(debate.CreateResponse()));
		observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(vote.CreateResponse([])));
		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveScapegoatHolderForTie);
		return builder;
	}

	private static void CompleteSubsequentNight(
		GameTestBuilder builder,
		Guid victimId)
	{
		builder.ConfirmNightStart();
		var afterWerewolves =
			builder.CompleteWerewolfNightActionSubsequentNight(victimId);
		var nightEnd =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterWerewolves,
				CoreTestReferences.InstructionContexts.NightEndConfirmation);
		builder.Process(nightEnd.CreateResponse());
	}

	private static string MoveNativeRestrictionAfterEvent(
		string serializedSession,
		bool moveAfterExpiry)
	{
		var payload = JsonSerializer.Deserialize<GameSessionDto>(
			serializedSession,
			RecoverySerializationOptions)
			?? throw new InvalidOperationException(
				"The recovery test payload could not be deserialized.");
		var restrictionIndex = payload.GameHistoryLog.FindIndex(
			entry => entry is VoterEligibilityRestrictionCommittedLogEntry);
		var eventIndex = payload.GameHistoryLog.FindLastIndex(entry =>
			moveAfterExpiry
				? entry is VoterEligibilityRestrictionExpiredLogEntry
				: entry is
					VoterEligibilityRestrictionAnnouncementAcknowledgedLogEntry);
		if (restrictionIndex < 0 || eventIndex <= restrictionIndex)
		{
			throw new InvalidOperationException(
				"The recovery test payload lacks the expected voter-restriction chronology.");
		}

		var restriction = payload.GameHistoryLog[restrictionIndex];
		payload.GameHistoryLog.RemoveAt(restrictionIndex);
		eventIndex--;
		payload.GameHistoryLog.Insert(eventIndex + 1, restriction);
		return JsonSerializer.Serialize(payload, RecoverySerializationOptions);
	}

	private sealed class RecordingAvailabilityPolicy(
		RolePowerAvailabilityResult result)
		: IRolePowerAvailabilityPolicy
	{
		internal List<RolePowerAttempt> ObservedAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			ObservedAttempts.Add(attempt);
			return result;
		}
	}

	private sealed class ScapegoatTriggeredReaction
		: IEliminationCascadeReaction
	{
		private Guid _scapegoatId;
		private Guid _victimId;

		public string ReactionId => nameof(ScapegoatTriggeredReaction);
		internal int InvocationCount { get; private set; }

		internal void Configure(Guid scapegoatId, Guid victimId)
		{
			_scapegoatId = scapegoatId;
			_victimId = victimId;
		}

		public EliminationCascadeReactionResult Advance(
			GameSession session,
			IReadOnlyCollection<Guid> eliminatedPlayerIds,
			ModeratorResponse input)
		{
			if (!eliminatedPlayerIds.Contains(_scapegoatId))
			{
				return EliminationCascadeReactionResult.Complete();
			}

			InvocationCount++;
			return EliminationCascadeReactionResult.Complete(
			[
				new EliminationRequest(
					_victimId,
					EliminationReason.EventElimination)
			]);
		}
	}
}
