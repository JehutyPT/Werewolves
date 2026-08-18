using FluentAssertions;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
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
		MainRoleType.StutteringJudge,
		MainRoleType.Hunter,
		MainRoleType.Elder,
		MainRoleType.Scapegoat,
		MainRoleType.VillageIdiot,
		MainRoleType.BearTamer,
		MainRoleType.KnightWithRustySword
	];

	private static readonly PhysicalCharacterCard[] SourceCards =
	[
		new(Guid.Parse("00000000-0000-0000-0000-000000000151"), MainRoleType.Seer),
		new(Guid.Parse("00000000-0000-0000-0000-000000000152"), MainRoleType.Cupid),
		new(Guid.Parse("00000000-0000-0000-0000-000000000153"), MainRoleType.Witch),
		new(Guid.Parse("00000000-0000-0000-0000-000000000154"), MainRoleType.LittleGirl),
		new(Guid.Parse("00000000-0000-0000-0000-000000000155"), MainRoleType.Defender),
		new(Guid.Parse("00000000-0000-0000-0000-000000000156"), MainRoleType.Fox),
		new(Guid.Parse("00000000-0000-0000-0000-000000000157"), MainRoleType.StutteringJudge),
		new(Guid.Parse("00000000-0000-0000-0000-000000000158"), MainRoleType.Hunter),
		new(Guid.Parse("00000000-0000-0000-0000-000000000159"), MainRoleType.Elder),
		new(Guid.Parse("00000000-0000-0000-0000-000000000160"), MainRoleType.Scapegoat),
		new(Guid.Parse("00000000-0000-0000-0000-000000000161"), MainRoleType.VillageIdiot),
		new(Guid.Parse("00000000-0000-0000-0000-000000000162"), MainRoleType.BearTamer),
		new(Guid.Parse("00000000-0000-0000-0000-000000000163"), MainRoleType.KnightWithRustySword)
	];
	private static readonly RunSeedMaterial SeedMaterial = CreateSeedMaterial(
		BaselineRandomDecisionStrategy.Identity);
	private static readonly RunSeedMaterial ScapegoatSeedMaterial =
		CreateSeedMaterial(
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity);

	private static readonly HeadlessResponsePolicy ExactActorPolicy = new(
		BaselineRandomDecisionStrategy.Identity,
		SourceRoles
			.Where(sourceRole => sourceRole != MainRoleType.Scapegoat)
			.SelectMany(ExpectedTrace)
			.Distinct());
	private static readonly HeadlessResponsePolicy ExactScapegoatPolicy = new(
		BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
		ExpectedTrace(MainRoleType.Scapegoat));

	[Fact]
	public void BaselineRandom_TestOwnedActorPolicyAnswersEveryEmittedBorrowedSourceSemantic()
	{
		var actualTraces = SourceRoles.ToDictionary(
			sourceRole => sourceRole,
			sourceRole => TraceSource(
				sourceRole,
				sourceRole == MainRoleType.Scapegoat
					? ExactScapegoatPolicy
					: ExactActorPolicy));

		foreach (var sourceRole in SourceRoles)
		{
			actualTraces[sourceRole].Should().Equal(ExpectedTrace(sourceRole));
		}

		actualTraces.Values.SelectMany(trace => trace).Distinct().Should()
			.BeEquivalentTo(
				ExactActorPolicy.AdmittedSemantics.Concat(
					ExactScapegoatPolicy.AdmittedSemantics));
	}

	[Theory]
	[InlineData(MainRoleType.Seer, 7L)]
	[InlineData(MainRoleType.Cupid, 1L)]
	[InlineData(MainRoleType.Witch, 0L)]
	[InlineData(MainRoleType.LittleGirl, 1L)]
	[InlineData(MainRoleType.Defender, 0L)]
	[InlineData(MainRoleType.Fox, 4L)]
	[InlineData(MainRoleType.StutteringJudge, 0L)]
	[InlineData(MainRoleType.Hunter, 171L)]
	[InlineData(MainRoleType.Elder, 6L)]
	[InlineData(MainRoleType.Scapegoat, 14L)]
	[InlineData(MainRoleType.VillageIdiot, 46L)]
	[InlineData(MainRoleType.BearTamer, 90L)]
	[InlineData(MainRoleType.KnightWithRustySword, 24L)]
	public void ProductionSafetyDriver_ExactThreeSetup_GenuinelySelectsAndExecutesEveryActorSource(
		MainRoleType sourceRole,
		long runNumber)
	{
		var setupRoles = CreateProductionActorSourceSetup(sourceRole);
		var execution = ExecuteDirectProductionActorTrace(
			setupRoles,
			runNumber);

		setupRoles.Should().HaveCount(ActorSetupCards.RequiredCount);
		setupRoles.Should().OnlyHaveUniqueItems();
		execution.Run.Should().BeOfType<CompletedSimulationRun>();
		execution.Trace.UsesProductionBaseline.Should().BeTrue();
		execution.Trace.SelectedSources.Should().StartWith(sourceRole);
		execution.Trace.ActivatedSources.Should().Contain(sourceRole);
		HasProductionSourceEvidence(execution, sourceRole).Should().BeTrue(
			"the production trace must contain borrowed {0}'s native source anchor",
			sourceRole);
		execution.Trace.Observations.Should().Contain(observation =>
			observation.Phase == GamePhase.Night &&
			observation.TurnNumber > 1,
			"the completed production schedule must continue into a later Night");
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
		var trace = sourceRole switch
		{
			MainRoleType.Hunter => TraceHunterFinalShot(fixture),
			MainRoleType.Elder => TraceElder(fixture),
			MainRoleType.Scapegoat => TraceScapegoat(fixture),
			MainRoleType.VillageIdiot => TraceVillageIdiot(fixture),
			MainRoleType.BearTamer => TraceBearTamer(fixture),
			MainRoleType.KnightWithRustySword => TraceKnight(fixture),
			_ => TraceNightSource(fixture)
		};
		if (sourceRole == MainRoleType.StutteringJudge)
		{
			trace.AddRange(TraceStutteringJudgeDay(fixture));
		}
		if (sourceRole == MainRoleType.Hunter)
		{
			AssertHunterBorrowedAttemptIdentity(fixture);
		}
		if (sourceRole == MainRoleType.Elder)
		{
			AssertElderBorrowedAttemptIdentities(fixture);
		}
		if (sourceRole == MainRoleType.Scapegoat)
		{
			AssertScapegoatBorrowedAttemptIdentity(fixture);
		}
		if (sourceRole == MainRoleType.VillageIdiot)
		{
			AssertVillageIdiotBorrowedAttemptIdentity(fixture);
		}
		if (sourceRole == MainRoleType.BearTamer)
		{
			AssertBearTamerBorrowedAttemptIdentity(fixture);
		}
		if (sourceRole == MainRoleType.KnightWithRustySword)
		{
			AssertKnightBorrowedAttemptIdentity(fixture);
		}

		fixture.Session.GetPlayerState(fixture.ActorId).CurrentRole.Should().Be(
			MainRoleType.Actor);
		if (sourceRole == MainRoleType.KnightWithRustySword)
		{
			fixture.Session.GetModeratorActiveActorBorrowedRolePowerActivation()
				.Should().BeNull();
		}
		else
		{
			fixture.Session.GetModeratorActiveActorBorrowedRolePowerActivation()!
				.SourceRole.Should().Be(sourceRole);
		}
		fixture.Session.GetFactionBeneficiaryKnowledge(fixture.ActorId).Should()
			.Be(beneficiaryBefore);
		return trace;
	}

	private static List<ModeratorInstructionSemantic> TraceHunterFinalShot(
		ActorFixture fixture)
	{
		fixture.Session.TransitionMainPhase(GamePhase.Day);
		SeedKnownFactionFacts(
			fixture.Session,
			fixture.Session.GetPlayers().Skip(1).First().Id);
		var service = fixture.RestoreWithPolicyAt(fixture.Start);
		var debate = service.ProcessInstruction(
				fixture.Session.Id,
				fixture.Start.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = service.ProcessInstruction(
				fixture.Session.Id,
				debate.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var actorReveal = service.ProcessInstruction(
				fixture.Session.Id,
				vote.CreateResponse([fixture.ActorId])).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		actorReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDayVoteTargetRole);
		var elimination = service.ProcessInstruction(
				fixture.Session.Id,
				actorReveal.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		elimination.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceDayElimination);
		var finalShot = service.ProcessInstruction(
				fixture.Session.Id,
				elimination.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		finalShot.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget);

		var finalShotResponse = CreateCheckedResponse(
			fixture.Policy,
			fixture.Strategy,
			finalShot,
			fixture.Session);
		var targetReveal = service.ProcessInstruction(
				fixture.Session.Id,
				finalShotResponse).ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>().Subject;
		targetReveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignEliminationCascadeRoles);
		targetReveal.PlayersForAssignment.Should().Equal(
			finalShotResponse.SelectedPlayerIds!);
		var continuation = service.ProcessInstruction(
				fixture.Session.Id,
				targetReveal.CreateResponse(
					targetReveal.PlayersForAssignment.ToDictionary(
						playerId => playerId,
						playerId => fixture.Session
							.GetPlayerState(playerId)
							.CurrentRole!.Value))).ModeratorInstruction;
		continuation.Should().NotBeNull();
		return [finalShot.Semantic];
	}

	private static List<ModeratorInstructionSemantic> TraceElder(
		ActorFixture fixture)
	{
		fixture.Session.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			fixture.ActorId);
		fixture.Session.TransitionMainPhase(GamePhase.Dawn);
		NightInteractionResolver.ResolveNightPhase(fixture.Session);
		fixture.Session.GetPlayerState(fixture.ActorId).Health.Should().Be(
			PlayerHealth.Alive);
		fixture.Session.GetActorBorrowedElderResistanceCommits().Should()
			.ContainSingle(commit =>
				commit.PowerIdentity.ActingPlayerId == fixture.ActorId &&
				commit.PowerIdentity.PowerInstanceId == fixture.ActivationId &&
				commit.PowerIdentity.PowerInstanceOrigin ==
					RolePowerInstanceOrigin.Borrowed);

		fixture.Session.TransitionMainPhase(GamePhase.Day);
		fixture.Session.PerformDayVote(fixture.ActorId);
		fixture.Session.RevealRoles(
			new Dictionary<Guid, MainRoleType>
			{
				[fixture.ActorId] = MainRoleType.Actor
			});
		var currentVote = GameSessionQueries.GetCurrentDayVoteOutcome(
			fixture.Session);
		currentVote.Should().NotBeNull();
		var vote = currentVote!.Value;
		var scopeId =
			$"Day:{fixture.Session.TurnNumber}:Vote:{vote.VoteOrdinal}";
		EliminationCascadeRuntimeStore.Configure(fixture.Session, []);
		var cascade = EliminationCascadeStage.CascadeStage(
			ActorSemanticCascadeStage.ElderVillageVote,
			_ => new EliminationCascadeSeed(
				scopeId,
				vote.LogIndex,
				[
					new EliminationRequest(
						fixture.ActorId,
						EliminationReason.DayVote)
				]),
			ModeratorInstructionSemantic.AssignDayVoteTargetRole);
		var cascadeComplete = cascade.Execute(
				fixture.Session,
				fixture.Start.CreateResponse())
			.Should().BeOfType<StayInSubPhaseHandlerResult>().Subject;
		cascadeComplete.StageComplete.Should().BeTrue();
		cascadeComplete.ModeratorInstruction.Should().BeNull();
		fixture.Session.GetPlayerState(fixture.ActorId).Health.Should().Be(
			PlayerHealth.Dead);

		var suppression = Advance(
				fixture.Listener,
				fixture.Session,
				fixture.Start.CreateResponse())
			.Instruction.Should().BeOfType<ConfirmationInstruction>().Subject;
		suppression.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillagerRolePowerSuppression);
		Advance(
				fixture.Listener,
				fixture.Session,
				CreateCheckedResponse(
					fixture.Policy,
					fixture.Strategy,
					suppression,
					fixture.Session))
			.Outcome.Should().Be(HookListenerOutcome.Complete);
		return [suppression.Semantic];
	}

	private static List<ModeratorInstructionSemantic> TraceScapegoat(
		ActorFixture fixture)
	{
		fixture.Session.TransitionMainPhase(GamePhase.Day);
		fixture.RestoreAt(fixture.Start);
		var debate = GameFlowManager.HandleInput(
				fixture.Session,
				fixture.Start.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		debate.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		var vote = GameFlowManager.HandleInput(
				fixture.Session,
				debate.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		vote.Semantic.Should().Be(ModeratorInstructionSemantic.RecordDayVote);

		var reveal = GameFlowManager.HandleInput(
				fixture.Session,
				vote.CreateResponse([]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		reveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.RevealScapegoatForTie);
		var selection = GameFlowManager.HandleInput(
				fixture.Session,
				CreateCheckedResponse(
					fixture.Policy,
					fixture.Strategy,
					reveal,
					fixture.Session),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		selection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectScapegoatPermittedVoters);
		var announcement = GameFlowManager.HandleInput(
				fixture.Session,
				CreateCheckedResponse(
					fixture.Policy,
					fixture.Strategy,
					selection,
					fixture.Session),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		announcement.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters);
		var continuation = GameFlowManager.HandleInput(
				fixture.Session,
				CreateCheckedResponse(
					fixture.Policy,
					fixture.Strategy,
					announcement,
					fixture.Session),
				SupportedRoleCatalog.Admissions).ModeratorInstruction;
		continuation.Should().NotBeNull();
		new[]
		{
			debate.Semantic,
			vote.Semantic,
			reveal.Semantic,
			selection.Semantic,
			announcement.Semantic,
			continuation!.Semantic
		}.Should().NotContain(
			ModeratorInstructionSemantic.ObserveScapegoatHolderForTie);
		return
		[
			reveal.Semantic,
			selection.Semantic,
			announcement.Semantic
		];
	}

	private static List<ModeratorInstructionSemantic> TraceVillageIdiot(
		ActorFixture fixture)
	{
		fixture.Session.TransitionMainPhase(GamePhase.Day);
		fixture.RestoreAt(fixture.Start);
		var debate = GameFlowManager.HandleInput(
				fixture.Session,
				fixture.Start.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		debate.Semantic.Should().Be(
			ModeratorInstructionSemantic.StartDayDebate);
		var vote = GameFlowManager.HandleInput(
				fixture.Session,
				debate.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		vote.Semantic.Should().Be(ModeratorInstructionSemantic.RecordDayVote);
		var reveal = GameFlowManager.HandleInput(
				fixture.Session,
				vote.CreateResponse([fixture.ActorId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		reveal.Semantic.Should().Be(
			ModeratorInstructionSemantic.AssignDayVoteTargetRole);

		var pardon = GameFlowManager.HandleInput(
				fixture.Session,
				reveal.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		pardon.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon);
		fixture.Session.GetPlayerState(fixture.ActorId).Health.Should().Be(
			PlayerHealth.Alive);
		fixture.Session.GetPlayerState(fixture.ActorId).DurableVotingPower
			.Should().Be(0);
		var continuation = GameFlowManager.HandleInput(
				fixture.Session,
				CreateCheckedResponse(
					fixture.Policy,
					fixture.Strategy,
					pardon,
					fixture.Session),
				SupportedRoleCatalog.Admissions).ModeratorInstruction;
		continuation.Should().NotBeNull();
		return [pardon.Semantic];
	}

	private static List<ModeratorInstructionSemantic> TraceBearTamer(
		ActorFixture fixture)
	{
		var sourceInput = fixture.SourceOpeningResponse;
		_ = Advance(
			fixture.Listener,
			fixture.Session,
			sourceInput);
		fixture.Session.GetPlayers().Should().OnlyContain(player =>
			fixture.Session.GetFactionAgentKnowledge(
				player.Id,
				Faction.Werewolf) != FactionAgentKnowledge.Unknown);

		fixture.Session.TransitionMainPhase(GamePhase.Dawn);
		var growl = Advance(
				fixture.Listener,
				fixture.Session,
				sourceInput)
			.Instruction.Should().BeOfType<ConfirmationInstruction>().Subject;
		growl.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceBearTamerGrowl);
		Advance(
				fixture.Listener,
				fixture.Session,
				CreateCheckedResponse(
					fixture.Policy,
					fixture.Strategy,
					growl,
					fixture.Session))
			.Outcome.Should().Be(HookListenerOutcome.Complete);
		return [growl.Semantic];
	}

	private static List<ModeratorInstructionSemantic> TraceKnight(
		ActorFixture fixture)
	{
		var players = fixture.Session.GetPlayers().ToArray();
		players.Should().HaveCount(6);
		var targetId = players[1].Id;
		var otherWerewolfId = players[2].Id;

		fixture.Session.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().BeEmpty();

		fixture.Session.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			fixture.ActorId);
		fixture.Session.TransitionMainPhase(GamePhase.Dawn);
		fixture.RestoreAt(fixture.Start);
		AdvanceKnightDawnToDay(
			fixture,
			new Dictionary<Guid, MainRoleType>
			{
				[fixture.ActorId] = MainRoleType.Actor
			},
			tracedRustySwordTargetId: null).Should().BeEmpty();

		var scheduled = fixture.Session
			.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle().Subject;
		scheduled.TargetPlayerId.Should().Be(targetId);
		scheduled.TurnNumber.Should().Be(1);
		scheduled.CurrentPhase.Should().Be(GamePhase.Dawn);

		fixture.Session.TransitionMainPhase(GamePhase.Night);
		fixture.Session.TryExpireActorBorrowedRolePowerActivation().Should()
			.BeTrue();
		CommitKnightFollowingNightAgentFacts(
			fixture.Session,
			otherWerewolfId);
		fixture.Session.GetFactionAgentKnowledge(targetId, Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownNonAgent);
		fixture.Session.GetFactionAgentKnowledge(
				otherWerewolfId,
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		var followingNightVictimId = fixture.Session.GetPlayers()
			.Where(player =>
				player.Id != targetId &&
				player.State.Health == PlayerHealth.Alive &&
				fixture.Session.GetFactionAgentKnowledge(
					player.Id,
					Faction.Werewolf) ==
					FactionAgentKnowledge.KnownNonAgent)
			.Select(player => player.Id)
			.First();
		AdvanceNightHookToCompletion(fixture, followingNightVictimId);

		fixture.Session.TransitionMainPhase(GamePhase.Dawn);
		fixture.RestoreAtDefaultPolicy(fixture.Start);
		var trace = AdvanceKnightDawnToDay(
			fixture,
			new Dictionary<Guid, MainRoleType>
			{
				[targetId] = MainRoleType.SimpleWerewolf,
				[followingNightVictimId] = fixture.Session
					.GetPlayerState(followingNightVictimId)
					.CurrentRole!.Value
			},
			targetId);

		fixture.Session.GameHistoryLog.OfType<DawnVictimDeterminedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.TurnNumber == 2 &&
				entry.PlayerId == targetId &&
				entry.Reason == EliminationReason.RustySword);
		fixture.Session.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == targetId &&
				entry.Reason == EliminationReason.RustySword);
		fixture.Session.GetPlayerState(targetId).Health.Should().Be(
			PlayerHealth.Dead);
		fixture.Session.GetPlayerState(otherWerewolfId).Health.Should().Be(
			PlayerHealth.Alive);
		return trace;
	}

	private static void AdvanceNightHookToCompletion(
		ActorFixture fixture,
		Guid victimId)
	{
		var input = fixture.Start.CreateResponse();
		for (var step = 0; step < 20; step++)
		{
			var result = Advance(fixture.Listener, fixture.Session, input);
			if (result.Outcome == HookListenerOutcome.Complete)
			{
				return;
			}

			result.Instruction.Should().NotBeNull();
			var instruction = result.Instruction!;
			if (instruction is SelectPlayersInstruction
				{
					Semantic: ModeratorInstructionSemantic
						.ObserveWerewolfFactionAgentGroup
				} observation)
			{
				input = observation.CreateResponse(
					observation.SelectablePlayerIds
						.Where(playerId =>
							fixture.Session.GetFactionAgentKnowledge(
								playerId,
								Faction.Werewolf) ==
							FactionAgentKnowledge.KnownAgent)
						.ToHashSet());
				continue;
			}
			if (instruction is SelectPlayersInstruction
				{
					Semantic: ModeratorInstructionSemantic.SelectWerewolfVictim
				} victimSelection)
			{
				victimSelection.SelectablePlayerIds.Should().Contain(victimId);
				input = victimSelection.CreateResponse([victimId]);
				continue;
			}

			input = CreateCheckedResponse(
				fixture.Policy,
				fixture.Strategy,
				instruction,
				fixture.Session);
		}

		throw new InvalidOperationException(
			"The following Night hook did not complete for the borrowed Knight trace.");
	}

	private static List<ModeratorInstructionSemantic> AdvanceKnightDawnToDay(
		ActorFixture fixture,
		IReadOnlyDictionary<Guid, MainRoleType> roleAssignments,
		Guid? tracedRustySwordTargetId)
	{
		var trace = new List<ModeratorInstructionSemantic>();
		ModeratorInstruction? instruction = GameFlowManager.HandleInput(
			fixture.Session,
			fixture.Start.CreateResponse(),
			SupportedRoleCatalog.Admissions).ModeratorInstruction;
		for (var step = 0; step < 30; step++)
		{
			if (instruction is ConfirmationInstruction sourceAnnouncement &&
				IsBorrowedKnightRustySwordAnnouncement(
					fixture,
					sourceAnnouncement,
					tracedRustySwordTargetId))
			{
				var targetId = tracedRustySwordTargetId!.Value;
				sourceAnnouncement.AffectedPlayerIds.Should().Contain(targetId);
				sourceAnnouncement.PublicAnnouncement.Should().Contain(
					string.Format(
						GameStrings.RustySwordDiseaseEliminationAnnouncement,
						fixture.Session.GetPlayer(targetId).Name));
				trace.Add(sourceAnnouncement.Semantic);
				instruction = GameFlowManager.HandleInput(
					fixture.Session,
					CreateCheckedResponse(
						fixture.Policy,
						fixture.Strategy,
						sourceAnnouncement,
						fixture.Session),
					SupportedRoleCatalog.Admissions).ModeratorInstruction;
				continue;
			}

			if (fixture.Session.GetCurrentPhase() == GamePhase.Day)
			{
				return trace;
			}

			switch (instruction)
			{
				case FinishedGameConfirmationInstruction terminal:
					throw new InvalidOperationException(
						$"Knight Dawn reached terminal victory ({terminal.GameResult}).");
				case ConfirmationInstruction confirmation:
					instruction = GameFlowManager.HandleInput(
						fixture.Session,
						confirmation.CreateResponse(),
						SupportedRoleCatalog.Admissions).ModeratorInstruction;
					break;
				case AssignRolesInstruction assignment:
					instruction = GameFlowManager.HandleInput(
						fixture.Session,
						assignment.CreateResponse(
							assignment.PlayersForAssignment.ToDictionary(
								playerId => playerId,
								playerId => roleAssignments.TryGetValue(
									playerId,
									out var role)
										? role
										: throw new InvalidOperationException(
											$"Missing Knight Dawn Role assignment for {playerId}."))),
						SupportedRoleCatalog.Admissions).ModeratorInstruction;
					break;
				case null:
					throw new InvalidOperationException(
						"Knight Dawn did not expose a pending Moderator Instruction.");
				default:
					throw new InvalidOperationException(
						$"Unexpected Knight Dawn instruction {instruction.GetType().Name}.");
			}
		}

		throw new InvalidOperationException(
			"Knight Dawn did not transition to Day.");
	}

	private static bool IsBorrowedKnightRustySwordAnnouncement(
		ActorFixture fixture,
		ConfirmationInstruction instruction,
		Guid? tracedRustySwordTargetId)
	{
		if (tracedRustySwordTargetId is not { } targetId ||
			instruction.Semantic !=
			ModeratorInstructionSemantic.AnnounceDawnVictims ||
			!fixture.Session.GetActorBorrowedKnightRustySwordScheduleCommits()
				.Any(commit => commit.TargetPlayerId == targetId))
		{
			return false;
		}

		var expectedAnnouncement = string.Format(
			GameStrings.RustySwordDiseaseEliminationAnnouncement,
			fixture.Session.GetPlayer(targetId).Name);
		return instruction.AffectedPlayerIds?.Contains(targetId) == true &&
			instruction.PublicAnnouncement?.Contains(
				expectedAnnouncement,
				StringComparison.Ordinal) == true;
	}

	private static void AssertHunterBorrowedAttemptIdentity(
		ActorFixture fixture)
	{
		var attempts = fixture.RecordingPolicy.ObservedAttempts
			.Where(attempt => attempt.SourceRole == MainRoleType.Hunter)
			.ToArray();
		attempts.Should().NotBeEmpty();
		attempts.Should().OnlyContain(attempt =>
			attempt.ActingPlayer.Id == fixture.ActorId &&
			attempt.ActingPlayer.State.CurrentRole == MainRoleType.Actor &&
			attempt.SourcePower.Identifier.Value ==
				EliminationCascadeReactionIds.HunterFinalShot &&
			attempt.SourcePower.Category == RolePowerCategory.Reactive &&
			attempt.PowerInstance.Id == fixture.ActivationId &&
			attempt.PowerInstance.SourceRole == MainRoleType.Hunter &&
			attempt.PowerInstance.SourcePower == attempt.SourcePower &&
			attempt.PowerInstance.Origin == RolePowerInstanceOrigin.Borrowed &&
			attempt.OneUseResource == null);
	}

	private static void AssertElderBorrowedAttemptIdentities(
		ActorFixture fixture)
	{
		var attempts = fixture.RecordingPolicy.ObservedAttempts
			.Where(attempt => attempt.SourceRole == MainRoleType.Elder)
			.ToArray();
		attempts.Should().ContainSingle(attempt =>
			attempt.SourcePower.Identifier.Value ==
				"elder-werewolf-attack-resistance");
		attempts.Should().ContainSingle(attempt =>
			attempt.SourcePower.Identifier.Value ==
				"elder-village-vote-suppression");
		attempts.Should().OnlyContain(attempt =>
			attempt.ActingPlayer.Id == fixture.ActorId &&
			attempt.ActingPlayer.State.CurrentRole == MainRoleType.Actor &&
			attempt.SourcePower.Category == RolePowerCategory.Reactive &&
			attempt.PowerInstance.Id == fixture.ActivationId &&
			attempt.PowerInstance.SourceRole == MainRoleType.Elder &&
			attempt.PowerInstance.SourcePower == attempt.SourcePower &&
			attempt.PowerInstance.Origin == RolePowerInstanceOrigin.Borrowed &&
			attempt.OneUseResource == null);
	}

	private static void AssertScapegoatBorrowedAttemptIdentity(
		ActorFixture fixture)
	{
		var attempts = fixture.RecordingPolicy.ObservedAttempts
			.Where(attempt => attempt.SourceRole == MainRoleType.Scapegoat)
			.ToArray();
		attempts.Should().NotBeEmpty();
		attempts.Should().OnlyContain(attempt =>
			attempt.ActingPlayer.Id == fixture.ActorId &&
			attempt.ActingPlayer.State.CurrentRole == MainRoleType.Actor &&
			attempt.SourcePower.Identifier.Value ==
				"scapegoat-tie-replacement" &&
			attempt.SourcePower.Category == RolePowerCategory.Automatic &&
			attempt.PowerInstance.Id == fixture.ActivationId &&
			attempt.PowerInstance.SourceRole == MainRoleType.Scapegoat &&
			attempt.PowerInstance.SourcePower == attempt.SourcePower &&
			attempt.PowerInstance.Origin == RolePowerInstanceOrigin.Borrowed &&
			attempt.OneUseResource == null);
	}

	private static void AssertVillageIdiotBorrowedAttemptIdentity(
		ActorFixture fixture)
	{
		var attempts = fixture.RecordingPolicy.ObservedAttempts
			.Where(attempt => attempt.SourceRole == MainRoleType.VillageIdiot)
			.ToArray();
		attempts.Should().NotBeEmpty();
		attempts.Should().OnlyContain(attempt =>
			attempt.ActingPlayer.Id == fixture.ActorId &&
			attempt.ActingPlayer.State.CurrentRole == MainRoleType.Actor &&
			attempt.SourcePower.Identifier.Value ==
				ActorBorrowedVillageIdiotPardonCommit
					.ExpectedSourcePowerIdentifier &&
			attempt.SourcePower.Category == RolePowerCategory.Automatic &&
			attempt.PowerInstance.Id == fixture.ActivationId &&
			attempt.PowerInstance.SourceRole == MainRoleType.VillageIdiot &&
			attempt.PowerInstance.SourcePower == attempt.SourcePower &&
			attempt.PowerInstance.Origin == RolePowerInstanceOrigin.Borrowed &&
			attempt.OneUseResource != null &&
			attempt.OneUseResource.Id ==
				ActorBorrowedVillageIdiotPardonCommit.ExpectedResourceId &&
			attempt.OneUseResource.OwningPowerInstance == attempt.PowerInstance);
		fixture.Session.GetActorBorrowedVillageIdiotPardonCommits().Should()
			.ContainSingle(commit =>
				commit.PowerIdentity.ActingPlayerId == fixture.ActorId &&
				commit.PowerIdentity.PowerInstanceId == fixture.ActivationId &&
				commit.PowerIdentity.PowerInstanceOrigin ==
					RolePowerInstanceOrigin.Borrowed &&
				commit.SpentResourceIdentity.OneUseResourceId ==
					ActorBorrowedVillageIdiotPardonCommit.ExpectedResourceId);
	}

	private static void AssertBearTamerBorrowedAttemptIdentity(
		ActorFixture fixture)
	{
		var attempts = fixture.RecordingPolicy.ObservedAttempts
			.Where(attempt => attempt.SourceRole == MainRoleType.BearTamer)
			.ToArray();
		attempts.Should().NotBeEmpty();
		attempts.Should().OnlyContain(attempt =>
			attempt.ActingPlayer.Id == fixture.ActorId &&
			attempt.ActingPlayer.State.CurrentRole == MainRoleType.Actor &&
			attempt.SourcePower.Identifier.Value ==
				ActorBorrowedBearTamerGrowlCommit
					.ExpectedSourcePowerIdentifier &&
			attempt.SourcePower.Category == RolePowerCategory.Automatic &&
			attempt.PowerInstance.Id == fixture.ActivationId &&
			attempt.PowerInstance.SourceRole == MainRoleType.BearTamer &&
			attempt.PowerInstance.SourcePower == attempt.SourcePower &&
			attempt.PowerInstance.Origin == RolePowerInstanceOrigin.Borrowed &&
			attempt.OneUseResource == null);
		fixture.Session.GetActorBorrowedBearTamerGrowlCommits().Should()
			.ContainSingle(commit =>
				commit.PowerIdentity.ActingPlayerId == fixture.ActorId &&
				commit.PowerIdentity.PowerInstanceId == fixture.ActivationId &&
				commit.PowerIdentity.PowerInstanceOrigin ==
					RolePowerInstanceOrigin.Borrowed);
	}

	private static void AssertKnightBorrowedAttemptIdentity(
		ActorFixture fixture)
	{
		var attempts = fixture.RecordingPolicy.ObservedAttempts
			.Where(observed =>
				observed.SourceRole == MainRoleType.KnightWithRustySword)
			.ToArray();
		var attempt = attempts.Should().ContainSingle().Subject;
		attempt.ActingPlayer.Id.Should().Be(fixture.ActorId);
		attempt.ActingPlayer.State.CurrentRole.Should().Be(MainRoleType.Actor);
		attempt.SourcePower.Identifier.Value.Should().Be(
			ActorBorrowedKnightRustySwordScheduleCommit
				.ExpectedSourcePowerIdentifier);
		attempt.SourcePower.Category.Should().Be(RolePowerCategory.Automatic);
		attempt.PowerInstance.Id.Should().Be(fixture.ActivationId);
		attempt.PowerInstance.SourceRole.Should().Be(
			MainRoleType.KnightWithRustySword);
		attempt.PowerInstance.SourcePower.Should().Be(attempt.SourcePower);
		attempt.PowerInstance.Origin.Should().Be(
			RolePowerInstanceOrigin.Borrowed);
		attempt.OneUseResource.Should().BeNull();

		fixture.Session.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Should().ContainSingle(commit =>
				commit.PowerIdentity.ActingPlayerId == fixture.ActorId &&
				commit.PowerIdentity.SourceRole ==
					MainRoleType.KnightWithRustySword &&
				commit.PowerIdentity.SourcePowerIdentifier ==
					ActorBorrowedKnightRustySwordScheduleCommit
						.ExpectedSourcePowerIdentifier &&
				commit.PowerIdentity.PowerInstanceId == fixture.ActivationId &&
				commit.PowerIdentity.PowerInstanceOrigin ==
					RolePowerInstanceOrigin.Borrowed);
	}

	private static List<ModeratorInstructionSemantic> TraceNightSource(
		ActorFixture fixture)
	{
		var trace = new List<ModeratorInstructionSemantic>();
		var input = fixture.SourceOpeningResponse;
		for (var step = 0; step < 8; step++)
		{
			var result = Advance(fixture.Listener, fixture.Session, input);
			if (trace.LastOrDefault() ==
				ModeratorInstructionSemantic.PutRoleToSleep)
			{
				return trace;
			}
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
			if (fixture.SourceRole is MainRoleType.Seer or MainRoleType.Fox &&
				instruction.Semantic is
					ModeratorInstructionSemantic.SelectSeerTarget or
					ModeratorInstructionSemantic.SelectFoxCenter)
			{
				SeedKnownFactionFacts(
					fixture.Session,
					fixture.Session.GetPlayers().Skip(1).First().Id);
			}
			if (fixture.SourceRole == MainRoleType.Cupid &&
				instruction.Semantic ==
					ModeratorInstructionSemantic.SelectCupidLovers)
			{
				fixture.RestoreAt(instruction, cacheSourceListener: false);
			}
			input = CreateCheckedResponse(
				fixture.Policy,
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
		fixture.Session.TransitionMainPhase(GamePhase.Day);
		fixture.RestoreAt(fixture.Start);
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
			fixture.Policy,
			fixture.Strategy,
			conductVote,
			fixture.Session);
		var signal = GameFlowManager.HandleInput(
				fixture.Session,
				conductResponse,
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var signalResponse = CreateCheckedResponse(
			fixture.Policy,
			fixture.Strategy,
			signal,
			fixture.Session);
		var firstVote = GameFlowManager.HandleInput(
				fixture.Session,
				signalResponse,
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		_ = CreateCheckedResponse(
			fixture.Policy,
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
		HeadlessResponsePolicy policy,
		BaselineRandomDecisionStrategy strategy,
		ModeratorInstruction instruction,
		GameSession session)
	{
		policy.Admits(instruction.Semantic).Should().BeTrue();
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
		List<string> playerNames =
			sourceRole == MainRoleType.KnightWithRustySword
				?
				[
					GameStrings.ActorRoleName,
					"Clockwise Werewolf",
					"Werewolf B",
					"Villager 1",
					"Villager 2",
					"Villager 3"
				]
				:
				[
					GameStrings.ActorRoleName,
					"Werewolf",
					"Villager 1",
					"Villager 2",
					"Villager 3"
				];
		List<MainRoleType> roles =
			sourceRole == MainRoleType.KnightWithRustySword
				?
				[
					MainRoleType.Actor,
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager
				]
				:
				[
					MainRoleType.Actor,
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager
				];
		var config = new GameSessionConfig(
			playerNames,
			roles,
			setup);
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		if (sourceRole is MainRoleType.Hunter or MainRoleType.Elder or
			MainRoleType.Scapegoat or MainRoleType.VillageIdiot or
			MainRoleType.BearTamer or MainRoleType.KnightWithRustySword)
		{
			session.AssignRole(players[1].Id, MainRoleType.SimpleWerewolf);
			var firstVillagerIndex = 2;
			if (sourceRole == MainRoleType.KnightWithRustySword)
			{
				session.AssignRole(players[2].Id, MainRoleType.SimpleWerewolf);
				firstVillagerIndex = 3;
			}
			foreach (var player in players.Skip(firstVillagerIndex))
			{
				session.AssignRole(player.Id, MainRoleType.SimpleVillager);
			}
			var actorCard = session.GetModeratorPhysicalCharacterCards()
				.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
			session.TryRecordPhysicalCharacterCardOwnership(
				session.RoleLockIn.Version,
				actorId,
				actorCard.Card.Id).Should().BeTrue();
		}
		session.IdentifyRole([actorId], MainRoleType.Actor);
		SeedKnownActorBeneficiary(session, actorId);
		if (sourceRole is MainRoleType.Seer or MainRoleType.Fox or
			MainRoleType.Witch)
		{
			SeedKnownFactionFacts(session, werewolfId: null);
		}

		if (sourceRole == MainRoleType.StutteringJudge)
		{
			SeedKnownFactionFacts(session, players[1].Id);
			session.TransitionMainPhase(GamePhase.Day);
			session.TransitionMainPhase(GamePhase.Night);
			session.TurnNumber.Should().Be(2);
		}

		var opening = PerformSpendOpening(
			CreateActorRole(),
			session,
			start,
			sourceCard.Id);
		var activation = opening.Activation;
		activation.SourceRole.Should().Be(sourceRole);

		if (sourceRole is MainRoleType.Scapegoat or MainRoleType.VillageIdiot or
			MainRoleType.BearTamer)
		{
			SeedKnownFactionFacts(session, players[1].Id);
		}
		if (sourceRole == MainRoleType.KnightWithRustySword)
		{
			SeedKnightFactionFacts(
				session,
				players[1].Id,
				players[2].Id);
		}
		if (sourceRole == MainRoleType.Witch)
		{
			session.PerformNightAction(
				NightActionType.WerewolfVictimSelection,
				players[^1].Id);
		}

		var recordingPolicy = new RecordingPolicy();
		var gateway = new RolePowerAvailabilityGateway(recordingPolicy);
		var listener = CreateSourceListener(sourceRole, gateway);
		session.GetOrCreateListener(listener.Id, () => listener).Should()
			.BeSameAs(listener);
		return new ActorFixture(
			sourceRole,
			session,
			start,
			actorId,
			activation.ActivationId,
			activation,
			opening.SourceOpeningResponse,
			listener,
			policy,
			CreateStrategy(
				policy,
				sourceRole == MainRoleType.Scapegoat
					? ScapegoatSeedMaterial
					: SeedMaterial),
			recordingPolicy);
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
			MainRoleType.Hunter => new HunterRole(gateway),
			MainRoleType.Elder => new ElderRole(gateway),
			MainRoleType.Scapegoat => new ScapegoatRole(gateway),
			MainRoleType.VillageIdiot => new VillageIdiotRole(gateway),
			MainRoleType.BearTamer => new BearTamerRole(gateway),
			MainRoleType.KnightWithRustySword =>
				new KnightWithTheRustySwordRole(gateway),
			_ => throw new ArgumentOutOfRangeException(nameof(sourceRole))
		};

	private static BaselineRandomDecisionStrategy CreateStrategy(
		HeadlessResponsePolicy policy,
		RunSeedMaterial seedMaterial)
	{
		var random = new DeterministicRandomSource(seedMaterial);
		var startState = SimulationStartStateDeriver.Derive(
			seedMaterial,
			random);
		return new BaselineRandomDecisionStrategy(
			seedMaterial,
			startState,
			policy,
			random);
	}

	private static MainRoleType[] CreateProductionActorSourceSetup(
		MainRoleType sourceRole) =>
	[
		sourceRole,
		.. new[]
			{
				MainRoleType.Seer,
				MainRoleType.Cupid,
				MainRoleType.Defender,
				MainRoleType.Witch
			}
			.Where(companion => companion != sourceRole)
			.Take(2)
	];

	private static DirectProductionTraceExecution
		ExecuteDirectProductionActorTrace(
			IEnumerable<MainRoleType> sourceRoles,
			long runNumber)
	{
		var desiredSources = sourceRoles.ToArray();
		MainRoleType[] roles =
		[
			MainRoleType.Actor,
			MainRoleType.SimpleWerewolf,
			.. Enumerable.Repeat(MainRoleType.SimpleVillager, 16)
		];
		var scenario = new SimulationScenario(
			roles.Length,
			roles,
			new ActorSetupCards(desiredSources));
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var material = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber);
		var random = new DeterministicRandomSource(material);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening,
			random);
		var productionStrategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy,
			random);
		var trace = new RecordingDecisionStrategy(productionStrategy);
		var execution = new HeadlessGameDriver(trace).CompleteGameSession(
			startState,
			CancellationToken.None);
		var history = execution.Session.GameHistoryLog.ToArray();
		return new DirectProductionTraceExecution(
			trace,
			(GameSession)execution.Session,
			SimulationExecutor.AdaptTerminalEvidence(material, history));
	}

	private static bool HasProductionSourceEvidence(
		DirectProductionTraceExecution execution,
		MainRoleType sourceRole)
	{
		var observations = execution.Trace.Observations;

		return sourceRole switch
		{
			MainRoleType.Seer => HasSemantic(
				ModeratorInstructionSemantic.SelectSeerTarget),
			MainRoleType.Cupid => HasSemantic(
				ModeratorInstructionSemantic.SelectCupidLovers),
			MainRoleType.Witch => HasSemantic(
				ModeratorInstructionSemantic.SelectWitchHealingTarget),
			MainRoleType.LittleGirl => observations.Any(observation =>
				observation.Instruction.Semantic ==
					ModeratorInstructionSemantic.WakeRole &&
				observation.Instruction.PrivateInstruction?.Contains(
					GameStrings.LittleGirlOpeningGuidance,
					StringComparison.Ordinal) == true),
			MainRoleType.Defender => HasSemantic(
				ModeratorInstructionSemantic.SelectDefenderTarget),
			MainRoleType.Fox => HasSemantic(
				ModeratorInstructionSemantic.SelectFoxCenter),
			MainRoleType.StutteringJudge => HasSemantic(
				ModeratorInstructionSemantic.EstablishStutteringJudgeSignal),
			MainRoleType.Elder => execution.Session
				.GetActorBorrowedElderResistanceCommits().Any(),
			MainRoleType.Hunter => HasSemantic(
				ModeratorInstructionSemantic.SelectHunterFinalShotTarget),
			MainRoleType.VillageIdiot => HasSemantic(
				ModeratorInstructionSemantic.AnnounceVillageIdiotPardon),
			MainRoleType.BearTamer => HasSemantic(
				ModeratorInstructionSemantic.AnnounceBearTamerGrowl),
			MainRoleType.Scapegoat => execution.Session
				.GetActorBorrowedScapegoatTieReplacementCommits().Any(),
			MainRoleType.KnightWithRustySword => execution.Session
				.GetActorBorrowedKnightRustySwordScheduleCommits().Any(),
			_ => throw new ArgumentOutOfRangeException(nameof(sourceRole))
		};

		bool HasSemantic(ModeratorInstructionSemantic semantic) =>
			observations.Any(observation =>
				observation.Instruction.Semantic == semantic);
	}

	private static RunSeedMaterial CreateSeedMaterial(
		DecisionStrategyIdentity strategyIdentity)
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
				strategyIdentity.Equals(
						BaselineRandomDecisionStrategy.SafetyScreeningIdentity)
					? SimulatorCapability.SafetyScreening.Identity
					: SimulatorCapability.FullProbability.Identity),
			strategyIdentity,
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
			MainRoleType.Hunter =>
			[
				ModeratorInstructionSemantic.SelectHunterFinalShotTarget
			],
			MainRoleType.Elder =>
			[
				ModeratorInstructionSemantic
					.AnnounceVillagerRolePowerSuppression
			],
			MainRoleType.Scapegoat =>
			[
				ModeratorInstructionSemantic.RevealScapegoatForTie,
				ModeratorInstructionSemantic.SelectScapegoatPermittedVoters,
				ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters
			],
			MainRoleType.VillageIdiot =>
			[
				ModeratorInstructionSemantic.AnnounceVillageIdiotPardon
			],
			MainRoleType.BearTamer =>
			[
				ModeratorInstructionSemantic.AnnounceBearTamerGrowl
			],
			MainRoleType.KnightWithRustySword =>
			[
				ModeratorInstructionSemantic.AnnounceDawnVictims
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

	private static SpendOpening PerformSpendOpening(
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
		return new SpendOpening(activation, sleep.CreateResponse());
	}

	private static void SeedKnightFactionFacts(
		GameSession session,
		Guid clockwiseWerewolfId,
		Guid otherWerewolfId)
	{
		var knownAgentIds = new HashSet<Guid>
		{
			clockwiseWerewolfId,
			otherWerewolfId
		};
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
						knownAgentIds.Contains(player.Id)
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

	private static void CommitKnightFollowingNightAgentFacts(
		GameSession session,
		Guid currentWerewolfId)
	{
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
				Facts =
				[
					.. session.GetPlayers().Select(player => FactionFact.Agent(
						player.Id,
						Faction.Werewolf,
						player.Id == currentWerewolfId
							? FactionAgentKnowledge.KnownAgent
							: FactionAgentKnowledge.KnownNonAgent,
						boundary))
				]
			};
		});
	}

	private static void SeedKnownFactionFacts(
		GameSession session,
		Guid? werewolfId)
	{
		var hadCompleteAgentKnowledge = session.GetPlayers().All(player =>
			session.GetFactionAgentKnowledge(player.Id, Faction.Werewolf) !=
			FactionAgentKnowledge.Unknown);
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

		if (werewolfId is not null && !hadCompleteAgentKnowledge)
		{
			InitialBeneficiaryClosureRules.TryCommitCurrentSession(
					session,
					agentBoundary)
				.Should().Be(InitialBeneficiaryClosureResult.Committed);
		}
	}

	private static HookListenerActionResult Advance(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response)
	{
		session.GetOrCreateListener(listener.Id, () => listener);
		var currentPhase = session.GetCurrentPhase();
		var hook = currentPhase switch
		{
			GamePhase.Night => GameHook.NightMainActionLoop,
			GamePhase.Dawn => GameHook.DawnMainActionLoop,
			GamePhase.Day => GameHook.OnVoteConcluded,
			_ => throw new InvalidOperationException(
				$"No hook harness is defined for {currentPhase}.")
		};
		var nextPhase = currentPhase switch
		{
			GamePhase.Night => GamePhase.Dawn,
			GamePhase.Dawn => GamePhase.Day,
			GamePhase.Day => GamePhase.Night,
			_ => throw new InvalidOperationException(
				$"No hook harness transition is defined for {currentPhase}.")
		};
		var manager = new SubPhaseManager<HookHarnessSubPhase>(
			HookHarnessSubPhase.Active,
			[
				HookSubPhaseStage.HookStage(hook),
				NavigationSubPhaseStage.NavigationEndStageSilent(nextPhase)
			]);
		var result = manager.Execute(session, response).Should()
			.BeOfType<StayInSubPhaseHandlerResult>().Subject;
		if (!result.StageComplete)
		{
			result.ModeratorInstruction.Should().NotBeNull();
			return HookListenerActionResult.NeedInput(
				result.ModeratorInstruction!,
				HookHarnessListenerState.AwaitingInput);
		}

		result.ModeratorInstruction.Should().BeNull();
		return HookListenerActionResult.Complete(
			HookHarnessListenerState.Complete);
	}

	private sealed record SpendOpening(
		ActorBorrowedRolePowerActivation Activation,
		ModeratorResponse SourceOpeningResponse);

	private sealed class ActorFixture
	{
		internal ActorFixture(
			MainRoleType sourceRole,
			GameSession session,
			StartGameConfirmationInstruction start,
			Guid actorId,
			Guid activationId,
			ActorBorrowedRolePowerActivation activation,
			ModeratorResponse sourceOpeningResponse,
			IGameHookListener listener,
			HeadlessResponsePolicy policy,
			BaselineRandomDecisionStrategy strategy,
			RecordingPolicy recordingPolicy)
		{
			SourceRole = sourceRole;
			Session = session;
			Start = start;
			ActorId = actorId;
			ActivationId = activationId;
			Activation = activation;
			SourceOpeningResponse = sourceOpeningResponse;
			Listener = listener;
			Policy = policy;
			Strategy = strategy;
			RecordingPolicy = recordingPolicy;
		}

		internal MainRoleType SourceRole { get; }
		internal GameSession Session { get; private set; }
		internal StartGameConfirmationInstruction Start { get; }
		internal Guid ActorId { get; }
		internal Guid ActivationId { get; }
		internal ActorBorrowedRolePowerActivation Activation { get; }
		internal ModeratorResponse SourceOpeningResponse { get; }
		internal IGameHookListener Listener { get; }
		internal HeadlessResponsePolicy Policy { get; }
		internal BaselineRandomDecisionStrategy Strategy { get; }
		internal RecordingPolicy RecordingPolicy { get; }

		internal void RestoreAt(
			ModeratorInstruction instruction,
			bool cacheSourceListener = true)
		{
			var recovered = new GameSession(
				RecoveryPayloadTestDriver.Capture(Session)
				.RecordActorSetupCardSpend(Activation)
				.WithPendingInstruction(instruction)
				.Serialize());
			if (cacheSourceListener)
			{
				recovered.GetOrCreateListener(Listener.Id, () => Listener);
			}
			GameFlowManager.RestoreDurableContinuation(
				recovered,
				SupportedRoleCatalog.Admissions);
			Session = recovered;
		}

		internal GameService RestoreWithPolicyAt(
			ModeratorInstruction instruction)
		{
			var service = new GameService(RecordingPolicy);
			var gameId = service.RehydrateSession(
				RecoveryPayloadTestDriver.Capture(Session)
					.RecordActorSetupCardSpend(Activation)
					.WithPendingInstruction(instruction)
					.Serialize());
			Session = (GameSession)(service.GetGameStateView(gameId)
				?? throw new InvalidOperationException(
					"The Actor semantic trace recovery session was not registered."));
			return service;
		}

		internal void RestoreAtDefaultPolicy(
			ModeratorInstruction instruction)
		{
			Session = RecoveryPayloadTestDriver.Capture(Session)
				.WithPendingInstruction(instruction)
				.RehydrateGameSession();
		}
	}

	private sealed record ProductionInstructionObservation(
		ModeratorInstruction Instruction,
		int TurnNumber,
		GamePhase Phase);

	private sealed record DirectProductionTraceExecution(
		RecordingDecisionStrategy Trace,
		GameSession Session,
		SimulationRun Run);

	private sealed class RecordingDecisionStrategy : IModeratorDecisionStrategy
	{
		private readonly IModeratorDecisionStrategy _inner;

		internal RecordingDecisionStrategy(
			IModeratorDecisionStrategy inner)
		{
			ArgumentNullException.ThrowIfNull(inner);
			_inner = inner;
			UsesProductionBaseline = inner is BaselineRandomDecisionStrategy;
		}

		internal bool UsesProductionBaseline { get; }
		internal List<MainRoleType> SelectedSources { get; } = [];
		internal HashSet<MainRoleType> ActivatedSources { get; } = [];
		internal List<ProductionInstructionObservation> Observations { get; } = [];

		public ModeratorResponse CreateResponse(
			ModeratorInstruction instruction,
			IGameSession session)
		{
			if (session is GameSession concreteSession &&
			    concreteSession
				    .GetModeratorActiveActorBorrowedRolePowerActivation() is
				    { } activation)
			{
				ActivatedSources.Add(activation.SourceRole);
			}

			var productionResponse = _inner.CreateResponse(instruction, session);
			Observations.Add(new ProductionInstructionObservation(
				instruction,
				session.TurnNumber,
				session.GetCurrentPhase()));
			if (instruction is SelectOptionsInstruction
			    {
				    Semantic: ModeratorInstructionSemantic.ChooseActorSetupCard
			    } &&
			    productionResponse.SelectedOptionIds is [var selectedOptionId])
			{
				var selectedCardId = Guid.Parse(selectedOptionId);
				var selectedCard = session.GetModeratorActorSetupCards()
					.Cards.Single(card => card.Id == selectedCardId);
				SelectedSources.Add(selectedCard.PrintedRole);
			}

			return productionResponse;
		}
	}

	private sealed class RecordingPolicy : IRolePowerAvailabilityPolicy
	{
		internal List<RolePowerAttempt> ObservedAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			ObservedAttempts.Add(attempt);
			return RolePowerAvailabilityResult.Allowed;
		}
	}

	private enum ActorSemanticCascadeStage
	{
		HunterFinalShot,
		ElderVillageVote
	}

	private enum HookHarnessSubPhase
	{
		Active
	}

	private enum HookHarnessListenerState
	{
		AwaitingInput,
		Complete
	}
}
