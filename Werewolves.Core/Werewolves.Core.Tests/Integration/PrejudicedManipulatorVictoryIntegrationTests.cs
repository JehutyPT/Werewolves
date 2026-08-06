using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class PrejudicedManipulatorVictoryIntegrationTests
{
	[Fact]
	public void DawnVictory_WhenHunterShotAngelCompletesPiperAndManipulatorConditions_AwardsAllThree()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(Guid.NewGuid(), "Manipulator"),
			new(Guid.NewGuid(), "Player B"),
			new(Guid.NewGuid(), "Werewolf"),
			new(Guid.NewGuid(), "Hunter"),
			new(Guid.NewGuid(), "Player E")
		];
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			[roster[0].Id, roster[1].Id, roster[2].Id],
			[roster[3].Id, roster[4].Id]);
		HashSet<Guid> charmTargetIds = [roster[0].Id, roster[2].Id];
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			roster,
			[
				MainRoleType.PrejudicedManipulator,
				MainRoleType.Piper,
				MainRoleType.SimpleWerewolf,
				MainRoleType.Hunter,
				MainRoleType.Angel
			],
			publicGroupPartition: partition));
		var nightStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					start.CreateResponse()));
		var manipulatorIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					nightStart.CreateResponse()));
		manipulatorIdentification.RoleIdentification.Should().Be(
			MainRoleType.PrejudicedManipulator);
		var werewolfObservation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					manipulatorIdentification.CreateResponse([roster[0].Id])));
		werewolfObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		werewolfObservation.RoleIdentification.Should().BeNull();
		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					werewolfObservation.CreateResponse([roster[2].Id])));
		var werewolfSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					victimSelection.CreateResponse([roster[3].Id])));
		var piperIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					werewolfSleep.CreateResponse()));
		piperIdentification.RoleIdentification.Should().Be(MainRoleType.Piper);
		var piperWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					piperIdentification.CreateResponse([roster[1].Id])));
		var charmSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					piperWake.CreateResponse()));
		var piperSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					charmSelection.CreateResponse(charmTargetIds)));
		var charmedRecognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					piperSleep.CreateResponse()));
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					charmedRecognition.CreateResponse()));
		var hunterReveal =
			InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					finishNight.CreateResponse()));
		var finalShot =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					hunterReveal.CreateResponse(
						new Dictionary<Guid, MainRoleType>
						{
							[roster[3].Id] = MainRoleType.Hunter
						})));
		var angelReveal =
			InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					finalShot.CreateResponse([roster[4].Id])));

		var finished = InstructionAssert
			.ExpectSuccessWithType<FinishedGameConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					angelReveal.CreateResponse(
						new Dictionary<Guid, MainRoleType>
						{
							[roster[4].Id] = MainRoleType.Angel
						})));

		var expected = new SharedVictoryGameResult(
			[
				Faction.Piper,
				Faction.Angel,
				Faction.PrejudicedManipulator
			]);
		finished.GameResult.Should().Be(expected);
		finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
		var session = service.GetGameStateView(start.GameGuid)!;
		session.PublicGroupPartition.Should().Be(partition);
		session.RequireKnownFactionBeneficiary(roster[0].Id)
			.Should().Be(Faction.PrejudicedManipulator);
		session.RequireKnownFactionBeneficiary(roster[1].Id)
			.Should().Be(Faction.Piper);
		session.RequireKnownFactionBeneficiary(roster[4].Id)
			.Should().Be(Faction.Villager);
		charmTargetIds.Should().OnlyContain(playerId =>
			session.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Charmed));
		session.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry =>
				entry.Role == MainRoleType.Angel &&
				entry.CurrentPhase == GamePhase.Night);
		session.GameHistoryLog
			.OfType<RoleRevealLogEntry>()
			.Should().ContainSingle(entry =>
				entry.RevealedRoles.GetValueOrDefault(roster[4].Id) ==
				MainRoleType.Angel);
		session.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.PiperCharm &&
				entry.TargetIds!.ToHashSet().SetEquals(charmTargetIds));
		session.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().ContainSingle(entry =>
				entry.GameResult.Equals(expected) &&
				entry.VictoryCheckWindow == VictoryCheckWindow.Dawn);
	}

	[Fact]
	public void DawnVictory_WhenPiperCharmsAllEventualSurvivors_AwardsPiperAndManipulator()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(Guid.NewGuid(), "Manipulator"),
			new(Guid.NewGuid(), "Player B"),
			new(Guid.NewGuid(), "Werewolf"),
			new(Guid.NewGuid(), "Hunter"),
			new(Guid.NewGuid(), "Opposing villager")
		];
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			[roster[0].Id, roster[1].Id, roster[2].Id],
			[roster[3].Id, roster[4].Id]);
		HashSet<Guid> charmTargetIds = [roster[0].Id, roster[2].Id];
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			roster,
			[
				MainRoleType.PrejudicedManipulator,
				MainRoleType.Piper,
				MainRoleType.SimpleWerewolf,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager
			],
			publicGroupPartition: partition));
		var nightStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					start.CreateResponse()));
		var manipulatorIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					nightStart.CreateResponse()));
		var werewolfObservation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					manipulatorIdentification.CreateResponse([roster[0].Id])));
		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					werewolfObservation.CreateResponse([roster[2].Id])));
		var werewolfSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					victimSelection.CreateResponse([roster[3].Id])));
		var piperIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					werewolfSleep.CreateResponse()));
		piperIdentification.RoleIdentification.Should().Be(MainRoleType.Piper);
		var piperWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					piperIdentification.CreateResponse([roster[1].Id])));
		var charmSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					piperWake.CreateResponse()));
		charmSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectPiperTargets);
		var piperSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					charmSelection.CreateResponse(
						charmTargetIds)));
		var charmedRecognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					piperSleep.CreateResponse()));
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					charmedRecognition.CreateResponse()));
		var hunterReveal =
			InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					finishNight.CreateResponse()));
		var finalShot =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					hunterReveal.CreateResponse(
						new Dictionary<Guid, MainRoleType>
						{
							[roster[3].Id] = MainRoleType.Hunter
						})));
		var opposingVillagerReveal =
			InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					finalShot.CreateResponse([roster[4].Id])));

		var finished = InstructionAssert
			.ExpectSuccessWithType<FinishedGameConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					opposingVillagerReveal.CreateResponse(
						new Dictionary<Guid, MainRoleType>
						{
							[roster[4].Id] = MainRoleType.SimpleVillager
						})));

		var expected = new SharedVictoryGameResult(
			[Faction.Piper, Faction.PrejudicedManipulator]);
		finished.GameResult.Should().Be(expected);
		finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
		var session = service.GetGameStateView(start.GameGuid)!;
		session.PublicGroupPartition.Should().Be(partition);
		session.RequireKnownFactionBeneficiary(roster[0].Id)
			.Should().Be(Faction.PrejudicedManipulator);
		session.RequireKnownFactionBeneficiary(roster[1].Id)
			.Should().Be(Faction.Piper);
		charmTargetIds.Should().OnlyContain(playerId =>
			session.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Charmed));
		session.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.PiperCharm &&
				entry.TargetIds!.ToHashSet().SetEquals(charmTargetIds));
		session.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().ContainSingle(entry =>
				entry.GameResult.Equals(expected) &&
				entry.VictoryCheckWindow == VictoryCheckWindow.Dawn);
	}

	[Fact]
	public void DawnVictory_WhenAngelIsLastOpposingPlayer_AwardsAngelAndManipulator()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(Guid.NewGuid(), "Manipulator"),
			new(Guid.NewGuid(), "Werewolf"),
			new(Guid.NewGuid(), "Player C"),
			new(Guid.NewGuid(), "Villager A"),
			new(Guid.NewGuid(), "Villager B")
		];
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			[roster[0].Id, roster[1].Id, roster[3].Id, roster[4].Id],
			[roster[2].Id]);
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			roster,
			[
				MainRoleType.PrejudicedManipulator,
				MainRoleType.SimpleWerewolf,
				MainRoleType.Angel,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			publicGroupPartition: partition));
		var nightStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					start.CreateResponse()));
		var manipulatorIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					nightStart.CreateResponse()));
		manipulatorIdentification.RoleIdentification.Should().Be(
			MainRoleType.PrejudicedManipulator);
		var werewolfObservation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					manipulatorIdentification.CreateResponse([roster[0].Id])));
		werewolfObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		werewolfObservation.RoleIdentification.Should().BeNull();
		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					werewolfObservation.CreateResponse([roster[1].Id])));
		var werewolfSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					victimSelection.CreateResponse([roster[2].Id])));
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					werewolfSleep.CreateResponse()));
		var angelReveal =
			InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					finishNight.CreateResponse()));

		var finished = InstructionAssert
			.ExpectSuccessWithType<FinishedGameConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					angelReveal.CreateResponse(
						new Dictionary<Guid, MainRoleType>
						{
							[roster[2].Id] = MainRoleType.Angel
						})));

		var expected = new SharedVictoryGameResult(
			[Faction.Angel, Faction.PrejudicedManipulator]);
		finished.GameResult.Should().Be(expected);
		finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
		var session = service.GetGameStateView(start.GameGuid)!;
		session.PublicGroupPartition.Should().Be(partition);
		session.RequireKnownFactionBeneficiary(roster[0].Id)
			.Should().Be(Faction.PrejudicedManipulator);
		session.RequireKnownFactionBeneficiary(roster[2].Id)
			.Should().Be(Faction.Villager);
		session.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry =>
				entry.Role == MainRoleType.Angel &&
				entry.CurrentPhase == GamePhase.Night);
		session.GameHistoryLog
			.OfType<RoleRevealLogEntry>()
			.Should().ContainSingle(entry =>
				entry.RevealedRoles.GetValueOrDefault(roster[2].Id) ==
				MainRoleType.Angel);
		session.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().ContainSingle(entry =>
				entry.GameResult.Equals(expected) &&
				entry.VictoryCheckWindow == VictoryCheckWindow.Dawn);
	}

	[Fact]
	public void DawnVictory_WhenLivingManipulatorWasInfected_DoesNotAwardManipulatorFaction()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(Guid.NewGuid(), "Werewolf"),
			new(Guid.NewGuid(), "Accursed Wolf-Father"),
			new(Guid.NewGuid(), "Big Bad Wolf"),
			new(Guid.NewGuid(), "Manipulator"),
			new(Guid.NewGuid(), "Opposing villager")
		];
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			[roster[0].Id, roster[1].Id, roster[2].Id, roster[3].Id],
			[roster[4].Id]);
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			roster,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.BigBadWolf,
				MainRoleType.PrejudicedManipulator,
				MainRoleType.SimpleVillager
			],
			publicGroupPartition: partition));
		var nightStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					start.CreateResponse()));
		var manipulatorIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					nightStart.CreateResponse()));
		var werewolfObservation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					manipulatorIdentification.CreateResponse([roster[3].Id])));
		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					werewolfObservation.CreateResponse(
						[roster[0].Id, roster[1].Id, roster[2].Id])));
		var werewolfSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					victimSelection.CreateResponse([roster[3].Id])));
		var wolfFatherIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					werewolfSleep.CreateResponse()));
		var infectionChoice =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					wolfFatherIdentification.CreateResponse([roster[1].Id])));
		var wolfFatherSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					infectionChoice.CreateResponse(
						AccursedWolfFatherInfectionOptionIds.Infect)));
		var bigBadWolfIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					wolfFatherSleep.CreateResponse()));
		var additionalVictimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					bigBadWolfIdentification.CreateResponse([roster[2].Id])));
		var bigBadWolfSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					additionalVictimSelection.CreateResponse([roster[4].Id])));
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					bigBadWolfSleep.CreateResponse()));
		var opposingVillagerReveal =
			InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					finishNight.CreateResponse()));

		var finished = InstructionAssert
			.ExpectSuccessWithType<FinishedGameConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					opposingVillagerReveal.CreateResponse(
						new Dictionary<Guid, MainRoleType>
						{
							[roster[4].Id] = MainRoleType.SimpleVillager
						})));

		finished.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.Werewolf));
		finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
		var session = service.GetGameStateView(start.GameGuid)!;
		session.PublicGroupPartition.Should().Be(partition);
		session.GetPlayerState(roster[3].Id).Health.Should().Be(
			PlayerHealth.Alive);
		session.RequireKnownFactionBeneficiary(roster[3].Id)
			.Should().Be(Faction.Werewolf);
		session.GetFactionAgentKnowledge(roster[3].Id, Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		session.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType ==
					NightActionType.AccursedWolfFatherInfection &&
				entry.TargetIds!.SequenceEqual(new[] { roster[3].Id }));
		session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.SelectMany(entry => entry.Facts)
			.Should().Contain(fact =>
				fact.PlayerId == roster[3].Id &&
				fact.Type == FactionFactType.Beneficiary &&
				fact.Faction == Faction.Werewolf);
		session.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().ContainSingle(entry =>
				entry.GameResult.Equals(
					new SingleFactionGameResult(Faction.Werewolf)) &&
				entry.VictoryCheckWindow == VictoryCheckWindow.Dawn);
	}

	[Fact]
	public void DawnCascade_WhenOpposingHunterAndManipulatorBothDie_DoesNotAwardPosthumousVictory()
	{
		GameSessionPlayerConfig[] roster =
		[
			new(Guid.NewGuid(), "Manipulator"),
			new(Guid.NewGuid(), "Werewolf"),
			new(Guid.NewGuid(), "Hunter"),
			new(Guid.NewGuid(), "Villager A"),
			new(Guid.NewGuid(), "Villager B")
		];
		var partition = PublicGroupPartition.Create(
			roster.Select(player => player.Id),
			[roster[0].Id, roster[1].Id, roster[3].Id, roster[4].Id],
			[roster[2].Id]);
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			roster,
			[
				MainRoleType.PrejudicedManipulator,
				MainRoleType.SimpleWerewolf,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			publicGroupPartition: partition));
		var nightStart =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					start.CreateResponse()));
		var manipulatorIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					nightStart.CreateResponse()));
		var werewolfObservation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					manipulatorIdentification.CreateResponse([roster[0].Id])));
		var victimSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					werewolfObservation.CreateResponse([roster[1].Id])));
		var werewolfSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					victimSelection.CreateResponse([roster[2].Id])));
		werewolfSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					werewolfSleep.CreateResponse()));
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		var hunterReveal =
			InstructionAssert.ExpectSuccessWithType<AssignRolesInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					finishNight.CreateResponse()));
		var finalShot =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					hunterReveal.CreateResponse(new Dictionary<Guid, MainRoleType>
					{
						[roster[2].Id] = MainRoleType.Hunter
					})));
		var knownManipulatorReveal =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				service.ProcessInstruction(
					start.GameGuid,
					finalShot.CreateResponse([roster[0].Id])));
		knownManipulatorReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignEliminationCascadeRoles);
		var afterCascade = service.ProcessInstruction(
			start.GameGuid,
			knownManipulatorReveal.CreateResponse());

		afterCascade.IsSuccess.Should().BeTrue();
		afterCascade.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>()
			.Which.Semantic.Should().Be(
				ModeratorInstructionSemantic.StartDayDebate);
		var session = service.GetGameStateView(start.GameGuid)!;
		session.PublicGroupPartition.Should().Be(partition);
		session.GetPlayerState(roster[0].Id).Health.Should().Be(
			PlayerHealth.Dead);
		session.GetPlayerState(roster[2].Id).Health.Should().Be(
			PlayerHealth.Dead);
		session.RequireKnownFactionBeneficiary(roster[0].Id)
			.Should().Be(Faction.PrejudicedManipulator);
		session.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == roster[2].Id &&
				entry.Reason == EliminationReason.WerewolfAttack);
		session.GameHistoryLog
			.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == roster[0].Id &&
				entry.Reason == EliminationReason.HunterShot);
		var victoryResults = session.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Select(entry => entry.GameResult)
			.ToArray();
		victoryResults.OfType<SingleFactionGameResult>()
			.Should().NotContain(result =>
				result.Faction == Faction.PrejudicedManipulator);
		victoryResults.OfType<SharedVictoryGameResult>()
			.SelectMany(result => result.Factions)
			.Should().NotContain(Faction.PrejudicedManipulator);
	}
}
