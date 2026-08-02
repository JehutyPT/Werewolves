using FluentAssertions;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.StateModels.Resources;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorBorrowedSemanticTraceTests
{
	private static readonly MainRoleType[] SourceRoles =
	[
		MainRoleType.Seer,
		MainRoleType.Cupid,
		MainRoleType.Witch,
		MainRoleType.LittleGirl,
		MainRoleType.Defender,
		MainRoleType.Fox,
		MainRoleType.StutteringJudge
	];

	private static readonly PhysicalCharacterCard[] SourceCards =
	[
		new(Guid.Parse("00000000-0000-0000-0000-000000000151"), MainRoleType.Seer),
		new(Guid.Parse("00000000-0000-0000-0000-000000000152"), MainRoleType.Cupid),
		new(Guid.Parse("00000000-0000-0000-0000-000000000153"), MainRoleType.Witch),
		new(Guid.Parse("00000000-0000-0000-0000-000000000154"), MainRoleType.LittleGirl),
		new(Guid.Parse("00000000-0000-0000-0000-000000000155"), MainRoleType.Defender),
		new(Guid.Parse("00000000-0000-0000-0000-000000000156"), MainRoleType.Fox),
		new(Guid.Parse("00000000-0000-0000-0000-000000000157"), MainRoleType.StutteringJudge)
	];

	private static readonly RunSeedMaterial SeedMaterial = CreateSeedMaterial();

	private static readonly HeadlessResponsePolicy ExactActorPolicy = new(
		BaselineRandomDecisionStrategy.Identity,
		SourceRoles.SelectMany(ExpectedTrace).Distinct());

	private static readonly TestSubPhaseManagerKey SubPhaseKey = new();
	private static readonly TestHookSubPhaseKey HookKey = new();
	private static readonly TestGameFlowManagerKey FlowKey = new();

	[Fact]
	public void BaselineRandom_TestOwnedActorPolicyAnswersEveryEmittedBorrowedSourceSemantic()
	{
		var actualTraces = SourceRoles.ToDictionary(
			sourceRole => sourceRole,
			sourceRole => TraceSource(sourceRole, ExactActorPolicy));

		foreach (var sourceRole in SourceRoles)
		{
			actualTraces[sourceRole].Should().Equal(ExpectedTrace(sourceRole));
		}

		actualTraces.Values.SelectMany(trace => trace).Distinct().Should()
			.BeEquivalentTo(ExactActorPolicy.AdmittedSemantics);
	}

	private static IReadOnlyList<ModeratorInstructionSemantic> TraceSource(
		MainRoleType sourceRole,
		HeadlessResponsePolicy policy)
	{
		var fixture = CreateActorFixture(sourceRole, policy);
		var beneficiaryBefore = fixture.Session
			.GetFactionBeneficiaryKnowledge(fixture.ActorId);
		beneficiaryBefore.Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Villager));
		var trace = TraceNightSource(fixture);
		if (sourceRole == MainRoleType.StutteringJudge)
		{
			trace.AddRange(TraceStutteringJudgeDay(fixture));
		}

		fixture.Session.GetPlayerState(fixture.ActorId).CurrentRole.Should().Be(
			MainRoleType.Actor);
		fixture.Session.GetModeratorActiveActorBorrowedRolePowerActivation()!
			.SourceRole.Should().Be(sourceRole);
		fixture.Session.GetFactionBeneficiaryKnowledge(fixture.ActorId).Should()
			.Be(beneficiaryBefore);
		return trace;
	}

	private static List<ModeratorInstructionSemantic> TraceNightSource(
		ActorFixture fixture)
	{
		var trace = new List<ModeratorInstructionSemantic>();
		var input = fixture.Start.CreateResponse();
		for (var step = 0; step < 8; step++)
		{
			var result = Advance(fixture.Listener, fixture.Session, input);
			if (result.Outcome == HookListenerOutcome.Complete)
			{
				return trace;
			}
			if (result.Outcome == HookListenerOutcome.Skip &&
				fixture.SourceRole == MainRoleType.StutteringJudge &&
				trace.SequenceEqual(
				[
					ModeratorInstructionSemantic.WakeRole,
					ModeratorInstructionSemantic.EstablishStutteringJudgeSignal,
					ModeratorInstructionSemantic.PutRoleToSleep
				]))
			{
				return trace;
			}

			result.Outcome.Should().Be(
				HookListenerOutcome.NeedInput,
				"borrowed {0} Night source step {1} must request input or complete; emitted trace: {2}",
				fixture.SourceRole,
				step,
				string.Join(", ", trace));
			result.Instruction.Should().NotBeNull();
			var instruction = result.Instruction!;
			trace.Add(instruction.Semantic);
			fixture.Session.SetPendingModeratorInstruction(FlowKey, instruction);
			input = CreateCheckedResponse(
				fixture.Strategy,
				instruction,
				fixture.Session);
		}

		throw new InvalidOperationException(
			$"Borrowed {fixture.SourceRole} did not complete its Night source slot.");
	}

	private static IReadOnlyList<ModeratorInstructionSemantic>
		TraceStutteringJudgeDay(ActorFixture fixture)
	{
		fixture.Session.ClearCurrentListenerCache(HookKey);
		fixture.Session.TransitionMainPhase(GamePhase.Day);
		fixture.Session.SetPendingModeratorInstruction(FlowKey, fixture.Start);
		var debate = GameFlowManager.HandleInput(
				fixture.Session,
				fixture.Start.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var conductVote = GameFlowManager.HandleInput(
				fixture.Session,
				debate.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var conductResponse = CreateCheckedResponse(
			fixture.Strategy,
			conductVote,
			fixture.Session);
		var signal = GameFlowManager.HandleInput(
				fixture.Session,
				conductResponse,
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var signalResponse = CreateCheckedResponse(
			fixture.Strategy,
			signal,
			fixture.Session);
		var firstVote = GameFlowManager.HandleInput(
				fixture.Session,
				signalResponse,
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		_ = CreateCheckedResponse(
			fixture.Strategy,
			firstVote,
			fixture.Session);

		return
		[
			conductVote.Semantic,
			signal.Semantic,
			firstVote.Semantic
		];
	}

	private static ModeratorResponse CreateCheckedResponse(
		BaselineRandomDecisionStrategy strategy,
		ModeratorInstruction instruction,
		GameSession session)
	{
		ExactActorPolicy.Admits(instruction.Semantic).Should().BeTrue();
		var response = strategy.CreateResponse(instruction, session);
		response.InstructionId.Should().Be(instruction.InstructionId);

		switch (instruction)
		{
			case ConfirmationInstruction:
				response.Type.Should().Be(ExpectedInputType.Continue);
				break;
			case SelectPlayersInstruction playerSelection:
			{
				response.Type.Should().Be(ExpectedInputType.PlayerSelection);
				response.SelectedPlayerIds.Should().NotBeNull();
				var selectedPlayerIds = response.SelectedPlayerIds!;
				selectedPlayerIds.Should().OnlyContain(selectedPlayerId =>
					playerSelection.SelectablePlayerIds.Contains(selectedPlayerId));
				playerSelection.CountConstraint
					.IsValid(selectedPlayerIds.ToArray()).Should().BeTrue();
				break;
			}
			case SelectOptionsInstruction optionSelection:
			{
				response.Type.Should().Be(ExpectedInputType.OptionSelection);
				response.SelectedOptionIds.Should().NotBeNull();
				var selectedOptionIds = response.SelectedOptionIds!;
				selectedOptionIds.Should().OnlyContain(selectedId =>
					optionSelection.Options.Any(option =>
						StringComparer.Ordinal.Equals(option.Id, selectedId)));
				optionSelection.SelectionRange
					.IsValid(selectedOptionIds.ToArray()).Should().BeTrue();
				break;
			}
			default:
				throw new InvalidOperationException(
					$"Unexpected Actor semantic fixture instruction '{instruction.GetType().Name}'.");
		}

		return response;
	}

	private static ActorFixture CreateActorFixture(
		MainRoleType sourceRole,
		HeadlessResponsePolicy policy)
	{
		var sourceCard = SourceCards.Single(card =>
			card.PrintedRole == sourceRole);
		var setupCards = SourceCards
			.Where(card => card.PrintedRole != sourceRole)
			.Take(2)
			.Prepend(sourceCard)
			.ToArray();
		var setup = new ActorSetupCards(version: 7, setupCards);
		var config = new GameSessionConfig(
			[GameStrings.ActorRoleName, "Werewolf", "Villager 1", "Villager 2", "Villager 3"],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		session.IdentifyRole([actorId], MainRoleType.Actor);
		SeedKnownActorBeneficiary(session, actorId);

		if (sourceRole == MainRoleType.StutteringJudge)
		{
			SeedKnownFactionFacts(session, players[1].Id);
			session.TransitionMainPhase(GamePhase.Day);
			session.TransitionMainPhase(GamePhase.Night);
			session.TurnNumber.Should().Be(2);
		}

		session.TryEnterSubPhaseStage(
			SubPhaseKey,
			GameHook.NightMainActionLoop.ToString()).Should().BeTrue();
		var activation = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			sourceCard.Id);
		activation.SourceRole.Should().Be(sourceRole);

		if (sourceRole is MainRoleType.Seer or MainRoleType.Fox)
		{
			SeedKnownFactionFacts(session, players[1].Id);
		}
		if (sourceRole == MainRoleType.Witch)
		{
			session.PerformNightAction(
				NightActionType.WerewolfVictimSelection,
				players[^1].Id);
		}

		var gateway = new RolePowerAvailabilityGateway(
			AllowAllRolePowerAvailabilityPolicy.Instance);
		return new ActorFixture(
			sourceRole,
			session,
			start,
			actorId,
			CreateSourceListener(sourceRole, gateway),
			CreateStrategy(policy));
	}

	private static IGameHookListener CreateSourceListener(
		MainRoleType sourceRole,
		RolePowerAvailabilityGateway gateway) =>
		sourceRole switch
		{
			MainRoleType.Seer => new SeerRole(gateway),
			MainRoleType.Cupid => new CupidRole(gateway),
			MainRoleType.Witch => new WitchRole(gateway),
			MainRoleType.LittleGirl => new SimpleWerewolfRole(gateway),
			MainRoleType.Defender => new DefenderRole(gateway),
			MainRoleType.Fox => new FoxRole(gateway),
			MainRoleType.StutteringJudge => new StutteringJudgeRole(gateway),
			_ => throw new ArgumentOutOfRangeException(nameof(sourceRole))
		};

	private static BaselineRandomDecisionStrategy CreateStrategy(
		HeadlessResponsePolicy policy)
	{
		var random = new DeterministicRandomSource(SeedMaterial);
		var startState = SimulationStartStateDeriver.Derive(
			SeedMaterial,
			random);
		return new BaselineRandomDecisionStrategy(
			SeedMaterial,
			startState,
			policy,
			random);
	}

	private static RunSeedMaterial CreateSeedMaterial()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.WildChild,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		return new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.FullProbability.Identity),
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 4);
	}

	private static IReadOnlyList<ModeratorInstructionSemantic> ExpectedTrace(
		MainRoleType sourceRole) =>
		sourceRole switch
		{
			MainRoleType.Seer =>
			[
				ModeratorInstructionSemantic.WakeRole,
				ModeratorInstructionSemantic.SelectSeerTarget,
				ModeratorInstructionSemantic.RevealSeerResult,
				ModeratorInstructionSemantic.PutRoleToSleep
			],
			MainRoleType.Cupid =>
			[
				ModeratorInstructionSemantic.WakeRole,
				ModeratorInstructionSemantic.SelectCupidLovers,
				ModeratorInstructionSemantic.RecognizeLovers,
				ModeratorInstructionSemantic.PutRoleToSleep
			],
			MainRoleType.Witch =>
			[
				ModeratorInstructionSemantic.WakeRole,
				ModeratorInstructionSemantic.SelectWitchHealingTarget,
				ModeratorInstructionSemantic.SelectWitchPoisonTarget,
				ModeratorInstructionSemantic.PutRoleToSleep
			],
			MainRoleType.LittleGirl =>
			[
				ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup,
				ModeratorInstructionSemantic.SelectWerewolfVictim,
				ModeratorInstructionSemantic.PutRoleToSleep
			],
			MainRoleType.Defender =>
			[
				ModeratorInstructionSemantic.WakeRole,
				ModeratorInstructionSemantic.SelectDefenderTarget,
				ModeratorInstructionSemantic.PutRoleToSleep
			],
			MainRoleType.Fox =>
			[
				ModeratorInstructionSemantic.WakeRole,
				ModeratorInstructionSemantic.SelectFoxCenter,
				ModeratorInstructionSemantic.RevealFoxResult,
				ModeratorInstructionSemantic.PutRoleToSleep
			],
			MainRoleType.StutteringJudge =>
			[
				ModeratorInstructionSemantic.WakeRole,
				ModeratorInstructionSemantic.EstablishStutteringJudgeSignal,
				ModeratorInstructionSemantic.PutRoleToSleep,
				ModeratorInstructionSemantic.ConductDayVote,
				ModeratorInstructionSemantic.ObserveStutteringJudgeSignal,
				ModeratorInstructionSemantic.RecordDayVote
			],
			_ => throw new ArgumentOutOfRangeException(nameof(sourceRole))
		};

	private static ActorRole CreateActorRole() => new(
		new RolePowerAvailabilityGateway(
			new VillagerRolePowerSuppressionPolicy(
				AllowAllRolePowerAvailabilityPolicy.Instance)));

	private static void SeedKnownActorBeneficiary(
		GameSession session,
		Guid actorId)
	{
		var boundary = new FactionFactEffectiveBoundary(
			session.TurnNumber,
			session.GetCurrentPhase(),
			session.GameHistoryLog.Count());
		session.CommitFactionFactBatch(context =>
			new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ExplicitTransition,
					"test-actor-borrowed-semantic-trace-beneficiary"),
				Facts =
				[
					FactionFact.Beneficiary(
						actorId,
						Faction.Villager,
						boundary)
				]
			});
	}

	private static ActorBorrowedRolePowerActivation PerformSpendOpening(
		IGameHookListener listener,
		GameSession session,
		StartGameConfirmationInstruction start,
		Guid selectedCardId)
	{
		var wake = Advance(listener, session, start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(listener, session, wake.CreateResponse()).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var sleep = Advance(
			listener,
			session,
			choice.CreateResponse(selectedCardId.ToString("D"))).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		Advance(listener, session, sleep.CreateResponse()).Outcome.Should()
			.Be(HookListenerOutcome.Complete);
		session.ClearCurrentListenerCache(HookKey);
		return activation;
	}

	private static void SeedKnownFactionFacts(
		GameSession session,
		Guid werewolfId)
	{
		FactionFactEffectiveBoundary? agentBoundary = null;
		session.CommitFactionFactBatch(context =>
		{
			var boundary = new FactionFactEffectiveBoundary(
				context.TurnNumber,
				context.CurrentPhase,
				session.GameHistoryLog.Count());
			agentBoundary = boundary;
			return new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ScheduledObservation,
					FactionFactSource
						.WerewolfFactionAgentGroupObservationIdentifier),
				Facts =
				[
					.. session.GetPlayers().Select(player => FactionFact.Agent(
						player.Id,
						Faction.Werewolf,
						player.Id == werewolfId
							? FactionAgentKnowledge.KnownAgent
							: FactionAgentKnowledge.KnownNonAgent,
						boundary))
				]
			};
		});

		InitialBeneficiaryClosureRules.TryCommitCurrentSession(
				session,
				agentBoundary)
			.Should().Be(InitialBeneficiaryClosureResult.Committed);
	}

	private static HookListenerActionResult Advance(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response)
	{
		var result = listener.Execute(session, response);
		if (result.Outcome != HookListenerOutcome.Skip)
		{
			session.TransitionListenerStateCache(
				HookKey,
				listener.Id,
				result.NextListenerPhase!);
		}

		return result;
	}

	private sealed record ActorFixture(
		MainRoleType SourceRole,
		GameSession Session,
		StartGameConfirmationInstruction Start,
		Guid ActorId,
		IGameHookListener Listener,
		BaselineRandomDecisionStrategy Strategy);

	private sealed class TestSubPhaseManagerKey : ISubPhaseManagerKey;
	private sealed class TestHookSubPhaseKey : IHookSubPhaseKey;
	private sealed class TestGameFlowManagerKey : IGameFlowManagerKey;
}
