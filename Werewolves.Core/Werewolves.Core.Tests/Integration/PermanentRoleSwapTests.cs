using System.Collections.Immutable;
using FluentAssertions;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public class PermanentRoleSwapTests
{
	[Fact]
	public void ThiefSwap_RequiresExactPolicy_ThenCommitsOrderedThreeCardExchange()
	{
		var gameId = Guid.NewGuid();
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
			version: 41,
			playerCount: 5,
			cards,
			cards.Take(5).Select(card => card.Id),
			cards[5].Id,
			cards[6].Id);
		var seedSession = new GameSession(
			gameId,
			new StartGameConfirmationInstruction(gameId),
			new GameSessionConfig(
				Enumerable.Range(1, 5).Select(index => $"Player{index}").ToList(),
				lockIn));
		var service = new GameService();
		var rehydratedGameId = service.RehydrateSession(seedSession.Serialize());
		var session = service.GetGameStateView(rehydratedGameId)!;
		var mutableSession = (GameSession)session;
		var player = session.GetPlayers().First();
		service.TryRecordPhysicalCharacterCardOwnership(
			rehydratedGameId,
			lockIn.Version,
			player.Id,
			cards[0].Id).Should().BeTrue();
		mutableSession.AssignRole(player.Id, MainRoleType.Thief);
		mutableSession.IdentifyRole([player.Id], MainRoleType.Thief);
		var compositionIds = lockIn.RoleComposition.Select(card => card.Id).ToArray();
		var movement = new PermanentRoleSwapCardMovement(
			cards[0].Id,
			cards[5].Id,
			[cards[6].Id]);
		var almostThiefPolicy = ThiefPolicy() with
		{
			StatusEffects = PermanentRoleSwapDisposition.Clear
		};
		var rejected = new PermanentRoleSwapRequest(
			lockIn.Version,
			player.Id,
			MainRoleType.Thief,
			MainRoleType.Seer,
			movement,
			almostThiefPolicy,
			VillagerFactionReplacement(),
			PermanentRoleSwapStateChanges.None);

		service.TryCommitPermanentRoleSwap(rehydratedGameId, rejected)
			.Should().BeFalse();
		session.GameHistoryLog.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().BeEmpty();
		session.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.Id == cards[0].Id).Zone
			.Should().Be(PhysicalCharacterCardZone.PlayerOwned);

		var committed = rejected with { Policy = ThiefPolicy() };
		service.TryCommitPermanentRoleSwap(rehydratedGameId, committed)
			.Should().BeTrue();

		player.State.CurrentRole.Should().Be(MainRoleType.Seer);
		player.State.ModeratorKnownRole.Should().Be(MainRoleType.Seer);
		player.State.PubliclyRevealedRole.Should().BeNull();
		player.State.PhysicalCharacterCardId.Should().Be(cards[5].Id);
		session.RoleLockIn.RoleComposition.Select(card => card.Id)
			.Should().Equal(compositionIds);
		var states = session.GetModeratorPhysicalCharacterCards();
		states.Single(state => state.Card.Id == cards[5].Id)
			.Should().Match<PhysicalCharacterCardState>(state =>
				state.Zone == PhysicalCharacterCardZone.PlayerOwned &&
				state.OwnerPlayerId == player.Id);
		states.Single(state => state.Card.Id == cards[0].Id).Zone
			.Should().Be(PhysicalCharacterCardZone.SetAside);
		states.Single(state => state.Card.Id == cards[6].Id).Zone
			.Should().Be(PhysicalCharacterCardZone.SetAside);
		var swap = session.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		swap.Policy.Should().Be(ThiefPolicy());
		swap.PhysicalCards.AdditionalSetAsideCardIds.Should()
			.Equal(cards[6].Id);
		swap.Facts.Should().HaveCount(FactionFactFactions.All.Count + 1);
	}

	[Fact]
	public void NonThiefSwap_WithSelectiveStatusClear_CommitsOneAtomicSemanticMutation()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.Cupid,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var player = session.GetPlayers().First();
		var outgoing = session.GetModeratorPhysicalCharacterCards()
			.Single(cardState => cardState.Card.PrintedRole == MainRoleType.Seer);
		var acquired = session.GetModeratorPhysicalCharacterCards()
			.Single(cardState => cardState.Card.PrintedRole == MainRoleType.Cupid);
		builder.GameService.TryRecordPhysicalCharacterCardOwnership(
			builder.GameId,
			session.RoleLockIn.Version,
			player.Id,
			outgoing.Card.Id).Should().BeTrue();
		builder
			.ArrangeKnownRole(player.Id, MainRoleType.Seer)
			.ArrangePubliclyRevealedRole(player.Id, MainRoleType.Seer)
			.ArrangeStatusEffect(player.Id, StatusEffectTypes.Charmed)
			.ArrangeStatusEffect(player.Id, StatusEffectTypes.LycanthropyInfection);
		var compositionIds = session.RoleLockIn.RoleComposition
			.Select(card => card.Id)
			.ToArray();
		var policy = new PermanentRoleSwapPolicy(
			PrivateRoleKnowledge: PermanentRoleSwapDisposition.Change,
			PublicRevealHistory: PermanentRoleSwapDisposition.Preserve,
			FactionBeneficiary: PermanentRoleSwapDisposition.Change,
			FactionAgents: PermanentRoleSwapDisposition.Change,
			Relationships: PermanentRoleSwapDisposition.Preserve,
			StatusEffects: PermanentRoleSwapDisposition.Clear,
			VotingState: PermanentRoleSwapDisposition.Preserve,
			Restrictions: PermanentRoleSwapDisposition.Preserve,
			Assignments: PermanentRoleSwapDisposition.Preserve,
			RolePowerState: PermanentRoleSwapDisposition.Change);
		var request = new PermanentRoleSwapRequest(
			session.RoleLockIn.Version,
			player.Id,
			MainRoleType.Seer,
			MainRoleType.Cupid,
			new PermanentRoleSwapCardMovement(
				outgoing.Card.Id,
				acquired.Card.Id,
				[]),
			policy,
			VillagerFactionReplacement(),
			new PermanentRoleSwapStateChanges(
				new HashSet<StatusEffectTypes>(),
				new HashSet<StatusEffectTypes> { StatusEffectTypes.Charmed },
				votingStateAfterSwap: null,
				new HashSet<string>(),
				new HashSet<string>()));

		var committed = builder.GameService.TryCommitPermanentRoleSwap(
			builder.GameId,
			request);

		committed.Should().BeTrue();
		player.State.CurrentRole.Should().Be(MainRoleType.Cupid);
		player.State.ModeratorKnownRole.Should().Be(MainRoleType.Cupid);
		player.State.PubliclyRevealedRole.Should().Be(MainRoleType.Seer);
		player.State.GetActiveStatusEffects().Should()
			.NotContain(StatusEffectTypes.Charmed)
			.And.Contain(StatusEffectTypes.LycanthropyInfection);
		session.RoleLockIn.RoleComposition.Select(card => card.Id)
			.Should().Equal(compositionIds);
		var cardStates = session.GetModeratorPhysicalCharacterCards();
		cardStates.Single(state => state.Card.Id == outgoing.Card.Id).Zone
			.Should().Be(PhysicalCharacterCardZone.SetAside);
		cardStates.Single(state => state.Card.Id == acquired.Card.Id)
			.Should().Match<PhysicalCharacterCardState>(state =>
				state.Zone == PhysicalCharacterCardZone.PlayerOwned &&
				state.OwnerPlayerId == player.Id);
		session.GetFactionBeneficiaryKnowledge(player.Id).Faction
			.Should().Be(Faction.Villager);
		session.GetFactionAgentKnowledge(player.Id, Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownNonAgent);
		var swap = session.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		swap.NewPowerInstanceId.Should().NotBe(Guid.Empty);
		swap.PowerInstanceOrigin.Should().Be(RolePowerInstanceOrigin.Swapped);

		builder.GameService.TryCommitPermanentRoleSwap(builder.GameId, request)
			.Should().BeFalse();
		session.GameHistoryLog.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle();
	}

	[Theory]
	[InlineData(PlayerProjectionTamper.CurrentRole)]
	[InlineData(PlayerProjectionTamper.ModeratorKnownRole)]
	[InlineData(PlayerProjectionTamper.PubliclyRevealedRole)]
	[InlineData(PlayerProjectionTamper.ClearedStatusEffect)]
	[InlineData(PlayerProjectionTamper.PreservedStatusEffect)]
	[InlineData(PlayerProjectionTamper.HasVotingRight)]
	[InlineData(PlayerProjectionTamper.DurableVotingPower)]
	public void PermanentRoleSwap_RecoveryRejectsContradictoryPlayerProjection(
		PlayerProjectionTamper tamper)
	{
		var (session, playerId) = CreateStableSimpleSwap();
		var payload = RecoveryPayloadTestDriver.Parse(session.Serialize());
		switch (tamper)
		{
			case PlayerProjectionTamper.CurrentRole:
				payload.RewriteCurrentRole(playerId, MainRoleType.Seer);
				break;
			case PlayerProjectionTamper.ModeratorKnownRole:
				payload.RewriteModeratorKnownRole(playerId, MainRoleType.Seer);
				break;
			case PlayerProjectionTamper.PubliclyRevealedRole:
				payload.RewritePubliclyRevealedRole(
					playerId,
					MainRoleType.SimpleVillager);
				break;
			case PlayerProjectionTamper.ClearedStatusEffect:
				payload.RewriteActiveEffects(
					playerId,
					payload.GetActiveEffects(playerId) |
						StatusEffectTypes.Charmed);
				break;
			case PlayerProjectionTamper.PreservedStatusEffect:
				payload.RewriteActiveEffects(
					playerId,
					payload.GetActiveEffects(playerId) &
						~StatusEffectTypes.LycanthropyInfection);
				break;
			case PlayerProjectionTamper.HasVotingRight:
				payload.RewriteVotingState(
					playerId,
					hasVotingRight: true,
					durableVotingPower: 2);
				break;
			case PlayerProjectionTamper.DurableVotingPower:
				payload.RewriteVotingState(
					playerId,
					hasVotingRight: false,
					durableVotingPower: 1);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(tamper));
		}
		var tampered = payload.Serialize();

		Action rehydrate = () => new GameService().RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
	}

	[Theory]
	[InlineData(FactionSourceTamper.Kind)]
	[InlineData(FactionSourceTamper.Identifier)]
	public void PermanentRoleSwap_RecoveryRejectsNonCanonicalFactionSource(
		FactionSourceTamper tamper)
	{
		var (session, _) = CreateStableSimpleSwap();
		var payload = RecoveryPayloadTestDriver.Parse(session.Serialize());
		if (tamper == FactionSourceTamper.Kind)
		{
			payload.RewriteLatestPermanentRoleSwapSource(
				FactionFactSourceKind.ScheduledObservation);
		}
		else
		{
			payload.RewriteLatestPermanentRoleSwapSourceIdentifier(
				"permanent-role-swap:noncanonical");
		}
		var tampered = payload.Serialize();

		Action rehydrate = () => new GameService().RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
	}

	[Theory]
	[InlineData(FactionBatchTamper.MissingAgent)]
	[InlineData(FactionBatchTamper.ExtraAgent)]
	[InlineData(FactionBatchTamper.WrongBeneficiary)]
	[InlineData(FactionBatchTamper.WrongBeneficiaryPrecedence)]
	[InlineData(FactionBatchTamper.WrongBoundaryOrder)]
	[InlineData(FactionBatchTamper.WrongBoundaryPhase)]
	public void PermanentRoleSwap_RecoveryRejectsInvalidFactionBatch(
		FactionBatchTamper tamper)
	{
		var (session, _) = CreateStableSimpleSwap();
		var payload = ApplyFactionBatchTamper(
			RecoveryPayloadTestDriver.Parse(session.Serialize()),
			tamper);
		var tampered = payload.Serialize();

		Action rehydrate = () => new GameService().RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void PermanentRoleSwap_RecoveryRejectsWrongRoleDerivedAgentDefault()
	{
		var (session, _) = CreateStableSimpleSwap();
		var tampered = RecoveryPayloadTestDriver.Parse(session.Serialize())
			.RewriteLatestPermanentRoleSwapAgentAndCache(
				Faction.Piper,
				FactionAgentKnowledge.KnownAgent)
			.Serialize();

		Action rehydrate = () => new GameService().RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*Permanent Role Swap Faction defaults are invalid*");
	}

	[Fact]
	public void PermanentRoleSwap_RecoveryReplaysLaterSemanticPlayerChanges()
	{
		var (session, playerId) = CreateStableSimpleSwap((builder, player) =>
		{
			builder
				.ArrangeKnownRole(player.Id, MainRoleType.Seer)
				.ArrangePubliclyRevealedRole(
					player.Id,
					MainRoleType.SimpleVillager)
				.ArrangeStatusEffect(player.Id, StatusEffectTypes.Charmed)
				.ArrangeVotingRight(player.Id, hasVotingRight: true);
		});
		var acquiredCardId = session.GetPlayerState(playerId)
			.PhysicalCharacterCardId!.Value;

		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(session.Serialize());
		var recovered = recoveredService.GetGameStateView(recoveredId)!;
		var player = recovered.GetPlayer(playerId);

		player.State.CurrentRole.Should().Be(MainRoleType.Seer);
		player.State.ModeratorKnownRole.Should().Be(MainRoleType.Seer);
		player.State.PubliclyRevealedRole.Should().Be(
			MainRoleType.SimpleVillager);
		player.State.GetActiveStatusEffects().Should()
			.Contain(StatusEffectTypes.Charmed)
			.And.Contain(StatusEffectTypes.LycanthropyInfection);
		player.State.HasVotingRight.Should().BeTrue();
		player.State.DurableVotingPower.Should().Be(2);
		player.State.PhysicalCharacterCardId.Should().Be(acquiredCardId);
		player.State.PhysicalCharacterCardRole.Should().Be(
			MainRoleType.SimpleVillager);
	}

	[Fact]
	public void PermanentRoleSwap_RecoveryRejectsPowerInstanceCollidingWithNativeIdentity()
	{
		var (session, playerId) = CreateStableSimpleSwap();
		var tampered = RecoveryPayloadTestDriver
			.Parse(session.Serialize())
			.RewriteLatestPermanentRoleSwapPowerInstanceId(playerId)
			.Serialize();

		Action rehydrate = () => new GameService().RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*power-instance identity is not fresh*");
	}

	[Fact]
	public void PermanentRoleSwap_RecoveryRejectsReusedEarlierSwapIdentity()
	{
		var (session, firstPowerInstanceId) = CreateStableTwoSwapSession();
		var tampered = RecoveryPayloadTestDriver
			.Parse(session.Serialize())
			.RewriteLatestPermanentRoleSwapPowerInstanceId(firstPowerInstanceId)
			.Serialize();

		Action rehydrate = () => new GameService().RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*power-instance identity is not fresh*");
	}

	[Fact]
	public void NonThiefSwap_AcquiresAnotherPlayersCard_WithoutCopyingPlayerBoundState()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.Cupid,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var continuingPlayer = players[0];
		var priorCardOwner = players[1];
		var outgoing = session.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.PrintedRole == MainRoleType.Seer);
		var acquired = session.GetModeratorPhysicalCharacterCards()
			.First(state => state.Card.PrintedRole == MainRoleType.SimpleVillager);
		builder.GameService.TryRecordPhysicalCharacterCardOwnership(
			builder.GameId,
			session.RoleLockIn.Version,
			continuingPlayer.Id,
			outgoing.Card.Id).Should().BeTrue();
		builder.GameService.TryRecordPhysicalCharacterCardOwnership(
			builder.GameId,
			session.RoleLockIn.Version,
			priorCardOwner.Id,
			acquired.Card.Id).Should().BeTrue();
		builder
			.ArrangeKnownRole(continuingPlayer.Id, MainRoleType.Seer)
			.ArrangeKnownRole(priorCardOwner.Id, MainRoleType.SimpleVillager)
			.ArrangePubliclyRevealedRole(priorCardOwner.Id, MainRoleType.SimpleVillager)
			.ArrangeStatusEffect(priorCardOwner.Id, StatusEffectTypes.Charmed)
			.ArrangeVotingRight(priorCardOwner.Id, hasVotingRight: false);
		var request = new PermanentRoleSwapRequest(
			session.RoleLockIn.Version,
			continuingPlayer.Id,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			new PermanentRoleSwapCardMovement(
				outgoing.Card.Id,
				acquired.Card.Id,
				[],
				expectedAcquiredCardOwnerPlayerId: priorCardOwner.Id),
			ThiefPolicy(),
			VillagerFactionReplacement(),
			PermanentRoleSwapStateChanges.None);

		builder.GameService.TryCommitPermanentRoleSwap(builder.GameId, request)
			.Should().BeTrue();

		continuingPlayer.State.CurrentRole.Should().Be(MainRoleType.SimpleVillager);
		continuingPlayer.State.PhysicalCharacterCardId.Should().Be(acquired.Card.Id);
		priorCardOwner.State.PhysicalCharacterCardId.Should().BeNull();
		priorCardOwner.State.PhysicalCharacterCardRole.Should().BeNull();
		priorCardOwner.State.CurrentRole.Should().Be(MainRoleType.SimpleVillager);
		priorCardOwner.State.ModeratorKnownRole.Should().Be(MainRoleType.SimpleVillager);
		priorCardOwner.State.PubliclyRevealedRole.Should().Be(MainRoleType.SimpleVillager);
		priorCardOwner.State.GetActiveStatusEffects().Should()
			.Contain(StatusEffectTypes.Charmed);
		priorCardOwner.State.HasVotingRight.Should().BeFalse();
		var cardState = session.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.Id == acquired.Card.Id);
		cardState.Zone.Should().Be(PhysicalCharacterCardZone.PlayerOwned);
		cardState.OwnerPlayerId.Should().Be(continuingPlayer.Id);
		AdvanceToStableWerewolfObservationBoundary(builder, players[2].Id);

		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(session.Serialize());
		var recoveredSession = recoveredService.GetGameStateView(recoveredGameId)!;
		var recoveredPriorOwner = recoveredSession.GetPlayer(priorCardOwner.Id);
		recoveredPriorOwner.State.PhysicalCharacterCardId.Should().BeNull();
		recoveredPriorOwner.State.CurrentRole.Should().Be(MainRoleType.SimpleVillager);
		recoveredPriorOwner.State.ModeratorKnownRole.Should().Be(MainRoleType.SimpleVillager);
		recoveredPriorOwner.State.PubliclyRevealedRole.Should().Be(MainRoleType.SimpleVillager);
		recoveredPriorOwner.State.GetActiveStatusEffects().Should()
			.Contain(StatusEffectTypes.Charmed);
		recoveredPriorOwner.State.HasVotingRight.Should().BeFalse();
		recoveredSession.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void PermanentRoleSwap_RejectsUnsupportedOpaqueClearTargetsWithoutMutation(
		bool clearRestrictions)
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.Cupid,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var player = session.GetPlayers().First();
		var outgoing = session.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.PrintedRole == MainRoleType.Seer);
		var acquired = session.GetModeratorPhysicalCharacterCards()
			.First(state => state.Card.PrintedRole == MainRoleType.SimpleVillager);
		builder.GameService.TryRecordPhysicalCharacterCardOwnership(
			builder.GameId,
			session.RoleLockIn.Version,
			player.Id,
			outgoing.Card.Id).Should().BeTrue();
		builder.ArrangeKnownRole(player.Id, MainRoleType.Seer);
		var policy = ThiefPolicy() with
		{
			Restrictions = clearRestrictions
				? PermanentRoleSwapDisposition.Clear
				: PermanentRoleSwapDisposition.Preserve,
			Assignments = clearRestrictions
				? PermanentRoleSwapDisposition.Preserve
				: PermanentRoleSwapDisposition.Clear
		};
		var stateChanges = new PermanentRoleSwapStateChanges(
			new HashSet<StatusEffectTypes>(),
			new HashSet<StatusEffectTypes>(),
			votingStateAfterSwap: null,
			clearRestrictions ? new HashSet<string> { "restriction-1" } : [],
			clearRestrictions ? [] : new HashSet<string> { "assignment-1" });
		var request = new PermanentRoleSwapRequest(
			session.RoleLockIn.Version,
			player.Id,
			MainRoleType.Seer,
			MainRoleType.Cupid,
			new PermanentRoleSwapCardMovement(
				outgoing.Card.Id,
				acquired.Card.Id,
				[]),
			policy,
			VillagerFactionReplacement(),
			stateChanges);
		var beforeCards = session.GetModeratorPhysicalCharacterCards().ToArray();
		var beforeHistoryCount = session.GameHistoryLog.Count();

		builder.GameService.TryCommitPermanentRoleSwap(builder.GameId, request)
			.Should().BeFalse();

		player.State.CurrentRole.Should().Be(MainRoleType.Seer);
		session.GetModeratorPhysicalCharacterCards().Should().Equal(beforeCards);
		session.GameHistoryLog.Should().HaveCount(beforeHistoryCount);
	}

	[Fact]
	public void PermanentRoleSwap_ClearsCommittedLoversRelationshipAtomicallyAndRecovers()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.Cupid,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var swappedPlayer = players[0];
		var lover = players[1];
		var outgoing = session.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.PrintedRole == MainRoleType.Seer);
		var acquired = session.GetModeratorPhysicalCharacterCards()
			.First(state => state.Card.PrintedRole == MainRoleType.SimpleVillager);
		builder.GameService.TryRecordPhysicalCharacterCardOwnership(
			builder.GameId,
			session.RoleLockIn.Version,
			swappedPlayer.Id,
			outgoing.Card.Id).Should().BeTrue();
		builder.ArrangeKnownRole(swappedPlayer.Id, MainRoleType.Seer);
		((GameSession)session).CommitLoversPair(
			[swappedPlayer.Id, lover.Id],
			new RolePowerInstanceIdentity(
				swappedPlayer.Id,
				MainRoleType.Cupid,
				"test-lovers-link",
				swappedPlayer.Id,
				RolePowerInstanceOrigin.Native));
		var request = new PermanentRoleSwapRequest(
			session.RoleLockIn.Version,
			swappedPlayer.Id,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			new PermanentRoleSwapCardMovement(
				outgoing.Card.Id,
				acquired.Card.Id,
				[]),
			ThiefPolicy() with
			{
				Relationships = PermanentRoleSwapDisposition.Clear
			},
			VillagerFactionReplacement(),
			new PermanentRoleSwapStateChanges(
				new HashSet<StatusEffectTypes> { StatusEffectTypes.Lovers },
				new HashSet<StatusEffectTypes>(),
				votingStateAfterSwap: null,
				new HashSet<string>(),
				new HashSet<string>()));

		builder.GameService.TryCommitPermanentRoleSwap(builder.GameId, request)
			.Should().BeTrue();

		swappedPlayer.State.HasStatusEffect(StatusEffectTypes.Lovers)
			.Should().BeFalse();
		lover.State.HasStatusEffect(StatusEffectTypes.Lovers).Should().BeFalse();
		AdvanceToStableWerewolfObservationBoundary(builder, players[2].Id);
		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(session.Serialize());
		var recovered = recoveredService.GetGameStateView(recoveredGameId)!;
		recovered.GetPlayer(swappedPlayer.Id).State
			.HasStatusEffect(StatusEffectTypes.Lovers).Should().BeFalse();
		recovered.GetPlayer(lover.Id).State
			.HasStatusEffect(StatusEffectTypes.Lovers).Should().BeFalse();
		recovered.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle();
	}

	[Fact]
	public void PermanentRoleSwap_PreservesDominantBeneficiary_WhileReplacingCompleteAgentFacts()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.Cupid,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var player = session.GetPlayers().First();
		var outgoing = session.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.PrintedRole == MainRoleType.Seer);
		var acquired = session.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.PrintedRole == MainRoleType.Cupid);
		builder.GameService.TryRecordPhysicalCharacterCardOwnership(
			builder.GameId,
			session.RoleLockIn.Version,
			player.Id,
			outgoing.Card.Id).Should().BeTrue();
		builder.ArrangeKnownRole(player.Id, MainRoleType.Seer);
		var dominantBoundary = new FactionFactEffectiveBoundary(
			session.TurnNumber,
			session.GetCurrentPhase(),
			session.GameHistoryLog.Count());
		builder.ArrangeExplicitFactionTransition(
			"test-cross-faction-lovers-before-swap",
			FactionFact.Beneficiary(
				player.Id,
				Faction.CrossFactionLovers,
				dominantBoundary,
				beneficiaryPrecedence: 1),
			FactionFact.Agent(
				player.Id,
				Faction.Werewolf,
				FactionAgentKnowledge.KnownAgent,
				dominantBoundary));
		var request = new PermanentRoleSwapRequest(
			session.RoleLockIn.Version,
			player.Id,
			MainRoleType.Seer,
			MainRoleType.Cupid,
			new PermanentRoleSwapCardMovement(
				outgoing.Card.Id,
				acquired.Card.Id,
				[]),
			ThiefPolicy(),
			VillagerFactionReplacement(),
			PermanentRoleSwapStateChanges.None);

		builder.GameService.TryCommitPermanentRoleSwap(builder.GameId, request)
			.Should().BeTrue();

		session.GetFactionBeneficiaryKnowledge(player.Id).Faction
			.Should().Be(Faction.CrossFactionLovers);
		foreach (var faction in FactionFactFactions.All)
		{
			session.GetFactionAgentKnowledge(player.Id, faction)
				.Should().Be(FactionAgentKnowledge.KnownNonAgent);
		}
		session.GetFactionAgentKnowledge(player.Id, Faction.Angel)
			.Should().Be(FactionAgentKnowledge.Unknown);
		var swap = session.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		swap.Facts.Where(fact => fact.Type == FactionFactType.Agent)
			.Should().HaveCount(FactionFactFactions.All.Count);
		swap.Facts.Should().ContainSingle(fact =>
			fact.Type == FactionFactType.Beneficiary &&
			fact.Faction == Faction.Villager &&
			fact.BeneficiaryPrecedence == 0);
	}

	[Fact]
	public void SwappedRolePower_AfterStableRecovery_UsesFreshSwapLineage()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var swappedPlayer = players[0];
		var werewolf = players[1];
		var victim = players[4];
		var outgoing = session.GetModeratorPhysicalCharacterCards()
			.First(state => state.Card.PrintedRole == MainRoleType.SimpleVillager);
		var acquired = session.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.PrintedRole == MainRoleType.Seer);
		builder.GameService.TryRecordPhysicalCharacterCardOwnership(
			builder.GameId,
			session.RoleLockIn.Version,
			swappedPlayer.Id,
			outgoing.Card.Id).Should().BeTrue();
		builder.ArrangeKnownRole(swappedPlayer.Id, MainRoleType.SimpleVillager);
		var request = new PermanentRoleSwapRequest(
			session.RoleLockIn.Version,
			swappedPlayer.Id,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer,
			new PermanentRoleSwapCardMovement(
				outgoing.Card.Id,
				acquired.Card.Id,
				[]),
			ThiefPolicy(),
			VillagerFactionReplacement(),
			PermanentRoleSwapStateChanges.None);
		builder.GameService.TryCommitPermanentRoleSwap(builder.GameId, request)
			.Should().BeTrue();
		var swap = session.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		builder.ConfirmGameStart().IsSuccess.Should().BeTrue();
		var afterNightStart = builder.ConfirmNightStart();
		var observeWerewolves = InstructionAssert
			.ExpectSuccessWithType<SelectPlayersInstruction>(afterNightStart);
		builder.Process(observeWerewolves.CreateResponse([werewolf.Id]))
			.IsSuccess.Should().BeTrue();
		var serialized = session.Serialize();
		var recoveredPolicy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var recoveredService = new GameService(recoveredPolicy);

		var recoveredGameId = recoveredService.RehydrateSession(serialized);

		var recoveredSession = recoveredService.GetGameStateView(recoveredGameId)!;
		var recoveredSwap = recoveredSession.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		recoveredSwap.NewPowerInstanceId.Should().Be(swap.NewPowerInstanceId);
		recoveredSession.GetPlayer(swappedPlayer.Id).State.PhysicalCharacterCardId
			.Should().Be(acquired.Card.Id);
		var selectVictim = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			recoveredService.GetCurrentInstruction(recoveredGameId));
		var afterVictim = recoveredService.ProcessInstruction(
			recoveredGameId,
			selectVictim.CreateResponse([victim.Id]));
		var sleepWerewolves = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(afterVictim);
		var afterWerewolves = recoveredService.ProcessInstruction(
			recoveredGameId,
			sleepWerewolves.CreateResponse());
		var wakeSeer = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(afterWerewolves);

		var afterSeerWake = recoveredService.ProcessInstruction(
			recoveredGameId,
			wakeSeer.CreateResponse());

		InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			afterSeerWake).Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectSeerTarget);
		var attempt = recoveredPolicy.ObservedAttempts
			.Should().ContainSingle().Subject;
		attempt.ActingPlayer.Id.Should().Be(swappedPlayer.Id);
		attempt.SourceRole.Should().Be(MainRoleType.Seer);
		attempt.PowerInstance.Id.Should().Be(swap.NewPowerInstanceId);
		attempt.PowerInstance.Origin.Should().Be(RolePowerInstanceOrigin.Swapped);
	}

	[Fact]
	public void SwappedOneUsePower_CommitsAndRecoversOnlyTheFreshCompositeResource()
	{
		var livePolicy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var builder = GameTestBuilder.Create()
			.WithRolePowerAvailabilityPolicy(livePolicy)
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var swappedPlayer = players[0];
		var werewolf = players[1];
		var victim = players[6];
		var outgoing = session.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.PrintedRole == MainRoleType.Seer);
		var acquired = session.GetModeratorPhysicalCharacterCards()
			.Single(state =>
				state.Card.PrintedRole == MainRoleType.AccursedWolfFather);
		builder.GameService.TryRecordPhysicalCharacterCardOwnership(
			builder.GameId,
			session.RoleLockIn.Version,
			swappedPlayer.Id,
			outgoing.Card.Id).Should().BeTrue();
		builder.ArrangeKnownRole(swappedPlayer.Id, MainRoleType.Seer);
		var request = new PermanentRoleSwapRequest(
			session.RoleLockIn.Version,
			swappedPlayer.Id,
			MainRoleType.Seer,
			MainRoleType.AccursedWolfFather,
			new PermanentRoleSwapCardMovement(
				outgoing.Card.Id,
				acquired.Card.Id,
				[]),
			ThiefPolicy(),
			WerewolfFactionReplacement(),
			PermanentRoleSwapStateChanges.None);
		builder.GameService.TryCommitPermanentRoleSwap(builder.GameId, request)
			.Should().BeTrue();
		var swap = session.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		builder.ConfirmGameStart().IsSuccess.Should().BeTrue();
		builder.ConfirmNightStart().IsSuccess.Should().BeTrue();
		var wake = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id, swappedPlayer.Id],
					victim.Id));
		var choice = InstructionAssert
			.ExpectSuccessWithType<SelectOptionsInstruction>(
				builder.Process(wake.CreateResponse()));

		var expectedSleep = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(choice.CreateResponse(
					AccursedWolfFatherInfectionOptionIds.Infect)));

		var attempt = livePolicy.ObservedAttempts
			.Should().ContainSingle().Subject;
		attempt.PowerInstance.Id.Should().Be(swap.NewPowerInstanceId);
		attempt.PowerInstance.Origin.Should().Be(RolePowerInstanceOrigin.Swapped);
		attempt.OneUseResource.Should().NotBeNull();
		attempt.OneUseResource!.OwningPowerInstance.Should()
			.Be(attempt.PowerInstance);
		var liveCommit = session.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		liveCommit.PowerInstanceId.Should().Be(swap.NewPowerInstanceId);
		liveCommit.PowerInstanceOrigin.Should().Be(RolePowerInstanceOrigin.Swapped);
		var recoveredPolicy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var recoveredService = new GameService(recoveredPolicy);

		var recoveredGameId = recoveredService.RehydrateSession(session.Serialize());

		var recoveredSession = recoveredService.GetGameStateView(recoveredGameId)!;
		var recoveredSleep = InstructionAssert.ExpectType<ConfirmationInstruction>(
			recoveredService.GetCurrentInstruction(recoveredGameId));
		recoveredSleep.InstructionId.Should().Be(expectedSleep.InstructionId);
		recoveredSleep.Semantic.Should().Be(expectedSleep.Semantic);
		recoveredSleep.AffectedPlayerIds.Should().Equal(swappedPlayer.Id);
		var recoveredCommit = recoveredSession.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		recoveredCommit.ResourceIdentity.Should().Be(liveCommit.ResourceIdentity);
		var swappedResource = new OneUseRolePowerResourceIdentity(
			swappedPlayer.Id,
			MainRoleType.AccursedWolfFather,
			"accursed-wolf-father-infection",
			swap.NewPowerInstanceId,
			RolePowerInstanceOrigin.Swapped,
			AccursedWolfFatherRole.InfectionResourceId);
		var nativeResource = swappedResource with
		{
			PowerInstanceId = swappedPlayer.Id,
			PowerInstanceOrigin = RolePowerInstanceOrigin.Native
		};
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			recoveredSession,
			swappedResource).Should().BeTrue();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			recoveredSession,
			nativeResource).Should().BeFalse();
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();

		recoveredService.ProcessInstruction(
			recoveredGameId,
			recoveredSleep.CreateResponse()).IsSuccess.Should().BeTrue();
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();
		recoveredSession.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
	}

	private static void AdvanceToStableWerewolfObservationBoundary(
		GameTestBuilder builder,
		Guid werewolfId)
	{
		builder.ConfirmGameStart().IsSuccess.Should().BeTrue();
		var afterNightStart = builder.ConfirmNightStart();
		var observeWerewolves = InstructionAssert
			.ExpectSuccessWithType<SelectPlayersInstruction>(afterNightStart);
		builder.Process(observeWerewolves.CreateResponse([werewolfId]))
			.IsSuccess.Should().BeTrue();
	}

	private static RecoveryPayloadTestDriver ApplyFactionBatchTamper(
		RecoveryPayloadTestDriver payload,
		FactionBatchTamper tamper)
	{
		if (tamper == FactionBatchTamper.WrongBeneficiary)
		{
			return payload.RewriteLatestPermanentRoleSwapBeneficiaryAndCache(
				Faction.Werewolf);
		}

		payload.RewriteLatestPermanentRoleSwapFacts(facts =>
			RewriteFactionBatch(facts, tamper));
		return payload;
	}

	private static ImmutableArray<FactionFact> RewriteFactionBatch(
		ImmutableArray<FactionFact> facts,
		FactionBatchTamper tamper)
	{
		var firstAgent = facts.First(fact =>
			fact.Type == FactionFactType.Agent);
		return tamper switch
		{
			FactionBatchTamper.MissingAgent => facts
				.Where(fact => fact != firstAgent)
				.ToImmutableArray(),
			FactionBatchTamper.ExtraAgent => facts.Add(
				FactionFact.Agent(
					firstAgent.PlayerId,
					firstAgent.Faction,
					firstAgent.AgentKnowledge!.Value,
					new FactionFactEffectiveBoundary(
						firstAgent.EffectiveBoundary.TurnNumber,
						firstAgent.EffectiveBoundary.Phase,
						firstAgent.EffectiveBoundary.Order + 1))),
			FactionBatchTamper.WrongBeneficiaryPrecedence => facts
				.Select(fact =>
					fact.Type == FactionFactType.Beneficiary
						? FactionFact.Beneficiary(
							fact.PlayerId,
							fact.Faction,
							fact.EffectiveBoundary,
							beneficiaryPrecedence: 1)
						: fact)
				.ToImmutableArray(),
			FactionBatchTamper.WrongBoundaryOrder => facts
				.Select(fact => RewriteFactionFactBoundary(
					fact,
					new FactionFactEffectiveBoundary(
						fact.EffectiveBoundary.TurnNumber,
						fact.EffectiveBoundary.Phase,
						fact.EffectiveBoundary.Order + 1)))
				.ToImmutableArray(),
			FactionBatchTamper.WrongBoundaryPhase => facts
				.Select(fact => RewriteFactionFactBoundary(
					fact,
					new FactionFactEffectiveBoundary(
						fact.EffectiveBoundary.TurnNumber,
						fact.EffectiveBoundary.Phase == GamePhase.Night
							? GamePhase.Dawn
							: GamePhase.Night,
						fact.EffectiveBoundary.Order)))
				.ToImmutableArray(),
			_ => throw new ArgumentOutOfRangeException(nameof(tamper))
		};
	}

	private static FactionFact RewriteFactionFactBoundary(
		FactionFact fact,
		FactionFactEffectiveBoundary boundary) =>
		fact.Type == FactionFactType.Beneficiary
			? FactionFact.Beneficiary(
				fact.PlayerId,
				fact.Faction,
				boundary,
				fact.BeneficiaryPrecedence!.Value)
			: FactionFact.Agent(
				fact.PlayerId,
				fact.Faction,
				fact.AgentKnowledge!.Value,
				boundary);

	private static (IGameSession Session, Guid PlayerId) CreateStableSimpleSwap(
		Action<GameTestBuilder, IPlayer>? afterSwap = null)
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var player = players[0];
		var outgoing = session.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.PrintedRole == MainRoleType.Seer);
		var acquired = session.GetModeratorPhysicalCharacterCards()
			.First(state => state.Card.PrintedRole == MainRoleType.SimpleVillager);
		builder.GameService.TryRecordPhysicalCharacterCardOwnership(
			builder.GameId,
			session.RoleLockIn.Version,
			player.Id,
			outgoing.Card.Id).Should().BeTrue();
		builder
			.ArrangeKnownRole(player.Id, MainRoleType.Seer)
			.ArrangePubliclyRevealedRole(player.Id, MainRoleType.Seer)
			.ArrangeStatusEffect(player.Id, StatusEffectTypes.Charmed)
			.ArrangeStatusEffect(
				player.Id,
				StatusEffectTypes.LycanthropyInfection);
		builder.GameService.TryCommitPermanentRoleSwap(
			builder.GameId,
			new PermanentRoleSwapRequest(
				session.RoleLockIn.Version,
				player.Id,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				new PermanentRoleSwapCardMovement(
					outgoing.Card.Id,
					acquired.Card.Id,
					[]),
				ThiefPolicy() with
				{
					StatusEffects = PermanentRoleSwapDisposition.Clear,
					VotingState = PermanentRoleSwapDisposition.Change
				},
				VillagerFactionReplacement(),
				new PermanentRoleSwapStateChanges(
					new HashSet<StatusEffectTypes>(),
					new HashSet<StatusEffectTypes>
					{
						StatusEffectTypes.Charmed
					},
					new PermanentRoleSwapVotingState(
						HasVotingRight: false,
						DurableVotingPower: 2),
					new HashSet<string>(),
					new HashSet<string>()))).Should().BeTrue();
		afterSwap?.Invoke(builder, player);
		AdvanceToStableWerewolfObservationBoundary(builder, players[1].Id);
		return (session, player.Id);
	}

	private static (IGameSession Session, Guid FirstPowerInstanceId)
		CreateStableTwoSwapSession()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var seerCard = session.GetModeratorPhysicalCharacterCards()
			.Single(state => state.Card.PrintedRole == MainRoleType.Seer);
		var werewolfCard = session.GetModeratorPhysicalCharacterCards()
			.First(state =>
				state.Card.PrintedRole == MainRoleType.SimpleWerewolf);
		var villagerCards = session.GetModeratorPhysicalCharacterCards()
			.Where(state =>
				state.Card.PrintedRole == MainRoleType.SimpleVillager)
			.ToArray();
		builder.GameService.TryRecordPhysicalCharacterCardOwnership(
			builder.GameId,
			session.RoleLockIn.Version,
			players[0].Id,
			seerCard.Card.Id).Should().BeTrue();
		builder.GameService.TryRecordPhysicalCharacterCardOwnership(
			builder.GameId,
			session.RoleLockIn.Version,
			players[1].Id,
			werewolfCard.Card.Id).Should().BeTrue();
		builder
			.ArrangeKnownRole(players[0].Id, MainRoleType.Seer)
			.ArrangeKnownRole(players[1].Id, MainRoleType.SimpleWerewolf);
		builder.GameService.TryCommitPermanentRoleSwap(
			builder.GameId,
			CreateRequest(
				players[0].Id,
				MainRoleType.Seer,
				seerCard.Card.Id,
				villagerCards[0].Card.Id)).Should().BeTrue();
		builder.GameService.TryCommitPermanentRoleSwap(
			builder.GameId,
			CreateRequest(
				players[1].Id,
				MainRoleType.SimpleWerewolf,
				werewolfCard.Card.Id,
				villagerCards[1].Card.Id)).Should().BeTrue();
		var firstPowerInstanceId = session.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.First().NewPowerInstanceId;
		AdvanceToStableWerewolfObservationBoundary(builder, players[2].Id);
		return (session, firstPowerInstanceId);

		PermanentRoleSwapRequest CreateRequest(
			Guid playerId,
			MainRoleType expectedRole,
			Guid outgoingCardId,
			Guid acquiredCardId) =>
			new(
				session.RoleLockIn.Version,
				playerId,
				expectedRole,
				MainRoleType.SimpleVillager,
				new PermanentRoleSwapCardMovement(
					outgoingCardId,
					acquiredCardId,
					[]),
				ThiefPolicy(),
				VillagerFactionReplacement(),
				PermanentRoleSwapStateChanges.None);
	}

	private static PermanentRoleSwapFactionReplacement VillagerFactionReplacement() =>
		new(
			Faction.Villager,
			FactionFactFactions.All.ToDictionary(
				faction => faction,
				_ => FactionAgentKnowledge.KnownNonAgent));

	private static PermanentRoleSwapFactionReplacement WerewolfFactionReplacement() =>
		new(
			Faction.Werewolf,
			FactionFactFactions.All.ToDictionary(
				faction => faction,
				faction => faction == Faction.Werewolf
					? FactionAgentKnowledge.KnownAgent
					: FactionAgentKnowledge.KnownNonAgent));

	private static PermanentRoleSwapPolicy ThiefPolicy() =>
		new(
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

	public enum PlayerProjectionTamper
	{
		CurrentRole,
		ModeratorKnownRole,
		PubliclyRevealedRole,
		ClearedStatusEffect,
		PreservedStatusEffect,
		HasVotingRight,
		DurableVotingPower
	}

	public enum FactionSourceTamper
	{
		Kind,
		Identifier
	}

	public enum FactionBatchTamper
	{
		MissingAgent,
		ExtraAgent,
		WrongBeneficiary,
		WrongBeneficiaryPrecedence,
		WrongBoundaryOrder,
		WrongBoundaryPhase
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
}
