using FluentAssertions;
using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class DevotedServantRoleTests
{
	[Fact]
	public void NonTiedVote_WithUnknownDealPoolHolder_OpensPublicWindowBeforeRoleReveal()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.DevotedServant,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var dawnVictim = players[1];
		var voteTarget = players[2];

		builder.CompleteNightPhase([werewolf.Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new()
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		var staleVoteResponse = vote.CreateResponse([voteTarget.Id]);
		var window = builder.Process(staleVoteResponse)
			.ModeratorInstruction.Should()
			.BeOfType<DevotedServantVoteWindowInstruction>().Subject;
		var expectedSelectablePlayerIds = players
			.Where(player =>
				player.State.Health == PlayerHealth.Alive &&
				player.Id != voteTarget.Id)
			.Select(player => player.Id)
			.ToArray();

		window.Semantic.Should().Be(
			ModeratorInstructionSemantic.ResolveDevotedServantVoteWindow);
		window.VoteTargetId.Should().Be(voteTarget.Id);
		window.SelectablePlayerIds.Should().BeEquivalentTo(
			expectedSelectablePlayerIds);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RoleRevealLogEntry>()
			.Should().NotContain(entry =>
				entry.RevealedRoles.ContainsKey(voteTarget.Id));
		voteTarget.State.Health.Should().Be(PlayerHealth.Alive);

		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredWindow = recoveredService
			.GetCurrentInstruction(recoveredId)
			.Should().BeOfType<DevotedServantVoteWindowInstruction>().Subject;
		recoveredWindow.InstructionId.Should().Be(window.InstructionId);
		recoveredWindow.Semantic.Should().Be(window.Semantic);
		recoveredWindow.VoteTargetId.Should().Be(window.VoteTargetId);
		recoveredWindow.SelectablePlayerIds.Should().BeEquivalentTo(
			window.SelectablePlayerIds);
		var continueResponse = recoveredWindow.CreateContinueResponse();
		continueResponse.InstructionId.Should().Be(window.InstructionId);
		continueResponse.Type.Should().Be(ExpectedInputType.Continue);
		var revealingPlayerId = expectedSelectablePlayerIds[0];
		var revealResponse = recoveredWindow.CreatePublicSelfRevealResponse(
			revealingPlayerId);
		revealResponse.InstructionId.Should().Be(window.InstructionId);
		revealResponse.Type.Should().Be(ExpectedInputType.PlayerSelection);
		revealResponse.SelectedPlayerIds.Should().Equal(revealingPlayerId);
		var stale = () => recoveredService.ProcessInstruction(
			recoveredId,
			staleVoteResponse);
		stale.Should().Throw<InvalidOperationException>()
			.WithMessage("*pending Moderator Instruction*");
		recoveredService.GetCurrentInstruction(recoveredId)!.InstructionId
			.Should().Be(window.InstructionId);
	}

	[Fact]
	public void PublicSelfReveal_CommitsAtomicIdentityAndSpend_ThenRecoversPrivateCardRecord()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.DevotedServant,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var dawnVictim = players[1];
		var voteTarget = players[2];
		var servant = players[3];
		builder.CompleteNightPhase([werewolf.Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new()
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var window = builder.Process(vote.CreateResponse([voteTarget.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<DevotedServantVoteWindowInstruction>().Subject;

		var acquiredCard = builder.Process(
				window.CreatePublicSelfRevealResponse(servant.Id))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		acquiredCard.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDevotedServantAcquiredCard);
		acquiredCard.PlayersForAssignment.Should().Equal(voteTarget.Id);
		acquiredCard.AffectedPlayerIds.Should().Equal(
			servant.Id,
			voteTarget.Id);
		servant.State.CurrentRole.Should().Be(MainRoleType.DevotedServant);
		servant.State.ModeratorKnownRole.Should().Be(
			MainRoleType.DevotedServant);
		servant.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.DevotedServant);
		servant.State.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.DevotedServant);
		voteTarget.State.Health.Should().Be(PlayerHealth.Alive);
		voteTarget.State.PubliclyRevealedRole.Should().BeNull();
		builder.GetGameState()!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry =>
				entry.Role == MainRoleType.DevotedServant);
		builder.GetGameState()!.GameHistoryLog
			.OfType<DevotedServantPublicSelfRevealCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActingPlayerId == servant.Id &&
				entry.VoteTargetId == voteTarget.Id);

		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredInstruction = recoveredService
			.GetCurrentInstruction(recoveredId)
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		recoveredInstruction.InstructionId.Should().Be(
			acquiredCard.InstructionId);
		recoveredInstruction.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecordDevotedServantAcquiredCard);
		recoveredService.GetGameStateView(recoveredId)!.GameHistoryLog
			.OfType<DevotedServantPublicSelfRevealCommittedLogEntry>()
			.Should().ContainSingle();
	}

	[Fact]
	public void KnownRoleWithoutBoundCard_AcquiredCardInstructionOffersOnlyKnownRole()
	{
		var (_, target, _, acquiredCard) =
			CreateKnownRoleTakeScenario(MainRoleType.Seer);

		target.State.PhysicalCharacterCardId.Should().BeNull();
		acquiredCard.PlayersForAssignment.Should().Equal(target.Id);
		acquiredCard.RolesForAssignment.Should().ContainSingle()
			.Which.Should().Be(MainRoleType.Seer);
	}

	[Fact]
	public void ConflictingTakeAgainstKnownRoleWithoutBoundCard_IsRejectedWithoutMutation()
	{
		var (builder, target, servant, _) =
			CreateKnownRoleTakeScenario(MainRoleType.Seer);
		var session = (GameSession)builder.GetGameState()!;
		var servantBefore = CapturePlayerState(servant);
		var targetBefore = CapturePlayerState(target);
		var historyBefore = session.GameHistoryLog.ToArray();
		var cardsBefore = session.GetModeratorPhysicalCharacterCards().ToArray();
		var conflictingRequest =
			PermanentRoleSwapRules.CreateDevotedServantRoleTakeRequest(
				session,
				servant.Id,
				target.Id,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);

		session.TryCommitDevotedServantRoleTake(conflictingRequest)
			.Should().BeFalse();

		CapturePlayerState(servant).Should().BeEquivalentTo(servantBefore);
		CapturePlayerState(target).Should().BeEquivalentTo(targetBefore);
		session.GameHistoryLog.Should().Equal(historyBefore);
		session.GetModeratorPhysicalCharacterCards().Should().Equal(cardsBefore);
		session.GameHistoryLog
			.OfType<DevotedServantRoleTakenCommittedLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void AcquiredCardRecord_TransfersPrivatelyAndEliminatesTargetWithoutItsRoleBehavior()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.DevotedServant,
				MainRoleType.VillageIdiot,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var dawnVictim = players[1];
		var voteTarget = players[2];
		var servant = players[3];
		builder.ArrangeKnownPhysicalRole(
			voteTarget.Id,
			MainRoleType.VillageIdiot);
		var targetCardId = voteTarget.State.PhysicalCharacterCardId!.Value;
		builder.ConfirmGameStart();
		builder.CompleteNightPhase([werewolf.Id], dawnVictim.Id);
		builder.CompleteDawnPhase(new()
		{
			[dawnVictim.Id] = MainRoleType.SimpleVillager
		});
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var window = builder.Process(vote.CreateResponse([voteTarget.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<DevotedServantVoteWindowInstruction>().Subject;
		var acquiredCard = builder.Process(
				window.CreatePublicSelfRevealResponse(servant.Id))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var servantCardId = servant.State.PhysicalCharacterCardId!.Value;

		var announcement = builder.Process(acquiredCard.CreateResponse(new()
			{
				[voteTarget.Id] = MainRoleType.VillageIdiot
			}))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		announcement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		voteTarget.State.Health.Should().Be(PlayerHealth.Dead);
		voteTarget.State.CurrentRole.Should().BeNull();
		voteTarget.State.PhysicalCharacterCardId.Should().BeNull();
		servant.State.CurrentRole.Should().Be(MainRoleType.VillageIdiot);
		servant.State.ModeratorKnownRole.Should().Be(
			MainRoleType.VillageIdiot);
		servant.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.DevotedServant);
		servant.State.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.VillageIdiot);
		var cardStates = builder.GetGameState()!
			.GetModeratorPhysicalCharacterCards();
		cardStates.Single(state => state.Card.Id == servantCardId)
			.Should().Be(new PhysicalCharacterCardState(
				cardStates.Single(state => state.Card.Id == servantCardId).Card,
				PhysicalCharacterCardZone.Discarded,
				OwnerPlayerId: null));
		cardStates.Single(state => state.Card.Id == targetCardId)
			.Zone.Should().Be(PhysicalCharacterCardZone.PlayerOwned);
		cardStates.Single(state => state.Card.Id == targetCardId)
			.OwnerPlayerId.Should().Be(servant.Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RoleRevealLogEntry>()
			.Should().NotContain(entry =>
				entry.RevealedRoles.ContainsKey(voteTarget.Id));
		builder.GetGameState()!.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();
		var session = builder.GetGameState()!;
		var roleTake = session.GameHistoryLog
			.OfType<DevotedServantRoleTakenCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActingPlayerId == servant.Id &&
				entry.VoteTargetId == voteTarget.Id).Subject;
		roleTake.Facts.Should().ContainSingle(fact =>
			fact.PlayerId == servant.Id &&
			fact.Type == FactionFactType.Beneficiary &&
			fact.Faction == Faction.Villager);
		var agentFacts = roleTake.Facts
			.Where(fact => fact.Type == FactionFactType.Agent)
			.ToArray();
		agentFacts.Should().HaveCount(FactionFactFactions.All.Count);
		foreach (var faction in FactionFactFactions.All)
		{
			agentFacts.Should().ContainSingle(fact =>
				fact.PlayerId == servant.Id &&
				fact.Faction == faction &&
				fact.AgentKnowledge == FactionAgentKnowledge.KnownNonAgent);
			session.GetFactionAgentKnowledge(servant.Id, faction).Should().Be(
				FactionAgentKnowledge.KnownNonAgent);
		}
		session.GetFactionBeneficiaryKnowledge(servant.Id).Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Villager));
	}

	[Fact]
	public void Continue_ResumesOrdinaryVoteRevealWithoutIdentityCardOrSpendMutation()
	{
		var (builder, players) = CreateDayOneScenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.DevotedServant,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var voteTarget = players[2];
		var beforeCards = builder.GetGameState()!
			.GetModeratorPhysicalCharacterCards()
			.ToArray();
		var window = OpenWindow(builder, voteTarget.Id);

		var reveal = builder.Process(window.CreateContinueResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		reveal.PlayersForAssignment.Should().Contain(voteTarget.Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<DevotedServantPublicSelfRevealCommittedLogEntry>()
			.Should().BeEmpty();
		builder.GetGameState()!.GameHistoryLog
			.OfType<DevotedServantRoleTakenCommittedLogEntry>()
			.Should().BeEmpty();
		builder.GetGameState()!.GetModeratorPhysicalCharacterCards()
			.Should().Equal(beforeCards);
		players.Should().OnlyContain(player =>
			player.State.PubliclyRevealedRole != MainRoleType.DevotedServant);
	}

	[Fact]
	public void InvalidStaleTargetAndLoverSelfRevealResponses_AreSideEffectFree()
	{
		var (builder, players) = CreateDayOneScenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.DevotedServant,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var voteTarget = players[2];
		var lover = players[3];
		builder.ArrangeStatusEffect(lover.Id, StatusEffectTypes.Lovers);
		builder.ArrangeKnownPhysicalRole(
			players[4].Id,
			MainRoleType.SimpleVillager);
		var window = OpenWindow(builder, voteTarget.Id);
		window.SelectablePlayerIds.Should().NotContain(lover.Id);
		var beforeHistory = builder.GetGameState()!.GameHistoryLog.ToArray();
		var beforeCards = builder.GetGameState()!
			.GetModeratorPhysicalCharacterCards()
			.ToArray();
		var invalidResponses = new[]
		{
			new ModeratorResponse
			{
				InstructionId = Guid.NewGuid(),
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds = ImmutableHashSet.Create(players[4].Id)
			},
			new ModeratorResponse
			{
				InstructionId = window.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds = ImmutableHashSet.Create(players[4].Id)
			},
			new ModeratorResponse
			{
				InstructionId = window.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds = ImmutableHashSet.Create(voteTarget.Id)
			},
			new ModeratorResponse
			{
				InstructionId = window.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds = ImmutableHashSet.Create(lover.Id)
			}
		};

		foreach (var response in invalidResponses)
		{
			var act = () => builder.Process(response);
			act.Should().Throw<InvalidOperationException>();
			builder.GetCurrentInstruction()!.InstructionId.Should().Be(
				window.InstructionId);
			builder.GetGameState()!.GameHistoryLog.Should().Equal(beforeHistory);
			builder.GetGameState()!.GetModeratorPhysicalCharacterCards()
				.Should().Equal(beforeCards);
		}
	}

	[Fact]
	public void TiedVote_OmitsDevotedServantWindow()
	{
		var (builder, _) = CreateDayOneScenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.DevotedServant,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		var afterTie = builder.Process(vote.CreateResponse([]))
			.ModeratorInstruction;

		afterTie.Should().NotBeOfType<DevotedServantVoteWindowInstruction>();
		builder.GetGameState()!.GameHistoryLog
			.OfType<DevotedServantPublicSelfRevealCommittedLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void DawnElimination_DoesNotOpenWindowOrSpendDevotedServant()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.DevotedServant,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();

		builder.CompleteNightPhase([players[0].Id], players[1].Id);
		builder.CompleteDawnPhase(new()
		{
			[players[1].Id] = MainRoleType.SimpleVillager
		});

		builder.GetCurrentInstruction().Should()
			.NotBeOfType<DevotedServantVoteWindowInstruction>();
		builder.GetGameState()!.GameHistoryLog
			.OfType<DevotedServantPublicSelfRevealCommittedLogEntry>()
			.Should().BeEmpty();
		builder.GetGameState()!.GameHistoryLog
			.OfType<DevotedServantRoleTakenCommittedLogEntry>()
			.Should().BeEmpty();
		builder.GetGameState()!.GetModeratorPhysicalCharacterCards()
			.Should().ContainSingle(state =>
				state.Card.PrintedRole == MainRoleType.DevotedServant &&
				state.Zone == PhysicalCharacterCardZone.DealPool);
	}

	[Fact]
	public void ScapegoatTieReplacement_OmitsWindowAndFollowingDayRestrictionSurvivesSwap()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(8)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.DevotedServant,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var scapegoat = players[1];
		var servant = players[2];
		var voteTarget = players[3];
		builder.ArrangeKnownPhysicalRole(scapegoat.Id, MainRoleType.Scapegoat);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase([players[0].Id], players[7].Id);
		builder.CompleteDawnPhase(new()
		{
			[players[7].Id] = MainRoleType.SimpleVillager
		});
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var firstVote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		var scapegoatReveal = builder.Process(firstVote.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		scapegoatReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealScapegoatForTie);
		var voterChoice = builder.Process(scapegoatReveal.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		voterChoice.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectScapegoatPermittedVoters);
		var restrictionAnnouncement = builder.Process(
				voterChoice.CreateResponse([servant.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		_ = builder.Process(restrictionAnnouncement.CreateResponse());
		builder.GetGameState()!.GameHistoryLog
			.OfType<DevotedServantPublicSelfRevealCommittedLogEntry>()
			.Should().BeEmpty();

		builder.ConfirmNightStart();
		_ = builder.CompleteWerewolfNightActionSubsequentNight(players[5].Id);
		var nightEnd = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		_ = builder.Process(nightEnd.CreateResponse());
		builder.CompleteDawnPhase(new()
		{
			[players[5].Id] = MainRoleType.SimpleVillager
		});
		var activeBefore = DayVoteRules.GetActiveVoterEligibilityRestriction(
			builder.GetGameState()!);
		activeBefore.Should().NotBeNull();
		activeBefore!.PermittedVoterIds.Should().Equal(servant.Id);

		var window = OpenWindow(builder, voteTarget.Id);
		var acquired = builder.Process(
				window.CreatePublicSelfRevealResponse(servant.Id))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		_ = builder.Process(acquired.CreateResponse(new()
		{
			[voteTarget.Id] = MainRoleType.SimpleVillager
		}));

		var session = builder.GetGameState()!;
		var transaction = session.GameHistoryLog
			.OfType<DevotedServantRoleTakenCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		transaction.Policy.Restrictions.Should().Be(
			PermanentRoleSwapDisposition.Preserve);
		transaction.StateChanges.RestrictionScopeIdsToClear.Should().BeEmpty();
		DayVoteRules.GetActiveVoterEligibilityRestriction(session)
			.Should().Be(activeBefore);
		session.GameHistoryLog
			.OfType<VoterEligibilityRestrictionExpiredLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void AlreadyPublicVoteTarget_OmitsDevotedServantWindow()
	{
		var (builder, players) = CreateDayOneScenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.DevotedServant,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var voteTarget = players[2];
		builder.ArrangeKnownPhysicalRole(voteTarget.Id, MainRoleType.SimpleVillager)
			.ArrangePubliclyRevealedRole(
				voteTarget.Id,
				MainRoleType.SimpleVillager);

		var afterVote = SubmitVote(builder, voteTarget.Id).ModeratorInstruction;

		afterVote.Should().NotBeOfType<DevotedServantVoteWindowInstruction>();
		builder.GetGameState()!.GameHistoryLog
			.OfType<DevotedServantPublicSelfRevealCommittedLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void SamePrintedCards_BindAnyUnusedMatchWithoutAllocationOrderContract()
	{
		var (builder, players) = CreateDayOneScenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.DevotedServant,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var voteTarget = players[2];
		var servant = players[3];
		var unusedMatchingCardIds = builder.GetGameState()!
			.GetModeratorPhysicalCharacterCards()
			.Where(state =>
				state.Zone == PhysicalCharacterCardZone.DealPool &&
				state.Card.PrintedRole == MainRoleType.SimpleVillager)
			.Select(state => state.Card.Id)
			.ToHashSet();
		var window = OpenWindow(builder, voteTarget.Id);
		var acquired = builder.Process(
				window.CreatePublicSelfRevealResponse(servant.Id))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;

		_ = builder.Process(acquired.CreateResponse(new()
		{
			[voteTarget.Id] = MainRoleType.SimpleVillager
		}));

		servant.State.PhysicalCharacterCardId.Should().NotBeNull();
		unusedMatchingCardIds.Should().Contain(
			servant.State.PhysicalCharacterCardId!.Value);
		servant.State.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.SimpleVillager);
		voteTarget.State.PhysicalCharacterCardId.Should().BeNull();
	}

	[Fact]
	public void SuccessfulSwap_ClearsNamedOldIdentityStateAndRecoversExactlyOnce()
	{
		var (builder, players) = CreateDayOneScenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.DevotedServant,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var voteTarget = players[2];
		var servant = players[3];
		builder.ArrangeStatusEffect(servant.Id, StatusEffectTypes.Charmed)
			.ArrangeStatusEffect(servant.Id, StatusEffectTypes.Sheriff)
			.ArrangeStatusEffect(servant.Id, StatusEffectTypes.TownCrier)
			.ArrangeStatusEffect(
				servant.Id,
				StatusEffectTypes.LycanthropyInfection)
			.ArrangeVotingRight(servant.Id, hasVotingRight: false);
		var infectedBoundary = new FactionFactEffectiveBoundary(
			builder.GetGameState()!.TurnNumber,
			builder.GetGameState()!.GetCurrentPhase(),
			builder.GetGameState()!.GameHistoryLog.Count());
		builder.ArrangeExplicitFactionTransition(
			"test-infected-devoted-servant",
			FactionFact.Beneficiary(
				servant.Id,
				Faction.Werewolf,
				infectedBoundary),
			FactionFact.Agent(
				servant.Id,
				Faction.Werewolf,
				FactionAgentKnowledge.KnownAgent,
				infectedBoundary));
		builder.GetGameState()!.GetFactionBeneficiaryKnowledge(servant.Id)
			.Should().Be(FactionBeneficiaryKnowledge.Known(Faction.Werewolf));
		var window = OpenWindow(builder, voteTarget.Id);
		var acquired = builder.Process(
				window.CreatePublicSelfRevealResponse(servant.Id))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var announcement = builder.Process(acquired.CreateResponse(new()
		{
			[voteTarget.Id] = MainRoleType.SimpleVillager
		})).ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		servant.State.HasStatusEffect(StatusEffectTypes.Charmed).Should().BeFalse();
		servant.State.HasStatusEffect(StatusEffectTypes.Sheriff).Should().BeFalse();
		servant.State.HasStatusEffect(StatusEffectTypes.TownCrier).Should().BeFalse();
		servant.State.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)
			.Should().BeTrue();
		builder.GetGameState()!.GetFactionBeneficiaryKnowledge(servant.Id)
			.Should().Be(FactionBeneficiaryKnowledge.Known(Faction.Werewolf));
		servant.State.HasVotingRight.Should().BeFalse();
		servant.State.DurableVotingPower.Should().Be(0);
		var transaction = builder.GetGameState()!.GameHistoryLog
			.OfType<DevotedServantRoleTakenCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		transaction.StateChanges.StatusEffectsToClear.Should().BeEquivalentTo(new[]
		{
			StatusEffectTypes.Charmed,
			StatusEffectTypes.Sheriff,
			StatusEffectTypes.TownCrier
		});

		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recovered = recoveredService.GetGameStateView(recoveredId)!;
		var recoveredServant = recovered.GetPlayer(servant.Id);
		var recoveredTarget = recovered.GetPlayer(voteTarget.Id);
		var recoveredAnnouncement = recoveredService
			.GetCurrentInstruction(recoveredId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recoveredAnnouncement.InstructionId.Should().Be(
			announcement.InstructionId);
		recoveredServant.State.CurrentRole.Should().Be(MainRoleType.SimpleVillager);
		recoveredServant.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.DevotedServant);
		recoveredServant.State.HasStatusEffect(
			StatusEffectTypes.LycanthropyInfection).Should().BeTrue();
		recovered.GetFactionBeneficiaryKnowledge(servant.Id).Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Werewolf));
		recoveredServant.State.HasVotingRight.Should().BeFalse();
		recoveredServant.State.DurableVotingPower.Should().Be(0);
		recoveredTarget.State.Health.Should().Be(PlayerHealth.Dead);
		recovered.GameHistoryLog
			.OfType<DevotedServantRoleTakenCommittedLogEntry>()
			.Should().ContainSingle();
	}

	[Fact]
	public void ConsecutiveVote_OpensFreshDevotedServantWindow()
	{
		var (builder, players, firstVote) = CreateSignalledJudgeDayOneVote();
		var firstTarget = players[3];
		var consecutiveTarget = players[4];
		builder.ArrangeKnownPhysicalRole(
				firstTarget.Id,
				MainRoleType.SimpleVillager)
			.ArrangePubliclyRevealedRole(
				firstTarget.Id,
				MainRoleType.SimpleVillager);
		var firstAnnouncement = builder.Process(
				firstVote.CreateResponse([firstTarget.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var consecutiveVote = builder.Process(
				firstAnnouncement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		var window = builder.Process(
				consecutiveVote.CreateResponse([consecutiveTarget.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<DevotedServantVoteWindowInstruction>().Subject;

		window.VoteTargetId.Should().Be(consecutiveTarget.Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<VoteOutcomeReportedLogEntry>()
			.Where(entry => entry.CurrentPhase == GamePhase.Day)
			.Should().HaveCount(2);
	}

	[Fact]
	public void AcquiredVillageIdiot_IsDormantDuringSameDayConsecutiveVote()
	{
		var (builder, servant, consecutiveVote) =
			AcquireRoleBeforeSignalledConsecutiveVote(
				MainRoleType.VillageIdiot);

		_ = builder.Process(consecutiveVote.CreateResponse([servant.Id]));

		servant.State.Health.Should().Be(PlayerHealth.Dead);
		builder.GetGameState()!.GameHistoryLog
			.OfType<VillageIdiotPardonCommittedLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void AcquiredHunter_IsDormantWhenEliminatedDuringSameDayConsecutiveVote()
	{
		var (builder, servant, consecutiveVote) =
			AcquireRoleBeforeSignalledConsecutiveVote(MainRoleType.Hunter);

		var announcement = builder.Process(
				consecutiveVote.CreateResponse([servant.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		announcement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		servant.State.Health.Should().Be(PlayerHealth.Dead);

		var afterAnnouncement = builder.Process(announcement.CreateResponse());

		afterAnnouncement.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		var session = builder.GetGameState()!;
		session.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry =>
				entry.Reason == EliminationReason.HunterShot);
		session.GameHistoryLog
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Where(entry =>
				entry.ReactionId ==
					EliminationCascadeReactionIds.HunterFinalShot &&
				entry.TriggeringEliminations.Any(elimination =>
					elimination.PlayerId == servant.Id))
			.Should().ContainSingle()
			.Which.AdmittedEliminations.Should().BeEmpty();
	}

	[Fact]
	public void AcquiredScapegoat_IsDormantDuringSameDayConsecutiveTie()
	{
		var (builder, servant, consecutiveVote) =
			AcquireRoleBeforeSignalledConsecutiveVote(MainRoleType.Scapegoat);

		var afterTie = builder.Process(consecutiveVote.CreateResponse([]));

		afterTie.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		servant.State.Health.Should().Be(PlayerHealth.Alive);
		var session = builder.GetGameState()!;
		session.GameHistoryLog.OfType<ScapegoatTieReplacementLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry =>
				entry.Reason == EliminationReason.ScapegoatSacrifice);
		session.GameHistoryLog
			.OfType<VoterEligibilityRestrictionCommittedLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void AcquiredSeer_ActivatesNextNightWithFreshSwapLineage()
	{
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var builder = GameTestBuilder.Create()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.Seer,
				MainRoleType.DevotedServant,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var voteTarget = players[2];
		var servant = players[3];
		builder.ConfirmGameStart();
		builder.CompleteNightPhase(
			[players[0].Id],
			players[1].Id,
			seerId: voteTarget.Id,
			seerTargetId: players[4].Id);
		builder.CompleteDawnPhase(new()
		{
			[players[1].Id] = MainRoleType.SimpleVillager
		});
		voteTarget.State.PhysicalCharacterCardId.Should().BeNull(
			"the printed card remains unbound until the vote observation");
		policy.ObservedAttempts.Should().NotContain(attempt =>
			attempt.SourceRole == MainRoleType.Seer &&
			attempt.ActingPlayer.Id == servant.Id);
		var window = OpenWindow(builder, voteTarget.Id);
		var acquired = builder.Process(
				window.CreatePublicSelfRevealResponse(servant.Id))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var announcement = builder.Process(acquired.CreateResponse(new()
		{
			[voteTarget.Id] = MainRoleType.Seer
		})).ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var transaction = builder.GetGameState()!.GameHistoryLog
			.OfType<DevotedServantRoleTakenCommittedLogEntry>()
			.Should().ContainSingle().Subject;

		_ = builder.Process(announcement.CreateResponse());
		builder.ConfirmNightStart();
		var seerWake = builder.CompleteWerewolfNightActionSubsequentNight(
			players[4].Id)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var selectSeerTarget = builder.Process(seerWake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		selectSeerTarget.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectSeerTarget);
		selectSeerTarget.AffectedPlayerIds.Should().ContainSingle()
			.Which.Should().Be(servant.Id);
		var seerAttempt = policy.ObservedAttempts
			.Where(attempt =>
				attempt.SourceRole == MainRoleType.Seer &&
				attempt.ActingPlayer.Id == servant.Id)
			.Should().ContainSingle().Subject;
		seerAttempt.ActingPlayer.Id.Should().Be(servant.Id);
		seerAttempt.PowerInstance.Id.Should().Be(
			transaction.NewPowerInstanceId);
		seerAttempt.PowerInstance.Id.Should().NotBe(servant.Id);
		seerAttempt.PowerInstance.Origin.Should().Be(
			RolePowerInstanceOrigin.Swapped);
	}

	[Fact]
	public void AcquiredAngel_NewHolderKilledOnNightTwoWinsWithoutSecondPublicReveal()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(8)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.Angel,
				MainRoleType.DevotedServant,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var formerAngelHolder = players[2];
		var servant = players[3];
		builder.ArrangeKnownPhysicalRole(
			formerAngelHolder.Id,
			MainRoleType.Angel);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase([players[0].Id], players[1].Id);
		builder.CompleteDawnPhase(new()
		{
			[players[1].Id] = MainRoleType.SimpleVillager
		});
		var window = OpenWindow(builder, formerAngelHolder.Id);
		var acquired = builder.Process(
				window.CreatePublicSelfRevealResponse(servant.Id))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var announcement = builder.Process(acquired.CreateResponse(new()
		{
			[formerAngelHolder.Id] = MainRoleType.Angel
		})).ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;

		servant.State.CurrentRole.Should().Be(MainRoleType.Angel);
		servant.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.DevotedServant);
		formerAngelHolder.State.Health.Should().Be(PlayerHealth.Dead);
		_ = builder.Process(announcement.CreateResponse());
		builder.GetGameState()!.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().BeEmpty(
				"the former physical Angel holder no longer qualifies after transfer");

		builder.ConfirmNightStart();
		_ = builder.CompleteWerewolfNightActionSubsequentNight(servant.Id);
		var nightEnd = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		_ = builder.Process(nightEnd.CreateResponse());
		builder.CompleteDawnPhase();

		var session = builder.GetGameState()!;
		var victory = session.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().ContainSingle().Subject;
		victory.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.Angel));
		victory.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
		session.GameHistoryLog.OfType<RoleRevealLogEntry>()
			.Should().NotContain(entry =>
				entry.RevealedRoles.GetValueOrDefault(servant.Id) ==
				MainRoleType.Angel);
		servant.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.DevotedServant);
	}

	[Fact]
	public void AcquiredAngel_AfterNightTwoDawnBehavesAsSimpleVillager()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(8)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.Angel,
				MainRoleType.DevotedServant,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var expiredAngelHolder = players[2];
		var servant = players[3];
		builder.ArrangeKnownPhysicalRole(
			expiredAngelHolder.Id,
			MainRoleType.Angel);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase([players[0].Id], players[1].Id);
		builder.CompleteDawnPhase(new()
		{
			[players[1].Id] = MainRoleType.SimpleVillager
		});
		_ = builder.CompleteDayPhaseWithTie();
		builder.ConfirmNightStart();
		_ = builder.CompleteWerewolfNightActionSubsequentNight(players[4].Id);
		var nightEnd = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		_ = builder.Process(nightEnd.CreateResponse());
		builder.CompleteDawnPhase(new()
		{
			[players[4].Id] = MainRoleType.SimpleVillager
		});
		expiredAngelHolder.State.CurrentRole.Should().Be(
			MainRoleType.SimpleVillager);

		var window = OpenWindow(builder, expiredAngelHolder.Id);
		var acquired = builder.Process(
				window.CreatePublicSelfRevealResponse(servant.Id))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		acquired.RolesForAssignment.Should().ContainSingle()
			.Which.Should().Be(MainRoleType.Angel);
		var announcement = builder.Process(acquired.CreateResponse(new()
		{
			[expiredAngelHolder.Id] = MainRoleType.Angel
		})).ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		_ = builder.Process(announcement.CreateResponse());

		servant.State.PhysicalCharacterCardRole.Should().Be(MainRoleType.Angel);
		servant.State.CurrentRole.Should().Be(MainRoleType.SimpleVillager);
		servant.State.ModeratorKnownRole.Should().Be(
			MainRoleType.SimpleVillager);
		servant.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.DevotedServant);
		builder.GetGameState()!.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().BeEmpty();
		builder.GetGameState()!.GameHistoryLog.OfType<RoleRevealLogEntry>()
			.Should().NotContain(entry =>
				entry.RevealedRoles.GetValueOrDefault(servant.Id) ==
				MainRoleType.Angel);
	}

	private static (GameTestBuilder Builder, IPlayer[] Players)
		CreateDayOneScenario(params MainRoleType[] roles)
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(roles.Length)
			.WithRoles(roles);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.CompleteNightPhase([players[0].Id], players[1].Id);
		builder.CompleteDawnPhase(new()
		{
			[players[1].Id] = MainRoleType.SimpleVillager
		});
		return (builder, players);
	}

	private static ProcessResult SubmitVote(
		GameTestBuilder builder,
		Guid voteTargetId)
	{
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		return builder.Process(vote.CreateResponse([voteTargetId]));
	}

	private static DevotedServantVoteWindowInstruction OpenWindow(
		GameTestBuilder builder,
		Guid voteTargetId) =>
		SubmitVote(builder, voteTargetId)
			.ModeratorInstruction.Should()
			.BeOfType<DevotedServantVoteWindowInstruction>().Subject;

	private static (
		GameTestBuilder Builder,
		IPlayer[] Players,
		SelectPlayersInstruction FirstVote) CreateSignalledJudgeDayOneVote(
		MainRoleType firstAcquiredRole = MainRoleType.SimpleVillager)
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleWerewolf,
				MainRoleType.DevotedServant,
				firstAcquiredRole,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(players[0].Id, MainRoleType.StutteringJudge);
		builder.ConfirmGameStart();
		var judgeWake = builder.ConfirmNightStart()
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var judgeSetup = builder.Process(judgeWake.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		_ = builder.Process(judgeSetup.CreateResponse());
		var finishNight = builder.CompleteWerewolfNightAction(
				[players[1].Id],
				players[6].Id)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		_ = builder.Process(finishNight.CreateResponse());
		builder.CompleteDawnPhase(new()
		{
			[players[6].Id] = MainRoleType.SimpleVillager
		});
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var conductVote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var signal = builder.Process(conductVote.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectOptionsInstruction>().Subject;
		var firstVote = builder.Process(signal.CreateResponse(
				StutteringJudgeSignalOptionIds.Occurred))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		return (builder, players, firstVote);
	}

	private static (
		GameTestBuilder Builder,
		IPlayer Target,
		IPlayer Servant,
		AssignRolesInstruction AcquiredCard)
		CreateKnownRoleTakeScenario(MainRoleType knownRole)
	{
		var (builder, players) = CreateDayOneScenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.DevotedServant,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var target = players[2];
		var servant = players[3];
		builder.ArrangeKnownRole(target.Id, knownRole);
		var window = OpenWindow(builder, target.Id);
		var acquiredCard = builder.Process(
				window.CreatePublicSelfRevealResponse(servant.Id))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		return (builder, target, servant, acquiredCard);
	}

	private static PlayerStateSnapshot CapturePlayerState(IPlayer player) => new(
		player.State.CurrentRole,
		player.State.PhysicalCharacterCardId,
		player.State.PhysicalCharacterCardRole,
		player.State.ModeratorKnownRole,
		player.State.PubliclyRevealedRole,
		player.State.Health,
		player.State.HasVotingRight,
		player.State.DurableVotingPower,
		player.State.GetActiveStatusEffects().Order().ToImmutableArray(),
		player.State.FactionBeneficiary,
		Enum.GetValues<Faction>().ToImmutableDictionary(
			faction => faction,
			player.State.GetFactionAgentKnowledge));

	private static (
		GameTestBuilder Builder,
		IPlayer Servant,
		SelectPlayersInstruction ConsecutiveVote)
		AcquireRoleBeforeSignalledConsecutiveVote(MainRoleType acquiredRole)
	{
		var (builder, players, firstVote) =
			CreateSignalledJudgeDayOneVote(acquiredRole);
		var voteTarget = players[3];
		var servant = players[2];
		builder.ArrangeKnownPhysicalRole(voteTarget.Id, acquiredRole);
		var window = builder.Process(firstVote.CreateResponse([voteTarget.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<DevotedServantVoteWindowInstruction>().Subject;
		var acquired = builder.Process(
				window.CreatePublicSelfRevealResponse(servant.Id))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		var firstAnnouncement = builder.Process(acquired.CreateResponse(new()
		{
			[voteTarget.Id] = acquiredRole
		})).ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var consecutiveVote = builder.Process(
				firstAnnouncement.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		return (builder, servant, consecutiveVote);
	}

	private sealed class RecordingPolicy(RolePowerAvailabilityResult result)
		: IRolePowerAvailabilityPolicy
	{
		public List<RolePowerAttempt> ObservedAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			ObservedAttempts.Add(attempt);
			return result;
		}
	}

	private sealed record PlayerStateSnapshot(
		MainRoleType? CurrentRole,
		Guid? PhysicalCharacterCardId,
		MainRoleType? PhysicalCharacterCardRole,
		MainRoleType? ModeratorKnownRole,
		MainRoleType? PubliclyRevealedRole,
		PlayerHealth Health,
		bool HasVotingRight,
		int DurableVotingPower,
		ImmutableArray<StatusEffectTypes> StatusEffects,
		FactionBeneficiaryKnowledge FactionBeneficiary,
		ImmutableDictionary<Faction, FactionAgentKnowledge> FactionAgents);
}
