using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class WhiteWerewolfRoleTests : DiagnosticTestBase
{
	public WhiteWerewolfRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void NightOne_UnknownHolder_UsesReservedSlotForExactRoleIdentificationOnly()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var whiteWerewolf = players[1];
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			[players[0].Id, whiteWerewolf.Id]);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id, whiteWerewolf.Id],
					players[4].Id));

		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(
			MainRoleType.WhiteWerewolf);
		identification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.Single);
		identification.PublicAnnouncement.Should().BeNull();
		identification.PrivateInstruction.Should().NotBeNullOrWhiteSpace();
		MarkTestCompleted();
	}

	[Fact]
	public void NightOne_IdentificationRunsAfterWolfFatherAndBeforeBigBadWolf()
	{
		var builder = CreateBuilder()
			.WithPlayers(8)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.WhiteWerewolf,
				MainRoleType.BigBadWolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolfAgentIds = new HashSet<Guid>
		{
			players[0].Id,
			players[1].Id,
			players[2].Id,
			players[3].Id
		};
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			[.. werewolfAgentIds]);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var wolfFatherIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.CompleteWerewolfNightAction(
					werewolfAgentIds,
					players[6].Id));
		wolfFatherIdentification.RoleIdentification.Should().Be(
			MainRoleType.AccursedWolfFather);

		var whiteIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.CompleteAccursedWolfFatherNightAction(
					players[1].Id,
					infectsVictim: false));
		whiteIdentification.RoleIdentification.Should().Be(
			MainRoleType.WhiteWerewolf);

		var bigBadWolfIdentification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(
					whiteIdentification.CreateResponse([players[2].Id])));
		bigBadWolfIdentification.RoleIdentification.Should().Be(
			MainRoleType.BigBadWolf);
		MarkTestCompleted();
	}

	[Fact]
	public void NightTwo_KnownLivingHolder_WakesAndReceivesOptionalKnownAgentTargets()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var whiteWerewolf = players[1];
		builder.ArrangeKnownRole(
			whiteWerewolf.Id,
			MainRoleType.WhiteWerewolf);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			[players[0].Id, whiteWerewolf.Id]);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [players[0].Id, whiteWerewolf.Id],
			WerewolfVictimId = players[4].Id
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[players[4].Id] = MainRoleType.SimpleVillager
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();
		builder.ConfirmNightStart();

		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id, whiteWerewolf.Id],
					players[5].Id));
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().Equal(whiteWerewolf.Id);

		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));

		targetSelection.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.SingleOptional);
		targetSelection.EmptySelectionOptionLabel.Should().Be(
			GameStrings.DeclineOption);
		targetSelection.SelectablePlayerIds.Should().Equal(players[0].Id);
		targetSelection.AffectedPlayerIds.Should().Equal(whiteWerewolf.Id);
		targetSelection.PublicAnnouncement.Should().BeNull();
		targetSelection.PrivateInstruction.Should().NotBeNullOrWhiteSpace();
		MarkTestCompleted();
	}

	[Fact]
	public void NightTwo_KnownEmptyLivingHolderSet_OmitsEntireSoloCall()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var whiteWerewolf = players[1];
		builder.ArrangeKnownRole(
			whiteWerewolf.Id,
			MainRoleType.WhiteWerewolf);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			[players[0].Id, whiteWerewolf.Id]);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [players[0].Id, whiteWerewolf.Id],
			WerewolfVictimId = players[4].Id
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[players[4].Id] = MainRoleType.SimpleVillager
		}).IsSuccess.Should().BeTrue();
		builder.ArrangeEliminatedPlayer(whiteWerewolf.Id);
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();
		builder.ConfirmNightStart();

		var afterCollective =
			builder.CompleteWerewolfNightAction(
				[players[0].Id],
				players[5].Id);

		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterCollective);
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		MarkTestCompleted();
	}

	[Fact]
	public void LaterAcquiredHolder_UsesAbsoluteEvenNightSchedule()
	{
		var builder = CreateBuilder()
			.WithPlayers(10)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var originalWhiteWerewolf = players[1];
		var laterWhiteWerewolf = players[2];
		builder.ArrangeKnownPhysicalRole(
			originalWhiteWerewolf.Id,
			MainRoleType.WhiteWerewolf);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			werewolf.Id,
			originalWhiteWerewolf.Id);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [werewolf.Id, originalWhiteWerewolf.Id],
			WerewolfVictimId = players[6].Id
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[players[6].Id] = MainRoleType.SimpleVillager
		}).IsSuccess.Should().BeTrue();
		builder.ArrangeCurrentRole(
			originalWhiteWerewolf.Id,
			MainRoleType.SimpleVillager);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();

		builder.ConfirmNightStart();
		var secondFinishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					players[7].Id));
		secondFinishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		builder.Process(secondFinishNight.CreateResponse())
			.IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[players[7].Id] = MainRoleType.SimpleVillager
		}).IsSuccess.Should().BeTrue();
		builder.ArrangeKnownPhysicalRole(
			laterWhiteWerewolf.Id,
			MainRoleType.SimpleVillager);
		builder.ArrangeKnownRole(
			laterWhiteWerewolf.Id,
			MainRoleType.WhiteWerewolf);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			werewolf.Id,
			laterWhiteWerewolf.Id);
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();

		builder.ConfirmNightStart();
		var thirdFinishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id, laterWhiteWerewolf.Id],
					players[8].Id));
		thirdFinishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		builder.Process(thirdFinishNight.CreateResponse())
			.IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[players[8].Id] = MainRoleType.SimpleVillager
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();

		builder.ConfirmNightStart();
		var fourthNightWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id, laterWhiteWerewolf.Id],
					players[9].Id));

		fourthNightWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		fourthNightWake.AffectedPlayerIds.Should().Equal(
			laterWhiteWerewolf.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void NightTwo_DeclineSleepsWithoutCommittingSoloAttack()
	{
		var (builder, _, targetSelection) =
			CreateNightTwoTargetSelection();

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(targetSelection.CreateResponse([])));

		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType ==
				NightActionType.WhiteWerewolfVictimSelection);
		MarkTestCompleted();
	}

	[Fact]
	public void NightTwo_NoOtherLivingWerewolfAgent_SleepsWithoutTargetSelectionOrCommit()
	{
		var (builder, players, wake) =
			CreateNightTwoWake(
				eliminateOtherAgentBeforeNightTwo: true);

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(wake.CreateResponse()));

		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(players[1].Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType ==
				NightActionType.WhiteWerewolfVictimSelection);
		MarkTestCompleted();
	}

	[Fact]
	public void OnlyWhiteAndOrdinaryWerewolfAlive_CollectiveOmitsVictimAndContinuesToSoloCall()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var whiteWerewolf = players[1];
		builder.ArrangeKnownRole(
			whiteWerewolf.Id,
			MainRoleType.WhiteWerewolf);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			werewolf.Id,
			whiteWerewolf.Id);
		foreach (var villager in players.Skip(2))
		{
			builder.ArrangeEliminatedPlayer(villager.Id);
		}
		builder.ConfirmGameStart();

		var firstCollectiveWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var firstCollectiveSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(firstCollectiveWake.CreateResponse()));
		firstCollectiveSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		var firstFinishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(firstCollectiveSleep.CreateResponse()));
		firstFinishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		builder.Process(firstFinishNight.CreateResponse())
			.IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase().IsSuccess.Should().BeTrue();
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();

		var secondCollectiveWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var secondCollectiveSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(secondCollectiveWake.CreateResponse()));
		secondCollectiveSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);

		var whiteWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(secondCollectiveSleep.CreateResponse()));
		whiteWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		whiteWake.AffectedPlayerIds.Should().Equal(whiteWerewolf.Id);
		var whiteTargetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(whiteWake.CreateResponse()));
		whiteTargetSelection.SelectablePlayerIds.Should().Equal(werewolf.Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType ==
				NightActionType.WerewolfVictimSelection);
		MarkTestCompleted();
	}

	[Fact]
	public void NightTwo_AvailabilityDenied_SleepsWithoutOfferingOrCommittingAttack()
	{
		var policy = new SequenceAvailabilityPolicy(false);
		var (builder, players, wake) =
			CreateNightTwoWake(policy);

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(wake.CreateResponse()));

		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(players[1].Id);
		policy.Attempts.Should().ContainSingle();
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType ==
				NightActionType.WhiteWerewolfVictimSelection);
		MarkTestCompleted();
	}

	[Fact]
	public void NightTwo_TargetEliminatedAfterInstruction_IsRejectedWithoutCommit()
	{
		var (builder, players, targetSelection) =
			CreateNightTwoTargetSelection();
		var target = players[0];
		var acceptedTarget =
			targetSelection.CreateResponse([target.Id]);
		builder.ArrangeEliminatedPlayer(target.Id);
		var before = PublicGameSessionSnapshot.Capture(builder);

		Action process = () => builder.Process(acceptedTarget);

		process.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(builder)
			.Should().BeEquivalentTo(
				before,
				options => options.WithStrictOrdering());
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType ==
				NightActionType.WhiteWerewolfVictimSelection);
		MarkTestCompleted();
	}

	[Fact]
	public void NightOne_AcceptedIdentification_AssignsWhiteBeneficiaryAndClosesResidualDefaults()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var whiteWerewolf = players[1];
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			[players[0].Id, whiteWerewolf.Id]);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id, whiteWerewolf.Id],
					players[4].Id));
		var before = builder.GetGameState()!;

		before.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		before.GetFactionBeneficiaryKnowledge(whiteWerewolf.Id)
			.IsKnown.Should().BeFalse();

		builder.Process(identification.CreateResponse([whiteWerewolf.Id]))
			.IsSuccess.Should().BeTrue();
		var after = builder.GetGameState()!;

		after.RequireKnownFactionBeneficiary(whiteWerewolf.Id)
			.Should().Be(Faction.WhiteWerewolf);
		after.RequireKnownFactionBeneficiary(players[0].Id)
			.Should().Be(Faction.Werewolf);
		foreach (var villager in players.Skip(2))
		{
			after.RequireKnownFactionBeneficiary(villager.Id)
				.Should().Be(Faction.Villager);
		}
		after.GetFactionAgentKnowledge(
				whiteWerewolf.Id,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		whiteWerewolf.State.PhysicalCharacterCardRole.Should().BeNull();
		whiteWerewolf.State.PubliclyRevealedRole.Should().BeNull();
		after.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		MarkTestCompleted();
	}

	[Fact]
	public void NightTwo_CommittedTarget_FreshServiceRestoresExactSleepWithoutDuplicatingAttack()
	{
		var (builder, players, targetSelection) =
			CreateNightTwoTargetSelection();
		var whiteWerewolf = players[1];
		var target = players[0];
		var acceptedTarget =
			targetSelection.CreateResponse([target.Id]);

		var expectedSleep = builder.Process(acceptedTarget)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		expectedSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		var committedAttack = builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType ==
				NightActionType.WhiteWerewolfVictimSelection)
			.Subject;
		committedAttack.ActingPlayerId.Should().Be(whiteWerewolf.Id);
		committedAttack.SourceRole.Should().Be(
			MainRoleType.WhiteWerewolf);
		committedAttack.SourcePowerIdentifier.Should().Be(
			"white-werewolf-solo-attack");
		committedAttack.TargetIds.Should().Equal(target.Id);

		var serializedSession = builder.GetGameState()!.Serialize();
		var freshService = new GameService();
		var recoveredGameId =
			freshService.RehydrateSession(serializedSession);
		var recoveredSleep = freshService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredSleep.Should().BeEquivalentTo(expectedSleep);
		var recoveredSession =
			freshService.GetGameStateView(recoveredGameId)!;
		recoveredSession.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType ==
				NightActionType.WhiteWerewolfVictimSelection &&
				entry.TargetIds!.SequenceEqual(new[] { target.Id }));
		var beforeReplay = PublicGameSessionSnapshot.Capture(
			freshService,
			recoveredGameId);

		Action replay = () => freshService.ProcessInstruction(
			recoveredGameId,
			acceptedTarget);

		replay.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(
				freshService,
				recoveredGameId)
			.Should().BeEquivalentTo(
				beforeReplay,
				options => options.WithStrictOrdering());
		freshService.ProcessInstruction(
				recoveredGameId,
				recoveredSleep.CreateResponse())
			.IsSuccess.Should().BeTrue();
		MarkTestCompleted();
	}

	private (
			GameTestBuilder Builder,
			IPlayer[] Players,
			SelectPlayersInstruction TargetSelection)
		CreateNightTwoTargetSelection()
	{
		var (builder, players, wake) = CreateNightTwoWake();
		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		return (builder, players, targetSelection);
	}

	private (
			GameTestBuilder Builder,
			IPlayer[] Players,
			ConfirmationInstruction Wake)
		CreateNightTwoWake(
			IRolePowerAvailabilityPolicy? availabilityPolicy = null,
			bool eliminateOtherAgentBeforeNightTwo = false)
	{
		var builder = CreateBuilder()
			.WithOptionalRolePowerAvailabilityPolicy(availabilityPolicy)
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var whiteWerewolf = players[1];
		builder.ArrangeKnownRole(
			whiteWerewolf.Id,
			MainRoleType.WhiteWerewolf);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			[players[0].Id, whiteWerewolf.Id]);
		builder.ConfirmGameStart();
		builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [players[0].Id, whiteWerewolf.Id],
			WerewolfVictimId = players[4].Id
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[players[4].Id] = MainRoleType.SimpleVillager
		}).IsSuccess.Should().BeTrue();
		if (eliminateOtherAgentBeforeNightTwo)
		{
			builder.ArrangeEliminatedPlayer(players[0].Id);
		}
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();
		builder.ConfirmNightStart();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					eliminateOtherAgentBeforeNightTwo
						? [whiteWerewolf.Id]
						: [players[0].Id, whiteWerewolf.Id],
					players[5].Id));
		return (builder, players, wake);
	}

	private sealed class SequenceAvailabilityPolicy(params bool[] decisions)
		: IRolePowerAvailabilityPolicy
	{
		private int _nextDecision;

		internal List<RolePowerAttempt> Attempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			Attempts.Add(attempt);
			if (_nextDecision >= decisions.Length)
			{
				throw new InvalidOperationException(
					"The availability policy was evaluated more often than expected.");
			}

			return decisions[_nextDecision++]
				? RolePowerAvailabilityResult.Allowed
				: RolePowerAvailabilityResult.Denied;
		}
	}
}
