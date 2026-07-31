using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class CupidRoleTests(ITestOutputHelper output)
	: DiagnosticTestBase(output)
{
	[Fact]
	public void NightOne_KnownHolder_WakesAndSelectsExactlyTwoLivingPlayers()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Cupid,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var cupid = players[1];
		builder.ArrangeKnownRole(cupid.Id, MainRoleType.Cupid);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().Equal(cupid.Id);
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		selection.CountConstraint.Should().Be(NumberRangeConstraint.Exact(2));
		selection.SelectablePlayerIds.Should().BeEquivalentTo(
			players.Select(player => player.Id));
		selection.AffectedPlayerIds.Should().Equal(cupid.Id);
		selection.PublicAnnouncement.Should().BeNull();
		selection.PrivateInstruction.Should().NotBeNullOrWhiteSpace();

		var lovers = new[] { cupid.Id, players[3].Id };
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(selection.CreateResponse(lovers.ToHashSet())));

		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeLovers);
		recognition.AffectedPlayerIds.Should().BeEquivalentTo(lovers);
		recognition.PublicAnnouncement.Should().BeNull();
		recognition.PrivateInstruction.Should().NotBeNullOrWhiteSpace();
		var session = builder.GetGameState()!;
		lovers.Should().OnlyContain(playerId =>
			session.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Lovers));
		session.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType == NightActionType.CupidLink);

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(recognition.CreateResponse()));
		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().BeEquivalentTo(lovers);
		var werewolfWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));
		werewolfWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		werewolfWake.AffectedPlayerIds.Should().Equal(werewolf.Id);
		builder.CompleteWerewolfNightAction(
				[werewolf.Id],
				players[6].Id)
			.IsSuccess.Should().BeTrue();
		session.RequireKnownFactionBeneficiary(cupid.Id)
			.Should().Be(Faction.Villager);
		session.RequireKnownFactionBeneficiary(players[3].Id)
			.Should().Be(Faction.Villager);
		session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.SelectMany(entry => entry.Facts)
			.Should().NotContain(fact =>
				lovers.Contains(fact.PlayerId) &&
				fact.Faction == Faction.CrossFactionLovers);
		MarkTestCompleted();
	}

	[Fact]
	public void UnknownLinkBeneficiaries_InitialClosureClassifiesAtLinkBoundaryAndDominatesLaterChange()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Cupid,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var cupid = players[1];
		var villager = players[2];
		builder.ArrangeKnownRole(cupid.Id, MainRoleType.Cupid);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(selection.CreateResponse(
					[werewolf.Id, villager.Id])));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(recognition.CreateResponse()));
		builder.Process(sleep.CreateResponse()).IsSuccess.Should().BeTrue();

		builder.CompleteWerewolfNightAction(
			[werewolf.Id],
			players[6].Id).IsSuccess.Should().BeTrue();

		var session = builder.GetGameState()!;
		session.RequireKnownFactionBeneficiary(werewolf.Id)
			.Should().Be(Faction.CrossFactionLovers);
		session.RequireKnownFactionBeneficiary(villager.Id)
			.Should().Be(Faction.CrossFactionLovers);
		var laterBoundary = new FactionFactEffectiveBoundary(
			session.TurnNumber,
			session.GetCurrentPhase(),
			session.GameHistoryLog.Count());
		builder.ArrangeExplicitFactionTransition(
			"later-lover-beneficiary-change",
			FactionFact.Beneficiary(
				villager.Id,
				Faction.Werewolf,
				laterBoundary));
		session.RequireKnownFactionBeneficiary(villager.Id)
			.Should().Be(Faction.CrossFactionLovers);
		MarkTestCompleted();
	}

	[Fact]
	public void CommittedPair_FreshServiceRestoresRecognitionAndStatusesWithoutReselection()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Cupid,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var cupid = players[1];
		var lovers = new[] { players[2].Id, players[4].Id };
		builder.ArrangeKnownRole(cupid.Id, MainRoleType.Cupid);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var acceptedSelection = selection.CreateResponse(lovers.ToHashSet());
		var expectedRecognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(acceptedSelection));
		var serialized = builder.GetGameState()!.Serialize();
		var freshService = new GameService();

		var gameId = freshService.RehydrateSession(serialized);
		var recoveredSession = freshService.GetGameStateView(gameId)!;
		var recoveredRecognition = freshService.GetCurrentInstruction(gameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredRecognition.Should().BeEquivalentTo(expectedRecognition);
		lovers.Should().OnlyContain(playerId =>
			recoveredSession.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Lovers));
		var beforeReplay = PublicGameSessionSnapshot.Capture(
			freshService,
			gameId);
		Action replay = () => freshService.ProcessInstruction(
			gameId,
			acceptedSelection);
		replay.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(freshService, gameId)
			.Should().BeEquivalentTo(
				beforeReplay,
				options => options.WithStrictOrdering());

		var sleep = freshService.ProcessInstruction(
				gameId,
				recoveredRecognition.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().BeEquivalentTo(lovers);
		freshService.ProcessInstruction(gameId, sleep.CreateResponse())
			.IsSuccess.Should().BeTrue();
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void CommittedPair_RecoveryRejectsExtraOrMissingLoversStatus(
		bool addExtraLover)
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Cupid,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var cupid = players[1];
		var lovers = new[] { players[2].Id, players[4].Id };
		builder.ArrangeKnownRole(cupid.Id, MainRoleType.Cupid);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		builder.Process(selection.CreateResponse(lovers.ToHashSet()))
			.IsSuccess.Should().BeTrue();
		var payload = RecoveryPayloadTestDriver.Parse(
			builder.GetGameState()!.Serialize());
		var tamperedPlayerId = addExtraLover ? players[3].Id : lovers[0];
		var activeEffects = payload.GetActiveEffects(tamperedPlayerId);
		payload.RewriteActiveEffects(
			tamperedPlayerId,
			addExtraLover
				? activeEffects | StatusEffectTypes.Lovers
				: activeEffects & ~StatusEffectTypes.Lovers);
		var freshService = new GameService();

		Action rehydrate = () =>
			freshService.RehydrateSession(payload.Serialize());

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*Lovers statuses do not match committed history*");
		MarkTestCompleted();
	}

	[Fact]
	public void TwoSistersSleep_WhenSistersAreLovers_FreshServiceRestoresUnambiguously()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Cupid,
				MainRoleType.TwoSisters,
				MainRoleType.TwoSisters,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var cupid = players[1];
		var sisters = players.Skip(2).Take(2).ToArray();
		var sisterIds = sisters.Select(player => player.Id).ToHashSet();
		builder.ArrangeKnownRole(cupid.Id, MainRoleType.Cupid);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var cupidWake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var loversSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(cupidWake.CreateResponse()));
		var loversRecognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					loversSelection.CreateResponse(sisterIds)));
		var loversSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(loversRecognition.CreateResponse()));
		var sistersIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(loversSleep.CreateResponse()));
		sistersIdentification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		sistersIdentification.RoleIdentification.Should().Be(
			MainRoleType.TwoSisters);
		var sistersRecognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					sistersIdentification.CreateResponse(sisterIds)));
		var sistersSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sistersRecognition.CreateResponse()));
		sistersSleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersGoToSleep.Format(
				MainRoleType.TwoSisters.GetPublicName()));
		var session = (GameSession)builder.GetGameState()!;
		session.CaptureRecoveryBoundary(RecoveryBoundaryKey.Instance);
		var serialized = session.Serialize();
		var freshService = new GameService();

		var gameId = freshService.RehydrateSession(serialized);
		var recoveredSleep = freshService.GetCurrentInstruction(gameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredSleep.Should().BeEquivalentTo(sistersSleep);
		recoveredSleep.AffectedPlayerIds.Should().BeEquivalentTo(sisterIds);
		MarkTestCompleted();
	}

	[Fact]
	public void LoverEliminated_HeartbreakRecursesBeforeHunterFinalShot()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Werewolf",
				"Cupid",
				"Hunter lover",
				"Attacked lover",
				"Shot target",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Cupid,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var cupid = players[1];
		var hunterLover = players[2];
		var attackedLover = players[3];
		builder.ArrangeKnownRole(cupid.Id, MainRoleType.Cupid);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(selection.CreateResponse(
					[hunterLover.Id, attackedLover.Id])));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(recognition.CreateResponse()));
		builder.Process(sleep.CreateResponse()).IsSuccess.Should().BeTrue();

		var finishNight = builder.CompleteWerewolfNightAction(
				[werewolf.Id],
				attackedLover.Id)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var attackedReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		attackedReveal.PlayersForAssignment.Should().Equal(attackedLover.Id);
		var hunterReveal = builder.Process(attackedReveal.CreateResponse(new()
			{
				[attackedLover.Id] = MainRoleType.SimpleVillager
			}))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		hunterReveal.PlayersForAssignment.Should().Equal(hunterLover.Id);

		var finalShot = builder.Process(hunterReveal.CreateResponse(new()
			{
				[hunterLover.Id] = MainRoleType.Hunter
			}))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		finalShot.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		var session = builder.GetGameState()!;
		session.GetPlayerState(attackedLover.Id).Health.Should().Be(
			PlayerHealth.Dead);
		session.GetPlayerState(hunterLover.Id).Health.Should().Be(
			PlayerHealth.Dead);
		session.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == attackedLover.Id &&
				entry.Reason == EliminationReason.WerewolfAttack);
		session.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == hunterLover.Id &&
				entry.Reason == EliminationReason.LoversHeartbreak);
		MarkTestCompleted();
	}

	[Fact]
	public void UnavailablePower_SleepsWithoutPairAndEvaluatesAvailabilityExactlyOnce()
	{
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Denied);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Cupid,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var cupid = players[1];
		builder.ArrangeKnownRole(cupid.Id, MainRoleType.Cupid);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[0].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(wake.CreateResponse()));

		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(cupid.Id);
		policy.Attempts.Should().ContainSingle();
		var attempt = policy.Attempts.Single();
		attempt.ActingPlayer.Id.Should().Be(cupid.Id);
		attempt.SourceRole.Should().Be(MainRoleType.Cupid);
		attempt.SourcePower.Identifier.Should().Be(
			new RolePowerIdentifier("cupid-link-lovers"));
		attempt.SourcePower.Category.Should().Be(RolePowerCategory.Chosen);
		attempt.PowerInstance.Id.Should().Be(cupid.Id);
		attempt.PowerInstance.Origin.Should().Be(
			RolePowerInstanceOrigin.Native);
		attempt.OneUseResource.Should().BeNull();
		var next = builder.Process(sleep.CreateResponse());
		next.IsSuccess.Should().BeTrue();
		policy.Attempts.Should().ContainSingle();
		builder.GetGameState()!.GetPlayers().Should().OnlyContain(player =>
			!player.State.HasStatusEffect(StatusEffectTypes.Lovers));
		MarkTestCompleted();
	}

	[Fact]
	public void StaleSelectionWithNewlyDeadTarget_IsRejectedWithoutMutation()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Cupid,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var cupid = players[1];
		builder.ArrangeKnownRole(cupid.Id, MainRoleType.Cupid);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var acceptedPayload = selection.CreateResponse(
			[players[2].Id, players[3].Id]);
		builder.ArrangeEliminatedPlayer(players[3].Id);
		var before = builder.GetGameState()!.Serialize();

		Action process = () => builder.Process(acceptedPayload);

		process.Should().Throw<InvalidOperationException>()
			.WithMessage("*living Players*");
		builder.GetGameState()!.Serialize().Should().Be(before);
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			selection.InstructionId);
		MarkTestCompleted();
	}

	[Fact]
	public void ExactCrossFactionLivingPair_EndsAtCentralDawnVictoryWindow()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Cupid,
				MainRoleType.Witch,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolfLover = players[0];
		var cupid = players[1];
		var witch = players[2];
		var villagerLover = players[3];
		builder.ArrangeKnownRole(cupid.Id, MainRoleType.Cupid);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolfLover.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(selection.CreateResponse(
					[werewolfLover.Id, villagerLover.Id])));
		var loversSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(recognition.CreateResponse()));
		builder.Process(loversSleep.CreateResponse()).IsSuccess.Should().BeTrue();
		var identifyWitch = builder.CompleteWerewolfNightAction(
				[werewolfLover.Id],
				villagerLover.Id)
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var healing = builder.Process(
				identifyWitch.CreateResponse([witch.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var poison = builder.Process(
				healing.CreateResponse([villagerLover.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var witchSleep = builder.Process(poison.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var finishNight = builder.Process(witchSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		foreach (var player in players.Where(player =>
			         player.Id != werewolfLover.Id &&
			         player.Id != villagerLover.Id))
		{
			builder.ArrangeEliminatedPlayer(player.Id);
		}

		var finished = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<FinishedGameConfirmationInstruction>().Subject;

		finished.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.CrossFactionLovers));
		finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
		finished.PublicAnnouncement.Should().Contain(
			GameStrings.VictoryConditionCrossFactionLovers);
		var session = builder.GetGameState()!;
		session.GetPlayers()
			.Where(player => player.State.Health == PlayerHealth.Alive)
			.Select(player => player.Id)
			.Should().BeEquivalentTo(
				[werewolfLover.Id, villagerLover.Id]);
		MarkTestCompleted();
	}

	private sealed class RecordingPolicy(RolePowerAvailabilityResult result)
		: IRolePowerAvailabilityPolicy
	{
		public List<RolePowerAttempt> Attempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			Attempts.Add(attempt);
			return result;
		}
	}

	private sealed class RecoveryBoundaryKey : IGameFlowManagerKey
	{
		internal static RecoveryBoundaryKey Instance { get; } = new();

		private RecoveryBoundaryKey()
		{
		}
	}
}
