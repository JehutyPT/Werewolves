using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class LittleGirlRoleTests : DiagnosticTestBase
{
	public LittleGirlRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void FirstNight_UnknownHolder_IdentifiesExactlyOnceThenContinuesToCollectiveWerewolfCall()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();

		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var littleGirl = builder.GetGameState()!.GetPlayers().First();

		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.LittleGirl);
		identification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.Single);

		var werewolfObservation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(
					identification.CreateResponse([littleGirl.Id])));

		werewolfObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.LittleGirl &&
				entry.PlayerIds.SetEquals(new[] { littleGirl.Id }));
		littleGirl.State.PhysicalCharacterCardRole.Should().BeNull();
		littleGirl.State.PubliclyRevealedRole.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedIdentification_RehydratesAtTheNextApplicableRolesIdentification()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.LittleGirl,
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var littleGirlIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var nextIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(
					littleGirlIdentification.CreateResponse([players[0].Id])));

		nextIdentification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		nextIdentification.RoleIdentification.Should().Be(
			MainRoleType.StutteringJudge);

		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			builder.SerializeSession());
		var secondRecoveredService = new GameService();
		var secondRecoveredGameId = secondRecoveredService.RehydrateSession(
			recoveredService.SerializeSession(recoveredGameId));
		var recoveredIdentification =
			InstructionAssert.ExpectType<SelectPlayersInstruction>(
				secondRecoveredService.GetCurrentInstruction(secondRecoveredGameId));

		recoveredIdentification.InstructionId.Should().Be(
			nextIdentification.InstructionId);
		recoveredIdentification.RoleIdentification.Should().Be(
			MainRoleType.StutteringJudge);
		var setup =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				secondRecoveredService.ProcessInstruction(
					secondRecoveredGameId,
					recoveredIdentification.CreateResponse([players[1].Id])));
		setup.Semantic.Should().Be(
			ModeratorInstructionSemantic.EstablishStutteringJudgeSignal);
		secondRecoveredService.GetGameStateView(secondRecoveredGameId)!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.LittleGirl &&
				entry.PlayerIds.SetEquals(new[] { players[0].Id }));
		MarkTestCompleted();
	}

	[Fact]
	public void UnknownCollective_WhenSpyingIsAllowed_RetainsGuidanceAcrossAcceptedObservationRecovery()
	{
		var originalPolicy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(originalPolicy)
			.WithPlayers("Little Girl", "Werewolf", "Villager A", "Villager B", "Villager C")
			.WithRoles(
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var identificationResponse =
			identification.CreateResponse([players[0].Id]);
		var observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(identificationResponse));

		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		observation.PrivateInstruction.Should().Contain(
			GameStrings.WerewolfFactionAgentObservationPrompt.Format(1));
		observation.PrivateInstruction.Should().Contain(
			GameStrings.LittleGirlOpeningGuidance);
		observation.AffectedPlayerIds.Should().BeNull();
		originalPolicy.ObservedAttempts.Should().ContainSingle();
		var attempt = originalPolicy.ObservedAttempts.Single();
		attempt.ActingPlayer.Id.Should().Be(players[0].Id);
		attempt.SourceRole.Should().Be(MainRoleType.LittleGirl);
		attempt.SourcePower.Identifier.Value.Should().Be("little-girl-spying");
		attempt.SourcePower.Category.Should().Be(RolePowerCategory.Passive);
		attempt.PowerInstance.Id.Should().Be(players[0].Id);
		attempt.PowerInstance.SourceRole.Should().Be(MainRoleType.LittleGirl);
		attempt.PowerInstance.Origin.Should().Be(RolePowerInstanceOrigin.Native);
		attempt.OneUseResource.Should().BeNull();

		var wakeRecoveryPolicy =
			new RecordingPolicy(RolePowerAvailabilityResult.Denied);
		var wakeRecoveryService = new GameService(wakeRecoveryPolicy);
		var wakeRecoveryGameId = wakeRecoveryService.RehydrateSession(
			builder.SerializeSession());
		var recoveredObservation =
			InstructionAssert.ExpectType<SelectPlayersInstruction>(
				wakeRecoveryService.GetCurrentInstruction(wakeRecoveryGameId));

		recoveredObservation.InstructionId.Should().Be(observation.InstructionId);
		recoveredObservation.PrivateInstruction.Should().Contain(
			GameStrings.WerewolfFactionAgentObservationPrompt.Format(1));
		recoveredObservation.PrivateInstruction.Should().Contain(
			GameStrings.LittleGirlOpeningGuidance);
		wakeRecoveryPolicy.ObservedAttempts.Should().BeEmpty();
		var wakeRecoveryState =
			wakeRecoveryService.GetGameStateView(wakeRecoveryGameId)!;
		var serializedBeforeStaleIdentification =
			wakeRecoveryService.SerializeSession(wakeRecoveryGameId);
		var logBeforeStaleIdentification =
			wakeRecoveryState.GameHistoryLog.ToArray();

		Action staleIdentification = () =>
			wakeRecoveryService.ProcessInstruction(
				wakeRecoveryGameId,
				identificationResponse);

		staleIdentification.Should().Throw<InvalidOperationException>()
			.WithMessage("*pending Moderator Instruction*");
		wakeRecoveryService.SerializeSession(wakeRecoveryGameId).Should().Be(
			serializedBeforeStaleIdentification);
		wakeRecoveryState.GameHistoryLog.Should().Equal(
			logBeforeStaleIdentification);
		wakeRecoveryPolicy.ObservedAttempts.Should().BeEmpty();

		var observationResponse =
			recoveredObservation.CreateResponse([players[1].Id]);
		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				wakeRecoveryService.ProcessInstruction(
					wakeRecoveryGameId,
					observationResponse));
		var recoveredPolicy = new RecordingPolicy(RolePowerAvailabilityResult.Denied);
		var recoveredService = new GameService(recoveredPolicy);
		var recoveredGameId = recoveredService.RehydrateSession(
			wakeRecoveryService.SerializeSession(wakeRecoveryGameId));
		var recoveredVictimSelection =
			InstructionAssert.ExpectType<SelectPlayersInstruction>(
				recoveredService.GetCurrentInstruction(recoveredGameId));

		recoveredVictimSelection.InstructionId.Should().Be(
			victimSelection.InstructionId);
		recoveredVictimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();
		var recoveredState = recoveredService.GetGameStateView(recoveredGameId)!;
		var serializedBeforeStaleResponse =
			recoveredService.SerializeSession(recoveredGameId);
		var logBeforeStaleResponse = recoveredState.GameHistoryLog.ToArray();

		Action staleResponse = () => recoveredService.ProcessInstruction(
			recoveredGameId,
			observationResponse);

		staleResponse.Should().Throw<InvalidOperationException>()
			.WithMessage("*pending Moderator Instruction*");
		recoveredService.SerializeSession(recoveredGameId).Should().Be(
			serializedBeforeStaleResponse);
		recoveredState.GameHistoryLog.Should().Equal(logBeforeStaleResponse);
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();

		var victimId = recoveredVictimSelection.SelectablePlayerIds
			.Single(playerId => playerId == players[2].Id);
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				recoveredService.ProcessInstruction(
					recoveredGameId,
					recoveredVictimSelection.CreateResponse([victimId])));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PrivateInstruction.Should().Be(GameStrings.LittleGirlClosingGuidance);
		sleep.AffectedPlayerIds.Should().Equal(players[1].Id);
		sleep.AffectedPlayerIds.Should().NotContain(players[0].Id);
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void UnknownCollective_WhenSpyingIsDenied_RetainsSuppressionAcrossAcceptedObservationRecovery()
	{
		var originalPolicy = new RecordingPolicy(RolePowerAvailabilityResult.Denied);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(originalPolicy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(identification.CreateResponse([players[0].Id])));

		observation.PrivateInstruction.Should().Be(
			GameStrings.WerewolfFactionAgentObservationPrompt.Format(1));
		originalPolicy.ObservedAttempts.Should().ContainSingle();
		var wakeRecoveryPolicy =
			new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var wakeRecoveryService = new GameService(wakeRecoveryPolicy);
		var wakeRecoveryGameId = wakeRecoveryService.RehydrateSession(
			builder.SerializeSession());
		var recoveredObservation =
			InstructionAssert.ExpectType<SelectPlayersInstruction>(
				wakeRecoveryService.GetCurrentInstruction(wakeRecoveryGameId));

		recoveredObservation.InstructionId.Should().Be(observation.InstructionId);
		recoveredObservation.PrivateInstruction.Should().Be(
			GameStrings.WerewolfFactionAgentObservationPrompt.Format(1));
		wakeRecoveryPolicy.ObservedAttempts.Should().BeEmpty();
		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				wakeRecoveryService.ProcessInstruction(
					wakeRecoveryGameId,
					recoveredObservation.CreateResponse([players[1].Id])));
		var recoveredPolicy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var recoveredService = new GameService(recoveredPolicy);
		var recoveredGameId = recoveredService.RehydrateSession(
			wakeRecoveryService.SerializeSession(wakeRecoveryGameId));
		var recoveredVictimSelection =
			InstructionAssert.ExpectType<SelectPlayersInstruction>(
				recoveredService.GetCurrentInstruction(recoveredGameId));

		recoveredVictimSelection.InstructionId.Should().Be(
			victimSelection.InstructionId);
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				recoveredService.ProcessInstruction(
					recoveredGameId,
					recoveredVictimSelection.CreateResponse([players[2].Id])));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PrivateInstruction.Should().BeNull();
		sleep.AffectedPlayerIds.Should().Equal(players[1].Id);
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void KnownCollective_AfterAcceptedIdentification_RetainsGuidanceWithoutReevaluation(
		bool spyingAllowed)
	{
		var originalPolicy = new RecordingPolicy(
			spyingAllowed
				? RolePowerAvailabilityResult.Allowed
				: RolePowerAvailabilityResult.Denied);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(originalPolicy)
			.WithPlayers(
				"Little Girl",
				"Werewolf",
				"Villager A",
				"Villager B",
				"Villager C")
			.WithRoles(
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					identification.CreateResponse([players[0].Id])));

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PrivateInstruction.Should().Be(
			spyingAllowed
				? GameStrings.LittleGirlOpeningGuidance
				: null);
		originalPolicy.ObservedAttempts.Should().ContainSingle();
		var recoveredPolicy = new RecordingPolicy(
			spyingAllowed
				? RolePowerAvailabilityResult.Denied
				: RolePowerAvailabilityResult.Allowed);
		var recoveredService = new GameService(recoveredPolicy);
		var recoveredGameId = recoveredService.RehydrateSession(
			builder.SerializeSession());
		var recoveredWake =
			InstructionAssert.ExpectType<ConfirmationInstruction>(
				recoveredService.GetCurrentInstruction(recoveredGameId));

		recoveredWake.InstructionId.Should().Be(wake.InstructionId);
		recoveredWake.PrivateInstruction.Should().Be(wake.PrivateInstruction);
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();
		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				recoveredService.ProcessInstruction(
					recoveredGameId,
					recoveredWake.CreateResponse()));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				recoveredService.ProcessInstruction(
					recoveredGameId,
					victimSelection.CreateResponse([players[2].Id])));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PrivateInstruction.Should().Be(
			spyingAllowed
				? GameStrings.LittleGirlClosingGuidance
				: null);
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(true, true)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(false, false)]
	public void AcceptedStutteringJudgeSetup_EnteringWerewolfCollective_RetainsGuidanceWithoutReevaluation(
		bool collectiveKnown,
		bool spyingAllowed)
	{
		var originalPolicy = new RecordingPolicy(
			spyingAllowed
				? RolePowerAvailabilityResult.Allowed
				: RolePowerAvailabilityResult.Denied);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(originalPolicy)
			.WithPlayers(
				"Little Girl",
				"Stuttering Judge",
				"Werewolf",
				"Villager A",
				"Villager B")
			.WithRoles(
				MainRoleType.LittleGirl,
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		if (collectiveKnown)
		{
			builder.ArrangeKnownWerewolfFactionAgentGroup(players[2].Id);
		}

		builder.ConfirmGameStart();
		var littleGirlIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var judgeIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(
					littleGirlIdentification.CreateResponse([players[0].Id])));
		var signalSetup =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					judgeIdentification.CreateResponse([players[1].Id])));
		var werewolfResult = builder.Process(signalSetup.CreateResponse());
		ModeratorInstruction werewolfInstruction = collectiveKnown
			? InstructionAssert
				.ExpectSuccessWithType<ConfirmationInstruction>(
					werewolfResult)
			: InstructionAssert
				.ExpectSuccessWithType<SelectPlayersInstruction>(
					werewolfResult);

		judgeIdentification.RoleIdentification.Should().Be(
			MainRoleType.StutteringJudge);
		signalSetup.Semantic.Should().Be(
			ModeratorInstructionSemantic.EstablishStutteringJudgeSignal);
		werewolfInstruction.Semantic.Should().Be(
			collectiveKnown
				? ModeratorInstructionSemantic.WakeRole
				: ModeratorInstructionSemantic
					.ObserveWerewolfFactionAgentGroup);
		if (spyingAllowed)
		{
			werewolfInstruction.PrivateInstruction.Should().Contain(
				GameStrings.LittleGirlOpeningGuidance);
		}
		else if (collectiveKnown)
		{
			werewolfInstruction.PrivateInstruction.Should().BeNull();
		}
		else
		{
			werewolfInstruction.PrivateInstruction.Should().Be(
				GameStrings.WerewolfFactionAgentObservationPrompt.Format(1));
		}

		if (collectiveKnown)
		{
			werewolfInstruction.AffectedPlayerIds.Should().Equal(
				players[2].Id);
		}
		else
		{
			werewolfInstruction.AffectedPlayerIds.Should().BeNull();
		}
		originalPolicy.ObservedAttempts.Should().ContainSingle();
		originalPolicy.ObservedAttempts.Single().SourceRole.Should().Be(
			MainRoleType.LittleGirl);

		var recoveredPolicy = new RecordingPolicy(
			spyingAllowed
				? RolePowerAvailabilityResult.Denied
				: RolePowerAvailabilityResult.Allowed);
		var recoveredService = new GameService(recoveredPolicy);
		var recoveredGameId = recoveredService.RehydrateSession(
			builder.SerializeSession());
		var recoveredWerewolfInstruction =
			recoveredService.GetCurrentInstruction(recoveredGameId)
			?? throw new InvalidOperationException(
				"Recovered Werewolf instruction is required.");

		recoveredWerewolfInstruction.InstructionId.Should().Be(
			werewolfInstruction.InstructionId);
		recoveredWerewolfInstruction.PrivateInstruction.Should().Be(
			werewolfInstruction.PrivateInstruction);
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();
		var werewolfResponse = collectiveKnown
			? InstructionAssert
				.ExpectType<ConfirmationInstruction>(
					recoveredWerewolfInstruction)
				.CreateResponse()
			: InstructionAssert
				.ExpectType<SelectPlayersInstruction>(
					recoveredWerewolfInstruction)
				.CreateResponse([players[2].Id]);
		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				recoveredService.ProcessInstruction(
					recoveredGameId,
					werewolfResponse));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				recoveredService.ProcessInstruction(
					recoveredGameId,
					victimSelection.CreateResponse([players[3].Id])));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PrivateInstruction.Should().Be(
			spyingAllowed
				? GameStrings.LittleGirlClosingGuidance
				: null);
		sleep.AffectedPlayerIds.Should().Equal(players[2].Id);
		recoveredPolicy.ObservedAttempts.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void KnownCollective_WhenSpyingIsDenied_PreservesWerewolfCadenceWithoutGuidance()
	{
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Denied);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers("Little Girl", "Werewolf", "Villager A", "Villager B", "Villager C")
			.WithRoles(
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder
			.ArrangeKnownRole(players[0].Id, MainRoleType.LittleGirl)
			.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();

		var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.ConfirmNightStart());

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleHoldersWakeUp.Format(GameStrings.WerewolvesGroupName));
		wake.PrivateInstruction.Should().BeNull();
		wake.AffectedPlayerIds.Should().Equal(players[1].Id);
		policy.ObservedAttempts.Should().ContainSingle();

		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					victimSelection.CreateResponse([players[2].Id])));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PrivateInstruction.Should().BeNull();
		sleep.AffectedPlayerIds.Should().Equal(players[1].Id);
		policy.ObservedAttempts.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void CollectiveAvailability_UsesLivingCurrentHolderAfterRoleTransfer()
	{
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers("Original Holder", "Werewolf", "Current Holder", "Villager A", "Villager B")
			.WithRoles(
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder
			.ArrangeKnownRole(players[0].Id, MainRoleType.LittleGirl)
			.ArrangeCurrentRole(players[0].Id, MainRoleType.SimpleVillager)
			.ArrangeKnownRole(players[2].Id, MainRoleType.LittleGirl)
			.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();

		var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.ConfirmNightStart());

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PrivateInstruction.Should().Be(GameStrings.LittleGirlOpeningGuidance);
		policy.ObservedAttempts.Should().ContainSingle();
		policy.ObservedAttempts.Single().ActingPlayer.Id.Should().Be(players[2].Id);
		policy.ObservedAttempts.Single().ActingPlayer.Id.Should().NotBe(players[0].Id);
		MarkTestCompleted();
	}

	[Fact]
	public void CollectiveSleep_ClosesGuidanceBeforeLaterSoloWerewolfCall()
	{
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers("Little Girl", "Werewolf", "Wolf-Father", "Villager A", "Villager B")
			.WithRoles(
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder
			.ArrangeKnownRole(players[0].Id, MainRoleType.LittleGirl)
			.ArrangeKnownRole(players[1].Id, MainRoleType.SimpleWerewolf)
			.ArrangeKnownRole(players[2].Id, MainRoleType.AccursedWolfFather)
			.ArrangeKnownWerewolfFactionAgentGroup(
				players[1].Id,
				players[2].Id);
		builder.ConfirmGameStart();
		var collectiveWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(collectiveWake.CreateResponse()));
		var collectiveSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					victimSelection.CreateResponse([players[3].Id])));

		collectiveWake.PrivateInstruction.Should().Be(
			GameStrings.LittleGirlOpeningGuidance);
		collectiveSleep.PrivateInstruction.Should().Be(
			GameStrings.LittleGirlClosingGuidance);

		var soloWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(collectiveSleep.CreateResponse()));

		soloWake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		soloWake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(
				GameStrings.AccursedWolfFatherRoleName));
		soloWake.PrivateInstruction.Should().BeNull();
		soloWake.AffectedPlayerIds.Should().Equal(players[2].Id);
		policy.ObservedAttempts.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void CollectiveWithoutLivingLittleGirl_DoesNotEvaluateOrEmitGuidance()
	{
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers("Little Girl", "Werewolf", "Villager A", "Villager B", "Villager C")
			.WithRoles(
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder
			.ArrangeKnownRole(players[0].Id, MainRoleType.LittleGirl)
			.ArrangeEliminatedPlayer(players[0].Id)
			.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();

		var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.ConfirmNightStart());

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PrivateInstruction.Should().BeNull();
		wake.AffectedPlayerIds.Should().Equal(players[1].Id);
		policy.ObservedAttempts.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedIdentification_WithKnownEmptyCollective_RehydratesAtStableFinishNightBoundary()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var littleGirl = builder.GetGameState()!.GetPlayers().First();
		builder.ArrangeKnownWerewolfFactionAgentGroup();
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var identificationResponse =
			identification.CreateResponse([littleGirl.Id]);
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(identificationResponse));

		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		var firstService = new GameService();
		var firstGameId = firstService.RehydrateSession(
			builder.SerializeSession());
		var secondService = new GameService();
		var secondGameId = secondService.RehydrateSession(
			firstService.SerializeSession(firstGameId));
		var recoveredFinishNight =
			InstructionAssert.ExpectType<ConfirmationInstruction>(
				secondService.GetCurrentInstruction(secondGameId));

		recoveredFinishNight.InstructionId.Should().Be(finishNight.InstructionId);
		recoveredFinishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		Action staleIdentification = () => secondService.ProcessInstruction(
			secondGameId,
			identificationResponse);
		staleIdentification.Should().Throw<InvalidOperationException>()
			.WithMessage("*pending Moderator Instruction*");
		secondService.ProcessInstruction(
				secondGameId,
				recoveredFinishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		MarkTestCompleted();
	}

	[Fact]
	public void Identification_RejectsMultipleOrDeadSelectionsWithoutStateAdvance()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ArrangeEliminatedPlayer(players[4].Id);
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var session = builder.GetGameState()!;
		var serializedBefore = builder.SerializeSession();
		var logBefore = session.GameHistoryLog.ToArray();

		identification.SelectablePlayerIds.Should().NotContain(players[4].Id);
		Action multipleSelection = () => builder.Process(
			identification.CreateResponse([players[0].Id, players[1].Id]));

		multipleSelection.Should().Throw<InvalidOperationException>();
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			identification.InstructionId);
		builder.SerializeSession().Should().Be(serializedBefore);
		session.GameHistoryLog.Should().Equal(logBefore);

		var next = builder.Process(
			identification.CreateResponse([players[0].Id]));
		next.IsSuccess.Should().BeTrue();
		session.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.LittleGirl);
		MarkTestCompleted();
	}

	[Fact]
	public void KnownEmptyCollective_OmitsCallAndLittleGirlAvailabilityEvaluation()
	{
		var policy = new RecordingPolicy(RolePowerAvailabilityResult.Allowed);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var littleGirl = builder.GetGameState()!.GetPlayers().First();
		builder
			.ArrangeKnownRole(littleGirl.Id, MainRoleType.LittleGirl)
			.ArrangeKnownWerewolfFactionAgentGroup();
		builder.ConfirmGameStart();

		var result = builder.ConfirmNightStart();
		result.IsSuccess.Should().BeTrue();
		result.ModeratorInstruction.Should().NotBeNull();
		var next = result.ModeratorInstruction!;

		next.Semantic.Should().NotBe(ModeratorInstructionSemantic.WakeRole);
		next.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		next.PublicAnnouncement.Should().NotContain(
			GameStrings.WerewolvesGroupName);
		policy.ObservedAttempts.Should().BeEmpty();
		MarkTestCompleted();
	}

	private sealed class RecordingPolicy(
		params RolePowerAvailabilityResult[] results)
		: IRolePowerAvailabilityPolicy
	{
		private readonly Queue<RolePowerAvailabilityResult> _results =
			new(results);

		internal List<RolePowerAttempt> ObservedAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			ObservedAttempts.Add(attempt);
			return _results.Count > 0
				? _results.Dequeue()
				: RolePowerAvailabilityResult.Allowed;
		}
	}
}
