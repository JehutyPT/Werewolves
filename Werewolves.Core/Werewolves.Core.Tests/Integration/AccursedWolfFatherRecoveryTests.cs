using FluentAssertions;
using FluentAssertions.Execution;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class AccursedWolfFatherRecoveryTests
{
	[Fact]
	public void AcceptedIdentification_FreshServiceRestoresExactInfectionChoiceWithoutReopeningIdentification()
	{
		var (builder, holderId, _, identification) =
			CreateGameAtIdentification();
		var acceptedIdentification =
			identification.CreateResponse([holderId]);
		var expectedChoice = builder.Process(acceptedIdentification)
			.ModeratorInstruction.Should()
			.BeOfType<SelectOptionsInstruction>().Subject;
		var serializedSession = builder.SerializeSession();
		var freshService = new GameService();

		var recoveredGameId =
			freshService.RehydrateSession(serializedSession);
		var recoveredSession =
			freshService.GetGameStateView(recoveredGameId)!;
		var recoveredChoice = freshService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<SelectOptionsInstruction>().Subject;

		using (new AssertionScope())
		{
			recoveredSession.GameHistoryLog
				.OfType<RoleIdentificationLogEntry>()
				.Should().ContainSingle(entry =>
					entry.Role == MainRoleType.AccursedWolfFather &&
					entry.PlayerIds.SetEquals(new[] { holderId }));
			AssertEquivalentInstruction(recoveredChoice, expectedChoice);
		}

		var beforeReplay =
			PublicGameSessionSnapshot.Capture(
				freshService,
				recoveredGameId);
		Action replayIdentification = () =>
			freshService.ProcessInstruction(
				recoveredGameId,
				acceptedIdentification);

		replayIdentification.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(freshService, recoveredGameId)
			.Should().BeEquivalentTo(
				beforeReplay,
				options => options.WithStrictOrdering());

		var continued = freshService.ProcessInstruction(
			recoveredGameId,
			recoveredChoice.CreateResponse(
				AccursedWolfFatherInfectionOptionIds.Decline));

		continued.IsSuccess.Should().BeTrue();
		continued.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>()
			.Which.Semantic.Should()
			.Be(ModeratorInstructionSemantic.PutRoleToSleep);
		recoveredSession.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Count(entry =>
				entry.Role == MainRoleType.AccursedWolfFather)
			.Should().Be(1);
		recoveredSession.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void ConfirmedInfection_FreshServiceRestoresExactSleepWithoutReapplyingIntentOrSpend()
	{
		var (builder, holderId, victimId, identification) =
			CreateGameAtIdentification();
		var choice = builder.Process(
				identification.CreateResponse([holderId]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectOptionsInstruction>().Subject;
		var acceptedInfection = choice.CreateResponse(
			AccursedWolfFatherInfectionOptionIds.Infect);
		var expectedSleep = builder.Process(acceptedInfection)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var serializedSession = builder.SerializeSession();
		var freshService = new GameService();

		var recoveredGameId =
			freshService.RehydrateSession(serializedSession);
		var recoveredSession =
			freshService.GetGameStateView(recoveredGameId)!;
		var recoveredSleep = freshService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var commit = recoveredSession.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle()
			.Subject;

		using (new AssertionScope())
		{
			AssertEquivalentInstruction(recoveredSleep, expectedSleep);
			commit.ActionType.Should().Be(
				NightActionType.AccursedWolfFatherInfection);
			commit.TargetIds.Should().Equal(victimId);
			commit.ActingPlayerId.Should().Be(holderId);
			commit.SourceRole.Should().Be(
				MainRoleType.AccursedWolfFather);
			commit.SourcePowerIdentifier.Should().Be(
				"accursed-wolf-father-infection");
			commit.PowerInstanceId.Should().Be(holderId);
			commit.PowerInstanceOrigin.Should().Be(
				RolePowerInstanceOrigin.Native);
			commit.OneUseResourceId.Should().Be(
				AccursedWolfFatherRole.InfectionResourceId);
		}

		var beforeReplay =
			PublicGameSessionSnapshot.Capture(
				freshService,
				recoveredGameId);
		Action replayInfection = () =>
			freshService.ProcessInstruction(
				recoveredGameId,
				acceptedInfection);

		replayInfection.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(freshService, recoveredGameId)
			.Should().BeEquivalentTo(
				beforeReplay,
				options => options.WithStrictOrdering());
		recoveredSession.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle();

		var finishNight = freshService.ProcessInstruction(
				recoveredGameId,
				recoveredSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		freshService.ProcessInstruction(
			recoveredGameId,
			finishNight.CreateResponse());

		recoveredSession.GetPlayerState(victimId)
			.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)
			.Should().BeTrue();
		recoveredSession.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		recoveredSession.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Source.Identifier ==
					"accursed-wolf-father-infection");
	}

	[Fact]
	public void LoneWerewolfAgentConfirmedInfection_FreshServiceRestoresExactSleepWithoutAmbiguousListenerResolution()
	{
		var (builder, holderId, victimId, identification) =
			CreateGameAtIdentification(loneWerewolfAgent: true);
		var choice = builder.Process(
				identification.CreateResponse([holderId]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectOptionsInstruction>().Subject;
		var expectedSleep = builder.Process(
				choice.CreateResponse(
					AccursedWolfFatherInfectionOptionIds.Infect))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var freshService = new GameService();

		var recoveredGameId = freshService.RehydrateSession(
			builder.SerializeSession());
		var recoveredSleep = freshService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		AssertEquivalentInstruction(recoveredSleep, expectedSleep);
		freshService.GetGameStateView(recoveredGameId)!
			.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType ==
					NightActionType.AccursedWolfFatherInfection &&
				entry.TargetIds!.SequenceEqual(new[] { victimId }));
		freshService.ProcessInstruction(
				recoveredGameId,
				recoveredSleep.CreateResponse())
			.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public void ConfirmedInfection_FreshServiceRejectsCursorAndCommitRetargetedAwayFromCollectiveVictim()
	{
		var (builder, holderId, victimId, identification) =
			CreateGameAtIdentification();
		var choice = builder.Process(
				identification.CreateResponse([holderId]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectOptionsInstruction>().Subject;
		builder.Process(
				choice.CreateResponse(
					AccursedWolfFatherInfectionOptionIds.Infect))
			.IsSuccess.Should().BeTrue();
		var differentTargetId = builder.GetGameState()!.GetPlayers()
			.Select(player => player.Id)
			.First(playerId =>
				playerId != holderId &&
				playerId != victimId);
		var tampered = RecoveryPayloadTestDriver
			.Parse(builder.SerializeSession())
			.RetargetLatestOneUseActionAndCursor(differentTargetId)
			.Serialize();
		var freshService = new GameService();

		Action rehydrate = () => freshService.RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*retained collective victim*");
	}

	[Fact]
	public void Decline_SerializeRehydrateReplaysLastDurableChoiceWithoutSpend()
	{
		var (builder, holderId, _, identification) =
			CreateGameAtIdentification();
		var expectedChoice = builder.Process(
				identification.CreateResponse([holderId]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectOptionsInstruction>().Subject;
		var liveSleep = builder.Process(
				expectedChoice.CreateResponse(
					AccursedWolfFatherInfectionOptionIds.Decline))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		liveSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		var freshService = new GameService();

		var recoveredGameId = freshService.RehydrateSession(
			builder.SerializeSession());
		var recoveredSession =
			freshService.GetGameStateView(recoveredGameId)!;
		var recoveredChoice = freshService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<SelectOptionsInstruction>().Subject;

		AssertEquivalentInstruction(recoveredChoice, expectedChoice);
		recoveredSession.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		recoveredSession.GetPlayers()
			.Should().OnlyContain(player =>
				!player.State.HasStatusEffect(
					StatusEffectTypes.LycanthropyInfection));
		recoveredSession.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.Source.Identifier ==
					"accursed-wolf-father-infection");

		var replayedDecline = freshService.ProcessInstruction(
			recoveredGameId,
			recoveredChoice.CreateResponse(
				AccursedWolfFatherInfectionOptionIds.Decline));

		replayedDecline.IsSuccess.Should().BeTrue();
		replayedDecline.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>()
			.Which.Semantic.Should()
			.Be(ModeratorInstructionSemantic.PutRoleToSleep);
		recoveredSession.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
	}

	private static (
		GameTestBuilder Builder,
		Guid HolderId,
		Guid VictimId,
		SelectPlayersInstruction Identification)
		CreateGameAtIdentification(bool loneWerewolfAgent = false)
	{
		MainRoleType[] roles = loneWerewolfAgent
			?
			[
				MainRoleType.AccursedWolfFather,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]
			:
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			];
		var builder = GameTestBuilder.Create()
			.WithPlayers(7)
			.WithRoles(roles);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var holderId = players[loneWerewolfAgent ? 0 : 1].Id;
		var victimId = players[6].Id;
		HashSet<Guid> werewolfAgentIds = loneWerewolfAgent
			? [holderId]
			: [players[0].Id, holderId];
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.CompleteWerewolfNightAction(
					werewolfAgentIds,
					victimId));

		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(
			MainRoleType.AccursedWolfFather);
		return (builder, holderId, victimId, identification);
	}

	private static void AssertEquivalentInstruction(
		SelectOptionsInstruction actual,
		SelectOptionsInstruction expected)
	{
		actual.InstructionId.Should().Be(expected.InstructionId);
		actual.Semantic.Should().Be(expected.Semantic);
		actual.PublicAnnouncement.Should().Be(expected.PublicAnnouncement);
		actual.PrivateInstruction.Should().Be(expected.PrivateInstruction);
		actual.AffectedPlayerIds.Should().Equal(expected.AffectedPlayerIds);
		actual.SoundEffects.Should().Equal(expected.SoundEffects);
		actual.Options.Should().Equal(expected.Options);
		actual.SelectionRange.Should().Be(expected.SelectionRange);
	}

	private static void AssertEquivalentInstruction(
		ConfirmationInstruction actual,
		ConfirmationInstruction expected)
	{
		actual.InstructionId.Should().Be(expected.InstructionId);
		actual.Semantic.Should().Be(expected.Semantic);
		actual.PublicAnnouncement.Should().Be(expected.PublicAnnouncement);
		actual.PrivateInstruction.Should().Be(expected.PrivateInstruction);
		actual.AffectedPlayerIds.Should().Equal(expected.AffectedPlayerIds);
		actual.SoundEffects.Should().Equal(expected.SoundEffects);
	}
}
