using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public class FactionStateTests
{
	[Fact]
	public void LiveSession_StartsWithAllFactionKnowledgeUnknown()
	{
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			["Ana", "Bruno", "Carla", "Diana", "Eva"],
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]));
		var session = service.GetGameStateView(start.GameGuid);

		session.Should().NotBeNull();
		foreach (var player in session!.GetPlayers())
		{
			player.State.FactionBeneficiary.Should()
				.Be(FactionBeneficiaryKnowledge.Unknown);
			player.State.GetFactionAgentKnowledge(Faction.Villager).Should()
				.Be(FactionAgentKnowledge.Unknown);
			player.State.GetFactionAgentKnowledge(Faction.Werewolf).Should()
				.Be(FactionAgentKnowledge.Unknown);
		}
	}

	[Fact]
	public void PublicQueries_KeepUnknownDistinctAndDoNotExposePartialAgentGroups()
	{
		var (session, _) = StartLiveSession();
		var player = session.GetPlayers().First();

		session.GetFactionBeneficiaryKnowledge(player.Id).Should()
			.Be(FactionBeneficiaryKnowledge.Unknown);
		session.GetFactionAgentKnowledge(player.Id, Faction.Werewolf).Should()
			.Be(FactionAgentKnowledge.Unknown);
		session.TryGetKnownFactionAgents(
				Faction.Werewolf,
				out var agents)
			.Should().BeFalse();
		agents.Should().BeEmpty();
	}

	[Fact]
	public void PublicAgentQueries_DistinguishUnknownFromKnownEmpty()
	{
		var (session, service) = StartLiveSession();
		var players = session.GetPlayers().ToArray();

		session.TryGetKnownFactionAgents(
				Faction.Werewolf,
				out var unknownAgents)
			.Should().BeFalse();
		unknownAgents.Should().BeEmpty();

		service.CommitScheduledFactionObservation(
			session.Id,
			"known-empty-werewolf-agent-observation",
			players.Select(player => FactionFact.Agent(
					player.Id,
					Faction.Werewolf,
					FactionAgentKnowledge.KnownNonAgent,
					Boundary(order: 10)))
				.ToArray());

		session.TryGetKnownFactionAgents(
				Faction.Werewolf,
				out var knownAgents)
			.Should().BeTrue();
		knownAgents.Should().BeEmpty();
		session.RequireKnownFactionAgents(Faction.Werewolf)
			.Should().BeEmpty();
	}

	[Fact]
	public void RequiredFactionGuards_WhenFactsAreUnknown_AreNeutralExactNoOps()
	{
		var (session, _) = StartLiveSession();
		var player = session.GetPlayers().First();
		var before = session.Serialize();
		var historyCount = session.GameHistoryLog.Count();

		var beneficiaryFailure = () =>
			session.RequireKnownFactionBeneficiary(player.Id);
		var agentsFailure = () =>
			session.RequireKnownFactionAgents(Faction.Werewolf);

		beneficiaryFailure.Should()
			.ThrowExactly<InvalidOperationException>()
			.WithMessage("Required Faction facts are not ready.");
		agentsFailure.Should()
			.ThrowExactly<InvalidOperationException>()
			.WithMessage("Required Faction facts are not ready.");
		session.Serialize().Should().Be(before);
		session.GameHistoryLog.Should().HaveCount(historyCount);
	}

	[Fact]
	public void NamedFactionBatches_ProjectFactsByEffectiveBoundaryNotAppendOrder()
	{
		var (session, service) = StartLiveSession();
		var players = session.GetPlayers().ToArray();
		var observedBoundary = Boundary(order: 20);
		var observationFacts = players
			.Select(player => FactionFact.Agent(
				player.Id,
				Faction.Werewolf,
				player.Id == players[1].Id
					? FactionAgentKnowledge.KnownAgent
					: FactionAgentKnowledge.KnownNonAgent,
				observedBoundary))
			.Append(FactionFact.Beneficiary(
				players[0].Id,
				Faction.Villager,
				observedBoundary))
			.ToArray();

		service.CommitScheduledFactionObservation(
			session.Id,
			"initial-werewolf-agent-observation",
			observationFacts);
		service.CommitExplicitFactionTransition(
			session.Id,
			"historical-late-arrival",
			[
				FactionFact.Agent(
					players[0].Id,
					Faction.Werewolf,
					FactionAgentKnowledge.KnownAgent,
					Boundary(order: 10))
			]);

		session.GetFactionAgentKnowledge(
				players[0].Id,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownNonAgent);
		session.RequireKnownFactionBeneficiary(players[0].Id).Should()
			.Be(Faction.Villager);
		session.RequireKnownFactionAgents(Faction.Werewolf)
			.Select(player => player.Id)
			.Should().Equal(players[1].Id);

		service.CommitExplicitFactionTransition(
			session.Id,
			"current-transition",
			[
				FactionFact.Agent(
					players[0].Id,
					Faction.Werewolf,
					FactionAgentKnowledge.KnownAgent,
					Boundary(order: 30))
			]);

		session.RequireKnownFactionAgents(Faction.Werewolf)
			.Select(player => player.Id)
			.Should().Equal(players[0].Id, players[1].Id);
		session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Select(entry => entry.Source.Kind)
			.Should().Equal(
				FactionFactSourceKind.ScheduledObservation,
				FactionFactSourceKind.ExplicitTransition,
				FactionFactSourceKind.ExplicitTransition);
	}

	[Fact]
	public void NamedFactionBatch_WithContradictoryFacts_IsAtomic()
	{
		var (session, service) = StartLiveSession();
		var player = session.GetPlayers().First();
		var boundary = Boundary(order: 10);
		var before = session.Serialize();

		var commit = () => service.CommitExplicitFactionTransition(
			session.Id,
			"contradictory-transition",
			[
				FactionFact.Agent(
					player.Id,
					Faction.Werewolf,
					FactionAgentKnowledge.KnownAgent,
					boundary),
				FactionFact.Agent(
					player.Id,
					Faction.Werewolf,
					FactionAgentKnowledge.KnownNonAgent,
					boundary)
			]);

		commit.Should().ThrowExactly<InvalidOperationException>();
		session.GetFactionAgentKnowledge(player.Id, Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.Unknown);
		session.GameHistoryLog.Should().BeEmpty();
		session.Serialize().Should().Be(before);
	}

	[Fact]
	public void InitialClosure_WhenInitialAgentGroupIsIncomplete_IsExactNoOp()
	{
		var (session, service) = StartLiveSession();
		var player = session.GetPlayers().First();
		var boundary = Boundary(order: 10);
		service.CommitScheduledFactionObservation(
			session.Id,
			"partial-agent-observation",
			[
				FactionFact.Agent(
					player.Id,
					Faction.Werewolf,
					FactionAgentKnowledge.KnownAgent,
					boundary)
			]);
		var request = ClosureRequest(boundary);
		var before = CaptureFactionState(session);
		var serializedBefore = session.Serialize();
		var historyCountBefore = session.GameHistoryLog.Count();

		service.GetInitialBeneficiaryClosureReadiness(session.Id, request)
			.Should().Be(InitialBeneficiaryClosureReadiness.Incomplete);
		service.TryCommitInitialBeneficiaryClosure(session.Id, request)
			.Should().Be(InitialBeneficiaryClosureResult.Incomplete);

		CaptureFactionState(session).Should().Equal(before);
		session.Serialize().Should().Be(serializedBefore);
		session.GameHistoryLog.Should().HaveCount(historyCountBefore);
		session.GameHistoryLog.OfType<FactionFactsCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
	}

	[Fact]
	public void InitialClosure_WhenApplicableExceptionIsIncomplete_IsExactNoOp()
	{
		var (session, service) = StartLiveSession();
		var boundary = Boundary(order: 10);
		SeedCompleteWerewolfAgentGroup(session, service, boundary);
		var request = new InitialBeneficiaryClosureRequest(
			boundary,
			[
				new InitialBeneficiaryClosurePrerequisite(
					"synthetic-agent-only-exception",
					isComplete: false)
			],
			[]);
		var before = CaptureFactionState(session);
		var serializedBefore = session.Serialize();
		var historyCountBefore = session.GameHistoryLog.Count();

		service.GetInitialBeneficiaryClosureReadiness(session.Id, request)
			.Should().Be(InitialBeneficiaryClosureReadiness.Incomplete);
		service.TryCommitInitialBeneficiaryClosure(session.Id, request)
			.Should().Be(InitialBeneficiaryClosureResult.Incomplete);

		CaptureFactionState(session).Should().Equal(before);
		session.Serialize().Should().Be(serializedBefore);
		session.GameHistoryLog.Should().HaveCount(historyCountBefore);
	}

	[Fact]
	public void InitialClosure_WhenDeferredResultIsIncomplete_IsExactNoOp()
	{
		var (session, service) = StartLiveSession();
		var boundary = Boundary(order: 10);
		SeedCompleteWerewolfAgentGroup(session, service, boundary);
		var request = new InitialBeneficiaryClosureRequest(
			boundary,
			[],
			[
				InitialBeneficiaryClosureDeferredResult.Pending(
					"synthetic-deferred-classifier")
			]);
		var before = CaptureFactionState(session);
		var serializedBefore = session.Serialize();
		var historyCountBefore = session.GameHistoryLog.Count();

		service.GetInitialBeneficiaryClosureReadiness(session.Id, request)
			.Should().Be(InitialBeneficiaryClosureReadiness.Incomplete);
		service.TryCommitInitialBeneficiaryClosure(session.Id, request)
			.Should().Be(InitialBeneficiaryClosureResult.Incomplete);

		CaptureFactionState(session).Should().Equal(before);
		session.Serialize().Should().Be(serializedBefore);
		session.GameHistoryLog.Should().HaveCount(historyCountBefore);
	}

	[Fact]
	public void InitialClosure_WhenComplete_CommitsOneOrderedBatchAndIsIdempotent()
	{
		var (session, service) = StartLiveSession();
		var players = session.GetPlayers().ToArray();
		var groupBoundary = Boundary(order: 20);
		var explicitBoundary = Boundary(order: 10);
		service.CommitScheduledFactionObservation(
			session.Id,
			"complete-agent-observation-with-explicit-beneficiary",
			players
				.Select(player => FactionFact.Agent(
					player.Id,
					Faction.Werewolf,
					player.Id == players[1].Id
						? FactionAgentKnowledge.KnownAgent
						: FactionAgentKnowledge.KnownNonAgent,
					groupBoundary))
				.Append(FactionFact.Beneficiary(
					players[0].Id,
					Faction.Villager,
					explicitBoundary,
					beneficiaryPrecedence: 1))
				.ToArray());
		var deferredFact = FactionFact.Beneficiary(
			players[2].Id,
			Faction.Werewolf,
			Boundary(order: 15),
			beneficiaryPrecedence: 1);
		var request = new InitialBeneficiaryClosureRequest(
			groupBoundary,
			[
				new InitialBeneficiaryClosurePrerequisite(
					"synthetic-agent-only-exception",
					isComplete: true)
			],
			[
				InitialBeneficiaryClosureDeferredResult.Complete(
					"synthetic-deferred-classifier",
					[deferredFact])
			]);

		service.GetInitialBeneficiaryClosureReadiness(session.Id, request)
			.Should().Be(InitialBeneficiaryClosureReadiness.Ready);
		service.TryCommitInitialBeneficiaryClosure(session.Id, request)
			.Should().Be(InitialBeneficiaryClosureResult.Committed);

		session.RequireKnownFactionBeneficiary(players[0].Id).Should()
			.Be(Faction.Villager);
		session.RequireKnownFactionBeneficiary(players[1].Id).Should()
			.Be(Faction.Werewolf);
		session.RequireKnownFactionBeneficiary(players[2].Id).Should()
			.Be(Faction.Werewolf);
		session.RequireKnownFactionBeneficiary(players[3].Id).Should()
			.Be(Faction.Villager);
		session.RequireKnownFactionBeneficiary(players[4].Id).Should()
			.Be(Faction.Villager);
		session.RequireKnownFactionAgents(Faction.Werewolf)
			.Select(player => player.Id)
			.Should().Equal(players[1].Id);

		var closure = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Single(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		closure.Facts.Should().HaveCount(4);
		closure.Facts.Should().ContainSingle(fact => fact == deferredFact);
		closure.Facts.Should().ContainSingle(fact =>
			fact.PlayerId == players[2].Id);
		closure.Facts.Select(fact => fact.PlayerId)
			.Should().OnlyHaveUniqueItems();
		closure.Facts.Should().NotContain(fact =>
			fact.PlayerId == players[0].Id);
		closure.Facts
			.Select(fact => fact.EffectiveBoundary.Order)
			.Should().BeInAscendingOrder();

		var after = CaptureFactionState(session);
		var historyCount = session.GameHistoryLog.Count();
		service.GetInitialBeneficiaryClosureReadiness(session.Id, request)
			.Should().Be(InitialBeneficiaryClosureReadiness.AlreadyCommitted);
		service.TryCommitInitialBeneficiaryClosure(session.Id, request)
			.Should().Be(InitialBeneficiaryClosureResult.AlreadyCommitted);
		CaptureFactionState(session).Should().Equal(after);
		session.GameHistoryLog.Should().HaveCount(historyCount);
	}

	[Fact]
	public void FactionState_BecomesDurableOnlyAtAnOrdinaryStableBoundary()
	{
		var service = new GameService();
		var start = service.StartNewGame(CreateConfig());
		var session = service.GetGameStateView(start.GameGuid)!;
		var players = session.GetPlayers().ToArray();
		var nightStart = service.ProcessInstruction(
				session.Id,
				start.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var observeWerewolfAgents = service.ProcessInstruction(
				session.Id,
				nightStart.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		observeWerewolfAgents.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		var preFactionBoundary = session.Serialize();
		service.CommitExplicitFactionTransition(
			session.Id,
			"post-closure-transition",
			[
				FactionFact.Beneficiary(
					players[2].Id,
					Faction.Werewolf,
					Boundary(order: 20))
			]);

		session.Serialize().Should().Be(preFactionBoundary);
		var afterOrdinaryBoundary = service.ProcessInstruction(
			session.Id,
			observeWerewolfAgents.CreateResponse([players[0].Id]));
		afterOrdinaryBoundary.IsSuccess.Should().BeTrue();
		var expectedPendingInstruction =
			afterOrdinaryBoundary.ModeratorInstruction.Should()
				.BeOfType<SelectPlayersInstruction>().Subject;
		expectedPendingInstruction.Semantic.Should()
			.Be(ModeratorInstructionSemantic.SelectWerewolfVictim);
		var groupObservation = session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Source.Kind ==
					FactionFactSourceKind.ScheduledObservation &&
				entry.Source.Identifier ==
					FactionFactSource
						.WerewolfFactionAgentGroupObservationIdentifier)
			.Subject;
		var groupBoundary = groupObservation.Facts
			.Select(fact => fact.EffectiveBoundary)
			.Distinct()
			.Should().ContainSingle().Subject;
		var closureRequest = ClosureRequest(groupBoundary);
		var serialized = session.Serialize();
		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(serialized);
		var recovered = recoveredService.GetGameStateView(recoveredId)!;
		var serializedAgain = recovered.Serialize();
		var twiceRecoveredService = new GameService();
		var twiceRecoveredId =
			twiceRecoveredService.RehydrateSession(serializedAgain);
		var twiceRecovered =
			twiceRecoveredService.GetGameStateView(twiceRecoveredId)!;

		serializedAgain.Should().Be(serialized);
		twiceRecovered.GetFactionBeneficiaryKnowledge(players[2].Id)
			.Should().Be(FactionBeneficiaryKnowledge.Known(Faction.Werewolf));
		twiceRecovered.GetFactionAgentKnowledge(
				players[2].Id,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownNonAgent);
		twiceRecovered.GetFactionAgentKnowledge(
				players[2].Id,
				Faction.Villager)
			.Should().Be(FactionAgentKnowledge.Unknown);
		twiceRecovered.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Select(entry => entry.Source.Kind)
			.Should().Equal(
				FactionFactSourceKind.ExplicitTransition,
				FactionFactSourceKind.ScheduledObservation,
				FactionFactSourceKind.InitialBeneficiaryClosure);
		twiceRecoveredService
			.GetInitialBeneficiaryClosureReadiness(
				twiceRecoveredId,
				closureRequest)
			.Should().Be(
				InitialBeneficiaryClosureReadiness.AlreadyCommitted);

		var recoveredPending = twiceRecoveredService
			.GetCurrentInstruction(twiceRecoveredId);
		var recoveredInstruction = recoveredPending.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		recoveredInstruction.InstructionId.Should()
			.Be(expectedPendingInstruction.InstructionId);
		recoveredInstruction.Semantic.Should()
			.Be(expectedPendingInstruction.Semantic);
		recoveredInstruction.SelectablePlayerIds.Should()
			.Equal(expectedPendingInstruction.SelectablePlayerIds);
	}

	[Fact]
	public void InitialClosure_DoesNotPatchAnInFlightRecoveryBoundary()
	{
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			["Wild Child", "Model", "Werewolf", "Villager A", "Villager B"],
			[
				MainRoleType.WildChild,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]));
		var session = service.GetGameStateView(start.GameGuid)!;
		var players = session.GetPlayers().ToArray();
		var nightStart = service.ProcessInstruction(
				session.Id,
				start.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var identifyWildChild = service.ProcessInstruction(
				session.Id,
				nightStart.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var modelSelection = service.ProcessInstruction(
				session.Id,
				identifyWildChild.CreateResponse([players[0].Id]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		var stableBoundaryBeforeFactionFacts = session.Serialize();
		var nonFactionStateBefore = CaptureNonFactionState(session);
		var nonFactionHistoryBefore = session.GameHistoryLog.ToArray();
		var turnBefore = session.TurnNumber;
		var phaseBefore = session.GetCurrentPhase();
		var boundary = Boundary(order: 10);
		SeedCompleteWerewolfAgentGroup(session, service, boundary);

		service.TryCommitInitialBeneficiaryClosure(
				session.Id,
				ClosureRequest(boundary))
			.Should().Be(InitialBeneficiaryClosureResult.Committed);
		session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().HaveCount(2);
		session.Serialize().Should().Be(stableBoundaryBeforeFactionFacts);

		var recoveredService = new GameService();
		var recoveredId = recoveredService.RehydrateSession(
			stableBoundaryBeforeFactionFacts);
		var recovered = recoveredService.GetGameStateView(recoveredId)!;
		var recoveredInstruction = recoveredService
			.GetCurrentInstruction(recoveredId)
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		recoveredInstruction.InstructionId.Should()
			.Be(modelSelection.InstructionId);
		recoveredInstruction.Semantic.Should().Be(modelSelection.Semantic);
		recovered.TurnNumber.Should().Be(turnBefore);
		recovered.GetCurrentPhase().Should().Be(phaseBefore);
		CaptureNonFactionState(recovered).Should()
			.Equal(nonFactionStateBefore);
		recovered.GameHistoryLog
			.Should().BeEquivalentTo(
				nonFactionHistoryBefore,
				options => options.WithStrictOrdering());
		recovered.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().BeEmpty();
		foreach (var player in recovered.GetPlayers())
		{
			recovered.GetFactionBeneficiaryKnowledge(player.Id).Should()
				.Be(FactionBeneficiaryKnowledge.Unknown);
			recovered.GetFactionAgentKnowledge(player.Id, Faction.Werewolf)
				.Should().Be(FactionAgentKnowledge.Unknown);
		}
		recovered.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().BeEmpty();

		var continued = recoveredService.ProcessInstruction(
			recoveredId,
			recoveredInstruction.CreateResponse([players[1].Id]));
		continued.IsSuccess.Should().BeTrue();
		continued.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>();
	}

	private static InitialBeneficiaryClosureRequest ClosureRequest(
		FactionFactEffectiveBoundary boundary) =>
		new(boundary, [], []);

	private static void SeedCompleteWerewolfAgentGroup(
		IGameSession session,
		GameService service,
		FactionFactEffectiveBoundary boundary)
	{
		var players = session.GetPlayers().ToArray();
		service.CommitScheduledFactionObservation(
			session.Id,
			"complete-werewolf-agent-observation",
			players.Select(player => FactionFact.Agent(
					player.Id,
					Faction.Werewolf,
					player.Id == players[0].Id
						? FactionAgentKnowledge.KnownAgent
						: FactionAgentKnowledge.KnownNonAgent,
					boundary))
				.ToArray());
	}

	private static IReadOnlyList<(
		Guid PlayerId,
		FactionBeneficiaryKnowledge Beneficiary,
		FactionAgentKnowledge VillagerAgent,
		FactionAgentKnowledge WerewolfAgent)> CaptureFactionState(
			IGameSession session) =>
		session.GetPlayers()
			.Select(player => (
				player.Id,
				session.GetFactionBeneficiaryKnowledge(player.Id),
				session.GetFactionAgentKnowledge(player.Id, Faction.Villager),
				session.GetFactionAgentKnowledge(player.Id, Faction.Werewolf)))
			.ToArray();

	private static IReadOnlyList<(
		Guid PlayerId,
		MainRoleType? CurrentRole,
		MainRoleType? PhysicalCard,
		MainRoleType? ModeratorKnownRole,
		MainRoleType? PubliclyRevealedRole,
		PlayerHealth Health,
		bool HasVotingRight,
		IReadOnlyList<StatusEffectTypes> Effects)> CaptureNonFactionState(
			IGameSession session) =>
		session.GetPlayers()
			.Select(player => (
				player.Id,
				player.State.CurrentRole,
				player.State.PhysicalCharacterCardRole,
				player.State.ModeratorKnownRole,
				player.State.PubliclyRevealedRole,
				player.State.Health,
				player.State.HasVotingRight,
				(IReadOnlyList<StatusEffectTypes>)player.State
					.GetActiveStatusEffects()
					.OrderBy(effect => effect)
					.ToArray()))
			.ToArray();

	private static FactionFactEffectiveBoundary Boundary(int order) =>
		new(turnNumber: 1, GamePhase.Night, order);

	private static (IGameSession Session, GameService Service) StartLiveSession()
	{
		var service = new GameService();
		var start = service.StartNewGame(CreateConfig());
		return (service.GetGameStateView(start.GameGuid)!, service);
	}

	private static GameSessionConfig CreateConfig() =>
		new(
			["Ana", "Bruno", "Carla", "Diana", "Eva"],
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
}
