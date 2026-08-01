using FluentAssertions;
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

public sealed class ThiefRoleTests
{
	[Fact]
	public void FirstNight_KnownHolder_Offer1CommitsSwapAndReturnsPublicSleep()
	{
		var (service, gameId, holder, lockIn, start) = StartKnownThief(
			MainRoleType.Seer,
			MainRoleType.Cupid);

		var nightStart = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(gameId, start.CreateResponse()));
		var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(gameId, nightStart.CreateResponse()));
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().Equal(holder.Id);

		var choice = InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
			service.ProcessInstruction(gameId, wake.CreateResponse()));
		choice.Semantic.ToString().Should().Be("ChooseThiefOffer");
		choice.PublicAnnouncement.Should().BeNull();
		choice.AffectedPlayerIds.Should().Equal(holder.Id);
		choice.SelectionRange.Should().Be(NumberRangeConstraint.Single);
		choice.Options.Select(option => option.Id).Should().Equal(
			"Offer1",
			"Offer2",
			"Decline");

		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(gameId, choice.CreateResponse("Offer1")));
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(holder.Id);

		var session = service.GetGameStateView(gameId)!;
		holder.State.CurrentRole.Should().Be(MainRoleType.Seer);
		holder.State.ModeratorKnownRole.Should().Be(MainRoleType.Seer);
		var cards = session.GetModeratorPhysicalCharacterCards();
		cards.Single(state => state.Card.Id == lockIn.Offer1!.Id)
			.Should().Match<PhysicalCharacterCardState>(state =>
				state.Zone == PhysicalCharacterCardZone.PlayerOwned &&
				state.OwnerPlayerId == holder.Id);
		cards.Single(state => state.Card.Id == lockIn.Offer2!.Id).Zone
			.Should().Be(PhysicalCharacterCardZone.SetAside);
		cards.Single(state => state.Card.PrintedRole == MainRoleType.Thief).Zone
			.Should().Be(PhysicalCharacterCardZone.SetAside);
		session.GameHistoryLog.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle();
	}

	[Fact]
	public void FirstNight_PublicIdentification_BindsMatchingDealPoolCardBeforeOfferChoice()
	{
		var cards = new[]
		{
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Thief),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleWerewolf),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Seer),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Cupid)
		};
		var lockIn = new RoleLockIn(
			version: 1,
			playerCount: 5,
			roleComposition: cards,
			dealPoolCardIds: cards.Take(5).Select(card => card.Id),
			offer1CardId: cards[5].Id,
			offer2CardId: cards[6].Id);
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			["Player1", "Player2", "Player3", "Player4", "Player5"],
			lockIn));
		var session = service.GetGameStateView(start.GameGuid)!;
		var holder = session.GetPlayers().First();
		var nightStart = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(start.GameGuid, start.CreateResponse()));
		var identification = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			service.ProcessInstruction(start.GameGuid, nightStart.CreateResponse()));

		var choice = InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
			service.ProcessInstruction(
				start.GameGuid,
				identification.CreateResponse([holder.Id])));

		choice.Semantic.Should().Be(ModeratorInstructionSemantic.ChooseThiefOffer);
		holder.State.CurrentRole.Should().Be(MainRoleType.Thief);
		var ownedThief = session.GetModeratorPhysicalCharacterCards()
			.Should().ContainSingle(state =>
				state.Zone == PhysicalCharacterCardZone.PlayerOwned &&
				state.OwnerPlayerId == holder.Id &&
				state.Card.PrintedRole == MainRoleType.Thief)
			.Which;
		holder.State.PhysicalCharacterCardId.Should().Be(ownedThief.Card.Id);

		_ = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(
				start.GameGuid,
				choice.CreateResponse(ThiefOfferOptionIds.Offer2)));
		holder.State.CurrentRole.Should().Be(MainRoleType.Cupid);
		session.GameHistoryLog.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PhysicalCards.AcquiredCardId == lockIn.Offer2!.Id);
	}

	[Fact]
	public void FirstNight_KnownHolder_Offer2UsesOrderedSlotWhenPrintedRolesMatch()
	{
		var (service, gameId, holder, lockIn, start) = StartKnownThief(
			MainRoleType.Seer,
			MainRoleType.Seer);
		var choice = ReachChoice(service, gameId, start, holder.Id);

		choice.Options.Select(option => (option.Id, option.Label)).Should().Equal(
			(ThiefOfferOptionIds.Offer1, MainRoleType.Seer.GetPublicName()),
			(ThiefOfferOptionIds.Offer2, MainRoleType.Seer.GetPublicName()),
			(ThiefOfferOptionIds.Decline, GameStrings.DeclineOption));

		_ = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				choice.CreateResponse(ThiefOfferOptionIds.Offer2)));
		holder.State.PhysicalCharacterCardId.Should().Be(lockIn.Offer2!.Id);
		service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PhysicalCards.AcquiredCardId == lockIn.Offer2.Id);
	}

	[Fact]
	public void FirstNight_PendingOfferChoice_RecoversWithoutCommittingAChoice()
	{
		var (service, gameId, holder, _, start) = StartKnownThief(
			MainRoleType.Seer,
			MainRoleType.Cupid);
		var choice = ReachChoice(service, gameId, start, holder.Id);
		var committed = service.GetGameStateView(gameId)!;

		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(committed.Serialize());
		var recoveredChoice = recoveredService.GetCurrentInstruction(recoveredId)
			.Should().BeOfType<SelectOptionsInstruction>().Subject;

		recoveredChoice.InstructionId.Should().Be(choice.InstructionId);
		recoveredChoice.Semantic.Should().Be(ModeratorInstructionSemantic.ChooseThiefOffer);
		recoveredChoice.AffectedPlayerIds.Should().Equal(holder.Id);
		recoveredChoice.Options.Select(option => option.Id).Should().Equal(
			ThiefOfferOptionIds.Offer1,
			ThiefOfferOptionIds.Offer2,
			ThiefOfferOptionIds.Decline);
		var recovered = recoveredService.GetGameStateView(recoveredId)!;
		recovered.GameHistoryLog.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().BeEmpty();
		recovered.GameHistoryLog.OfType<ThiefOfferDeclinedLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void FirstNight_CommittedOfferSwap_RecoversPendingSleepExactlyOnce()
	{
		var (service, gameId, holder, lockIn, start) = StartKnownThief(
			MainRoleType.Seer,
			MainRoleType.Cupid);
		var choice = ReachChoice(service, gameId, start, holder.Id);
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				choice.CreateResponse(ThiefOfferOptionIds.Offer1)));
		var committed = service.GetGameStateView(gameId)!;

		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(committed.Serialize());
		var recoveredSleep = recoveredService.GetCurrentInstruction(recoveredId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredSleep.InstructionId.Should().Be(sleep.InstructionId);
		recoveredSleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		recoveredSleep.AffectedPlayerIds.Should().Equal(holder.Id);
		var recovered = recoveredService.GetGameStateView(recoveredId)!;
		recovered.GetPlayerState(holder.Id).CurrentRole.Should().Be(MainRoleType.Seer);
		recovered.GetPlayerState(holder.Id).PhysicalCharacterCardId.Should().Be(lockIn.Offer1!.Id);
		recovered.GameHistoryLog.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle();

		_ = recoveredService.ProcessInstruction(
			recoveredId,
			recoveredSleep.CreateResponse());
		var replay = () => recoveredService.ProcessInstruction(
			recoveredId,
			choice.CreateResponse(ThiefOfferOptionIds.Offer1));
		replay.Should().Throw<InvalidOperationException>();
		recovered.GameHistoryLog.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle();
	}

	[Fact]
	public void FirstNight_LegalDecline_PreservesThiefAndRecoversPendingSleepExactlyOnce()
	{
		var (service, gameId, holder, lockIn, start) = StartKnownThief(
			MainRoleType.Seer,
			MainRoleType.Cupid);
		var choice = ReachChoice(service, gameId, start, holder.Id);
		var originalCardId = holder.State.PhysicalCharacterCardId;

		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				choice.CreateResponse(ThiefOfferOptionIds.Decline)));
		var committed = service.GetGameStateView(gameId)!;
		holder.State.CurrentRole.Should().Be(MainRoleType.Thief);
		holder.State.ModeratorKnownRole.Should().Be(MainRoleType.Thief);
		holder.State.PhysicalCharacterCardId.Should().Be(originalCardId);
		committed.GetFactionBeneficiaryKnowledge(holder.Id).Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Villager));
		committed.GetModeratorPhysicalCharacterCards()
			.Where(state => state.Card.Id == lockIn.Offer1!.Id ||
				state.Card.Id == lockIn.Offer2!.Id)
			.All(state =>
				state.Zone == PhysicalCharacterCardZone.SetAside &&
				state.OwnerPlayerId is null).Should().BeTrue();
		committed.GameHistoryLog.OfType<ThiefOfferDeclinedLogEntry>()
			.Should().ContainSingle();

		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(committed.Serialize());
		var recoveredSleep = recoveredService.GetCurrentInstruction(recoveredId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recoveredSleep.InstructionId.Should().Be(sleep.InstructionId);
		recoveredSleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		var recovered = recoveredService.GetGameStateView(recoveredId)!;
		recovered.GameHistoryLog.OfType<ThiefOfferDeclinedLogEntry>()
			.Should().ContainSingle();
		recovered.GetPlayerState(holder.Id).CurrentRole.Should().Be(MainRoleType.Thief);

		_ = recoveredService.ProcessInstruction(
			recoveredId,
			recoveredSleep.CreateResponse());
		var replay = () => recoveredService.ProcessInstruction(
			recoveredId,
			choice.CreateResponse(ThiefOfferOptionIds.Decline));
		replay.Should().Throw<InvalidOperationException>();
		recovered.GameHistoryLog.OfType<ThiefOfferDeclinedLogEntry>()
			.Should().ContainSingle();
	}

	[Fact]
	public void FirstNight_DoubleHardWerewolfOffers_OmitAndRejectDeclineWithoutMutation()
	{
		var (service, gameId, holder, lockIn, start) = StartKnownThief(
			MainRoleType.SimpleWerewolf,
			MainRoleType.BigBadWolf);
		var choice = ReachChoice(service, gameId, start, holder.Id);
		var before = service.GetGameStateView(gameId)!.Serialize();

		choice.Options.Select(option => option.Id).Should().Equal(
			ThiefOfferOptionIds.Offer1,
			ThiefOfferOptionIds.Offer2);
		var act = () => choice.CreateResponse(ThiefOfferOptionIds.Decline);

		act.Should().Throw<ArgumentException>();
		service.GetGameStateView(gameId)!.Serialize().Should().Be(before);
		service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>().Should().BeEmpty();
		service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<ThiefOfferDeclinedLogEntry>().Should().BeEmpty();
		lockIn.Offer1!.PrintedRole.Should().Be(MainRoleType.SimpleWerewolf);
		lockIn.Offer2!.PrintedRole.Should().Be(MainRoleType.BigBadWolf);
	}

	[Fact]
	public void FirstNight_OfferOnlyHunterAcquiredThenEliminated_ReceivesFinalShot()
	{
		var (service, gameId, holder, _, start) = StartKnownThief(
			MainRoleType.Hunter,
			MainRoleType.Cupid);
		var session = service.GetGameStateView(gameId)!;
		var players = session.GetPlayers().ToArray();
		var werewolfId = players[1].Id;
		var shotTargetId = players[2].Id;
		session.RoleInPlayCount(MainRoleType.Hunter).Should().Be(0);
		var choice = ReachChoice(service, gameId, start, holder.Id);
		var thiefSleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				choice.CreateResponse(ThiefOfferOptionIds.Offer1)));

		var werewolfWake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(
			gameId,
				thiefSleep.CreateResponse()));
		werewolfWake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		werewolfWake.AffectedPlayerIds.Should().Equal(werewolfId);
		var victim = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			service.ProcessInstruction(
				gameId,
				werewolfWake.CreateResponse()));
		victim.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		var werewolfSleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				victim.CreateResponse([holder.Id])));
		var finishNight = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(gameId, werewolfSleep.CreateResponse()));
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		var hunterReveal = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(gameId, finishNight.CreateResponse()));
		hunterReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDawnVictimRoles);

		var finalShot = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			service.ProcessInstruction(gameId, hunterReveal.CreateResponse()));

		finalShot.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		finalShot.AffectedPlayerIds.Should().Equal(holder.Id);
		finalShot.SelectablePlayerIds.Should().Contain(shotTargetId);
		holder.State.CurrentRole.Should().Be(MainRoleType.Hunter);
		session.RoleInPlayCount(MainRoleType.Hunter).Should().Be(1);
	}

	private static SelectOptionsInstruction ReachChoice(
		GameService service,
		Guid gameId,
		StartGameConfirmationInstruction start,
		Guid holderId)
	{
		var nightStart = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(gameId, start.CreateResponse()));
		var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(gameId, nightStart.CreateResponse()));
		wake.AffectedPlayerIds.Should().Equal(holderId);
		return InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
			service.ProcessInstruction(gameId, wake.CreateResponse()));
	}

	private static (
		GameService Service,
		Guid GameId,
		IPlayer Holder,
		RoleLockIn RoleLockIn,
		StartGameConfirmationInstruction Start) StartKnownThief(
		MainRoleType offer1,
		MainRoleType offer2)
	{
		var cards = new[]
		{
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.Thief),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleWerewolf),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), MainRoleType.SimpleVillager),
			new PhysicalCharacterCard(Guid.NewGuid(), offer1),
			new PhysicalCharacterCard(Guid.NewGuid(), offer2)
		};
		var lockIn = new RoleLockIn(
			version: 1,
			playerCount: 5,
			cards,
			cards.Take(5).Select(card => card.Id),
			cards[5].Id,
			cards[6].Id);
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			["Player1", "Player2", "Player3", "Player4", "Player5"],
			lockIn));
		var session = service.GetGameStateView(start.GameGuid)!;
		var mutableSession = (GameSession)session;
		var players = session.GetPlayers().ToArray();
		var holder = players[0];
		var werewolfId = players[1].Id;
		service.TryRecordPhysicalCharacterCardOwnership(
			start.GameGuid,
			lockIn.Version,
			holder.Id,
			cards[0].Id).Should().BeTrue();
		mutableSession.AssignRole(holder.Id, MainRoleType.Thief);
		mutableSession.IdentifyRole([holder.Id], MainRoleType.Thief);
		mutableSession.CommitFactionFactBatch(context =>
		{
			var boundary = new FactionFactEffectiveBoundary(
				context.TurnNumber,
				context.CurrentPhase,
				session.GameHistoryLog.Count());
			return new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ExplicitTransition,
					"test-thief-initial-faction"),
				Facts =
				[
					.. players.Select(player =>
						FactionFact.Beneficiary(
							player.Id,
							player.Id == werewolfId
								? Faction.Werewolf
								: Faction.Villager,
							boundary)),
					.. players.SelectMany(player =>
						Enum.GetValues<Faction>().Select(faction =>
							FactionFact.Agent(
								player.Id,
								faction,
								player.Id == werewolfId &&
								faction == Faction.Werewolf
									? FactionAgentKnowledge.KnownAgent
									: FactionAgentKnowledge.KnownNonAgent,
								boundary)))
				]
			};
		});
		return (service, start.GameGuid, holder, lockIn, start);
	}
}
