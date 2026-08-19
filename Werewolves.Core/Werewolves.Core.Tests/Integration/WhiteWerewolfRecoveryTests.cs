using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class WhiteWerewolfRecoveryTests
{
	[Fact]
	public void Decline_SerializeRehydrateReplaysStableNightStartWithoutCommit()
	{
		var (builder, stableNightStart, targetSelection) =
			CreateNightTwoTargetSelection();
		builder.Process(
				targetSelection.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Which.Semantic.Should().Be(
				ModeratorInstructionSemantic.PutRoleToSleep);
		var freshService = new GameService();

		var recoveredGameId = freshService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredSession =
			freshService.GetGameStateView(recoveredGameId)!;
		var recoveredNightStart = freshService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredNightStart.Should().BeEquivalentTo(stableNightStart);
		recoveredSession.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType ==
				NightActionType.WhiteWerewolfVictimSelection);

		Action replayDecline = () => freshService.ProcessInstruction(
			recoveredGameId,
			targetSelection.CreateResponse([]));

		replayDecline.Should().Throw<InvalidOperationException>();
		freshService.GetCurrentInstruction(recoveredGameId)
			.Should().BeEquivalentTo(recoveredNightStart);
		recoveredSession.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType ==
				NightActionType.WhiteWerewolfVictimSelection);
	}

	[Fact]
	public void CommittedAttack_SerializeRehydrateResumesTheSleepBoundary()
	{
		var (builder, _, targetSelection) = CreateNightTwoTargetSelection();
		var werewolfId = targetSelection.SelectablePlayerIds.Single();
		var sleep = InstructionAssert
			.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					targetSelection.CreateResponse([werewolfId])));
		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		var freshService = new GameService();

		var recoveredGameId = freshService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredSession =
			freshService.GetGameStateView(recoveredGameId)!;

		freshService.GetCurrentInstruction(recoveredGameId)
			.Should().BeEquivalentTo(sleep);
		recoveredSession.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType ==
				NightActionType.WhiteWerewolfVictimSelection)
			.Which.TargetIds.Should().Equal(werewolfId);

		freshService.ProcessInstruction(
				recoveredGameId,
				sleep.CreateResponse())
			.IsSuccess.Should().BeTrue();

		freshService.GetCurrentInstruction(recoveredGameId)!.Semantic
			.Should().NotBe(ModeratorInstructionSemantic.PutRoleToSleep);
	}

	[Fact]
	public void AcceptedIdentification_PreKnownWhiteBeneficiaryRehydratesDownstreamRole()
	{
		var recovery = CreateAcceptedWhiteIdentificationRecovery(
			preKnownWhiteBeneficiary: true);
		var freshService = new GameService();

		var recoveredGameId = freshService.RehydrateSession(
			recovery.Builder.GetGameState()!.Serialize());

		freshService.GetCurrentInstruction(recoveredGameId)
			.Should().BeEquivalentTo(recovery.NextInstruction);
	}

	[Fact]
	public void AcceptedAgentGroupObservation_KnownEmptyWhiteHolderSetRehydratesOneUnchangedClosureWithoutIdentification()
	{
		var builder = GameTestBuilder.Create()
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
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var whiteWerewolf = players[1];
		builder.ArrangeKnownRole(
			whiteWerewolf.Id,
			MainRoleType.WhiteWerewolf);
		builder.ArrangeEliminatedPlayer(whiteWerewolf.Id);
		builder.ArrangeExplicitFactionTransition(
			"test-dead-white-werewolf-agent",
			FactionFact.Agent(
				whiteWerewolf.Id,
				Faction.Werewolf,
				FactionAgentKnowledge.KnownAgent,
				new FactionFactEffectiveBoundary(
					session.TurnNumber,
					session.GetCurrentPhase(),
					session.GameHistoryLog.Count())));
		session.RoleInPlayCount(MainRoleType.WhiteWerewolf).Should().Be(1);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var observation = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		var collectiveVictimSelection = builder.Process(
				observation.CreateResponse([players[0].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var closure = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure)
			.Subject;
		var collectiveSleep = builder.Process(
				collectiveVictimSelection.CreateResponse([players[3].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var instructionAfterCollective = builder.Process(
				collectiveSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		instructionAfterCollective.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		var freshService = new GameService();

		var recoveredGameId = freshService.RehydrateSession(session.Serialize());
		var recoveredSession = freshService.GetGameStateView(recoveredGameId)!;

		freshService.GetCurrentInstruction(recoveredGameId)
			.Should().BeEquivalentTo(instructionAfterCollective);
		recoveredSession.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure)
			.Which.Should().BeEquivalentTo(
				closure,
				options => options.WithStrictOrdering());
	}

	[Fact]
	public void AcceptedAgentGroupObservation_MissingEntailedClosureFactIsRejected()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.BigBadWolf,
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
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var observation = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		var victimSelection = builder.Process(
				observation.CreateResponse(
					[players[0].Id, whiteWerewolf.Id, players[2].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		victimSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		var tampered = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize())
			.RemoveInitialBeneficiaryClosureFact(whiteWerewolf.Id)
			.Serialize();
		var freshService = new GameService();

		Action rehydrate = () => freshService.RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void AcceptedIdentification_CoherentlyRetargetedNonHolderClosureIsRejected()
	{
		var recovery = CreateAcceptedWhiteIdentificationRecovery(
			preKnownWhiteBeneficiary: false);
		var tampered = RecoveryPayloadTestDriver
			.Parse(recovery.Builder.GetGameState()!.Serialize())
			.SwapInitialBeneficiaryClosureAssignmentsAndCaches(
				recovery.WerewolfId,
				recovery.VillagerId)
			.Serialize();
		var freshService = new GameService();

		Action rehydrate = () => freshService.RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void AcceptedIdentification_MissingWhiteBeneficiaryClosureFactIsRejected()
	{
		var recovery = CreateAcceptedWhiteIdentificationRecovery(
			preKnownWhiteBeneficiary: false);
		var tampered = RecoveryPayloadTestDriver
			.Parse(recovery.Builder.GetGameState()!.Serialize())
			.RemoveInitialBeneficiaryClosureFact(recovery.WhiteWerewolfId)
			.Serialize();
		var freshService = new GameService();

		Action rehydrate = () => freshService.RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
	}

	private static AcceptedWhiteIdentificationRecovery
		CreateAcceptedWhiteIdentificationRecovery(
			bool preKnownWhiteBeneficiary)
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.BigBadWolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var whiteWerewolf = players[1];
		if (preKnownWhiteBeneficiary)
		{
			var session = builder.GetGameState()!;
			builder.ArrangeExplicitFactionTransition(
				"test-pre-known-white-beneficiary",
				FactionFact.Beneficiary(
					whiteWerewolf.Id,
					Faction.WhiteWerewolf,
					new FactionFactEffectiveBoundary(
						session.TurnNumber,
						session.GetCurrentPhase(),
						session.GameHistoryLog.Count())));
		}

		var werewolfAgentIds = new HashSet<Guid>
		{
			players[0].Id,
			whiteWerewolf.Id,
			players[2].Id
		};
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			[.. werewolfAgentIds]);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.CompleteWerewolfNightAction(
					werewolfAgentIds,
					players[4].Id));

		var nextInstruction =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(
					identification.CreateResponse([whiteWerewolf.Id])));

		nextInstruction.RoleIdentification.Should().Be(
			MainRoleType.BigBadWolf);
		return new AcceptedWhiteIdentificationRecovery(
			builder,
			whiteWerewolf.Id,
			players[0].Id,
			players[3].Id,
			nextInstruction);
	}

	private static (
			GameTestBuilder Builder,
			ConfirmationInstruction StableNightStart,
			SelectPlayersInstruction TargetSelection)
		CreateNightTwoTargetSelection()
	{
		var builder = GameTestBuilder.Create()
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
			players[0].Id,
			whiteWerewolf.Id);
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
		var stableNightStart = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		stableNightStart.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartNight);
		builder.Process(stableNightStart.CreateResponse())
			.IsSuccess.Should().BeTrue();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id, whiteWerewolf.Id],
					players[5].Id));
		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		return (builder, stableNightStart, targetSelection);
	}

	private sealed record AcceptedWhiteIdentificationRecovery(
		GameTestBuilder Builder,
		Guid WhiteWerewolfId,
		Guid WerewolfId,
		Guid VillagerId,
		SelectPlayersInstruction NextInstruction);
}
