using System.Collections.Immutable;
using FluentAssertions;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorBorrowedBearTamerKnightTests
{
	private sealed class TestExecutionCommitKey : IGameFlowManagerKey;
	private static readonly TestExecutionCommitKey ExecutionCommitKey = new();

	private static readonly PhysicalCharacterCard BearTamerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000150"),
		MainRoleType.BearTamer);
	private static readonly PhysicalCharacterCard SeerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000151"),
		MainRoleType.Seer);
	private static readonly PhysicalCharacterCard FoxCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000152"),
		MainRoleType.Fox);
	private static readonly PhysicalCharacterCard KnightCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000153"),
		MainRoleType.KnightWithRustySword);
	[Fact]
	public void BorrowedBearTamer_LivingAgentNeighborGrowlsAfterDawnCascadeBeforeTerminalVictory()
	{
		var fixture = CreateActiveBorrowedBearTamerDawn();

		var growl = AdvanceDawnToBearTamerGrowl(
			fixture.Session,
			fixture.Start,
			fixture.VictimId,
			fixture.Admissions);

		growl.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceBearTamerGrowl);
		growl.PublicAnnouncement.Should().BeNull();
		growl.PrivateInstruction.Should().Be(
			GameStrings.BearTamerGrowlInstruction);
		growl.AffectedPlayerIds.Should().BeNull();
		growl.SoundEffects.Should().Equal(SoundEffectsEnum.BearGrowl);
		var state = (IGameSession)fixture.Session;
		state.GetPlayerState(fixture.ActorId).Health.Should().Be(
			PlayerHealth.Alive);
		state.GetPlayerState(fixture.ActorId).CurrentRole.Should().Be(
			MainRoleType.Actor);
		var activation = fixture.Session
			.GetModeratorActiveActorBorrowedRolePowerActivation();
		activation.Should().NotBeNull();
		activation!.SourceRole.Should().Be(MainRoleType.BearTamer);
		state.GetPlayerState(fixture.VictimId).Health.Should().Be(
			PlayerHealth.Dead);
		state.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == fixture.VictimId &&
				entry.Reason == EliminationReason.WerewolfAttack);
		state.GameHistoryLog.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ScopeId.StartsWith("Dawn:", StringComparison.Ordinal));
		state.GameHistoryLog.OfType<VictoryConditionMetLogEntry>()
			.Should().BeEmpty();
		state.GameHistoryLog.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().BeEmpty();
		state.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry =>
				entry.Role == MainRoleType.BearTamer);
	}

	[Fact]
	public void BorrowedBearTamer_PendingGrowlRehydratesAndCommitsAuthenticatedLineageExactlyOnce()
	{
		var fixture = CreateActiveBorrowedBearTamerDawn();
		var growl = AdvanceDawnToBearTamerGrowl(
			fixture.Session,
			fixture.Start,
			fixture.VictimId,
			fixture.Admissions);
		var service = new GameService();
		var gameId = service.RehydrateSession(fixture.Session.Serialize());
		var recoveredGrowl = service.GetCurrentInstruction(gameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredGrowl.Should().BeEquivalentTo(growl);
		recoveredGrowl.InstructionId.Should().Be(growl.InstructionId);
		recoveredGrowl.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceBearTamerGrowl);
		recoveredGrowl.PublicAnnouncement.Should().BeNull();
		recoveredGrowl.PrivateInstruction.Should().Be(
			GameStrings.BearTamerGrowlInstruction);
		recoveredGrowl.AffectedPlayerIds.Should().BeNull();
		recoveredGrowl.SoundEffects.Should().Equal(
			SoundEffectsEnum.BearGrowl);
		var beforeStaleResponse = service.GetGameStateView(gameId)!.Serialize();
		Action stale = () => service.ProcessInstruction(
			gameId,
			fixture.Start.CreateResponse());

		stale.Should().Throw<InvalidOperationException>()
			.WithMessage("*pending Moderator Instruction*");
		service.GetGameStateView(gameId)!.Serialize()
			.Should().Be(beforeStaleResponse);

		var terminal = service.ProcessInstruction(
				gameId,
				recoveredGrowl.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<FinishedGameConfirmationInstruction>().Subject;

		terminal.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
		terminal.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.Werewolf));
		var committed = (GameSession)service.GetGameStateView(gameId)!;
		var privateCommit = committed
			.GetActorBorrowedBearTamerGrowlCommits()
			.Should().ContainSingle().Subject;
		privateCommit.PowerIdentity.ActingPlayerId.Should().Be(fixture.ActorId);
		privateCommit.PowerIdentity.SourceRole.Should().Be(
			MainRoleType.BearTamer);
		privateCommit.PowerIdentity.SourcePowerIdentifier.Should().Be(
			"bear-tamer-growl");
		privateCommit.PowerIdentity.PowerInstanceId.Should().Be(
			fixture.ActivationId);
		privateCommit.PowerIdentity.PowerInstanceOrigin.Should().Be(
			RolePowerInstanceOrigin.Borrowed);
		privateCommit.ActorSetupCardId.Should().Be(BearTamerCard.Id);
		privateCommit.CurrentPhase.Should().Be(GamePhase.Dawn);
		var history = committed.GameHistoryLog.ToArray();
		var cascadeIndex = Array.FindIndex(
			history,
			entry => entry is EliminationCascadeCompletedLogEntry);
		var markerIndex = privateCommit.PublicMarkerLogIndex;
		var occurrenceIndex = Array.FindIndex(
			history,
			entry => entry is BearTamerGrowlOccurredLogEntry);
		var victoryIndex = Array.FindIndex(
			history,
			entry => entry is VictoryConditionMetLogEntry);
		history[markerIndex].Should().BeOfType<
			ActorBorrowedRolePowerCommittedLogEntry>();
		history[markerIndex].ToString().Should()
			.Be("ActorBorrowedRolePowerCommitted")
			.And.NotContain(MainRoleType.BearTamer.ToString())
			.And.NotContain(fixture.ActorId.ToString())
			.And.NotContain(fixture.ActivationId.ToString())
			.And.NotContain(BearTamerCard.Id.ToString());
		committed.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		committed.GameHistoryLog.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().ContainSingle()
			.Which.ToString().Should().Be("BearTamerGrowlOccurred");
		cascadeIndex.Should().BeGreaterThanOrEqualTo(0);
		markerIndex.Should().BeGreaterThan(cascadeIndex);
		occurrenceIndex.Should().Be(markerIndex + 1);
		victoryIndex.Should().BeGreaterThan(occurrenceIndex);

		var recoveryService = new GameService();
		var recoveredGameId = recoveryService.RehydrateSession(
			committed.Serialize());
		var recoveredTerminal = recoveryService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<FinishedGameConfirmationInstruction>().Subject;
		recoveredTerminal.Should().BeEquivalentTo(terminal);
		var recovered = (GameSession)recoveryService
			.GetGameStateView(recoveredGameId)!;
		recovered.GetActorBorrowedBearTamerGrowlCommits()
			.Should().ContainSingle().Which.Should().Be(privateCommit);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		recovered.GameHistoryLog.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().ContainSingle();
		var beforeReplay = recovered.Serialize();
		var replay = recoveryService.ProcessInstruction(
			recoveredGameId,
			recoveredGrowl.CreateResponse());

		replay.IsSuccess.Should().BeFalse();
		replay.ModeratorInstruction.Should().BeEquivalentTo(
			recoveredTerminal);
		recovered.Serialize().Should().Be(beforeReplay);
	}

	[Fact]
	public void BorrowedBearTamer_IncompleteAgentFactsThrowBeforeGrowlMutation()
	{
		var fixture = CreateActiveBorrowedBearTamerDawn(
			BorrowedBearTamerScenario.IncompleteAgentFacts);
		fixture.Session.GetFactionAgentKnowledge(
				fixture.VictimId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.Unknown);
		fixture.Session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);

		Action act = () => AdvanceDawnWithoutGrowl(
			fixture.Session,
			fixture.Start,
			fixture.VictimId,
			fixture.Admissions);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Faction* facts*");
		fixture.Session.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().NotBeNull();
		fixture.Session.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().ContainSingle();
		AssertNoBorrowedBearGrowlMutation(fixture.Session);
	}

	[Theory]
	[InlineData(BorrowedBearTamerScenario.FalseNeighbor)]
	[InlineData(BorrowedBearTamerScenario.DeadActor)]
	[InlineData(BorrowedBearTamerScenario.Unavailable)]
	[InlineData(BorrowedBearTamerScenario.NoActivation)]
	public void BorrowedBearTamer_FalseDeadUnavailableOrInactiveNeedsNoInput(
		BorrowedBearTamerScenario scenario)
	{
		var fixture = CreateActiveBorrowedBearTamerDawn(scenario);

		var terminal = AdvanceDawnWithoutGrowl(
			fixture.Session,
			fixture.Start,
			fixture.VictimId,
			fixture.Admissions);

		terminal.Should().BeOfType<FinishedGameConfirmationInstruction>();
		AssertNoBorrowedBearGrowlMutation(fixture.Session);
	}

	[Fact]
	public void BorrowedKnight_GenericDawnRevealCommitsActorCardBeforePrivateSchedule()
	{
		var fixture = CreateActiveBorrowedKnightDawn(
			preserveGameBeyondDawn: true);
		var actorCardId = fixture.Session.GetPlayerState(fixture.ActorId)
			.PhysicalCharacterCardId;
		actorCardId.Should().NotBeNull();

		var observed = AdvanceDawnToDay(
			fixture.Session,
			fixture.Start.CreateResponse(),
			fixture.Admissions,
			new Dictionary<Guid, MainRoleType>
			{
				[fixture.ActorId] = MainRoleType.Actor
			});

		var reveal = observed.OfType<ConfirmationInstruction>()
			.Where(instruction =>
				instruction.Semantic ==
					ModeratorInstructionSemantic.AssignDawnVictimRoles &&
				instruction.AffectedPlayerIds != null &&
				instruction.AffectedPlayerIds.SequenceEqual(
					new[] { fixture.ActorId }))
			.Should().ContainSingle().Subject;
		reveal.PrivateInstruction.Should().Be(
			GameStrings.PublicRoleRevealInstruction);
		var state = (IGameSession)fixture.Session;
		var actor = state.GetPlayerState(fixture.ActorId);
		actor.Health.Should().Be(PlayerHealth.Dead);
		actor.CurrentRole.Should().Be(MainRoleType.Actor);
		actor.PubliclyRevealedRole.Should().Be(MainRoleType.Actor);
		actor.PhysicalCharacterCardId.Should().Be(actorCardId);
		state.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry =>
				entry.Role == MainRoleType.KnightWithRustySword);

		var history = state.GameHistoryLog.ToArray();
		var revealIndex = Array.FindIndex(
			history,
			entry => entry is RoleRevealLogEntry roleReveal &&
				roleReveal.RevealedRoles.TryGetValue(
					fixture.ActorId,
					out var role) &&
				role == MainRoleType.Actor);
		var eliminationIndex = Array.FindIndex(
			history,
			entry => entry is PlayerEliminatedLogEntry eliminated &&
				eliminated.PlayerId == fixture.ActorId &&
				eliminated.Reason == EliminationReason.WerewolfAttack);
		var cascadeIndex = Array.FindIndex(
			history,
			entry => entry is EliminationCascadeCompletedLogEntry);
		var markerIndex = Array.FindIndex(
			history,
			entry => entry is ActorBorrowedRolePowerCommittedLogEntry);
		revealIndex.Should().BeGreaterThanOrEqualTo(0);
		eliminationIndex.Should().BeGreaterThan(revealIndex);
		cascadeIndex.Should().BeGreaterThan(eliminationIndex);
		markerIndex.Should().BeGreaterThan(cascadeIndex);
		fixture.Session.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle().Which.TargetPlayerId.Should().Be(
				fixture.TargetId);
	}

	[Fact]
	public void BorrowedKnight_SuccessfulTriggeringNightInfectionIsEligibleAtSnapshot()
	{
		var fixture = CreateActiveBorrowedKnightDawn(
			scenario: BorrowedKnightScenario.SuccessfulTriggeringNightInfection);
		var beforeDawn = (IGameSession)fixture.Session;
		beforeDawn.GetPlayerState(fixture.TargetId).CurrentRole.Should().Be(
			MainRoleType.SimpleVillager);
		beforeDawn.GetFactionAgentKnowledge(
				fixture.TargetId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownNonAgent);

		var terminal = AdvanceDawnWithoutGrowl(
			fixture.Session,
			fixture.Start,
			fixture.ActorId,
			fixture.Admissions);

		terminal.Should().BeOfType<FinishedGameConfirmationInstruction>();
		var state = (IGameSession)fixture.Session;
		state.GetPlayerState(fixture.ActorId).Health.Should().Be(
			PlayerHealth.Dead);
		state.GetPlayerState(fixture.TargetId).Health.Should().Be(
			PlayerHealth.Alive);
		state.GetPlayerState(fixture.TargetId).CurrentRole.Should().Be(
			MainRoleType.SimpleVillager);
		state.GetPlayerState(fixture.TargetId).HasStatusEffect(
			StatusEffectTypes.LycanthropyInfection).Should().BeTrue();
		state.GetFactionAgentKnowledge(
				fixture.TargetId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		state.GameHistoryLog.OfType<FactionFactsCommittedLogEntry>()
			.SelectMany(entry => entry.Facts)
			.Should().ContainSingle(fact =>
				fact.PlayerId == fixture.TargetId &&
				fact.Type == FactionFactType.Agent &&
				fact.AgentKnowledge == FactionAgentKnowledge.KnownAgent &&
				fact.EffectiveBoundary.Phase == GamePhase.Night);
		fixture.Session.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle().Which.TargetPlayerId.Should().Be(
				fixture.TargetId);
	}

	[Fact]
	public void BorrowedKnight_CascadeTransformedAgentIsEligibleAtPostCascadeSnapshot()
	{
		var fixture = CreateActiveBorrowedKnightDawn(
			scenario: BorrowedKnightScenario.CascadeTransformedAgent);
		var beforeDawn = (IGameSession)fixture.Session;
		beforeDawn.GetPlayerState(fixture.TargetId).CurrentRole.Should().Be(
			MainRoleType.WildChild);
		beforeDawn.GetPlayerState(fixture.TargetId).HasStatusEffect(
			StatusEffectTypes.WildChildChanged).Should().BeFalse();
		beforeDawn.GetFactionAgentKnowledge(
				fixture.TargetId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownNonAgent);

		var terminal = AdvanceDawnWithoutGrowl(
			fixture.Session,
			fixture.Start,
			fixture.ActorId,
			fixture.Admissions);

		terminal.Should().BeOfType<FinishedGameConfirmationInstruction>();
		var state = (IGameSession)fixture.Session;
		state.GetPlayerState(fixture.TargetId).Health.Should().Be(
			PlayerHealth.Alive);
		state.GetPlayerState(fixture.TargetId).CurrentRole.Should().Be(
			MainRoleType.WildChild);
		state.GetPlayerState(fixture.TargetId).HasStatusEffect(
			StatusEffectTypes.WildChildChanged).Should().BeTrue();
		state.GetFactionAgentKnowledge(
				fixture.TargetId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		var history = state.GameHistoryLog.ToArray();
		var transformationIndex = Array.FindIndex(
			history,
			entry => entry is FactionFactsCommittedLogEntry facts &&
				facts.Source.Identifier ==
					EliminationCascadeReactionIds.WildChildModelEliminated &&
				facts.Facts.Any(fact =>
					fact.PlayerId == fixture.TargetId &&
					fact.Type == FactionFactType.Agent &&
					fact.AgentKnowledge == FactionAgentKnowledge.KnownAgent &&
					fact.EffectiveBoundary.Phase == GamePhase.Dawn));
		var cascadeIndex = Array.FindIndex(
			history,
			entry => entry is EliminationCascadeCompletedLogEntry);
		var markerIndex = Array.FindIndex(
			history,
			entry => entry is ActorBorrowedRolePowerCommittedLogEntry);
		transformationIndex.Should().BeGreaterThanOrEqualTo(0);
		cascadeIndex.Should().BeGreaterThan(transformationIndex);
		markerIndex.Should().BeGreaterThan(cascadeIndex);
		fixture.Session.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle().Which.TargetPlayerId.Should().Be(
				fixture.TargetId);
	}

	[Fact]
	public void BorrowedKnight_TriggeringNightTemporaryAgentRemainsEligibleAfterDawnExpiry()
	{
		var fixture = CreateActiveBorrowedKnightDawn(
			scenario: BorrowedKnightScenario.TriggeringNightTemporaryAgent);
		var beforeScan = (IGameSession)fixture.Session;
		beforeScan.GetPlayerState(fixture.TargetId).CurrentRole.Should().Be(
			MainRoleType.SimpleVillager);
		beforeScan.GetFactionAgentKnowledge(
				fixture.TargetId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownNonAgent);
		var targetAgentFacts = beforeScan.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.SelectMany(entry => entry.Facts)
			.Where(fact =>
				fact.PlayerId == fixture.TargetId &&
				fact.Type == FactionFactType.Agent)
			.ToArray();
		targetAgentFacts.Should().ContainSingle(fact =>
			fact.AgentKnowledge == FactionAgentKnowledge.KnownAgent &&
			fact.EffectiveBoundary.Phase == GamePhase.Night);
		targetAgentFacts.Should().ContainSingle(fact =>
			fact.AgentKnowledge == FactionAgentKnowledge.KnownNonAgent &&
			fact.EffectiveBoundary.Phase == GamePhase.Dawn);

		var terminal = AdvanceDawnWithoutGrowl(
			fixture.Session,
			fixture.Start,
			fixture.ActorId,
			fixture.Admissions);

		terminal.Should().BeOfType<FinishedGameConfirmationInstruction>();
		var state = (IGameSession)fixture.Session;
		state.GetFactionAgentKnowledge(
				fixture.TargetId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownNonAgent);
		state.GetPlayerState(fixture.TargetId).Health.Should().Be(
			PlayerHealth.Alive);
		fixture.Session.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle().Which.TargetPlayerId.Should().Be(
				fixture.TargetId);
	}

	[Fact]
	public void BorrowedKnight_MissingTriggeringNightAgentFactsThrowBeforePrivateScheduleMutation()
	{
		var fixture = CreateActiveBorrowedKnightDawn(
			scenario: BorrowedKnightScenario.MissingTriggeringAgentFacts);
		fixture.Session.GetFactionAgentKnowledge(
				fixture.OmittedAgentPlayerId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.Unknown);

		Action act = () => AdvanceDawnWithoutGrowl(
			fixture.Session,
			fixture.Start,
			fixture.ActorId,
			fixture.Admissions);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Faction* facts*");
		fixture.Session.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().NotBeNull();
		fixture.Session.GameHistoryLog
			.OfType<EliminationCascadeCompletedLogEntry>()
			.Should().ContainSingle();
		AssertNoBorrowedKnightScheduleMutation(fixture.Session);
	}

	[Theory]
	[InlineData(BorrowedKnightScenario.AvailabilityDenied)]
	[InlineData(BorrowedKnightScenario.NonWerewolfElimination)]
	[InlineData(BorrowedKnightScenario.NoEligibleSurvivingAgent)]
	public void BorrowedKnight_DeniedNonWerewolfOrNoEligibleAgentCompletesWithoutScheduling(
		BorrowedKnightScenario scenario)
	{
		var fixture = CreateActiveBorrowedKnightDawn(scenario: scenario);

		var terminal = AdvanceDawnWithoutGrowl(
			fixture.Session,
			fixture.Start,
			fixture.ActorId,
			fixture.Admissions);

		terminal.Should().BeOfType<FinishedGameConfirmationInstruction>();
		if (scenario == BorrowedKnightScenario.NonWerewolfElimination)
		{
			fixture.Session.GameHistoryLog
				.OfType<PlayerEliminatedLogEntry>()
				.Should().ContainSingle(entry =>
					entry.PlayerId == fixture.ActorId &&
					entry.Reason == EliminationReason.EventElimination);
			fixture.Session.GameHistoryLog
				.OfType<PlayerEliminatedLogEntry>()
				.Should().NotContain(entry =>
					entry.PlayerId == fixture.ActorId &&
					entry.Reason == EliminationReason.WerewolfAttack);
		}
		if (scenario == BorrowedKnightScenario.NoEligibleSurvivingAgent)
		{
			fixture.Session.GetPlayers()
				.Where(player => player.State.Health == PlayerHealth.Alive)
				.Should().OnlyContain(player =>
					fixture.Session.GetFactionAgentKnowledge(
						player.Id,
						Faction.Werewolf) ==
					FactionAgentKnowledge.KnownNonAgent);
		}
		AssertNoBorrowedKnightScheduleMutation(fixture.Session);
	}

	[Fact]
	public void BorrowedKnight_DirectWerewolfAttackSchedulesPrivateFirstClockwiseAgentAfterCascade()
	{
		var fixture = CreateActiveBorrowedKnightDawn();

		var terminal = AdvanceDawnWithoutGrowl(
			fixture.Session,
			fixture.Start,
			fixture.ActorId,
			fixture.Admissions);

		terminal.Should().BeOfType<FinishedGameConfirmationInstruction>();
		var state = (IGameSession)fixture.Session;
		state.GetPlayerState(fixture.ActorId).Health.Should().Be(
			PlayerHealth.Dead);
		state.GetPlayerState(fixture.TargetId).HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeFalse();
		state.GetPlayerState(fixture.OtherWerewolfId).HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeFalse();
		var history = state.GameHistoryLog.ToArray();
		var cascadeIndex = Array.FindIndex(
			history,
			entry => entry is EliminationCascadeCompletedLogEntry);
		var markerIndex = Array.FindIndex(
			history,
			entry => entry is ActorBorrowedRolePowerCommittedLogEntry);
		cascadeIndex.Should().BeGreaterThanOrEqualTo(0);
		markerIndex.Should().BeGreaterThan(cascadeIndex);
		history[markerIndex].ToString().Should()
			.Be("ActorBorrowedRolePowerCommitted")
			.And.NotContain(MainRoleType.KnightWithRustySword.ToString())
			.And.NotContain(fixture.ActorId.ToString())
			.And.NotContain(fixture.TargetId.ToString())
			.And.NotContain(fixture.ActivationId.ToString())
			.And.NotContain(KnightCard.Id.ToString());
		state.GameHistoryLog.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry =>
				entry.PlayerId == fixture.TargetId &&
				entry.EffectType == StatusEffectTypes.RustySwordDisease);
		state.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry =>
				entry.Role == MainRoleType.KnightWithRustySword);
	}

	[Fact]
	public void BorrowedKnight_PrivateScheduleRehydratesIntoOrdinaryRustySwordConsequenceForSnapshottedTargetExactlyOnce()
	{
		var fixture = CreateActiveBorrowedKnightDawn(
			preserveGameBeyondDawn: true);
		AdvanceDawnToDay(
			fixture.Session,
			fixture.Start.CreateResponse(),
			fixture.Admissions,
			new Dictionary<Guid, MainRoleType>
			{
				[fixture.ActorId] = MainRoleType.Actor
			});
		var scheduled = fixture.Session
			.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle().Subject;
		scheduled.TargetPlayerId.Should().Be(fixture.TargetId);
		scheduled.TurnNumber.Should().Be(1);
		scheduled.CurrentPhase.Should().Be(GamePhase.Dawn);
		fixture.Session.GetPlayerState(fixture.TargetId).HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeFalse();
		fixture.Session.GameHistoryLog.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry =>
				entry.PlayerId == fixture.TargetId &&
				entry.EffectType == StatusEffectTypes.RustySwordDisease);

		var recoveryService = new GameService();
		var recoveredGameId = recoveryService.RehydrateSession(
			fixture.Session.Serialize());
		var recovered = (GameSession)recoveryService
			.GetGameStateView(recoveredGameId)!;
		recovered.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle().Which.Should().Be(scheduled);
		recovered.GetCurrentPhase().Should().Be(GamePhase.Day);
		recovered.GetPlayerState(fixture.TargetId).HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeFalse();
		recovered.GameHistoryLog.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry =>
				entry.PlayerId == fixture.TargetId &&
				entry.EffectType == StatusEffectTypes.RustySwordDisease);
		recovered.TransitionMainPhase(GamePhase.Night);
		recovered.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().BeNull();
		CommitCurrentWerewolfAgentFacts(
			recovered,
			new HashSet<Guid> { fixture.OtherWerewolfId });
		recovered.GetFactionAgentKnowledge(
				fixture.TargetId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownNonAgent);
		recovered.GetFactionAgentKnowledge(
				fixture.OtherWerewolfId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);

		IGameHookListener knight = new KnightWithTheRustySwordRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var followingNightVictimId = FindFollowingNightVictim(
			recovered,
			fixture.TargetId);
		var hookInstructions = AdvanceNightHookToCompletion(
			knight,
			recovered,
			fixture.Start.CreateResponse(),
			followingNightVictimId);
		var prematureRustySwordAnnouncement = string.Format(
			GameStrings.RustySwordDiseaseEliminationAnnouncement,
			recovered.GetPlayer(fixture.TargetId).Name);

		hookInstructions.Any(instruction =>
			instruction.PublicAnnouncement?.Contains(
				prematureRustySwordAnnouncement,
				StringComparison.Ordinal) == true).Should().BeFalse();
		var publicFollowingNight = recoveryService
			.GetGameStateView(recoveredGameId)!;
		publicFollowingNight.GameHistoryLog.OfType<NightActionLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType == NightActionType.RustySword);
		publicFollowingNight.GetPlayerState(fixture.TargetId)
			.HasStatusEffect(StatusEffectTypes.RustySwordDisease)
			.Should().BeFalse();
		publicFollowingNight.GameHistoryLog.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry =>
				entry.PlayerId == fixture.TargetId &&
				entry.EffectType == StatusEffectTypes.RustySwordDisease);

		recovered.TransitionMainPhase(GamePhase.Dawn);
		recovered = RecoveryPayloadTestDriver.Capture(recovered)
			.WithPendingInstruction(fixture.Start)
			.RehydrateGameSession();
		var followingDawnInstructions = AdvanceDawnToDay(
			recovered,
			fixture.Start.CreateResponse(),
			fixture.Admissions,
			new Dictionary<Guid, MainRoleType>
			{
				[fixture.TargetId] = MainRoleType.SimpleWerewolf,
				[followingNightVictimId] = recovered
					.GetPlayerState(followingNightVictimId)
					.CurrentRole!.Value
			});
		recovered.GameHistoryLog.OfType<DawnVictimDeterminedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.TurnNumber == 2 &&
				entry.PlayerId == fixture.TargetId &&
				entry.Reason == EliminationReason.RustySword);
		recovered.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == fixture.TargetId &&
				entry.Reason == EliminationReason.RustySword);
		recovered.GameHistoryLog
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.TriggeringEliminations.Any(elimination =>
					elimination.PlayerId == fixture.TargetId &&
					elimination.Reason == EliminationReason.RustySword));
		var announcement = followingDawnInstructions
			.OfType<ConfirmationInstruction>()
			.Should().ContainSingle(instruction =>
				instruction.Semantic ==
					ModeratorInstructionSemantic.AnnounceDawnVictims)
			.Subject;
		announcement.PublicAnnouncement.Should().Contain(
			string.Format(
				GameStrings.RustySwordDiseaseEliminationAnnouncement,
				recovered.GetPlayer(fixture.TargetId).Name));
		recovered.GetPlayerState(fixture.TargetId).Health.Should().Be(
			PlayerHealth.Dead);
		recovered.GetPlayerState(fixture.OtherWerewolfId).Health.Should().Be(
			PlayerHealth.Alive);
		recovered.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == fixture.TargetId &&
				entry.Reason == EliminationReason.RustySword);
		recovered.GameHistoryLog
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.TriggeringEliminations.Any(elimination =>
					elimination.PlayerId == fixture.TargetId &&
					elimination.Reason == EliminationReason.RustySword));
		recovered.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle().Which.Should().Be(scheduled);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		recovered.GameHistoryLog.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry =>
				entry.PlayerId == fixture.TargetId &&
				entry.EffectType == StatusEffectTypes.RustySwordDisease);

		var finalService = new GameService();
		var finalGameId = finalService.RehydrateSession(recovered.Serialize());
		var final = (GameSession)finalService.GetGameStateView(finalGameId)!;
		final.GameHistoryLog.OfType<NightActionLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType == NightActionType.RustySword);
		final.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == fixture.TargetId &&
				entry.Reason == EliminationReason.RustySword);
	}

	[Theory]
	[InlineData(BorrowedKnightCommittedTargetMutation.TargetRoleChanged)]
	[InlineData(BorrowedKnightCommittedTargetMutation.GlobalSuppressionActivated)]
	public void BorrowedKnight_CommittedTargetRemainsFixedAcrossLaterRoleChangeOrSuppression(
		BorrowedKnightCommittedTargetMutation mutation)
	{
		var fixture = CreateActiveBorrowedKnightDawn(
			preserveGameBeyondDawn: true);
		AdvanceDawnToDay(
			fixture.Session,
			fixture.Start.CreateResponse(),
			fixture.Admissions,
			new Dictionary<Guid, MainRoleType>
			{
				[fixture.ActorId] = MainRoleType.Actor
			});
		var scheduled = fixture.Session
			.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle().Subject;
		scheduled.TargetPlayerId.Should().Be(fixture.TargetId);
		fixture.Session.GetPlayerState(fixture.TargetId).HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeFalse();
		var session = RecoveryPayloadTestDriver.Capture(fixture.Session)
			.WithPendingInstruction(fixture.Start)
			.RehydrateGameSession();
		var targetRoleAtFollowingDawn = MainRoleType.SimpleWerewolf;
		if (mutation == BorrowedKnightCommittedTargetMutation.TargetRoleChanged)
		{
			session.AssignRole(
				fixture.TargetId,
				MainRoleType.SimpleVillager);
			targetRoleAtFollowingDawn = MainRoleType.SimpleVillager;
		}
		else
		{
			session.CommitGameFact(context =>
				new VillagerRolePowerSuppressionCommittedLogEntry
				{
					Timestamp = context.Timestamp,
					TurnNumber = context.TurnNumber,
					CurrentPhase = context.CurrentPhase,
					AnnouncementInstructionId = Guid.NewGuid()
				});
		}

		session.TransitionMainPhase(GamePhase.Night);
		session.TryExpireActorBorrowedRolePowerActivation()
			.Should().BeTrue();
		CommitCurrentWerewolfAgentFacts(
			session,
			new HashSet<Guid> { fixture.OtherWerewolfId });
		session.GetFactionAgentKnowledge(
				fixture.TargetId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownNonAgent);
		session.GetFactionAgentKnowledge(
				fixture.OtherWerewolfId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		IGameHookListener knight = new KnightWithTheRustySwordRole(
			new RolePowerAvailabilityGateway(
				new VillagerRolePowerSuppressionPolicy(
					AllowAllRolePowerAvailabilityPolicy.Instance)));
		var followingNightVictimId = FindFollowingNightVictim(
			session,
			fixture.TargetId);
		AdvanceNightHookToCompletion(
			knight,
			session,
			fixture.Start.CreateResponse(),
			followingNightVictimId);

		session.GameHistoryLog.OfType<NightActionLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType == NightActionType.RustySword);
		session.GetPlayerState(fixture.TargetId).HasStatusEffect(
			StatusEffectTypes.RustySwordDisease).Should().BeFalse();
		session.GameHistoryLog.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry =>
				entry.PlayerId == fixture.TargetId &&
				entry.EffectType == StatusEffectTypes.RustySwordDisease);
		session.TransitionMainPhase(GamePhase.Dawn);
		session = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(fixture.Start)
			.RehydrateGameSession();
		AdvanceDawnToDay(
			session,
			fixture.Start.CreateResponse(),
			fixture.Admissions,
			new Dictionary<Guid, MainRoleType>
			{
				[fixture.TargetId] = targetRoleAtFollowingDawn,
				[followingNightVictimId] = session
					.GetPlayerState(followingNightVictimId)
					.CurrentRole!.Value
			});

		session.GameHistoryLog.OfType<DawnVictimDeterminedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.TurnNumber == 2 &&
				entry.PlayerId == fixture.TargetId &&
				entry.Reason == EliminationReason.RustySword);
		session.GameHistoryLog.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry =>
				entry.TurnNumber == 2 &&
				entry.PlayerId == fixture.OtherWerewolfId &&
				entry.Reason == EliminationReason.RustySword);
		session.GetPlayerState(fixture.TargetId).Health.Should().Be(
			PlayerHealth.Dead);
		session.GetPlayerState(fixture.OtherWerewolfId).Health.Should().Be(
			PlayerHealth.Alive);
		session.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle().Which.Should().Be(scheduled);
		if (mutation ==
			BorrowedKnightCommittedTargetMutation.GlobalSuppressionActivated)
		{
			session.GameHistoryLog
				.OfType<VillagerRolePowerSuppressionCommittedLogEntry>()
				.Should().ContainSingle();
		}
	}

	[Fact]
	public void BorrowedKnight_DeadScheduledTargetExpiresSilentlyWithoutRetargetingLivingAgent()
	{
		var fixture = CreateActiveBorrowedKnightDawn(
			preserveGameBeyondDawn: true);
		AdvanceDawnToDay(
			fixture.Session,
			fixture.Start.CreateResponse(),
			fixture.Admissions,
			new Dictionary<Guid, MainRoleType>
			{
				[fixture.ActorId] = MainRoleType.Actor
			});
		var scheduled = fixture.Session
			.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle().Subject;
		scheduled.TargetPlayerId.Should().Be(fixture.TargetId);

		var recoveryService = new GameService();
		var recoveredGameId = recoveryService.RehydrateSession(
			fixture.Session.Serialize());
		var recovered = (GameSession)recoveryService
			.GetGameStateView(recoveredGameId)!;
		recovered.EliminatePlayer(
			fixture.TargetId,
			EliminationReason.EventElimination);
		recovered.GetPlayerState(fixture.TargetId).Health.Should().Be(
			PlayerHealth.Dead);
		recovered.GetPlayerState(fixture.OtherWerewolfId).Health.Should().Be(
			PlayerHealth.Alive);
		recovered.TransitionMainPhase(GamePhase.Night);
		recovered.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		CommitCurrentWerewolfAgentFacts(
			recovered,
			new HashSet<Guid> { fixture.OtherWerewolfId });
		recovered.GetFactionAgentKnowledge(
				fixture.OtherWerewolfId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);

		IGameHookListener knight = new KnightWithTheRustySwordRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var followingNightVictimId = FindFollowingNightVictim(
			recovered,
			fixture.TargetId);
		AdvanceNightHookToCompletion(
			knight,
			recovered,
			fixture.Start.CreateResponse(),
			followingNightVictimId);

		var publicFollowingNight = recoveryService
			.GetGameStateView(recoveredGameId)!;
		publicFollowingNight.GameHistoryLog.OfType<NightActionLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType == NightActionType.RustySword);
		publicFollowingNight.GetPlayerState(fixture.TargetId)
			.HasStatusEffect(StatusEffectTypes.RustySwordDisease)
			.Should().BeFalse();

		recovered.TransitionMainPhase(GamePhase.Dawn);
		recovered = RecoveryPayloadTestDriver.Capture(recovered)
			.WithPendingInstruction(fixture.Start)
			.RehydrateGameSession();
		var followingDawnInstructions = AdvanceDawnToDay(
			recovered,
			fixture.Start.CreateResponse(),
			fixture.Admissions,
			new Dictionary<Guid, MainRoleType>
			{
				[followingNightVictimId] = recovered
					.GetPlayerState(followingNightVictimId)
					.CurrentRole!.Value
			});
		var forbiddenAnnouncement = string.Format(
			GameStrings.RustySwordDiseaseEliminationAnnouncement,
			recovered.GetPlayer(fixture.TargetId).Name);
		followingDawnInstructions.Any(instruction =>
			instruction.PublicAnnouncement?.Contains(
				forbiddenAnnouncement,
				StringComparison.Ordinal) == true).Should().BeFalse();
		recovered.GameHistoryLog.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry =>
				entry.TurnNumber == 2 &&
				entry.Reason == EliminationReason.RustySword);
		recovered.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Should().NotContain(entry =>
				entry.Reason == EliminationReason.RustySword);
		recovered.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == fixture.TargetId &&
				entry.Reason == EliminationReason.EventElimination);
		recovered.GetPlayerState(fixture.OtherWerewolfId).Health.Should().Be(
			PlayerHealth.Alive);
		recovered.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle().Which.Should().Be(scheduled);
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
	}

	private static ConfirmationInstruction AdvanceDawnToBearTamerGrowl(
		GameSession session,
		StartGameConfirmationInstruction start,
		Guid victimId,
		IRoleAdmissionSource admissions)
	{
		ModeratorInstruction? instruction = Advance(
			session,
			start.CreateResponse(),
			admissions);
		for (var step = 0; step < 20; step++)
		{
			switch (instruction)
			{
				case ConfirmationInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.AnnounceBearTamerGrowl
				} growl:
					return growl;
				case FinishedGameConfirmationInstruction terminal:
					throw new InvalidOperationException(
						$"Dawn reached terminal victory before the borrowed Bear Tamer growl ({terminal.GameResult}).");
				case ConfirmationInstruction confirmation:
					instruction = Advance(
						session,
						confirmation.CreateResponse(),
						admissions);
					break;
				case AssignRolesInstruction assignment:
					assignment.PlayersForAssignment.Should().Contain(victimId);
					instruction = Advance(
						session,
						assignment.CreateResponse(
							assignment.PlayersForAssignment.ToDictionary(
								playerId => playerId,
								_ => MainRoleType.SimpleVillager)),
						admissions);
					break;
				case null:
					throw new InvalidOperationException(
						"Dawn did not expose a pending Moderator Instruction.");
				default:
					throw new InvalidOperationException(
						$"Unexpected Dawn instruction {instruction.GetType().Name}.");
			}
		}

		throw new InvalidOperationException(
			"Dawn did not reach the borrowed Bear Tamer growl.");
	}

	private static ModeratorInstruction? Advance(
		GameSession session,
		ModeratorResponse response,
		IRoleAdmissionSource? admissions = null) =>
		GameFlowManager.HandleInput(
			session,
			response,
			admissions ?? SupportedRoleCatalog.Admissions)
			.ModeratorInstruction;

	private static IReadOnlyList<ModeratorInstruction>
		AdvanceNightHookToCompletion(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response,
		Guid werewolfVictimId)
	{
		session.GetOrCreateListener(listener.Id, () => listener);
		var hook = new SubPhaseManager<HookDriverSubPhase>(
			HookDriverSubPhase.Active,
			[
				HookSubPhaseStage.HookStage(GameHook.NightMainActionLoop),
				NavigationSubPhaseStage.NavigationEndStageSilent(
					HookDriverSubPhase.Complete)
			],
			possibleNextSubPhases: [HookDriverSubPhase.Complete]);
		var observed = new List<ModeratorInstruction>();
		var currentResponse = response;
		for (var step = 0; step < 20; step++)
		{
			var consumedInstruction = session.Execution.PendingInstruction
				?? throw new InvalidOperationException(
					"The Actor borrowed test workflow requires one Pending Instruction.");
			var instruction = hook.Execute(session, currentResponse)
				.ModeratorInstruction;
			if (instruction == null)
			{
				return observed;
			}

			observed.Add(instruction);
			var publicationResponse =
				currentResponse.InstructionId == consumedInstruction.InstructionId
					? currentResponse
					: new ModeratorResponse
					{
						InstructionId = consumedInstruction.InstructionId,
						Type = currentResponse.Type,
						SelectedPlayerIds = currentResponse.SelectedPlayerIds,
						AssignedPlayerRoles = currentResponse.AssignedPlayerRoles,
						SelectedOptionIds = currentResponse.SelectedOptionIds
					};
			session.CommitExecution(
				ExecutionCommitKey,
				ExecutionCommit.RetainRecoveryBoundary(
					session.Execution,
					consumedInstruction,
					publicationResponse,
					instruction));
			currentResponse = CreateNightHookResponse(
				session,
				instruction,
				werewolfVictimId);
		}

		throw new InvalidOperationException(
			"The following Night hook did not complete.");
	}

	private static ModeratorResponse CreateNightHookResponse(
		GameSession session,
		ModeratorInstruction instruction,
		Guid werewolfVictimId) => instruction switch
	{
		ConfirmationInstruction confirmation => confirmation.CreateResponse(),
		SelectPlayersInstruction
		{
			Semantic:
				ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup
		} observation => observation.CreateResponse(
			observation.SelectablePlayerIds
				.Where(playerId =>
					session.GetFactionAgentKnowledge(
						playerId,
						Faction.Werewolf) ==
					FactionAgentKnowledge.KnownAgent)
				.ToHashSet()),
		SelectPlayersInstruction
		{
			Semantic: ModeratorInstructionSemantic.SelectWerewolfVictim
		} victimSelection
			when victimSelection.SelectablePlayerIds.Contains(werewolfVictimId) =>
			victimSelection.CreateResponse([werewolfVictimId]),
		_ => throw new InvalidOperationException(
			$"Unexpected following Night instruction {instruction.Semantic}.")
	};

	private static Guid FindFollowingNightVictim(
		GameSession session,
		Guid excludedPlayerId) => session.GetPlayers()
		.Where(player =>
			player.Id != excludedPlayerId &&
			player.State.Health == PlayerHealth.Alive &&
			session.GetFactionAgentKnowledge(
				player.Id,
				Faction.Werewolf) ==
			FactionAgentKnowledge.KnownNonAgent)
		.Select(player => player.Id)
		.First();

	private static IReadOnlyList<ModeratorInstruction> AdvanceDawnToDay(
		GameSession session,
		ModeratorResponse initialResponse,
		IRoleAdmissionSource admissions,
		IReadOnlyDictionary<Guid, MainRoleType> roleAssignments)
	{
		var observed = new List<ModeratorInstruction>();
		ModeratorInstruction? instruction = Advance(
			session,
			initialResponse,
			admissions);
		for (var step = 0; step < 30; step++)
		{
			if (instruction != null)
			{
				observed.Add(instruction);
			}

			if (session.GetCurrentPhase() == GamePhase.Day)
			{
				return observed;
			}

			switch (instruction)
			{
				case FinishedGameConfirmationInstruction terminal:
					throw new InvalidOperationException(
						$"Dawn reached terminal victory ({terminal.GameResult}).");
				case ConfirmationInstruction confirmation:
					instruction = Advance(
						session,
						confirmation.CreateResponse(),
						admissions);
					break;
				case AssignRolesInstruction assignment:
					instruction = Advance(
						session,
						assignment.CreateResponse(
							assignment.PlayersForAssignment.ToDictionary(
								playerId => playerId,
								playerId => roleAssignments.TryGetValue(
									playerId,
									out var role)
										? role
										: throw new InvalidOperationException(
											$"Missing Dawn role assignment for {playerId}."))),
						admissions);
					break;
				case null:
					throw new InvalidOperationException(
						"Dawn did not expose a pending Moderator Instruction.");
				default:
					throw new InvalidOperationException(
						$"Unexpected Dawn instruction {instruction.GetType().Name}.");
			}
		}

		throw new InvalidOperationException(
			"Dawn did not transition to Day.");
	}

	private static ModeratorInstruction AdvanceDawnWithoutGrowl(
		GameSession session,
		StartGameConfirmationInstruction start,
		Guid victimId,
		IRoleAdmissionSource admissions)
	{
		ModeratorInstruction? instruction = Advance(
			session,
			start.CreateResponse(),
			admissions);
		for (var step = 0; step < 20; step++)
		{
			switch (instruction)
			{
				case ConfirmationInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.AnnounceBearTamerGrowl
				}:
					throw new InvalidOperationException(
						"Borrowed Bear Tamer unexpectedly requested input.");
				case FinishedGameConfirmationInstruction terminal:
					return terminal;
				case ConfirmationInstruction confirmation:
					instruction = Advance(
						session,
						confirmation.CreateResponse(),
						admissions);
					break;
				case AssignRolesInstruction assignment:
					assignment.PlayersForAssignment.Should().Contain(victimId);
					instruction = Advance(
						session,
						assignment.CreateResponse(
							assignment.PlayersForAssignment.ToDictionary(
								playerId => playerId,
								_ => MainRoleType.SimpleVillager)),
						admissions);
					break;
				case null:
					throw new InvalidOperationException(
						"Dawn did not expose a pending Moderator Instruction.");
				default:
					throw new InvalidOperationException(
						$"Unexpected Dawn instruction {instruction.GetType().Name}.");
			}
		}

		throw new InvalidOperationException(
			"Dawn did not reach its terminal continuation.");
	}

	private static BorrowedBearTamerDawnFixture
		CreateActiveBorrowedBearTamerDawn(
			BorrowedBearTamerScenario scenario =
				BorrowedBearTamerScenario.Positive)
	{
		var setup = new ActorSetupCards(
			version: 15,
			[BearTamerCard, SeerCard, FoxCard]);
		var config = new GameSessionConfig(
			[
				GameStrings.ActorRoleName,
				"Werewolf A",
				"Werewolf B",
				"Werewolf C",
				"Dawn victim"
			],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager
			],
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		IRolePowerAvailabilityPolicy availabilityPolicy =
			scenario == BorrowedBearTamerScenario.Unavailable
				? new DenyBearTamerAvailabilityPolicy()
				: AllowAllRolePowerAvailabilityPolicy.Instance;
		var admissions = scenario == BorrowedBearTamerScenario.Unavailable
			? SupportedRoleCatalog.CreateAdmissions(
				new RolePowerAvailabilityGateway(
					new VillagerRolePowerSuppressionPolicy(
						availabilityPolicy)))
			: SupportedRoleCatalog.Admissions;
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var werewolfIds = players.Skip(1).Take(3)
			.Select(player => player.Id)
			.ToHashSet();
		var victimId = players[4].Id;
		foreach (var player in players)
		{
			session.AssignRole(
				player.Id,
				player.Id == actorId
					? MainRoleType.Actor
					: werewolfIds.Contains(player.Id)
						? MainRoleType.SimpleWerewolf
						: MainRoleType.SimpleVillager);
		}

		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			actorId,
			actorCard.Card.Id).Should().BeTrue();
		session.IdentifyRole([actorId], MainRoleType.Actor);
		ActorBorrowedRolePowerActivation? activation = null;
		if (scenario != BorrowedBearTamerScenario.NoActivation)
		{
			session.TrySpendActorSetupCard(
				actorId,
				BearTamerCard.Id,
				out activation).Should().BeTrue();
		}

		var knownAgentIds = scenario ==
			BorrowedBearTamerScenario.FalseNeighbor
			? new HashSet<Guid> { players[2].Id }
			: werewolfIds;
		SeedCompleteFactionFacts(
			session,
			werewolfIds,
			knownAgentIds,
			scenario == BorrowedBearTamerScenario.IncompleteAgentFacts
				? victimId
				: null);
		if (scenario == BorrowedBearTamerScenario.DeadActor)
		{
			session.EliminatePlayer(
				actorId,
				EliminationReason.EventElimination);
		}

		session.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			victimId);
		session.TransitionMainPhase(GamePhase.Dawn);
		session = RehydrateAtPendingInstruction(
			session,
			start,
			availabilityPolicy);
		return new BorrowedBearTamerDawnFixture(
			session,
			start,
			admissions,
			actorId,
			activation?.ActivationId ?? Guid.Empty,
			victimId);
	}

	private static BorrowedKnightDawnFixture CreateActiveBorrowedKnightDawn(
		bool preserveGameBeyondDawn = false,
		BorrowedKnightScenario scenario = BorrowedKnightScenario.Positive)
	{
		var setup = new ActorSetupCards(
			version: 16,
			[KnightCard, BearTamerCard, SeerCard]);
		var playerNames = preserveGameBeyondDawn
			? new[]
			{
				GameStrings.ActorRoleName,
				"Clockwise Werewolf",
				"Werewolf B",
				"Villager A",
				"Villager B",
				"Villager C"
			}
			: new[]
			{
				GameStrings.ActorRoleName,
				"Clockwise Werewolf",
				"Werewolf B",
				"Villager A",
				"Villager B"
			};
		var roles = scenario ==
			BorrowedKnightScenario.SuccessfulTriggeringNightInfection
			? (preserveGameBeyondDawn
				? new[]
				{
					MainRoleType.Actor,
					MainRoleType.SimpleVillager,
					MainRoleType.AccursedWolfFather,
					MainRoleType.BigBadWolf,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager
				}
				: new[]
				{
					MainRoleType.Actor,
					MainRoleType.SimpleVillager,
					MainRoleType.AccursedWolfFather,
					MainRoleType.BigBadWolf,
					MainRoleType.SimpleVillager
				})
			: scenario == BorrowedKnightScenario.CascadeTransformedAgent
				? (preserveGameBeyondDawn
					? new[]
					{
						MainRoleType.Actor,
						MainRoleType.WildChild,
						MainRoleType.SimpleWerewolf,
						MainRoleType.SimpleWerewolf,
						MainRoleType.SimpleVillager,
						MainRoleType.SimpleVillager
					}
					: new[]
					{
						MainRoleType.Actor,
						MainRoleType.WildChild,
						MainRoleType.SimpleWerewolf,
						MainRoleType.SimpleWerewolf,
						MainRoleType.SimpleVillager
					})
			: scenario == BorrowedKnightScenario.TriggeringNightTemporaryAgent
				? (preserveGameBeyondDawn
					? new[]
					{
						MainRoleType.Actor,
						MainRoleType.SimpleVillager,
						MainRoleType.SimpleWerewolf,
						MainRoleType.SimpleWerewolf,
						MainRoleType.SimpleVillager,
						MainRoleType.SimpleVillager
					}
					: new[]
					{
						MainRoleType.Actor,
						MainRoleType.SimpleVillager,
						MainRoleType.SimpleWerewolf,
						MainRoleType.SimpleWerewolf,
						MainRoleType.SimpleVillager
					})
			: preserveGameBeyondDawn
			? new[]
			{
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			}
			: new[]
			{
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			};
		var config = new GameSessionConfig(
			playerNames.ToList(),
			roles.ToList(),
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		IRolePowerAvailabilityPolicy availabilityPolicy =
			scenario == BorrowedKnightScenario.AvailabilityDenied
				? new DenyKnightAvailabilityPolicy()
				: AllowAllRolePowerAvailabilityPolicy.Instance;
		var admissions = scenario == BorrowedKnightScenario.AvailabilityDenied
			? SupportedRoleCatalog.CreateAdmissions(
				new RolePowerAvailabilityGateway(
					new VillagerRolePowerSuppressionPolicy(
						availabilityPolicy)))
			: SupportedRoleCatalog.Admissions;
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var werewolfIds = players
			.Skip(scenario is
				BorrowedKnightScenario.SuccessfulTriggeringNightInfection or
				BorrowedKnightScenario.CascadeTransformedAgent or
				BorrowedKnightScenario.TriggeringNightTemporaryAgent
					? 2
					: 1)
			.Take(2)
			.Select(player => player.Id)
			.ToHashSet();
		for (var index = 0; index < players.Length; index++)
		{
			session.AssignRole(
				players[index].Id,
				roles[index]);
		}

		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		session.TryRecordPhysicalCharacterCardOwnership(
			session.RoleLockIn.Version,
			actorId,
			actorCard.Card.Id).Should().BeTrue();
		session.IdentifyRole([actorId], MainRoleType.Actor);
		session.TrySpendActorSetupCard(
			actorId,
			KnightCard.Id,
			out var activation).Should().BeTrue();
		activation.Should().NotBeNull();
		var omittedAgentPlayerId =
			scenario == BorrowedKnightScenario.MissingTriggeringAgentFacts
				? players[^1].Id
				: (Guid?)null;
		var knownAgentIds =
			scenario == BorrowedKnightScenario.NoEligibleSurvivingAgent
				? new HashSet<Guid>()
				: werewolfIds;
		SeedCompleteFactionFacts(
			session,
			werewolfIds,
			knownAgentIds,
			omittedAgentPlayerId);
		if (scenario == BorrowedKnightScenario.TriggeringNightTemporaryAgent)
		{
			CommitCurrentWerewolfAgentFacts(
				session,
				werewolfIds.Append(players[1].Id).ToHashSet());
			session.PerformNightAction(
				NightActionType.WerewolfVictimSelection,
				actorId);
		}
		else if (scenario == BorrowedKnightScenario.CascadeTransformedAgent)
		{
			var wildChildListenerId = ListenerIdentifier.Listener(
				MainRoleType.WildChild);
			var wildChild = session.GetOrCreateListener(
				wildChildListenerId,
				admissions.ListenerFactories[wildChildListenerId])
				.Should().BeOfType<WildChildRole>().Subject;
			EliminationCascadeRuntimeStore.Configure(
				session,
				[
					new EliminationCascadeReactionBinding(
						wildChild,
						EliminationCascadeReactionBoundary.PreReveal)
				]);
			session.PerformNightAction(
				NightActionType.WildChildModel,
				actorId);
			session.PerformNightAction(
				NightActionType.WerewolfVictimSelection,
				actorId);
		}
		else if (scenario ==
			BorrowedKnightScenario.SuccessfulTriggeringNightInfection)
		{
			session.PerformNightAction(
				NightActionType.WerewolfVictimSelection,
				players[1].Id);
			session.PerformNightAction(
				NightActionType.AccursedWolfFatherInfection,
				players[1].Id);
			session.PerformNightAction(
				NightActionType.BigBadWolfVictimSelection,
				actorId);
		}
		else if (scenario == BorrowedKnightScenario.NonWerewolfElimination)
		{
			session.EliminatePlayer(
				actorId,
				EliminationReason.EventElimination);
		}
		else
		{
			session.PerformNightAction(
				NightActionType.WerewolfVictimSelection,
				actorId);
		}
		session.TransitionMainPhase(GamePhase.Dawn);
		if (scenario == BorrowedKnightScenario.TriggeringNightTemporaryAgent)
		{
			CommitCurrentWerewolfAgentFacts(session, werewolfIds);
		}
		session = RehydrateAtPendingInstruction(
			session,
			start,
			availabilityPolicy);
		return new BorrowedKnightDawnFixture(
			session,
			start,
			admissions,
			actorId,
			players[1].Id,
			players[2].Id,
			omittedAgentPlayerId ?? Guid.Empty,
			activation!.ActivationId);
	}

	private static GameSession RehydrateAtPendingInstruction(
		GameSession session,
		ModeratorInstruction pendingInstruction,
		IRolePowerAvailabilityPolicy availabilityPolicy)
	{
		var serializedSession = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(pendingInstruction)
			.Serialize();
		var service = new GameService(availabilityPolicy);
		var gameId = service.RehydrateSession(serializedSession);
		return (GameSession)(service.GetGameStateView(gameId)
			?? throw new InvalidOperationException(
				"Rehydrated game session was not available."));
	}

	private static void SeedCompleteFactionFacts(
		GameSession session,
		IReadOnlySet<Guid> werewolfBeneficiaryIds,
		IReadOnlySet<Guid> werewolfAgentIds,
		Guid? omittedAgentPlayerId)
	{
		var players = session.GetPlayers().ToArray();
		FactionFactEffectiveBoundary? closureBoundary = null;
		session.CommitFactionFactBatch(context =>
		{
			var factBoundary = new FactionFactEffectiveBoundary(
				context.TurnNumber,
				context.CurrentPhase,
				session.GameHistoryLog.Count());
			closureBoundary = factBoundary;
			return new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ExplicitTransition,
					"test-actor-borrowed-bear-tamer-faction-state"),
				Facts = players.Select(player => FactionFact.Beneficiary(
						player.Id,
						werewolfBeneficiaryIds.Contains(player.Id)
							? Faction.Werewolf
							: Faction.Villager,
						factBoundary))
					.Concat(players
						.Where(player => player.Id != omittedAgentPlayerId)
						.Select(player => FactionFact.Agent(
							player.Id,
							Faction.Werewolf,
							werewolfAgentIds.Contains(player.Id)
								? FactionAgentKnowledge.KnownAgent
								: FactionAgentKnowledge.KnownNonAgent,
							factBoundary)))
					.ToImmutableArray()
			};
		});
		var closureResult = InitialBeneficiaryClosureRules.TryCommitCurrentSession(
				session,
				closureBoundary);
		closureResult.Should().Be(
			omittedAgentPlayerId.HasValue
				? InitialBeneficiaryClosureResult.Incomplete
				: InitialBeneficiaryClosureResult.Committed);
	}

	private static void CommitCurrentWerewolfAgentFacts(
		GameSession session,
		IReadOnlySet<Guid> currentAgentIds)
	{
		var players = session.GetPlayers().ToArray();
		session.CommitFactionFactBatch(context =>
		{
			var boundary = new FactionFactEffectiveBoundary(
				context.TurnNumber,
				context.CurrentPhase,
				session.GameHistoryLog.Count());
			return new FactionFactsCommittedLogEntry
			{
				Timestamp = context.Timestamp,
				TurnNumber = context.TurnNumber,
				CurrentPhase = context.CurrentPhase,
				Source = new FactionFactSource(
					FactionFactSourceKind.ExplicitTransition,
					"test-actor-borrowed-knight-current-agent-transition"),
				Facts = players.Select(player => FactionFact.Agent(
						player.Id,
						Faction.Werewolf,
						currentAgentIds.Contains(player.Id)
							? FactionAgentKnowledge.KnownAgent
							: FactionAgentKnowledge.KnownNonAgent,
						boundary))
					.ToImmutableArray()
			};
		});
	}

	private static void AssertNoBorrowedBearGrowlMutation(
		GameSession session)
	{
		session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry =>
				entry.Role == MainRoleType.BearTamer);
	}

	private static void AssertNoBorrowedKnightScheduleMutation(
		GameSession session)
	{
		session.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().BeEmpty();
		session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry =>
				entry.EffectType == StatusEffectTypes.RustySwordDisease);
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry =>
				entry.Role == MainRoleType.KnightWithRustySword);
	}

	private sealed record BorrowedBearTamerDawnFixture(
		GameSession Session,
		StartGameConfirmationInstruction Start,
		IRoleAdmissionSource Admissions,
		Guid ActorId,
		Guid ActivationId,
		Guid VictimId);

	private sealed record BorrowedKnightDawnFixture(
		GameSession Session,
		StartGameConfirmationInstruction Start,
		IRoleAdmissionSource Admissions,
		Guid ActorId,
		Guid TargetId,
		Guid OtherWerewolfId,
		Guid OmittedAgentPlayerId,
		Guid ActivationId);

	public enum BorrowedBearTamerScenario
	{
		Positive,
		IncompleteAgentFacts,
		FalseNeighbor,
		DeadActor,
		Unavailable,
		NoActivation
	}

	public enum BorrowedKnightScenario
	{
		Positive,
		SuccessfulTriggeringNightInfection,
		CascadeTransformedAgent,
		TriggeringNightTemporaryAgent,
		MissingTriggeringAgentFacts,
		AvailabilityDenied,
		NonWerewolfElimination,
		NoEligibleSurvivingAgent
	}

	public enum BorrowedKnightCommittedTargetMutation
	{
		TargetRoleChanged,
		GlobalSuppressionActivated
	}

	private enum HookDriverSubPhase
	{
		Active,
		Complete
	}

	private sealed class DenyBearTamerAvailabilityPolicy
		: IRolePowerAvailabilityPolicy
	{
		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt) =>
			attempt.SourceRole == MainRoleType.BearTamer
				? RolePowerAvailabilityResult.Denied
				: RolePowerAvailabilityResult.Allowed;
	}

	private sealed class DenyKnightAvailabilityPolicy
		: IRolePowerAvailabilityPolicy
	{
		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt) =>
			attempt.SourceRole == MainRoleType.KnightWithRustySword
				? RolePowerAvailabilityResult.Denied
				: RolePowerAvailabilityResult.Allowed;
	}

}
