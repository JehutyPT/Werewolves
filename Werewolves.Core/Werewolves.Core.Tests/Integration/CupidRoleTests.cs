using FluentAssertions;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
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
	public void KnownCrossFactionPair_ClassificationIsCommittedOnlyInsideAtomicInitialClosure()
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
		var lovers = new[] { werewolf.Id, villager.Id };
		builder.ArrangeKnownRole(cupid.Id, MainRoleType.Cupid);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));

		builder.Process(selection.CreateResponse(lovers.ToHashSet()))
			.IsSuccess.Should().BeTrue();

		var session = builder.GetGameState()!;
		var factEntries = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.ToArray();
		var closure = factEntries.Should().ContainSingle(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure).Subject;
		closure.Facts.Should().Contain(fact =>
			fact.PlayerId == werewolf.Id &&
			fact.Type == FactionFactType.Beneficiary &&
			fact.Faction == Faction.CrossFactionLovers);
		closure.Facts.Should().Contain(fact =>
			fact.PlayerId == villager.Id &&
			fact.Type == FactionFactType.Beneficiary &&
			fact.Faction == Faction.CrossFactionLovers);
		factEntries.Should().NotContain(entry =>
			entry.Source.Kind == FactionFactSourceKind.ExplicitTransition &&
			entry.Facts.Any(fact =>
				lovers.Contains(fact.PlayerId) &&
				fact.Type == FactionFactType.Beneficiary &&
				fact.Faction == Faction.CrossFactionLovers));
		session.RequireKnownFactionBeneficiary(werewolf.Id)
			.Should().Be(Faction.CrossFactionLovers);
		session.RequireKnownFactionBeneficiary(villager.Id)
			.Should().Be(Faction.CrossFactionLovers);
		MarkTestCompleted();
	}

	[Fact]
	public void CommittedPair_AfterCupidRoleChange_FreshServicePreservesHistoricalProvenance()
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
		var lovers = new[] { werewolf.Id, villager.Id };
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
				builder.Process(selection.CreateResponse(lovers.ToHashSet())));
		builder.ArrangeCurrentRole(cupid.Id, MainRoleType.SimpleVillager);
		var expectedSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(recognition.CreateResponse()));
		var serialized = builder.GetGameState()!.Serialize();
		var freshService = new GameService();

		var gameId = freshService.RehydrateSession(serialized);

		var recovered = freshService.GetGameStateView(gameId)!;
		freshService.GetCurrentInstruction(gameId)
			.Should().BeEquivalentTo(expectedSleep);
		recovered.GetPlayerState(cupid.Id).CurrentRole.Should().Be(
			MainRoleType.SimpleVillager);
		var committedPair = recovered.GameHistoryLog
			.OfType<LoversPairCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		committedPair.PowerIdentity.Should().Be(
			new RolePowerInstanceIdentity(
				cupid.Id,
				MainRoleType.Cupid,
				"cupid-link-lovers",
				cupid.Id,
				RolePowerInstanceOrigin.Native));
		committedPair.PlayerIds.Should().BeEquivalentTo(lovers);
		lovers.Should().OnlyContain(playerId =>
			recovered.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Lovers));
		recovered.RequireKnownFactionBeneficiary(werewolf.Id)
			.Should().Be(Faction.CrossFactionLovers);
		recovered.RequireKnownFactionBeneficiary(villager.Id)
			.Should().Be(Faction.CrossFactionLovers);
		MarkTestCompleted();
	}

	[Fact]
	public void PendingSelection_FreshServiceRestoresExactChoiceWithoutPairOrDuplicateWake()
	{
		var scenario = CreateCupidSelectionScenario(
			knownWerewolfAgentGroup: true);
		var serialized = scenario.Builder.GetGameState()!.Serialize();
		var freshService = new GameService();

		var gameId = freshService.RehydrateSession(serialized);

		var recoveredSelection = freshService.GetCurrentInstruction(gameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		recoveredSelection.Should().BeEquivalentTo(scenario.Selection);
		var recoveredSession = freshService.GetGameStateView(gameId)!;
		recoveredSession.GameHistoryLog
			.OfType<LoversPairCommittedLogEntry>()
			.Should().BeEmpty();
		recoveredSession.GetPlayers().Should().OnlyContain(player =>
			!player.State.HasStatusEffect(StatusEffectTypes.Lovers));
		var beforeReplay = PublicGameSessionSnapshot.Capture(
			freshService,
			gameId);
		Action replayWake = () => freshService.ProcessInstruction(
			gameId,
			scenario.Wake.CreateResponse());
		replayWake.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(freshService, gameId)
			.Should().BeEquivalentTo(
				beforeReplay,
				options => options.WithStrictOrdering());

		var lovers = new[]
		{
			scenario.Players[2].Id,
			scenario.Players[3].Id
		};
		var acceptedSelection =
			recoveredSelection.CreateResponse(lovers.ToHashSet());
		var recognition = freshService.ProcessInstruction(
				gameId,
				acceptedSelection)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeLovers);
		var beforeRepeatedSelection = PublicGameSessionSnapshot.Capture(
			freshService,
			gameId);
		Action repeatSelection = () => freshService.ProcessInstruction(
			gameId,
			acceptedSelection);
		repeatSelection.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(freshService, gameId)
			.Should().BeEquivalentTo(
				beforeRepeatedSelection,
				options => options.WithStrictOrdering());
		freshService.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<LoversPairCommittedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void PendingLoversSleep_FreshServiceContinuesOnceWithoutReplayingRecognition()
	{
		var scenario = CreateCupidSelectionScenario(
			knownWerewolfAgentGroup: true);
		var lovers = new[]
		{
			scenario.Players[2].Id,
			scenario.Players[3].Id
		};
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.Process(
					scenario.Selection.CreateResponse(lovers.ToHashSet())));
		var acceptedRecognition = recognition.CreateResponse();
		var expectedSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.Process(acceptedRecognition));
		var freshService = new GameService();

		var gameId = freshService.RehydrateSession(
			scenario.Builder.GetGameState()!.Serialize());

		var recoveredSleep = freshService.GetCurrentInstruction(gameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recoveredSleep.Should().BeEquivalentTo(expectedSleep);
		var recoveredSession = freshService.GetGameStateView(gameId)!;
		recoveredSession.GameHistoryLog
			.OfType<LoversPairCommittedLogEntry>()
			.Should().ContainSingle();
		lovers.Should().OnlyContain(playerId =>
			recoveredSession.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Lovers));
		var beforeReplay = PublicGameSessionSnapshot.Capture(
			freshService,
			gameId);
		Action replayRecognition = () => freshService.ProcessInstruction(
			gameId,
			acceptedRecognition);
		replayRecognition.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(freshService, gameId)
			.Should().BeEquivalentTo(
				beforeReplay,
				options => options.WithStrictOrdering());

		var werewolfWake = freshService.ProcessInstruction(
				gameId,
				recoveredSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		werewolfWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		werewolfWake.AffectedPlayerIds.Should().Equal(
			scenario.Werewolf.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void InitialClosure_FreshServiceRestoresBothSidesOfAtomicBoundary()
	{
		var scenario = CreateCupidSelectionScenario(
			knownWerewolfAgentGroup: false);
		var villager = scenario.Players[2];
		var lovers = new[] { scenario.Werewolf.Id, villager.Id };
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.Process(
					scenario.Selection.CreateResponse(lovers.ToHashSet())));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.Process(recognition.CreateResponse()));
		var expectedObservation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				scenario.Builder.Process(sleep.CreateResponse()));
		expectedObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		var preClosureService = new GameService();

		var preClosureGameId = preClosureService.RehydrateSession(
			scenario.Builder.GetGameState()!.Serialize());

		var recoveredObservation = preClosureService
			.GetCurrentInstruction(preClosureGameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		recoveredObservation.Should().BeEquivalentTo(expectedObservation);
		var preClosureSession =
			preClosureService.GetGameStateView(preClosureGameId)!;
		preClosureSession.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		lovers.Should().OnlyContain(playerId =>
			!preClosureSession.GetFactionBeneficiaryKnowledge(playerId)
				.IsKnown);
		var acceptedObservation = recoveredObservation.CreateResponse(
			[scenario.Werewolf.Id]);
		var expectedTarget = preClosureService.ProcessInstruction(
				preClosureGameId,
				acceptedObservation)
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		expectedTarget.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWerewolfVictim);
		var postClosureService = new GameService();

		var postClosureGameId = postClosureService.RehydrateSession(
			preClosureService.GetGameStateView(preClosureGameId)!.Serialize());

		var recoveredTarget = postClosureService
			.GetCurrentInstruction(postClosureGameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		recoveredTarget.Should().BeEquivalentTo(expectedTarget);
		var postClosureSession =
			postClosureService.GetGameStateView(postClosureGameId)!;
		var factEntries = postClosureSession.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.ToArray();
		factEntries.Should().ContainSingle(entry =>
			entry.Source.Kind ==
			FactionFactSourceKind.InitialBeneficiaryClosure);
		factEntries.Should().NotContain(entry =>
			entry.Source.Kind == FactionFactSourceKind.ExplicitTransition &&
			entry.Facts.Any(fact =>
				lovers.Contains(fact.PlayerId) &&
				fact.Type == FactionFactType.Beneficiary &&
				fact.Faction == Faction.CrossFactionLovers));
		postClosureSession.RequireKnownFactionBeneficiary(
				scenario.Werewolf.Id)
			.Should().Be(Faction.CrossFactionLovers);
		postClosureSession.RequireKnownFactionBeneficiary(villager.Id)
			.Should().Be(Faction.CrossFactionLovers);
		var beforeReplay = PublicGameSessionSnapshot.Capture(
			postClosureService,
			postClosureGameId);
		Action replayObservation = () =>
			postClosureService.ProcessInstruction(
				postClosureGameId,
				acceptedObservation);
		replayObservation.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(
				postClosureService,
				postClosureGameId)
			.Should().BeEquivalentTo(
				beforeReplay,
				options => options.WithStrictOrdering());
		MarkTestCompleted();
	}

	[Fact]
	public void SelectionResponseFactory_RejectsWrongDuplicateAndForeignShapesWithoutMutation()
	{
		var scenario = CreateCupidSelectionScenario(
			knownWerewolfAgentGroup: true);
		var first = scenario.Players[2].Id;
		var second = scenario.Players[3].Id;
		var third = scenario.Players[4].Id;
		var duplicateShape = new HashSet<Guid> { first, first };
		var wrongCountFactories = new Action[]
		{
			() => scenario.Selection.CreateResponse([]),
			() => scenario.Selection.CreateResponse([first]),
			() => scenario.Selection.CreateResponse([first, second, third]),
			() => scenario.Selection.CreateResponse(duplicateShape)
		};
		var before = scenario.Builder.GetGameState()!.Serialize();

		foreach (var wrongCountFactory in wrongCountFactories)
		{
			wrongCountFactory.Should().Throw<InvalidOperationException>();
		}
		Action foreignPlayerFactory = () =>
			scenario.Selection.CreateResponse([first, Guid.NewGuid()]);
		foreignPlayerFactory.Should().Throw<ArgumentException>();
		Action nullFactory = () => scenario.Selection.CreateResponse(null!);
		nullFactory.Should().Throw<ArgumentNullException>();

		scenario.Builder.GetGameState()!.Serialize().Should().Be(before);
		scenario.Builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			scenario.Selection.InstructionId);
		MarkTestCompleted();
	}

	[Fact]
	public void MalformedOrUncorrelatedSelectionResponses_AreSideEffectFree()
	{
		var scenario = CreateCupidSelectionScenario(
			knownWerewolfAgentGroup: true);
		var selectedPlayerIds = new HashSet<Guid>
		{
			scenario.Players[2].Id,
			scenario.Players[3].Id
		};
		var rejectedResponses = new[]
		{
			new ModeratorResponse
			{
				InstructionId = Guid.NewGuid(),
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds = selectedPlayerIds
			},
			new ModeratorResponse
			{
				InstructionId = scenario.Selection.InstructionId,
				Type = ExpectedInputType.Continue
			},
			new ModeratorResponse
			{
				InstructionId = scenario.Selection.InstructionId,
				Type = ExpectedInputType.PlayerSelection
			},
			new ModeratorResponse
			{
				InstructionId = scenario.Selection.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds =
					new HashSet<Guid> { scenario.Players[2].Id }
			},
			new ModeratorResponse
			{
				InstructionId = scenario.Selection.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds =
					new HashSet<Guid>
					{
						scenario.Players[2].Id,
						Guid.NewGuid()
					}
			}
		};

		foreach (var rejectedResponse in rejectedResponses)
		{
			var before = scenario.Builder.GetGameState()!.Serialize();
			Action process = () =>
				scenario.Builder.Process(rejectedResponse);
			process.Should().Throw<InvalidOperationException>();
			scenario.Builder.GetGameState()!.Serialize().Should().Be(before);
			scenario.Builder.GetCurrentInstruction()!.InstructionId.Should()
				.Be(scenario.Selection.InstructionId);
		}

		MarkTestCompleted();
	}

	[Fact]
	public void SelectionAfterHolderLeavesCupidRole_IsRejectedWithoutPairMutation()
	{
		var scenario = CreateCupidSelectionScenario(
			knownWerewolfAgentGroup: true);
		var acceptedSelection = scenario.Selection.CreateResponse(
			[scenario.Players[2].Id, scenario.Players[3].Id]);
		scenario.Builder.ArrangeCurrentRole(
			scenario.Cupid.Id,
			MainRoleType.SimpleVillager);
		var before = scenario.Builder.GetGameState()!.Serialize();

		Action process = () =>
			scenario.Builder.Process(acceptedSelection);

		process.Should().Throw<InvalidOperationException>()
			.WithMessage("*living Cupid*");
		scenario.Builder.GetGameState()!.Serialize().Should().Be(before);
		scenario.Builder.GetGameState()!.GameHistoryLog
			.OfType<LoversPairCommittedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void LaterNight_OmitsCupidAndRejectsFirstNightSelectionReplay()
	{
		var scenario = CreateCupidSelectionScenario(
			knownWerewolfAgentGroup: true);
		var acceptedSelection = scenario.Selection.CreateResponse(
			[scenario.Players[2].Id, scenario.Players[3].Id]);
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.Process(acceptedSelection));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				scenario.Builder.Process(recognition.CreateResponse()));
		scenario.Builder.Process(sleep.CreateResponse())
			.IsSuccess.Should().BeTrue();
		scenario.Builder.CompleteWerewolfNightAction(
				[scenario.Werewolf.Id],
				scenario.Players[6].Id)
			.IsSuccess.Should().BeTrue();
		scenario.Builder.CompleteDawnPhase(new()
			{
				[scenario.Players[6].Id] = MainRoleType.SimpleVillager
			})
			.IsSuccess.Should().BeTrue();
		scenario.Builder.CompleteDayPhaseWithTie()
			.IsSuccess.Should().BeTrue();
		scenario.Builder.ConfirmNightStart()
			.IsSuccess.Should().BeTrue();

		var laterNightInstruction = scenario.Builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		laterNightInstruction.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		laterNightInstruction.AffectedPlayerIds.Should().Equal(
			scenario.Werewolf.Id);
		var beforeReplay = scenario.Builder.GetGameState()!.Serialize();
		Action replaySelection = () =>
			scenario.Builder.Process(acceptedSelection);
		replaySelection.Should().Throw<InvalidOperationException>();
		scenario.Builder.GetGameState()!.Serialize().Should().Be(
			beforeReplay);
		scenario.Builder.GetGameState()!.GameHistoryLog
			.OfType<LoversPairCommittedLogEntry>()
			.Should().ContainSingle();
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
		builder.ArrangeCurrentRole(cupid.Id, MainRoleType.SimpleVillager);

		var finishNight = builder.CompleteWerewolfNightAction(
				[werewolf.Id],
				attackedLover.Id)
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var attackedReveal = builder.Process(finishNight.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		attackedReveal.PlayersForAssignment.Should().Equal(attackedLover.Id);
		var acceptedAttackedReveal = attackedReveal.CreateResponse(new()
		{
			[attackedLover.Id] = MainRoleType.SimpleVillager
		});
		var expectedHunterReveal = builder.Process(acceptedAttackedReveal)
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		expectedHunterReveal.PlayersForAssignment.Should().Equal(hunterLover.Id);
		var revealService = new GameService();
		var revealGameId = revealService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredHunterReveal = revealService
			.GetCurrentInstruction(revealGameId)
			.Should().BeOfType<AssignRolesInstruction>().Subject;
		recoveredHunterReveal.Should().BeEquivalentTo(expectedHunterReveal);
		var preHeartbreakSession = revealService.GetGameStateView(revealGameId)!;
		preHeartbreakSession.GetPlayerState(cupid.Id).CurrentRole.Should().Be(
			MainRoleType.SimpleVillager);
		preHeartbreakSession.GetPlayerState(attackedLover.Id).Health.Should().Be(
			PlayerHealth.Dead);
		preHeartbreakSession.GetPlayerState(hunterLover.Id).Health.Should().Be(
			PlayerHealth.Alive);
		var beforeAttackedReplay = PublicGameSessionSnapshot.Capture(
			revealService,
			revealGameId);
		Action replayAttackedReveal = () => revealService.ProcessInstruction(
			revealGameId,
			acceptedAttackedReveal);
		replayAttackedReveal.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(revealService, revealGameId)
			.Should().BeEquivalentTo(
				beforeAttackedReplay,
				options => options.WithStrictOrdering());

		var acceptedHunterReveal = recoveredHunterReveal.CreateResponse(new()
		{
			[hunterLover.Id] = MainRoleType.Hunter
		});
		var expectedFinalShot = revealService.ProcessInstruction(
				revealGameId,
				acceptedHunterReveal)
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;

		expectedFinalShot.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);
		var postHeartbreakService = new GameService();
		var postHeartbreakGameId = postHeartbreakService.RehydrateSession(
			revealService.GetGameStateView(revealGameId)!.Serialize());
		var recoveredFinalShot = postHeartbreakService
			.GetCurrentInstruction(postHeartbreakGameId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		recoveredFinalShot.Should().BeEquivalentTo(expectedFinalShot);
		var beforeHunterReplay = PublicGameSessionSnapshot.Capture(
			postHeartbreakService,
			postHeartbreakGameId);
		Action replayHunterReveal = () =>
			postHeartbreakService.ProcessInstruction(
				postHeartbreakGameId,
				acceptedHunterReveal);
		replayHunterReveal.Should().Throw<InvalidOperationException>();
		PublicGameSessionSnapshot.Capture(
				postHeartbreakService,
				postHeartbreakGameId)
			.Should().BeEquivalentTo(
				beforeHunterReplay,
				options => options.WithStrictOrdering());

		var session =
			postHeartbreakService.GetGameStateView(postHeartbreakGameId)!;
		session.GetPlayerState(cupid.Id).CurrentRole.Should().Be(
			MainRoleType.SimpleVillager);
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
		var committedPair = session.GameHistoryLog
			.OfType<LoversPairCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		committedPair.PowerIdentity.Should().Be(
			new RolePowerInstanceIdentity(
				cupid.Id,
				MainRoleType.Cupid,
				"cupid-link-lovers",
				cupid.Id,
				RolePowerInstanceOrigin.Native));
		committedPair.PlayerIds.Should().BeEquivalentTo(
			[hunterLover.Id, attackedLover.Id]);
		committedPair.PlayerIds.Should().OnlyContain(playerId =>
			session.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Lovers));
		var heartbreakCompletions = session.GameHistoryLog
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Where(entry =>
				entry.ReactionId ==
				EliminationCascadeReactionIds.LoversHeartbreak)
			.ToArray();
		heartbreakCompletions.Should().HaveCount(2);
		heartbreakCompletions
			.SelectMany(entry => entry.AdmittedEliminations)
			.Should().ContainSingle(elimination =>
				elimination.PlayerId == hunterLover.Id &&
				elimination.Reason == EliminationReason.LoversHeartbreak);
		heartbreakCompletions.Should().OnlyContain(entry =>
			heartbreakCompletions.Count(candidate =>
				candidate.ScopeId == entry.ScopeId &&
				candidate.ReactionId == entry.ReactionId &&
				candidate.TriggeringEliminations.SequenceEqual(
					entry.TriggeringEliminations)) == 1);
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
		builder.ArrangeCurrentRole(cupid.Id, MainRoleType.SimpleVillager);
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
		var terminalService = new GameService();
		var terminalGameId = terminalService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredFinished = terminalService
			.GetCurrentInstruction(terminalGameId)
			.Should().BeOfType<FinishedGameConfirmationInstruction>().Subject;
		recoveredFinished.Should().BeEquivalentTo(finished);
		var recoveredSession = terminalService.GetGameStateView(terminalGameId)!;
		recoveredSession.GetPlayerState(cupid.Id).CurrentRole.Should().Be(
			MainRoleType.SimpleVillager);
		recoveredSession.GetPlayers()
			.Where(player => player.State.Health == PlayerHealth.Alive)
			.Select(player => player.Id)
			.Should().BeEquivalentTo(
				[werewolfLover.Id, villagerLover.Id]);
		var committedPair = recoveredSession.GameHistoryLog
			.OfType<LoversPairCommittedLogEntry>()
			.Should().ContainSingle().Subject;
		committedPair.PowerIdentity.Should().Be(
			new RolePowerInstanceIdentity(
				cupid.Id,
				MainRoleType.Cupid,
				"cupid-link-lovers",
				cupid.Id,
				RolePowerInstanceOrigin.Native));
		committedPair.PlayerIds.Should().BeEquivalentTo(
			[werewolfLover.Id, villagerLover.Id]);
		committedPair.PlayerIds.Should().OnlyContain(playerId =>
			recoveredSession.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Lovers));
		recoveredSession.RequireKnownFactionBeneficiary(werewolfLover.Id)
			.Should().Be(Faction.CrossFactionLovers);
		recoveredSession.RequireKnownFactionBeneficiary(villagerLover.Id)
			.Should().Be(Faction.CrossFactionLovers);
		recoveredSession.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().ContainSingle(entry =>
				entry.GameResult.Equals(
					new SingleFactionGameResult(
						Faction.CrossFactionLovers)) &&
				entry.VictoryCheckWindow == VictoryCheckWindow.Dawn);

		var secondTerminalService = new GameService();
		var secondTerminalGameId = secondTerminalService.RehydrateSession(
			recoveredSession.Serialize());
		secondTerminalService.GetCurrentInstruction(secondTerminalGameId)
			.Should().BeEquivalentTo(recoveredFinished);
		secondTerminalService.GetGameStateView(secondTerminalGameId)!
			.GameHistoryLog.OfType<VictoryConditionMetLogEntry>()
			.Should().ContainSingle(entry =>
				entry.GameResult.Equals(
					new SingleFactionGameResult(
						Faction.CrossFactionLovers)) &&
				entry.VictoryCheckWindow == VictoryCheckWindow.Dawn);
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

	private CupidSelectionScenario CreateCupidSelectionScenario(
		bool knownWerewolfAgentGroup)
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
		if (knownWerewolfAgentGroup)
		{
			builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		}

		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		return new CupidSelectionScenario(
			builder,
			players,
			werewolf,
			cupid,
			wake,
			selection);
	}

	private sealed record CupidSelectionScenario(
		GameTestBuilder Builder,
		IPlayer[] Players,
		IPlayer Werewolf,
		IPlayer Cupid,
		ConfirmationInstruction Wake,
		SelectPlayersInstruction Selection);

	private sealed class RecoveryBoundaryKey : IGameFlowManagerKey
	{
		internal static RecoveryBoundaryKey Instance { get; } = new();

		private RecoveryBoundaryKey()
		{
		}
	}
}
