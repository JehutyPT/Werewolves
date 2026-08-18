using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Client.Tests.Helpers;

public enum ActorBorrowedPowerFamily
{
	Seer,
	Cupid,
	Witch,
	LittleGirl,
	Defender,
	Fox,
	StutteringJudge,
	Hunter,
	Elder,
	Scapegoat,
	VillageIdiot,
	BearTamer,
	KnightWithRustySword
}

internal sealed record ActorBorrowedInstructionExpectation(
	ModeratorInstruction Instruction,
	IReadOnlyList<string> PublicFragments,
	IReadOnlyList<string> PrivateFragments,
	IReadOnlyList<string> ConfidentialPublicFragments,
	IReadOnlyList<string> ForbiddenMarkupFragments,
	IReadOnlyList<Guid>? AffectedPlayerIds,
	bool AllowsActorIdentityInPublic);

internal sealed record ActorBorrowedInstructionScenario(
	MainRoleType SourceRole,
	IReadOnlyList<ActorBorrowedInstructionExpectation> Expectations,
	IReadOnlyList<DashboardRosterEntry> Roster,
	IReadOnlyList<Guid> SelectedPlayerIds,
	string? SelectedOptionId,
	IReadOnlyList<Guid> SensitiveLineageIds)
{
	internal ModeratorInstruction Instruction => Expectations[0].Instruction;
}

internal static class ActorBorrowedInstructionFixture
{
	private sealed class TestExecutionCommitKey : IGameFlowManagerKey;
	private static readonly TestExecutionCommitKey ExecutionCommitKey = new();

	private static readonly PhysicalCharacterCard[] SourceCards =
	[
		Card("72000000-0000-0000-0000-000000000101", MainRoleType.Seer),
		Card("72000000-0000-0000-0000-000000000102", MainRoleType.Cupid),
		Card("72000000-0000-0000-0000-000000000103", MainRoleType.Witch),
		Card("72000000-0000-0000-0000-000000000104", MainRoleType.LittleGirl),
		Card("72000000-0000-0000-0000-000000000105", MainRoleType.Defender),
		Card("72000000-0000-0000-0000-000000000106", MainRoleType.Fox),
		Card("72000000-0000-0000-0000-000000000107", MainRoleType.StutteringJudge),
		Card("72000000-0000-0000-0000-000000000108", MainRoleType.Hunter),
		Card("72000000-0000-0000-0000-000000000109", MainRoleType.Elder),
		Card("72000000-0000-0000-0000-000000000110", MainRoleType.Scapegoat),
		Card("72000000-0000-0000-0000-000000000111", MainRoleType.VillageIdiot),
		Card("72000000-0000-0000-0000-000000000112", MainRoleType.BearTamer),
		Card(
			"72000000-0000-0000-0000-000000000113",
			MainRoleType.KnightWithRustySword)
	];

	private static readonly SubPhaseManager<ListenerTestSubPhase>
		NightActionLoop = new(
			ListenerTestSubPhase.ActionLoop,
			[
				HookSubPhaseStage.HookStage(GameHook.NightMainActionLoop),
				NavigationSubPhaseStage.NavigationEndStageSilent(GamePhase.Dawn)
			]);

	internal static ActorBorrowedInstructionScenario Create(
		ActorBorrowedPowerFamily family)
	{
		var sourceRole = SourceRole(family);
		var fixture = CreateCoreFixture(sourceRole);
		var output = CreateInstruction(family, fixture);
		var sensitiveLineageIds = fixture.SetupCards
			.Select(card => card.Id)
			.Append(fixture.Activation.ActivationId)
			.Concat(output.ResourceIds)
			.Distinct()
			.ToArray();

		return new ActorBorrowedInstructionScenario(
			sourceRole,
			CreateExpectations(family, fixture, output),
			DashboardRoster.FromSession(fixture.Session),
			output.SelectedPlayerIds,
			output.SelectedOptionId,
			sensitiveLineageIds);
	}

	private static CoreFixture CreateCoreFixture(MainRoleType sourceRole)
	{
		var sourceCard = SourceCards.Single(card =>
			card.PrintedRole == sourceRole);
		var setupCards = SourceCards
			.Where(card => card.PrintedRole != sourceRole)
			.Take(2)
			.Prepend(sourceCard)
			.ToArray();
		var roles = new List<MainRoleType>
		{
			MainRoleType.Actor,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		};
		if (sourceRole == MainRoleType.KnightWithRustySword)
		{
			roles[2] = MainRoleType.SimpleWerewolf;
		}
		var config = new GameSessionConfig(
			[
				ClientTestReferences.PlayerNames.Ana,
				ClientTestReferences.PlayerNames.Bruno,
				ClientTestReferences.PlayerNames.Catarina,
				ClientTestReferences.PlayerNames.Diana,
				ClientTestReferences.PlayerNames.Eduardo,
				ClientTestReferences.PlayerNames.Filipe
			],
			roles,
			new ActorSetupCards(version: 7, setupCards));
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();

		for (var index = 0; index < players.Length; index++)
		{
			session.AssignRole(players[index].Id, roles[index]);
		}

		var actorId = players[0].Id;
		var werewolfId = players[1].Id;
		var werewolfIds = players
			.Where((_, index) => roles[index] == MainRoleType.SimpleWerewolf)
			.Select(player => player.Id)
			.ToHashSet();
		var actorCard = session.GetModeratorPhysicalCharacterCards()
			.Single(card => card.Card.PrintedRole == MainRoleType.Actor);
		if (!session.TryRecordPhysicalCharacterCardOwnership(
				session.RoleLockIn.Version,
				actorId,
				actorCard.Card.Id))
		{
			throw new InvalidOperationException(
				"The Actor UI fixture could not record Actor's physical Character Card.");
		}
		session.IdentifyRole([actorId], MainRoleType.Actor);
		SeedKnownActorBeneficiary(session, actorId);
		if (sourceRole != MainRoleType.LittleGirl)
		{
			SeedKnownWerewolfAgentFacts(session, werewolfIds);
		}

		session.TransitionMainPhase(GamePhase.Day);
		session.TransitionMainPhase(GamePhase.Night);
		if (sourceRole == MainRoleType.Witch)
		{
			session.PerformNightAction(
				NightActionType.WerewolfVictimSelection,
				players[^1].Id);
		}

		var opening = PerformSpendOpening(
			new ActorRole(CreateActorAvailabilityGateway()),
			session,
			start,
			sourceCard.Id,
			retainExecution: sourceRole == MainRoleType.Fox);
		return new CoreFixture(
			session,
			start,
			players,
			actorId,
			werewolfId,
			setupCards,
			opening.Activation,
			opening.Sleep);
	}

	private static FamilyOutput CreateInstruction(
		ActorBorrowedPowerFamily family,
		CoreFixture fixture) => family switch
	{
		ActorBorrowedPowerFamily.Seer => CreateSeerInstruction(fixture),
		ActorBorrowedPowerFamily.Cupid => CreateCupidInstruction(fixture),
		ActorBorrowedPowerFamily.Witch => CreateWitchInstruction(fixture),
		ActorBorrowedPowerFamily.LittleGirl =>
			CreateLittleGirlInstruction(fixture),
		ActorBorrowedPowerFamily.Defender => CreateDefenderInstruction(fixture),
		ActorBorrowedPowerFamily.Fox => CreateFoxInstruction(fixture),
		ActorBorrowedPowerFamily.StutteringJudge =>
			CreateStutteringJudgeInstruction(fixture),
		ActorBorrowedPowerFamily.Hunter => CreateHunterInstruction(fixture),
		ActorBorrowedPowerFamily.Elder => CreateElderInstruction(fixture),
		ActorBorrowedPowerFamily.Scapegoat => CreateScapegoatInstructions(fixture),
		ActorBorrowedPowerFamily.VillageIdiot =>
			CreateVillageIdiotInstruction(fixture),
		ActorBorrowedPowerFamily.BearTamer => CreateBearTamerInstruction(fixture),
		ActorBorrowedPowerFamily.KnightWithRustySword =>
			CreateKnightInstruction(fixture),
		_ => throw new ArgumentOutOfRangeException(nameof(family))
	};

	private static FamilyOutput CreateSeerInstruction(CoreFixture fixture)
	{
		var listener = CreateSourceListener(MainRoleType.Seer);
		var wake = AdvanceToActorInstruction<ConfirmationInstruction>(
			listener,
			fixture,
			fixture.ActorSleep.CreateResponse(),
			"Seer wake",
			retainExecution: true);
		var selection = AdvanceToInstruction<SelectPlayersInstruction>(
			listener,
			fixture.Session,
			wake.CreateResponse(),
			"Seer target selection");
		var feedback = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			fixture.Session,
			selection.CreateResponse([fixture.WerewolfId]),
			"Seer result");
		return FamilyOutput.Passive(feedback);
	}

	private static FamilyOutput CreateCupidInstruction(CoreFixture fixture)
	{
		var listener = CreateSourceListener(MainRoleType.Cupid);
		var wake = AdvanceToActorInstruction<ConfirmationInstruction>(
			listener,
			fixture,
			fixture.ActorSleep.CreateResponse(),
			"Cupid wake");
		var selection = AdvanceToInstruction<SelectPlayersInstruction>(
			listener,
			fixture.Session,
			wake.CreateResponse(),
			"Cupid target selection");
		var selectedPlayerIds = fixture.Players
			.Select(player => player.Id)
			.Where(playerId =>
				playerId != fixture.ActorId &&
				selection.SelectablePlayerIds.Contains(playerId))
			.Take(2)
			.ToArray();
		if (selectedPlayerIds.Length != 2)
		{
			throw new InvalidOperationException(
				"The Actor UI fixture requires two selectable Cupid targets.");
		}

		return new FamilyOutput(
			selection,
			selectedPlayerIds,
			SelectedOptionId: null,
			ResourceIds: []);
	}

	private static FamilyOutput CreateWitchInstruction(CoreFixture fixture)
	{
		var listener = CreateSourceListener(MainRoleType.Witch);
		var wake = AdvanceToActorInstruction<ConfirmationInstruction>(
			listener,
			fixture,
			fixture.ActorSleep.CreateResponse(),
			"Witch wake",
			retainExecution: true);
		var selection = AdvanceToInstruction<SelectPlayersInstruction>(
			listener,
			fixture.Session,
			wake.CreateResponse(),
			"Witch healing selection");
		return new FamilyOutput(
			selection,
			SelectedPlayerIds: [],
			SelectedOptionId: null,
			ResourceIds:
			[
				WitchRole.HealingResourceId,
				WitchRole.PoisonResourceId
			]);
	}

	private static FamilyOutput CreateLittleGirlInstruction(CoreFixture fixture)
	{
		var instruction = AdvanceToInstruction<SelectPlayersInstruction>(
			CreateSourceListener(MainRoleType.LittleGirl),
			fixture.Session,
			fixture.ActorSleep.CreateResponse(),
			"Little Girl Werewolf group observation");
		return FamilyOutput.Passive(instruction);
	}

	private static FamilyOutput CreateDefenderInstruction(CoreFixture fixture)
	{
		var listener = CreateSourceListener(MainRoleType.Defender);
		var wake = AdvanceToActorInstruction<ConfirmationInstruction>(
			listener,
			fixture,
			fixture.ActorSleep.CreateResponse(),
			"Defender wake");
		var selection = AdvanceToInstruction<SelectPlayersInstruction>(
			listener,
			fixture.Session,
			wake.CreateResponse(),
			"Defender target selection");
		return FamilyOutput.Passive(selection);
	}

	private static FamilyOutput CreateFoxInstruction(CoreFixture fixture)
	{
		var listener = CreateSourceListener(MainRoleType.Fox);
		var wake = AdvanceToActorInstruction<ConfirmationInstruction>(
			listener,
			fixture,
			fixture.ActorSleep.CreateResponse(),
			"Fox wake",
			retainExecution: true);
		var selection = AdvanceToInstruction<SelectPlayersInstruction>(
			listener,
			fixture.Session,
			wake.CreateResponse(),
			"Fox center selection",
			retainExecution: true);
		var nonWerewolfNeighborhoodCenterId = fixture.Players
			.Select(player =>
			{
				var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
					fixture.Session,
					player.Id);
				return new
				{
					player.Id,
					Neighbors = neighbors
				};
			})
			.First(candidate =>
				selection.SelectablePlayerIds.Contains(candidate.Id) &&
				candidate.Id != fixture.WerewolfId &&
				candidate.Neighbors.Clockwise?.Id != fixture.WerewolfId &&
				candidate.Neighbors.Counterclockwise?.Id != fixture.WerewolfId)
			.Id;
		var feedback = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			fixture.Session,
			selection.CreateResponse([nonWerewolfNeighborhoodCenterId]),
			"Fox result",
			retainExecution: true);
		var commit = fixture.Session.GetActorBorrowedFoxCheckCommits().Single();
		if (commit.SpentResourceIdentity is not { } spentResource)
		{
			throw new InvalidOperationException(
				"The Actor UI fixture requires a negative borrowed Fox check.");
		}

		return new FamilyOutput(
			feedback,
			SelectedPlayerIds: [],
			SelectedOptionId: null,
			ResourceIds: [spentResource.OneUseResourceId]);
	}

	private static FamilyOutput CreateStutteringJudgeInstruction(
		CoreFixture fixture)
	{
		var listener = CreateSourceListener(MainRoleType.StutteringJudge);
		var wake = AdvanceToActorInstruction<ConfirmationInstruction>(
			listener,
			fixture,
			fixture.ActorSleep.CreateResponse(),
			"Stuttering Judge wake");
		var setup = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			fixture.Session,
			wake.CreateResponse(),
			"Stuttering Judge signal setup");
		var sleep = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			fixture.Session,
			setup.CreateResponse(),
			"Stuttering Judge sleep");
		var terminal = Advance(listener, fixture.Session, sleep.CreateResponse());
		if (terminal.Instruction is null &&
			terminal.Outcome != HookListenerOutcome.Complete)
		{
			throw new InvalidOperationException(
				"The Actor UI fixture did not finish Stuttering Judge setup.");
		}

		fixture.Session.TransitionMainPhase(GamePhase.Day);
		var debate = RequireInstruction<ConfirmationInstruction>(
			GameFlowManager.HandleInput(
				fixture.Session,
				fixture.Start.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction,
			"day debate");
		var conductVote = RequireInstruction<ConfirmationInstruction>(
			GameFlowManager.HandleInput(
				fixture.Session,
				debate.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction,
			"day vote conduct");
		var signal = RequireInstruction<SelectOptionsInstruction>(
			GameFlowManager.HandleInput(
				fixture.Session,
				conductVote.CreateResponse(),
				SupportedRoleCatalog.Admissions).ModeratorInstruction,
			"Stuttering Judge signal observation");

		_ = RequireInstruction<SelectPlayersInstruction>(
			GameFlowManager.HandleInput(
				fixture.Session,
				signal.CreateResponse(StutteringJudgeSignalOptionIds.Occurred),
				SupportedRoleCatalog.Admissions).ModeratorInstruction,
			"first day vote");
		var observation = fixture.Session
			.GetActorBorrowedStutteringJudgeSignalObservationCommits()
			.Single();
		if (observation.SpentResourceIdentity is not { } spentResource)
		{
			throw new InvalidOperationException(
				"The Actor UI fixture did not spend the borrowed Stuttering Judge resource.");
		}

		return new FamilyOutput(
			signal,
			SelectedPlayerIds: [],
			SelectedOptionId: StutteringJudgeSignalOptionIds.Occurred,
			ResourceIds: [spentResource.OneUseResourceId]);
	}

	private static FamilyOutput CreateHunterInstruction(CoreFixture fixture)
	{
		var hunter = (HunterRole)fixture.Session.GetOrCreateListener(
				ListenerIdentifier.Listener(MainRoleType.Hunter),
				() => new HunterRole(CreateActorAvailabilityGateway()));
		EliminationCascadeRuntimeStore.Configure(
			fixture.Session,
			[
				new(
					hunter,
					EliminationCascadeReactionBoundary.Interactive)
			]);
		var vote = BeginDayVote(fixture);
		var reveal = RequireSemantic<ConfirmationInstruction>(
			AdvanceMainFlow(
				fixture.Session,
				vote.CreateResponse([fixture.ActorId])),
			ModeratorInstructionSemantic.AssignDayVoteTargetRole,
			"borrowed Hunter Actor reveal");
		var elimination = RequireSemantic<ConfirmationInstruction>(
			AdvanceMainFlow(fixture.Session, reveal.CreateResponse()),
			ModeratorInstructionSemantic.AnnounceDayElimination,
			"borrowed Hunter Actor elimination");
		var finalShot = RequireSemantic<SelectPlayersInstruction>(
			AdvanceMainFlow(fixture.Session, elimination.CreateResponse()),
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget,
			"borrowed Hunter final shot");
		var selectedTargetId = finalShot.SelectablePlayerIds
			.First(playerId => playerId != fixture.WerewolfId);

		return new FamilyOutput(
			finalShot,
			SelectedPlayerIds: [selectedTargetId],
			SelectedOptionId: null,
			ResourceIds: []);
	}

	private static FamilyOutput CreateElderInstruction(CoreFixture fixture)
	{
		fixture.Session.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			fixture.ActorId);
		fixture.Session.TransitionMainPhase(GamePhase.Dawn);
		var debate = RequireSemantic<ConfirmationInstruction>(
			AdvanceMainFlow(fixture.Session, fixture.Start.CreateResponse()),
			ModeratorInstructionSemantic.StartDayDebate,
			"independent flow after silent borrowed Elder resistance");
		if (fixture.Session.GetActorBorrowedElderResistanceCommits().Count != 1)
		{
			throw new InvalidOperationException(
				"The Actor UI fixture did not commit exactly one silent borrowed Elder resistance.");
		}
		var vote = RequireSemantic<SelectPlayersInstruction>(
			AdvanceMainFlow(fixture.Session, debate.CreateResponse()),
			ModeratorInstructionSemantic.RecordDayVote,
			"borrowed Elder suppression vote");
		var reveal = RequireSemantic<ConfirmationInstruction>(
			AdvanceMainFlow(
				fixture.Session,
				vote.CreateResponse([fixture.ActorId])),
			ModeratorInstructionSemantic.AssignDayVoteTargetRole,
			"borrowed Elder Actor reveal");
		var elimination = RequireSemantic<ConfirmationInstruction>(
			AdvanceMainFlow(fixture.Session, reveal.CreateResponse()),
			ModeratorInstructionSemantic.AnnounceDayElimination,
			"borrowed Elder Actor elimination");
		var suppression = RequireSemantic<ConfirmationInstruction>(
			AdvanceMainFlow(fixture.Session, elimination.CreateResponse()),
			ModeratorInstructionSemantic.AnnounceVillagerRolePowerSuppression,
			"borrowed Elder suppression announcement");

		return FamilyOutput.Passive(suppression);
	}

	private static FamilyOutput CreateScapegoatInstructions(CoreFixture fixture)
	{
		var vote = BeginDayVote(fixture);
		var reveal = RequireSemantic<ConfirmationInstruction>(
			AdvanceMainFlow(fixture.Session, vote.CreateResponse([])),
			ModeratorInstructionSemantic.RevealScapegoatForTie,
			"borrowed Scapegoat Actor reveal");
		if (reveal.PublicAnnouncement?.Contains(
				GameStrings.ActorRoleName,
				StringComparison.CurrentCulture) != true ||
			reveal.PublicAnnouncement.Contains(
				GameStrings.ScapegoatRoleName,
				StringComparison.CurrentCulture))
		{
			throw new InvalidOperationException(
				"The borrowed Scapegoat reveal did not expose only Actor's actual Character Card.");
		}
		var selection = RequireSemantic<SelectPlayersInstruction>(
			AdvanceMainFlow(fixture.Session, reveal.CreateResponse()),
			ModeratorInstructionSemantic.SelectScapegoatPermittedVoters,
			"borrowed Scapegoat permitted-voter selection");
		var selectedPlayerId = fixture.Players
			.Select(player => player.Id)
			.First(playerId =>
				playerId != fixture.ActorId &&
				playerId != fixture.WerewolfId &&
				selection.SelectablePlayerIds.Contains(playerId));
		var announcement = RequireSemantic<ConfirmationInstruction>(
			AdvanceMainFlow(
				fixture.Session,
				selection.CreateResponse([selectedPlayerId])),
			ModeratorInstructionSemantic.AnnounceScapegoatPermittedVoters,
			"borrowed Scapegoat permitted-voter announcement");

		return new FamilyOutput(
			[selection, announcement],
			SelectedPlayerIds: [selectedPlayerId],
			SelectedOptionId: null,
			ResourceIds: []);
	}

	private static FamilyOutput CreateVillageIdiotInstruction(CoreFixture fixture)
	{
		_ = fixture.Session.GetOrCreateListener(
			ListenerIdentifier.Listener(MainRoleType.VillageIdiot),
			() => new VillageIdiotRole(CreateActorAvailabilityGateway()));
		var vote = BeginDayVote(fixture);
		var reveal = RequireSemantic<ConfirmationInstruction>(
			AdvanceMainFlow(
				fixture.Session,
				vote.CreateResponse([fixture.ActorId])),
			ModeratorInstructionSemantic.AssignDayVoteTargetRole,
			"borrowed Village Idiot Actor reveal");
		var pardon = RequireSemantic<ConfirmationInstruction>(
			AdvanceMainFlow(fixture.Session, reveal.CreateResponse()),
			ModeratorInstructionSemantic.AnnounceVillageIdiotPardon,
			"borrowed Village Idiot pardon");

		var spentResourceId = fixture.Session
			.GetActorBorrowedVillageIdiotPardonCommits()
			.Single().SpentResourceIdentity.OneUseResourceId;
		return new FamilyOutput(
			pardon,
			SelectedPlayerIds: [],
			SelectedOptionId: null,
			ResourceIds: [spentResourceId]);
	}

	private static FamilyOutput CreateBearTamerInstruction(CoreFixture fixture)
	{
		var victimId = fixture.Players[^1].Id;
		fixture.Session.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			victimId);
		fixture.Session.TransitionMainPhase(GamePhase.Dawn);
		var growl = AdvanceDawnToConfirmation(
			fixture,
			fixture.Start.CreateResponse(),
			ModeratorInstructionSemantic.AnnounceBearTamerGrowl,
			_ => MainRoleType.SimpleVillager,
			"borrowed Bear Tamer growl");
		return FamilyOutput.Passive(growl);
	}

	private static FamilyOutput CreateKnightInstruction(CoreFixture fixture)
	{
		var knight = (KnightWithTheRustySwordRole)fixture.Session.GetOrCreateListener(
				ListenerIdentifier.Listener(MainRoleType.KnightWithRustySword),
				() => new KnightWithTheRustySwordRole(
					CreateActorAvailabilityGateway()));
		EliminationCascadeRuntimeStore.Configure(
			fixture.Session,
			[
				new(
					knight,
					EliminationCascadeReactionBoundary.PreReveal)
			]);
		fixture.Session.PerformNightAction(
			NightActionType.WerewolfVictimSelection,
			fixture.ActorId);
		fixture.Session.TransitionMainPhase(GamePhase.Dawn);
		var debate = AdvanceDawnToConfirmation(
			fixture,
			fixture.Start.CreateResponse(),
			ModeratorInstructionSemantic.StartDayDebate,
			playerId => playerId == fixture.ActorId
				? MainRoleType.Actor
				: MainRoleType.SimpleVillager,
			"silent borrowed Knight schedule");
		var schedule = fixture.Session
			.GetActorBorrowedKnightRustySwordScheduleCommits()
			.Single();
		if (schedule.TargetPlayerId != fixture.WerewolfId)
		{
			throw new InvalidOperationException(
				"The Actor UI fixture did not silently schedule the first clockwise Werewolf Agent.");
		}

		UpdateKnownWerewolfAgentFacts(fixture.Session, new HashSet<Guid>());
		fixture.Session.TransitionMainPhase(GamePhase.Night);
		if (!fixture.Session.TryExpireActorBorrowedRolePowerActivation())
		{
			throw new InvalidOperationException(
				"The Actor UI fixture could not expire the borrowed Knight activation before its scheduled consequence.");
		}
		DriveNightHookToCompletion(
			knight,
			fixture,
			fixture.Start.CreateResponse());

		UpdateKnownWerewolfAgentFacts(
			fixture.Session,
			new HashSet<Guid> { fixture.WerewolfId });
		fixture.Session.TransitionMainPhase(GamePhase.Dawn);
		var announcement = AdvanceDawnToConfirmation(
			fixture,
			debate.CreateResponse(),
			ModeratorInstructionSemantic.AnnounceDawnVictims,
			playerId => playerId == fixture.WerewolfId
				? MainRoleType.SimpleWerewolf
				: MainRoleType.SimpleVillager,
			"due borrowed Knight disease announcement");
		return new FamilyOutput(
			announcement,
			SelectedPlayerIds: [],
			SelectedOptionId: null,
			ResourceIds: []);
	}

	private static IReadOnlyList<ActorBorrowedInstructionExpectation>
		CreateExpectations(
			ActorBorrowedPowerFamily family,
			CoreFixture fixture,
			FamilyOutput output)
	{
		if (family == ActorBorrowedPowerFamily.Scapegoat)
		{
			var selection = (SelectPlayersInstruction)output.Instructions[0];
			var announcement = (ConfirmationInstruction)output.Instructions[1];
			var candidatePlayerIds = fixture.Players
				.Select(player => player.Id)
				.Where(playerId => playerId != fixture.ActorId)
				.ToArray();
			var selectedNames = output.SelectedPlayerIds
				.Select(playerId => fixture.Session.GetPlayer(playerId).Name)
				.ToArray();
			return
			[
				new(
					selection,
					PublicFragments: [],
					PrivateFragments:
					[GameStrings.ScapegoatPermittedVotersSelectionInstruction],
					ConfidentialPublicFragments: candidatePlayerIds
						.Select(playerId => fixture.Session.GetPlayer(playerId).Name)
						.ToArray(),
					ForbiddenMarkupFragments: [],
					AffectedPlayerIds: candidatePlayerIds,
					AllowsActorIdentityInPublic: false),
				new(
					announcement,
					PublicFragments:
					[
						GameStrings.ScapegoatPermittedVotersAnnouncement.Format(
							string.Join(Environment.NewLine, selectedNames))
					],
					PrivateFragments: [],
					ConfidentialPublicFragments: candidatePlayerIds
						.Except(output.SelectedPlayerIds)
						.Select(playerId => fixture.Session.GetPlayer(playerId).Name)
						.ToArray(),
					ForbiddenMarkupFragments: [],
					AffectedPlayerIds: output.SelectedPlayerIds,
					AllowsActorIdentityInPublic: false)
			];
		}

		return
		[
			new(
				output.PrimaryInstruction,
				PublicFragments(family, fixture),
				PrivateFragments(family, fixture),
				ConfidentialPublicFragments(family, fixture, output),
				ForbiddenMarkupFragments(family, fixture),
				ExpectedAffectedPlayerIds(family, fixture),
				AllowsActorIdentityInPublic(family))
		];
	}

	private static IReadOnlyList<string> PublicFragments(
		ActorBorrowedPowerFamily family,
		CoreFixture fixture) => family switch
	{
		ActorBorrowedPowerFamily.LittleGirl =>
			[GameStrings.RoleHoldersWakeUp.Format(
				GameStrings.WerewolvesGroupName)],
		ActorBorrowedPowerFamily.Hunter =>
			[GameStrings.ActorBorrowedHunterFinalShotSelectionInstruction],
		ActorBorrowedPowerFamily.Elder =>
			[GameStrings.VillagerRolePowerSuppressionAnnouncement],
		ActorBorrowedPowerFamily.VillageIdiot =>
			[
				GameStrings.ActorBorrowedVillageIdiotPardonAnnouncement.Format(
					fixture.Session.GetPlayer(fixture.ActorId).Name)
			],
		ActorBorrowedPowerFamily.KnightWithRustySword =>
			[
				GameStrings.RustySwordDiseaseEliminationAnnouncement.Format(
					fixture.Session.GetPlayer(fixture.WerewolfId).Name)
			],
		ActorBorrowedPowerFamily.Seer or
		ActorBorrowedPowerFamily.Cupid or
		ActorBorrowedPowerFamily.Witch or
		ActorBorrowedPowerFamily.Defender or
		ActorBorrowedPowerFamily.Fox or
		ActorBorrowedPowerFamily.StutteringJudge or
		ActorBorrowedPowerFamily.Scapegoat or
		ActorBorrowedPowerFamily.BearTamer => [],
		_ => throw new ArgumentOutOfRangeException(nameof(family))
	};

	private static IReadOnlyList<string> PrivateFragments(
		ActorBorrowedPowerFamily family,
		CoreFixture fixture) => family switch
	{
		ActorBorrowedPowerFamily.Seer =>
			[GameStrings.SeerResultWerewolfTeam.Format(
				fixture.Session.GetPlayer(fixture.WerewolfId).Name)],
		ActorBorrowedPowerFamily.Cupid =>
			[GameStrings.CupidTargetSelectionInstruction],
		ActorBorrowedPowerFamily.Witch =>
			[GameStrings.WitchHealingSelectionInstruction.Format(
				fixture.Players[^1].Name)],
		ActorBorrowedPowerFamily.LittleGirl =>
			[
				GameStrings.WerewolfFactionAgentObservationPrompt,
				GameStrings.LittleGirlOpeningGuidance
			],
		ActorBorrowedPowerFamily.Defender =>
			[GameStrings.DefenderTargetSelectionInstruction],
		ActorBorrowedPowerFamily.Fox =>
			[GameStrings.FoxNegativeFeedbackInstruction],
		ActorBorrowedPowerFamily.StutteringJudge =>
			[GameStrings.StutteringJudgeSignalObservationInstruction],
		ActorBorrowedPowerFamily.BearTamer =>
			[GameStrings.BearTamerGrowlInstruction],
		ActorBorrowedPowerFamily.Hunter or
		ActorBorrowedPowerFamily.Elder or
		ActorBorrowedPowerFamily.Scapegoat or
		ActorBorrowedPowerFamily.VillageIdiot or
		ActorBorrowedPowerFamily.KnightWithRustySword => [],
		_ => throw new ArgumentOutOfRangeException(nameof(family))
	};

	private static IReadOnlyList<string> ConfidentialPublicFragments(
		ActorBorrowedPowerFamily family,
		CoreFixture fixture,
		FamilyOutput output) => family switch
	{
		ActorBorrowedPowerFamily.Seer =>
			[fixture.Session.GetPlayer(fixture.WerewolfId).Name],
		ActorBorrowedPowerFamily.Cupid => output.SelectedPlayerIds
			.Select(playerId => fixture.Session.GetPlayer(playerId).Name)
			.ToArray(),
		ActorBorrowedPowerFamily.Witch => [fixture.Players[^1].Name],
		ActorBorrowedPowerFamily.LittleGirl =>
			[fixture.Session.GetPlayer(fixture.WerewolfId).Name],
		ActorBorrowedPowerFamily.Defender => [fixture.Players[2].Name],
		ActorBorrowedPowerFamily.Fox =>
			[fixture.Session.GetPlayer(fixture.WerewolfId).Name],
		ActorBorrowedPowerFamily.StutteringJudge =>
			[
				GameStrings.StutteringJudgeSignalOccurredOption,
				GameStrings.StutteringJudgeSignalDidNotOccurOption
			],
		ActorBorrowedPowerFamily.Hunter =>
			((SelectPlayersInstruction)output.PrimaryInstruction)
				.SelectablePlayerIds
				.Select(playerId => fixture.Session.GetPlayer(playerId).Name)
				.ToArray(),
		ActorBorrowedPowerFamily.Elder or
		ActorBorrowedPowerFamily.Scapegoat or
		ActorBorrowedPowerFamily.VillageIdiot or
		ActorBorrowedPowerFamily.BearTamer or
		ActorBorrowedPowerFamily.KnightWithRustySword => [],
		_ => throw new ArgumentOutOfRangeException(nameof(family))
	};

	private static IReadOnlyList<string> ForbiddenMarkupFragments(
		ActorBorrowedPowerFamily family,
		CoreFixture fixture) => family switch
	{
		ActorBorrowedPowerFamily.Seer or
		ActorBorrowedPowerFamily.Cupid or
		ActorBorrowedPowerFamily.Witch or
		ActorBorrowedPowerFamily.LittleGirl or
		ActorBorrowedPowerFamily.Defender or
		ActorBorrowedPowerFamily.Fox or
		ActorBorrowedPowerFamily.StutteringJudge or
		ActorBorrowedPowerFamily.Hunter or
		ActorBorrowedPowerFamily.Elder or
		ActorBorrowedPowerFamily.Scapegoat or
		ActorBorrowedPowerFamily.VillageIdiot or
		ActorBorrowedPowerFamily.KnightWithRustySword => [],
		ActorBorrowedPowerFamily.BearTamer => fixture.Players
			.Select(player => player.Name)
			.ToArray(),
		_ => throw new ArgumentOutOfRangeException(nameof(family))
	};

	private static IReadOnlyList<Guid>? ExpectedAffectedPlayerIds(
		ActorBorrowedPowerFamily family,
		CoreFixture fixture) => family switch
	{
		ActorBorrowedPowerFamily.LittleGirl => null,
		ActorBorrowedPowerFamily.Elder or
		ActorBorrowedPowerFamily.BearTamer => null,
		ActorBorrowedPowerFamily.VillageIdiot => [fixture.ActorId],
		ActorBorrowedPowerFamily.KnightWithRustySword =>
			[fixture.WerewolfId],
		ActorBorrowedPowerFamily.Seer or
		ActorBorrowedPowerFamily.Cupid or
		ActorBorrowedPowerFamily.Witch or
		ActorBorrowedPowerFamily.Defender or
		ActorBorrowedPowerFamily.Fox or
		ActorBorrowedPowerFamily.StutteringJudge or
		ActorBorrowedPowerFamily.Hunter => [fixture.ActorId],
		_ => throw new ArgumentOutOfRangeException(nameof(family))
	};

	private static bool AllowsActorIdentityInPublic(
		ActorBorrowedPowerFamily family) => family switch
	{
		ActorBorrowedPowerFamily.Hunter => true,
		ActorBorrowedPowerFamily.Seer or
		ActorBorrowedPowerFamily.Cupid or
		ActorBorrowedPowerFamily.Witch or
		ActorBorrowedPowerFamily.LittleGirl or
		ActorBorrowedPowerFamily.Defender or
		ActorBorrowedPowerFamily.Fox or
		ActorBorrowedPowerFamily.StutteringJudge or
		ActorBorrowedPowerFamily.Elder or
		ActorBorrowedPowerFamily.Scapegoat or
		ActorBorrowedPowerFamily.VillageIdiot or
		ActorBorrowedPowerFamily.BearTamer or
		ActorBorrowedPowerFamily.KnightWithRustySword => false,
		_ => throw new ArgumentOutOfRangeException(nameof(family))
	};

	private static MainRoleType SourceRole(
		ActorBorrowedPowerFamily family) => family switch
	{
		ActorBorrowedPowerFamily.Seer => MainRoleType.Seer,
		ActorBorrowedPowerFamily.Cupid => MainRoleType.Cupid,
		ActorBorrowedPowerFamily.Witch => MainRoleType.Witch,
		ActorBorrowedPowerFamily.LittleGirl => MainRoleType.LittleGirl,
		ActorBorrowedPowerFamily.Defender => MainRoleType.Defender,
		ActorBorrowedPowerFamily.Fox => MainRoleType.Fox,
		ActorBorrowedPowerFamily.StutteringJudge => MainRoleType.StutteringJudge,
		ActorBorrowedPowerFamily.Hunter => MainRoleType.Hunter,
		ActorBorrowedPowerFamily.Elder => MainRoleType.Elder,
		ActorBorrowedPowerFamily.Scapegoat => MainRoleType.Scapegoat,
		ActorBorrowedPowerFamily.VillageIdiot => MainRoleType.VillageIdiot,
		ActorBorrowedPowerFamily.BearTamer => MainRoleType.BearTamer,
		ActorBorrowedPowerFamily.KnightWithRustySword =>
			MainRoleType.KnightWithRustySword,
		_ => throw new ArgumentOutOfRangeException(nameof(family))
	};

	private static IGameHookListener CreateSourceListener(MainRoleType sourceRole)
	{
		var gateway = new RolePowerAvailabilityGateway(
			AllowAllRolePowerAvailabilityPolicy.Instance);
		return sourceRole switch
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
	}

	private static RolePowerAvailabilityGateway CreateActorAvailabilityGateway() =>
		new(new VillagerRolePowerSuppressionPolicy(
			AllowAllRolePowerAvailabilityPolicy.Instance));

	private static SelectPlayersInstruction BeginDayVote(CoreFixture fixture)
	{
		fixture.Session.TransitionMainPhase(GamePhase.Day);
		var debate = RequireSemantic<ConfirmationInstruction>(
			AdvanceMainFlow(fixture.Session, fixture.Start.CreateResponse()),
			ModeratorInstructionSemantic.StartDayDebate,
			"day debate");
		return RequireSemantic<SelectPlayersInstruction>(
			AdvanceMainFlow(fixture.Session, debate.CreateResponse()),
			ModeratorInstructionSemantic.RecordDayVote,
			"day vote");
	}

	private static ModeratorInstruction AdvanceMainFlow(
		GameSession session,
		ModeratorResponse response) =>
		GameFlowManager.HandleInput(
			session,
			response,
			SupportedRoleCatalog.Admissions).ModeratorInstruction
		?? throw new InvalidOperationException(
			"The Actor UI fixture expected a main-flow instruction.");

	private static ConfirmationInstruction AdvanceDawnToConfirmation(
		CoreFixture fixture,
		ModeratorResponse initialResponse,
		ModeratorInstructionSemantic expectedSemantic,
		Func<Guid, MainRoleType> roleAssignment,
		string step)
	{
		var instruction = AdvanceMainFlow(fixture.Session, initialResponse);
		for (var attempt = 0; attempt < 30; attempt++)
		{
			if (instruction.Semantic == expectedSemantic)
			{
				return RequireInstruction<ConfirmationInstruction>(instruction, step);
			}

			instruction = instruction switch
			{
				FinishedGameConfirmationInstruction terminal =>
					throw new InvalidOperationException(
						$"The Actor UI fixture reached {terminal.Semantic} before {step}."),
				ConfirmationInstruction confirmation => AdvanceMainFlow(
					fixture.Session,
					confirmation.CreateResponse()),
				AssignRolesInstruction assignment => AdvanceMainFlow(
					fixture.Session,
					assignment.CreateResponse(
						assignment.PlayersForAssignment.ToDictionary(
							playerId => playerId,
							roleAssignment))),
				_ => throw new InvalidOperationException(
					$"The Actor UI fixture received {instruction.GetType().Name} before {step}.")
			};
		}

		throw new InvalidOperationException(
			$"The Actor UI fixture did not reach {step}.");
	}

	private static SpendOpeningResult PerformSpendOpening(
		IGameHookListener listener,
		GameSession session,
		StartGameConfirmationInstruction start,
		Guid selectedCardId,
		bool retainExecution = false)
	{
		var wake = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			session,
			start.CreateResponse(),
			"Actor wake",
			retainExecution);
		var choice = AdvanceToInstruction<SelectOptionsInstruction>(
			listener,
			session,
			wake.CreateResponse(),
			"Actor setup-card choice",
			retainExecution);
		var sleep = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			session,
			choice.CreateResponse(selectedCardId.ToString("D")),
			"Actor sleep",
			retainExecution);
		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()
			?? throw new InvalidOperationException(
				"The Actor setup-card choice did not establish an activation.");
		return new SpendOpeningResult(activation, sleep);
	}

	private static TInstruction AdvanceToActorInstruction<TInstruction>(
		IGameHookListener listener,
		CoreFixture fixture,
		ModeratorResponse response,
		string step,
		bool retainExecution = false)
		where TInstruction : ModeratorInstruction
	{
		for (var attempt = 0; attempt < 20; attempt++)
		{
			var instruction = Advance(
				listener,
				fixture.Session,
				response,
				retainExecution).Instruction
				?? throw new InvalidOperationException(
					$"The Actor UI fixture completed the Night hook before {step}.");
			if (instruction is TInstruction typed &&
				instruction.AffectedPlayerIds?.Contains(fixture.ActorId) == true)
			{
				return typed;
			}

			response = CreateInterveningNightResponse(instruction, fixture);
		}

		throw new InvalidOperationException(
			$"The Actor UI fixture did not reach {step}.");
	}

	private static ModeratorResponse CreateInterveningNightResponse(
		ModeratorInstruction instruction,
		CoreFixture fixture) => instruction switch
	{
		ConfirmationInstruction confirmation => confirmation.CreateResponse(),
		SelectPlayersInstruction
		{
			Semantic: ModeratorInstructionSemantic.SelectWerewolfVictim
		} selection => selection.CreateResponse(
			[
				selection.SelectablePlayerIds.Contains(fixture.Players[^1].Id)
					? fixture.Players[^1].Id
					: selection.SelectablePlayerIds.First()
			]),
		SelectPlayersInstruction
		{
			Semantic:
				ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup
		} observation => observation.CreateResponse([fixture.WerewolfId]),
		SelectOptionsInstruction options => options.CreateResponse(),
		_ => throw new InvalidOperationException(
			$"The Actor UI fixture cannot pass intervening Night instruction {instruction.Semantic}.")
	};

	private static void DriveNightHookToCompletion(
		IGameHookListener listener,
		CoreFixture fixture,
		ModeratorResponse response)
	{
		for (var attempt = 0; attempt < 30; attempt++)
		{
			var result = Advance(listener, fixture.Session, response);
			if (result.Outcome == HookListenerOutcome.Complete &&
				result.Instruction is null)
			{
				return;
			}

			response = CreateInterveningNightResponse(
				result.Instruction ?? throw new InvalidOperationException(
					"The Actor UI fixture paused the Night hook without an instruction."),
				fixture);
		}

		throw new InvalidOperationException(
			"The Actor UI fixture did not complete the Night hook.");
	}

	private static TInstruction AdvanceToInstruction<TInstruction>(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response,
		string step,
		bool retainExecution = false)
		where TInstruction : ModeratorInstruction =>
		RequireInstruction<TInstruction>(
			Advance(
				listener,
				session,
				response,
				retainExecution).Instruction,
			step);

	private static TInstruction RequireInstruction<TInstruction>(
		ModeratorInstruction? instruction,
		string step)
		where TInstruction : ModeratorInstruction =>
		instruction as TInstruction ?? throw new InvalidOperationException(
			$"The Actor UI fixture expected {typeof(TInstruction).Name} during {step}.");

	private static TInstruction RequireSemantic<TInstruction>(
		ModeratorInstruction? instruction,
		ModeratorInstructionSemantic semantic,
		string step)
		where TInstruction : ModeratorInstruction
	{
		var typed = RequireInstruction<TInstruction>(instruction, step);
		if (typed.Semantic != semantic)
		{
			throw new InvalidOperationException(
				$"The Actor UI fixture expected {semantic} during {step}, but received {typed.Semantic}.");
		}

		return typed;
	}

	private static ListenerAdvanceResult Advance(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response,
		bool retainExecution = false)
	{
		var consumedInstruction = retainExecution
			? session.Execution.PendingInstruction
				?? throw new InvalidOperationException(
					"The Actor borrowed Fox UI fixture requires one Pending Instruction.")
			: null;
		_ = session.GetOrCreateListener(listener.Id, () => listener);
		var result = NightActionLoop.Execute(session, response);
		if (retainExecution && result.ModeratorInstruction is { } nextInstruction)
		{
			var publicationResponse =
				response.InstructionId == consumedInstruction!.InstructionId
					? response
					: new ModeratorResponse
					{
						InstructionId = consumedInstruction.InstructionId,
						Type = response.Type,
						SelectedPlayerIds = response.SelectedPlayerIds,
						AssignedPlayerRoles = response.AssignedPlayerRoles,
						SelectedOptionIds = response.SelectedOptionIds
					};
			session.CommitExecution(
				ExecutionCommitKey,
				ExecutionCommit.RetainRecoveryBoundary(
					session.Execution,
					consumedInstruction!,
					publicationResponse,
					nextInstruction));
		}

		return new ListenerAdvanceResult(
			result is StayInSubPhaseHandlerResult { StageComplete: false }
				? HookListenerOutcome.NeedInput
				: HookListenerOutcome.Complete,
			result.ModeratorInstruction);
	}

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
					"actor-borrowed-ui-fixture-beneficiary"),
				Facts =
				[
					FactionFact.Beneficiary(
						actorId,
						Faction.Villager,
						boundary)
				]
			});
	}

	private static void SeedKnownWerewolfAgentFacts(
		GameSession session,
		IReadOnlySet<Guid> werewolfIds)
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
					FactionFactSourceKind.ScheduledObservation,
					FactionFactSource
						.WerewolfFactionAgentGroupObservationIdentifier),
				Facts =
				[
					.. session.GetPlayers().Select(player => FactionFact.Agent(
						player.Id,
						Faction.Werewolf,
						werewolfIds.Contains(player.Id)
							? FactionAgentKnowledge.KnownAgent
							: FactionAgentKnowledge.KnownNonAgent,
						boundary))
				]
			});
		if (InitialBeneficiaryClosureRules.TryCommitCurrentSession(
				session,
				boundary) != InitialBeneficiaryClosureResult.Committed)
		{
			throw new InvalidOperationException(
				"The Actor UI fixture could not close initial beneficiary facts.");
		}
	}

	private static void UpdateKnownWerewolfAgentFacts(
		GameSession session,
		IReadOnlySet<Guid> werewolfIds)
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
					"actor-borrowed-ui-fixture-werewolf-agents"),
				Facts =
				[
					.. session.GetPlayers().Select(player => FactionFact.Agent(
						player.Id,
						Faction.Werewolf,
						werewolfIds.Contains(player.Id)
							? FactionAgentKnowledge.KnownAgent
							: FactionAgentKnowledge.KnownNonAgent,
						boundary))
				]
			});
	}

	private static PhysicalCharacterCard Card(string id, MainRoleType role) =>
		new(Guid.Parse(id), role);

	private sealed record CoreFixture(
		GameSession Session,
		StartGameConfirmationInstruction Start,
		IReadOnlyList<IPlayer> Players,
		Guid ActorId,
		Guid WerewolfId,
		IReadOnlyList<PhysicalCharacterCard> SetupCards,
		ActorBorrowedRolePowerActivation Activation,
		ConfirmationInstruction ActorSleep);

	private sealed record FamilyOutput(
		IReadOnlyList<ModeratorInstruction> Instructions,
		IReadOnlyList<Guid> SelectedPlayerIds,
		string? SelectedOptionId,
		IReadOnlyList<Guid> ResourceIds)
	{
		internal FamilyOutput(
			ModeratorInstruction instruction,
			IReadOnlyList<Guid> SelectedPlayerIds,
			string? SelectedOptionId,
			IReadOnlyList<Guid> ResourceIds)
			: this(
				[instruction],
				SelectedPlayerIds,
				SelectedOptionId,
				ResourceIds)
		{
		}

		internal ModeratorInstruction PrimaryInstruction => Instructions[0];

		internal static FamilyOutput Passive(ModeratorInstruction instruction) =>
			new(instruction, [], SelectedOptionId: null, ResourceIds: []);
	}

	private sealed record ListenerAdvanceResult(
		HookListenerOutcome Outcome,
		ModeratorInstruction? Instruction);
	private sealed record SpendOpeningResult(
		ActorBorrowedRolePowerActivation Activation,
		ConfirmationInstruction Sleep);

	private enum ListenerTestSubPhase
	{
		ActionLoop
	}
}
