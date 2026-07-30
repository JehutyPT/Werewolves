using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class PiperRecoveryTests
{
	[Fact]
	public void AcceptedIdentification_FreshServiceRestoresExactWakeWithoutReidentification()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Piper,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var piper = players[1];
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[0].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id],
					players[6].Id));
		var acceptedIdentification = identification.CreateResponse([piper.Id]);
		var expectedWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(acceptedIdentification));
		var freshService = new GameService();

		var recoveredGameId = freshService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredWake = freshService.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var recoveredSession = freshService.GetGameStateView(recoveredGameId)!;

		recoveredWake.Should().BeEquivalentTo(expectedWake);
		recoveredSession.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.Piper &&
				entry.PlayerIds.SetEquals(new[] { piper.Id }));
		recoveredSession.RequireKnownFactionBeneficiary(piper.Id)
			.Should().Be(Faction.Piper);
		var beforeReplay = PublicGameSessionSnapshot.Capture(
			freshService,
			recoveredGameId);

		Action replay = () => freshService.ProcessInstruction(
			recoveredGameId,
			acceptedIdentification);

		replay.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(freshService, recoveredGameId)
			.Should().BeEquivalentTo(
				beforeReplay,
				options => options.WithStrictOrdering());
	}

	[Fact]
	public void CommittedCharm_FreshServiceRestoresExactSleepAndGenericRemovalReplaysWithoutDuplicates()
	{
		var recovery = CreateCommittedCharm();
		var freshService = new GameService();
		var recoveredGameId = freshService.RehydrateSession(
			recovery.Builder.GetGameState()!.Serialize());
		var recoveredSleep = freshService.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var recoveredSession = freshService.GetGameStateView(recoveredGameId)!;

		recoveredSleep.Should().BeEquivalentTo(recovery.ExpectedSleep);
		AssertSingleCommittedCharm(recoveredSession, recovery);
		foreach (var targetId in recovery.TargetIds)
		{
			recoveredSession.GetPlayerState(targetId)
				.HasStatusEffect(StatusEffectTypes.Charmed)
				.Should().BeTrue();
		}

		var beforeReplay = PublicGameSessionSnapshot.Capture(
			freshService,
			recoveredGameId);

		Action replay = () => freshService.ProcessInstruction(
			recoveredGameId,
			recovery.AcceptedTargetSelection);

		replay.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(freshService, recoveredGameId)
			.Should().BeEquivalentTo(
				beforeReplay,
				options => options.WithStrictOrdering());

		var recognition = freshService.ProcessInstruction(
				recoveredGameId,
				recoveredSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeCharmedPlayers);
		recognition.AffectedPlayerIds.Should().BeEquivalentTo(recovery.TargetIds);
		var finishNight = freshService.ProcessInstruction(
				recoveredGameId,
				recognition.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		AssertSingleCommittedCharm(recoveredSession, recovery);

		var targetIdToRemove = recovery.TargetIds[0];
		var unaffectedBefore = recoveredSession.GetPlayerState(targetIdToRemove);
		var currentRoleBefore = unaffectedBefore.CurrentRole;
		var physicalRoleBefore = unaffectedBefore.PhysicalCharacterCardRole;
		var moderatorRoleBefore = unaffectedBefore.ModeratorKnownRole;
		var publicRoleBefore = unaffectedBefore.PubliclyRevealedRole;
		var healthBefore = unaffectedBefore.Health;
		var votingRightBefore = unaffectedBefore.HasVotingRight;
		var beneficiaryBefore = recoveredSession.GetFactionBeneficiaryKnowledge(
			targetIdToRemove);
		var agentKnowledgeBefore = Enum.GetValues<Faction>()
			.ToDictionary(
				faction => faction,
				faction => recoveredSession.GetFactionAgentKnowledge(
					targetIdToRemove,
					faction));
		((GameSession)recoveredSession).RemoveStatusEffect(
			StatusEffectTypes.Charmed,
			targetIdToRemove);
		recoveredSession.GetPlayerState(targetIdToRemove)
			.HasStatusEffect(StatusEffectTypes.Charmed)
			.Should().BeFalse();
		freshService.ProcessInstruction(
				recoveredGameId,
				finishNight.CreateResponse())
			.IsSuccess.Should().BeTrue();
		recoveredSession.GetCurrentPhase().Should().Be(GamePhase.Dawn);
		while (recoveredSession.GetCurrentPhase() != GamePhase.Day)
		{
			var instruction = freshService.GetCurrentInstruction(recoveredGameId);
			var result = instruction switch
			{
				AssignRolesInstruction assignment => freshService.ProcessInstruction(
					recoveredGameId,
					assignment.CreateResponse(
						assignment.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							_ => MainRoleType.SimpleVillager))),
				ConfirmationInstruction confirmation =>
					freshService.ProcessInstruction(
						recoveredGameId,
						confirmation.CreateResponse()),
				_ => throw new InvalidOperationException(
					"Unexpected Dawn instruction while advancing the removal recovery boundary.")
			};
			result.IsSuccess.Should().BeTrue();
		}
		recoveredSession.GetPlayerState(targetIdToRemove)
			.HasStatusEffect(StatusEffectTypes.Charmed)
			.Should().BeFalse();
		var serializedAfterRemoval = recoveredSession.Serialize();
		RecoveryPayloadTestDriver.Parse(serializedAfterRemoval)
			.GetActiveEffects(targetIdToRemove)
			.HasFlag(StatusEffectTypes.Charmed)
			.Should().BeFalse();

		var removalService = new GameService();
		var removedGameId = removalService.RehydrateSession(
			serializedAfterRemoval);
		var removedSession = removalService.GetGameStateView(removedGameId)!;
		var removedState = removedSession.GetPlayerState(targetIdToRemove);

		removedState.HasStatusEffect(StatusEffectTypes.Charmed)
			.Should().BeFalse();
		removedState.HasStatusEffect(StatusEffectTypes.Sheriff)
			.Should().BeTrue();
		removedSession.GetPlayerState(recovery.TargetIds[1])
			.HasStatusEffect(StatusEffectTypes.Charmed)
			.Should().BeTrue();
		removedState.CurrentRole.Should().Be(currentRoleBefore);
		removedState.PhysicalCharacterCardRole.Should().Be(physicalRoleBefore);
		removedState.ModeratorKnownRole.Should().Be(moderatorRoleBefore);
		removedState.PubliclyRevealedRole.Should().Be(publicRoleBefore);
		removedState.Health.Should().Be(healthBefore);
		removedState.HasVotingRight.Should().Be(votingRightBefore);
		removedSession.GetFactionBeneficiaryKnowledge(targetIdToRemove)
			.Should().Be(beneficiaryBefore);
		foreach (var (faction, knowledge) in agentKnowledgeBefore)
		{
			removedSession.GetFactionAgentKnowledge(targetIdToRemove, faction)
				.Should().Be(knowledge);
		}

		removedSession.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == targetIdToRemove &&
				entry.EffectType == StatusEffectTypes.Charmed &&
				!entry.IsActive);
		AssertSingleCommittedCharm(removedSession, recovery);
	}

	[Fact]
	public void CommittedCharm_TamperedMultiTargetCursorIsRejected()
	{
		var recovery = CreateCommittedCharm();
		var tampered = RecoveryPayloadTestDriver
			.Parse(recovery.Builder.GetGameState()!.Serialize())
			.RewriteRecurringCursorTargets(
				recovery.TargetIds[0],
				recovery.NonCharmedPlayerId)
			.Serialize();

		Action rehydrate = () => new GameService().RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
	}

	private static CommittedCharmRecovery CreateCommittedCharm()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Piper,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var piper = players[1];
		var targetIds = new[] { players[2].Id, players[3].Id };
		builder.ArrangeKnownRole(piper.Id, MainRoleType.Piper);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[0].Id);
		builder.ArrangeStatusEffect(targetIds[0], StatusEffectTypes.Sheriff);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id],
					players[6].Id));
		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var acceptedTargetSelection = targetSelection.CreateResponse(
			targetIds.ToHashSet());
		var expectedSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(acceptedTargetSelection));
		expectedSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		return new CommittedCharmRecovery(
			builder,
			piper.Id,
			targetIds,
			players[4].Id,
			acceptedTargetSelection,
			expectedSleep);
	}

	private static void AssertSingleCommittedCharm(
		IGameSession session,
		CommittedCharmRecovery recovery)
	{
		session.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.PiperCharm &&
				entry.ActingPlayerId == recovery.PiperId &&
				entry.SourceRole == MainRoleType.Piper &&
				entry.TargetIds!.ToHashSet().SetEquals(recovery.TargetIds));
		foreach (var targetId in recovery.TargetIds)
		{
			session.GameHistoryLog
				.OfType<StatusEffectLogEntry>()
				.Should().ContainSingle(entry =>
					entry.PlayerId == targetId &&
					entry.EffectType == StatusEffectTypes.Charmed &&
					entry.IsActive);
		}
	}

	private sealed record CommittedCharmRecovery(
		GameTestBuilder Builder,
		Guid PiperId,
		Guid[] TargetIds,
		Guid NonCharmedPlayerId,
		ModeratorResponse AcceptedTargetSelection,
		ConfirmationInstruction ExpectedSleep);
}
