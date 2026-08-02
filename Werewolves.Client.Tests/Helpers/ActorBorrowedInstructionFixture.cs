using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
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
	StutteringJudge
}

internal sealed record ActorBorrowedInstructionScenario(
	MainRoleType SourceRole,
	Guid ActorId,
	ModeratorInstruction Instruction,
	IReadOnlyList<DashboardRosterEntry> Roster,
	IReadOnlyList<string> PrivateFragments,
	IReadOnlyList<string> PrivateFacts,
	IReadOnlyList<Guid> SelectedPlayerIds,
	string? SelectedOptionId,
	IReadOnlyList<Guid> SensitiveLineageIds);

internal static class ActorBorrowedInstructionFixture
{
	private static readonly PhysicalCharacterCard[] SourceCards =
	[
		Card("72000000-0000-0000-0000-000000000101", MainRoleType.Seer),
		Card("72000000-0000-0000-0000-000000000102", MainRoleType.Cupid),
		Card("72000000-0000-0000-0000-000000000103", MainRoleType.Witch),
		Card("72000000-0000-0000-0000-000000000104", MainRoleType.LittleGirl),
		Card("72000000-0000-0000-0000-000000000105", MainRoleType.Defender),
		Card("72000000-0000-0000-0000-000000000106", MainRoleType.Fox),
		Card("72000000-0000-0000-0000-000000000107", MainRoleType.StutteringJudge)
	];

	private static readonly TestSubPhaseManagerKey SubPhaseKey = new();
	private static readonly TestHookSubPhaseKey HookKey = new();
	private static readonly TestGameFlowManagerKey FlowKey = new();

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
			fixture.ActorId,
			output.Instruction,
			DashboardRoster.FromSession(fixture.Session),
			PrivateFragments(family, fixture),
			PrivateFacts(family, fixture, output),
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
		session.IdentifyRole([actorId], MainRoleType.Actor);
		SeedKnownActorBeneficiary(session, actorId);
		if (sourceRole != MainRoleType.LittleGirl)
		{
			SeedKnownWerewolfAgentFacts(session, werewolfId);
		}

		session.TransitionMainPhase(GamePhase.Day);
		session.TransitionMainPhase(GamePhase.Night);
		if (sourceRole == MainRoleType.Witch)
		{
			session.PerformNightAction(
				NightActionType.WerewolfVictimSelection,
				players[^1].Id);
		}

		if (!session.TryEnterSubPhaseStage(
				SubPhaseKey,
				GameHook.NightMainActionLoop.ToString()))
		{
			throw new InvalidOperationException(
				"The Actor UI fixture could not enter the night action loop.");
		}

		var activation = PerformSpendOpening(
			new ActorRole(CreateActorAvailabilityGateway()),
			session,
			start,
			sourceCard.Id);
		return new CoreFixture(
			session,
			start,
			players,
			actorId,
			werewolfId,
			setupCards,
			activation);
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
		_ => throw new ArgumentOutOfRangeException(nameof(family))
	};

	private static FamilyOutput CreateSeerInstruction(CoreFixture fixture)
	{
		var listener = CreateSourceListener(MainRoleType.Seer);
		var wake = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			fixture.Session,
			fixture.Start.CreateResponse(),
			"Seer wake");
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
		var wake = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			fixture.Session,
			fixture.Start.CreateResponse(),
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
		var wake = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			fixture.Session,
			fixture.Start.CreateResponse(),
			"Witch wake");
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
			fixture.Start.CreateResponse(),
			"Little Girl Werewolf group observation");
		return FamilyOutput.Passive(instruction);
	}

	private static FamilyOutput CreateDefenderInstruction(CoreFixture fixture)
	{
		var listener = CreateSourceListener(MainRoleType.Defender);
		var wake = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			fixture.Session,
			fixture.Start.CreateResponse(),
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
		var wake = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			fixture.Session,
			fixture.Start.CreateResponse(),
			"Fox wake");
		var selection = AdvanceToInstruction<SelectPlayersInstruction>(
			listener,
			fixture.Session,
			wake.CreateResponse(),
			"Fox center selection");
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
			"Fox result");
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
		var wake = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			fixture.Session,
			fixture.Start.CreateResponse(),
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
		if (terminal.Outcome != HookListenerOutcome.Skip)
		{
			throw new InvalidOperationException(
				"The Actor UI fixture did not finish Stuttering Judge setup.");
		}
		fixture.Session.ClearCurrentListenerCache(HookKey);

		fixture.Session.TransitionMainPhase(GamePhase.Day);
		fixture.Session.SetPendingModeratorInstruction(FlowKey, fixture.Start);
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
		_ => throw new ArgumentOutOfRangeException(nameof(family))
	};

	private static IReadOnlyList<string> PrivateFacts(
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

	private static ActorBorrowedRolePowerActivation PerformSpendOpening(
		IGameHookListener listener,
		GameSession session,
		StartGameConfirmationInstruction start,
		Guid selectedCardId)
	{
		var wake = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			session,
			start.CreateResponse(),
			"Actor wake");
		var choice = AdvanceToInstruction<SelectOptionsInstruction>(
			listener,
			session,
			wake.CreateResponse(),
			"Actor setup-card choice");
		var sleep = AdvanceToInstruction<ConfirmationInstruction>(
			listener,
			session,
			choice.CreateResponse(selectedCardId.ToString("D")),
			"Actor sleep");
		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()
			?? throw new InvalidOperationException(
				"The Actor setup-card choice did not establish an activation.");
		var completed = Advance(listener, session, sleep.CreateResponse());
		if (completed.Outcome != HookListenerOutcome.Complete)
		{
			throw new InvalidOperationException(
				"The Actor setup-card opening did not complete.");
		}
		session.ClearCurrentListenerCache(HookKey);
		return activation;
	}

	private static TInstruction AdvanceToInstruction<TInstruction>(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response,
		string step)
		where TInstruction : ModeratorInstruction =>
		RequireInstruction<TInstruction>(
			Advance(listener, session, response).Instruction,
			step);

	private static TInstruction RequireInstruction<TInstruction>(
		ModeratorInstruction? instruction,
		string step)
		where TInstruction : ModeratorInstruction =>
		instruction as TInstruction ?? throw new InvalidOperationException(
			$"The Actor UI fixture expected {typeof(TInstruction).Name} during {step}.");

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
		Guid werewolfId)
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
						player.Id == werewolfId
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

	private static PhysicalCharacterCard Card(string id, MainRoleType role) =>
		new(Guid.Parse(id), role);

	private sealed record CoreFixture(
		GameSession Session,
		StartGameConfirmationInstruction Start,
		IReadOnlyList<IPlayer> Players,
		Guid ActorId,
		Guid WerewolfId,
		IReadOnlyList<PhysicalCharacterCard> SetupCards,
		ActorBorrowedRolePowerActivation Activation);

	private sealed record FamilyOutput(
		ModeratorInstruction Instruction,
		IReadOnlyList<Guid> SelectedPlayerIds,
		string? SelectedOptionId,
		IReadOnlyList<Guid> ResourceIds)
	{
		internal static FamilyOutput Passive(ModeratorInstruction instruction) =>
			new(instruction, [], SelectedOptionId: null, ResourceIds: []);
	}

	private sealed class TestSubPhaseManagerKey : ISubPhaseManagerKey;
	private sealed class TestHookSubPhaseKey : IHookSubPhaseKey;
	private sealed class TestGameFlowManagerKey : IGameFlowManagerKey;
}
