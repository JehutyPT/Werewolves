using FluentAssertions;
using Werewolves.Core.GameLogic;
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
		choice.Semantic.Should().Be(ModeratorInstructionSemantic.ChooseThiefOffer);
		choice.PublicAnnouncement.Should().BeNull();
		choice.AffectedPlayerIds.Should().Equal(holder.Id);
		choice.SelectionRange.Should().Be(NumberRangeConstraint.Single);
		choice.Options.Select(option => option.Id).Should().Equal(
			ThiefOfferOptionIds.Offer1,
			ThiefOfferOptionIds.Offer2,
			ThiefOfferOptionIds.Decline);

		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				choice.CreateResponse(ThiefOfferOptionIds.Offer1)));
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
	public void FirstNight_AlignedWerewolfOffer_ReplacesFactionAndPowerWhilePreservingPlayerState()
	{
		var (service, gameId, holder, lockIn, start) = StartKnownThief(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer);
		var session = (GameSession)service.GetGameStateView(gameId)!;
		session.RevealRoles(new Dictionary<Guid, MainRoleType>
		{
			[holder.Id] = MainRoleType.Thief
		});
		session.ApplyStatusEffect(StatusEffectTypes.Charmed, holder.Id);
		session.SetPlayerVotingRight(holder.Id, false);
		var lockedCardIds = lockIn.RoleComposition.Select(card => card.Id).ToArray();
		var initialFactionFacts = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Single(entry =>
				entry.Source.Identifier == "test-thief-initial-faction");
		var choice = ReachChoice(service, gameId, start, holder.Id);

		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				choice.CreateResponse(ThiefOfferOptionIds.Offer1)));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		holder.State.CurrentRole.Should().Be(MainRoleType.SimpleWerewolf);
		holder.State.ModeratorKnownRole.Should().Be(MainRoleType.SimpleWerewolf);
		holder.State.PubliclyRevealedRole.Should().Be(MainRoleType.Thief);
		holder.State.GetActiveStatusEffects().Should().Contain(StatusEffectTypes.Charmed);
		holder.State.HasVotingRight.Should().BeFalse();
		holder.State.DurableVotingPower.Should().Be(1);
		session.GetFactionBeneficiaryKnowledge(holder.Id).Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Werewolf));
		foreach (var faction in FactionFactFactions.All)
		{
			session.GetFactionAgentKnowledge(holder.Id, faction).Should().Be(
				faction == Faction.Werewolf
					? FactionAgentKnowledge.KnownAgent
					: FactionAgentKnowledge.KnownNonAgent);
		}
		session.GetFactionAgentKnowledge(holder.Id, Faction.Angel).Should()
			.Be(FactionAgentKnowledge.Unknown);
		session.GameHistoryLog.Should().Contain(initialFactionFacts);

		var swap = session.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		swap.Policy.Should().Be(ExpectedThiefSwapPolicy());
		swap.StateChanges.IsEmpty.Should().BeTrue();
		swap.NewPowerInstanceId.Should().NotBeEmpty()
			.And.NotBe(holder.Id);
		lockedCardIds.Should().NotContain(swap.NewPowerInstanceId);
		swap.PowerInstanceOrigin.Should().Be(RolePowerInstanceOrigin.Swapped);
		swap.Source.Should().Be(PermanentRoleSwapFactionFacts.CreateSource(
			holder.Id,
			swap.NewPowerInstanceId));
		swap.Facts.Should().ContainSingle(fact =>
			fact.Type == FactionFactType.Beneficiary &&
			fact.Faction == Faction.Werewolf);
		swap.Facts.Where(fact => fact.Type == FactionFactType.Agent)
			.Should().HaveCount(FactionFactFactions.All.Count);

		AssertThiefExchangeCardConservation(session, lockIn, holder.Id);
		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(session.Serialize());
		var recovered = recoveredService.GetGameStateView(recoveredId)!;
		var recoveredSwap = recovered.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		recoveredSwap.NewPowerInstanceId.Should().Be(swap.NewPowerInstanceId);
		recoveredSwap.PowerInstanceOrigin.Should().Be(RolePowerInstanceOrigin.Swapped);
		recovered.GetPlayerState(holder.Id).CurrentRole
			.Should().Be(MainRoleType.SimpleWerewolf);
		recovered.GetPlayerState(holder.Id).PubliclyRevealedRole
			.Should().Be(MainRoleType.Thief);
		recovered.GetPlayerState(holder.Id).GetActiveStatusEffects()
			.Should().Contain(StatusEffectTypes.Charmed);
		recovered.GetPlayerState(holder.Id).HasVotingRight.Should().BeFalse();
		recovered.GetFactionBeneficiaryKnowledge(holder.Id).Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Werewolf));
		foreach (var faction in FactionFactFactions.All)
		{
			recovered.GetFactionAgentKnowledge(holder.Id, faction).Should().Be(
				faction == Faction.Werewolf
					? FactionAgentKnowledge.KnownAgent
					: FactionAgentKnowledge.KnownNonAgent);
		}
		recovered.GetFactionAgentKnowledge(holder.Id, Faction.Angel).Should()
			.Be(FactionAgentKnowledge.Unknown);
		AssertThiefExchangeCardConservation(recovered, lockIn, holder.Id);
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
		session.GetFactionAgentKnowledge(holder.Id, Faction.Werewolf).Should().Be(
			FactionAgentKnowledge.Unknown);

		var choice = InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
			service.ProcessInstruction(
				start.GameGuid,
				identification.CreateResponse([holder.Id])));

		choice.Semantic.Should().Be(ModeratorInstructionSemantic.ChooseThiefOffer);
		holder.State.CurrentRole.Should().Be(MainRoleType.Thief);
		session.GetFactionAgentKnowledge(holder.Id, Faction.Werewolf).Should().Be(
			FactionAgentKnowledge.Unknown);
		session.GameHistoryLog.OfType<FactionFactsCommittedLogEntry>().Should()
			.NotContain(entry => entry.Source.Identifier ==
				FactionFactSource
					.RoleIdentificationWerewolfFactionAgencyEntailmentIdentifier);
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
	public void Recovery_ThiefSwapWithNonCanonicalPolicy_IsRejected()
	{
		var (service, gameId, holder, _, start) = StartKnownThief(
			MainRoleType.Seer,
			MainRoleType.Cupid);
		var choice = ReachChoice(service, gameId, start, holder.Id);
		_ = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				choice.CreateResponse(ThiefOfferOptionIds.Offer1)));
		var tampered = RecoveryPayloadTestDriver
			.Parse(service.GetGameStateView(gameId)!.Serialize())
			.RewriteLatestPermanentRoleSwapPolicy(policy => policy with
			{
				Relationships = PermanentRoleSwapDisposition.Clear
			})
			.Serialize();

		var act = () => new GameService().RehydrateSession(tampered);

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void Recovery_ThiefSwapWithNonOfferAcquiredCard_IsRejected()
	{
		var (service, gameId, holder, lockIn, start) = StartKnownThief(
			MainRoleType.Seer,
			MainRoleType.Cupid);
		var choice = ReachChoice(service, gameId, start, holder.Id);
		_ = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				choice.CreateResponse(ThiefOfferOptionIds.Offer1)));
		var unusedVillagerCardId = lockIn.DealPool
			.First(card => card.PrintedRole == MainRoleType.SimpleVillager).Id;
		var tampered = RecoveryPayloadTestDriver
			.Parse(service.GetGameStateView(gameId)!.Serialize())
			.RewriteLatestThiefSwapAcquiredCard(unusedVillagerCardId)
			.Serialize();

		var act = () => new GameService().RehydrateSession(tampered);

		act.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void Recovery_ThiefSwapWithNonOfferUnchosenCard_IsRejected()
	{
		var (service, gameId, holder, lockIn, start) = StartKnownThief(
			MainRoleType.Seer,
			MainRoleType.Cupid);
		var choice = ReachChoice(service, gameId, start, holder.Id);
		_ = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				choice.CreateResponse(ThiefOfferOptionIds.Offer1)));
		var unusedVillagerCardId = lockIn.DealPool
			.First(card => card.PrintedRole == MainRoleType.SimpleVillager).Id;
		var tampered = RecoveryPayloadTestDriver
			.Parse(service.GetGameStateView(gameId)!.Serialize())
			.RewriteLatestThiefSwapUnchosenCard(unusedVillagerCardId)
			.Serialize();

		var act = () => new GameService().RehydrateSession(tampered);

		act.Should().Throw<InvalidOperationException>();
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
		var unavailableDecline = new ModeratorResponse
		{
			InstructionId = choice.InstructionId,
			Type = ExpectedInputType.OptionSelection,
			SelectedOptionIds = [ThiefOfferOptionIds.Decline]
		};
		var act = () => service.ProcessInstruction(gameId, unavailableDecline);

		act.Should().Throw<InvalidOperationException>();
		service.GetGameStateView(gameId)!.Serialize().Should().Be(before);
		service.GetCurrentInstruction(gameId)!.InstructionId
			.Should().Be(choice.InstructionId);
		service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>().Should().BeEmpty();
		service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<ThiefOfferDeclinedLogEntry>().Should().BeEmpty();
		lockIn.Offer1!.PrintedRole.Should().Be(MainRoleType.SimpleWerewolf);
		lockIn.Offer2!.PrintedRole.Should().Be(MainRoleType.BigBadWolf);
	}

	[Fact]
	public void FirstNight_RawInvalidOfferResponses_AreAtomicAndDoNotCorruptContinuation()
	{
		var (service, gameId, holder, _, start) = StartKnownThief(
			MainRoleType.Seer,
			MainRoleType.Cupid);
		var choice = ReachChoice(service, gameId, start, holder.Id);
		var foreignPlayerId = service.GetGameStateView(gameId)!.GetPlayers()
			.First(player => player.Id != holder.Id).Id;
		var cases = new (string Name, ModeratorResponse? Response)[]
		{
			("zero", RawOptionResponse(choice.InstructionId, [])),
			("multiple", RawOptionResponse(
				choice.InstructionId,
				[ThiefOfferOptionIds.Offer1, ThiefOfferOptionIds.Offer2])),
			("unknown", RawOptionResponse(choice.InstructionId, ["unknown-thief-option"])),
			("foreign-payload", new ModeratorResponse
			{
				InstructionId = choice.InstructionId,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds = [ThiefOfferOptionIds.Offer1],
				SelectedPlayerIds = new HashSet<Guid> { foreignPlayerId }
			}),
			("mismatched-type", new ModeratorResponse
			{
				InstructionId = choice.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds = new HashSet<Guid> { holder.Id }
			}),
			("mismatched-instruction", RawOptionResponse(
				Guid.NewGuid(),
				[ThiefOfferOptionIds.Offer1])),
			("canceled", null)
		};

		foreach (var (name, response) in cases)
		{
			var before = service.GetGameStateView(gameId)!.Serialize();
			var act = () => service.ProcessInstruction(gameId, response);

			if (response is null)
			{
				act.Should().Throw<ArgumentNullException>(name);
			}
			else
			{
				act.Should().Throw<InvalidOperationException>(name);
			}
			service.GetGameStateView(gameId)!.Serialize().Should().Be(before, name);
			service.GetCurrentInstruction(gameId)!.InstructionId
				.Should().Be(choice.InstructionId, name);
		}

		var accepted = choice.CreateResponse(ThiefOfferOptionIds.Offer1);
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(gameId, accepted));
		var afterAccepted = service.GetGameStateView(gameId)!.Serialize();
		var stale = () => service.ProcessInstruction(gameId, accepted);

		stale.Should().Throw<InvalidOperationException>();
		service.GetGameStateView(gameId)!.Serialize().Should().Be(afterAccepted);
		service.GetCurrentInstruction(gameId)!.InstructionId
			.Should().Be(sleep.InstructionId);
		service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>().Should().ContainSingle();
	}

	[Fact]
	public void Recovery_DeclineRewrittenToDoubleHardWerewolfOffers_IsRejected()
	{
		var (service, gameId, holder, _, start) = StartKnownThief(
			MainRoleType.Seer,
			MainRoleType.Cupid);
		var choice = ReachChoice(service, gameId, start, holder.Id);
		_ = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			service.ProcessInstruction(
				gameId,
				choice.CreateResponse(ThiefOfferOptionIds.Decline)));
		var tampered = RecoveryPayloadTestDriver
			.Parse(service.GetGameStateView(gameId)!.Serialize())
			.RewriteThiefOfferPrintedRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.BigBadWolf)
			.Serialize();

		var act = () => new GameService().RehydrateSession(tampered);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Thief offer decline history is invalid.");
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

	[Fact]
	public void FirstNight_OfferOnlyElderAcquired_ResistsLaterWerewolfAttackWithoutReidentification()
	{
		var (service, gameId, holder, _, start) = StartKnownThief(
			MainRoleType.Elder,
			MainRoleType.Seer);
		var session = service.GetGameStateView(gameId)!;
		var werewolfId = session.GetPlayers().ElementAt(1).Id;
		var choice = ReachChoice(service, gameId, start, holder.Id);
		var thiefSleep = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					gameId,
					choice.CreateResponse(ThiefOfferOptionIds.Offer1)));

		var werewolfWake = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					gameId,
					thiefSleep.CreateResponse()));
		werewolfWake.AffectedPlayerIds.Should().Equal(werewolfId);
		var victim = InstructionAssert
			.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					gameId,
					werewolfWake.CreateResponse()));
		var werewolfSleep = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					gameId,
					victim.CreateResponse([holder.Id])));
		var finishNight = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					gameId,
					werewolfSleep.CreateResponse()));

		var afterResolution = service.ProcessInstruction(
			gameId,
			finishNight.CreateResponse());

		afterResolution.IsSuccess.Should().BeTrue();
		holder.State.CurrentRole.Should().Be(MainRoleType.Elder);
		holder.State.ModeratorKnownRole.Should().Be(MainRoleType.Elder);
		holder.State.Health.Should().Be(PlayerHealth.Alive);
		holder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeTrue();
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry => entry.Role == MainRoleType.Elder);
		session.GameHistoryLog.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == holder.Id);
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

	private static ModeratorResponse RawOptionResponse(
		Guid instructionId,
		IReadOnlyList<string> selectedOptionIds) => new()
	{
		InstructionId = instructionId,
		Type = ExpectedInputType.OptionSelection,
		SelectedOptionIds = selectedOptionIds
	};

	private static PermanentRoleSwapPolicy ExpectedThiefSwapPolicy() => new(
		PrivateRoleKnowledge: PermanentRoleSwapDisposition.Change,
		PublicRevealHistory: PermanentRoleSwapDisposition.Preserve,
		FactionBeneficiary: PermanentRoleSwapDisposition.Change,
		FactionAgents: PermanentRoleSwapDisposition.Change,
		Relationships: PermanentRoleSwapDisposition.Preserve,
		StatusEffects: PermanentRoleSwapDisposition.Preserve,
		VotingState: PermanentRoleSwapDisposition.Preserve,
		Restrictions: PermanentRoleSwapDisposition.Preserve,
		Assignments: PermanentRoleSwapDisposition.Preserve,
		RolePowerState: PermanentRoleSwapDisposition.Change);

	private static void AssertThiefExchangeCardConservation(
		IGameSession session,
		RoleLockIn lockIn,
		Guid holderId)
	{
		var states = session.GetModeratorPhysicalCharacterCards().ToArray();
		states.Should().HaveCount(lockIn.RoleComposition.Count);
		states.Select(state => state.Card.Id).Should().BeEquivalentTo(
			lockIn.RoleComposition.Select(card => card.Id));
		states.Select(state => state.Card.Id).Should().OnlyHaveUniqueItems();
		states.Should().ContainSingle(state =>
			state.Zone == PhysicalCharacterCardZone.PlayerOwned &&
			state.OwnerPlayerId == holderId &&
			state.Card.Id == lockIn.Offer1!.Id);
		states.Should().ContainSingle(state =>
			state.Zone == PhysicalCharacterCardZone.SetAside &&
			state.Card.Id == lockIn.Offer2!.Id);
		states.Should().ContainSingle(state =>
			state.Zone == PhysicalCharacterCardZone.SetAside &&
			state.Card.PrintedRole == MainRoleType.Thief);
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
		RoleFactionKnowledge.CommitRoleIdentification(
			mutableSession,
			new HashSet<Guid> { holder.Id },
			MainRoleType.Thief);
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
						FactionFactFactions.All.Select(faction =>
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
