using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class DefenderRoleTests : DiagnosticTestBase
{
	public DefenderRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void FirstNight_UnknownHolder_IdentifiesThenRequiresOneLivingTarget()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var defender = players[0];
		builder.ConfirmGameStart();

		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());

		identification.RoleIdentification.Should().Be(MainRoleType.Defender);
		identification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.Single);

		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(
					identification.CreateResponse([defender.Id])));

		targetSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectDefenderTarget);
		targetSelection.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.Single);
		targetSelection.SelectablePlayerIds.Should().BeEquivalentTo(
			players.Select(player => player.Id));
		targetSelection.AffectedPlayerIds.Should().Equal(defender.Id);
		targetSelection.PublicAnnouncement.Should().BeNull();
		targetSelection.PrivateInstruction.Should().NotBeNullOrWhiteSpace();
		MarkTestCompleted();
	}

	[Fact]
	public void KnownHolder_UsesAvailabilityAndCommitsOwnerQualifiedProtectionBeforePublicSleep()
	{
		var policy = new RecordingAvailabilityPolicy();
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var defender = players[0];
		var littleGirl = players[1];
		var target = players[3];
		var eliminatedPlayer = players[5];
		builder.ArrangeKnownRole(defender.Id, MainRoleType.Defender);
		builder.ArrangeKnownRole(littleGirl.Id, MainRoleType.LittleGirl);
		builder.ArrangeEliminatedPlayer(eliminatedPlayer.Id);
		builder.ConfirmGameStart();

		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().Equal(defender.Id);
		targetSelection.SelectablePlayerIds.Should()
			.Contain(defender.Id)
			.And.Contain(target.Id)
			.And.NotContain(littleGirl.Id)
			.And.NotContain(eliminatedPlayer.Id);
		policy.Attempts.Should().ContainSingle();
		var attempt = policy.Attempts.Single();
		attempt.ActingPlayer.Id.Should().Be(defender.Id);
		attempt.SourceRole.Should().Be(MainRoleType.Defender);
		attempt.SourcePower.Identifier.Value.Should().Be(
			"defender-protection");
		attempt.PowerInstance.Id.Should().Be(defender.Id);
		attempt.PowerInstance.Origin.Should().Be(
			RolePowerInstanceOrigin.Native);
		attempt.OneUseResource.Should().BeNull();

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					targetSelection.CreateResponse([target.Id])));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().NotBeNullOrWhiteSpace();
		sleep.PrivateInstruction.Should().BeNull();
		sleep.AffectedPlayerIds.Should().Equal(defender.Id);
		var commit = builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle()
			.Subject;
		commit.ActionType.Should().Be(NightActionType.DefenderProtect);
		commit.TargetIds.Should().Equal(target.Id);
		commit.ActingPlayerId.Should().Be(defender.Id);
		commit.SourceRole.Should().Be(MainRoleType.Defender);
		commit.SourcePowerIdentifier.Should().Be("defender-protection");
		commit.PowerInstanceId.Should().Be(defender.Id);
		commit.PowerInstanceOrigin.Should().Be(
			RolePowerInstanceOrigin.Native);
		MarkTestCompleted();
	}

	[Fact]
	public void PublicFlow_ProtectingElderFromCollectiveWerewolvesPreservesLifeAndElderProtection()
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var defender = players[0];
		var werewolf = players[1];
		var elder = players[2];
		builder.ArrangeCurrentRole(elder.Id, MainRoleType.Elder);
		builder.ConfirmGameStart();

		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(
					identification.CreateResponse([defender.Id])));
		var defenderSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					targetSelection.CreateResponse([elder.Id])));

		builder.Process(defenderSleep.CreateResponse()).IsSuccess.Should().BeTrue();
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					elder.Id));
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase().IsSuccess.Should().BeTrue();

		elder.State.Health.Should().Be(PlayerHealth.Alive);
		elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeFalse();
		builder.GetGameState()!.GameHistoryLog
			.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == elder.Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.DefenderProtect &&
				entry.TargetIds!.SequenceEqual(new[] { elder.Id }));
		MarkTestCompleted();
	}

	[Fact]
	public void NextNight_SameNativePowerCannotProtectItsImmediatelyPreviousTarget()
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var defender = players[0];
		var werewolf = players[1];
		var firstVictim = players[2];
		builder.ArrangeKnownRole(defender.Id, MainRoleType.Defender);
		builder.ArrangeKnownWerewolfFactionAgentGroup([werewolf.Id]);
		builder.ConfirmGameStart();
		var firstWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var firstTargetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(firstWake.CreateResponse()));
		var firstSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					firstTargetSelection.CreateResponse([defender.Id])));
		var werewolfWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(firstSleep.CreateResponse()));
		werewolfWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightActionSubsequentNight(
					firstVictim.Id));
		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(
			new Dictionary<Guid, MainRoleType>
			{
				[firstVictim.Id] = MainRoleType.SimpleVillager
			}).IsSuccess.Should().BeTrue();
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();

		var secondWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var secondTargetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(secondWake.CreateResponse()));

		secondTargetSelection.SelectablePlayerIds.Should()
			.NotContain(defender.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedUnknownHolderIdentification_FreshServiceRestoresExactTargetAndRejectsStaleResponses()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var defender = players[0];
		var target = players[2];
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var acceptedIdentification =
			identification.CreateResponse([defender.Id]);
		var expectedTarget =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(acceptedIdentification));
		var freshService = new GameService();

		var recoveredGameId = freshService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredTarget =
			InstructionAssert.ExpectType<SelectPlayersInstruction>(
				freshService.GetCurrentInstruction(recoveredGameId));

		recoveredTarget.Semantic.Should().Be(expectedTarget.Semantic);
		PendingInstructionSnapshot.Capture(recoveredTarget)
			.Should().BeEquivalentTo(
				PendingInstructionSnapshot.Capture(expectedTarget),
				options => options.WithStrictOrdering());

		var beforeStaleIdentification =
			PublicGameSessionSnapshot.Capture(freshService, recoveredGameId);
		Action replayIdentification = () =>
			freshService.ProcessInstruction(
				recoveredGameId,
				acceptedIdentification);

		replayIdentification.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(freshService, recoveredGameId)
			.Should().BeEquivalentTo(
				beforeStaleIdentification,
				options => options.WithStrictOrdering());

		var acceptedTarget = recoveredTarget.CreateResponse([target.Id]);
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				freshService.ProcessInstruction(
					recoveredGameId,
					acceptedTarget));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		var recoveredSession = freshService.GetGameStateView(recoveredGameId)!;
		recoveredSession.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.DefenderProtect &&
				entry.ActingPlayerId == defender.Id &&
				entry.TargetIds!.SequenceEqual(new[] { target.Id }));

		var beforeTargetReplay =
			PublicGameSessionSnapshot.Capture(freshService, recoveredGameId);
		Action replayTarget = () =>
			freshService.ProcessInstruction(recoveredGameId, acceptedTarget);

		replayTarget.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(freshService, recoveredGameId)
			.Should().BeEquivalentTo(
				beforeTargetReplay,
				options => options.WithStrictOrdering());
		recoveredSession.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedProtection_FreshServicesRestoreExactSleepWithoutDuplicateCommit()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var defender = players[0];
		var target = players[2];
		builder.ArrangeKnownRole(defender.Id, MainRoleType.Defender);
		builder.ConfirmGameStart();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					targetSelection.CreateResponse([target.Id])));

		var firstRecoveredService = new GameService();
		var firstRecoveredId = firstRecoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var firstRecoveredSleep =
			InstructionAssert.ExpectType<ConfirmationInstruction>(
				firstRecoveredService.GetCurrentInstruction(firstRecoveredId));

		firstRecoveredSleep.InstructionId.Should().Be(sleep.InstructionId);
		firstRecoveredService.GetGameStateView(firstRecoveredId)!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle();

		var secondRecoveredService = new GameService();
		var secondRecoveredId = secondRecoveredService.RehydrateSession(
			firstRecoveredService.GetGameStateView(firstRecoveredId)!.Serialize());
		var secondRecoveredSleep =
			InstructionAssert.ExpectType<ConfirmationInstruction>(
				secondRecoveredService.GetCurrentInstruction(secondRecoveredId));

		secondRecoveredSleep.InstructionId.Should().Be(sleep.InstructionId);
		secondRecoveredService.GetGameStateView(secondRecoveredId)!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	private sealed class RecordingAvailabilityPolicy
		: IRolePowerAvailabilityPolicy
	{
		internal List<RolePowerAttempt> Attempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			Attempts.Add(attempt);
			return RolePowerAvailabilityResult.Allowed;
		}
	}
}
