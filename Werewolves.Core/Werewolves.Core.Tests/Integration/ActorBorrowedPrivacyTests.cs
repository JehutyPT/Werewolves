using FluentAssertions;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorBorrowedPrivacyTests
{
	private const string InvalidResponseMessage =
		"The borrowed Role Power response is invalid or no longer available.";

	private static readonly PhysicalCharacterCard[] SourceCards =
	[
		Card("00000000-0000-0000-0000-000000000251", MainRoleType.Seer),
		Card("00000000-0000-0000-0000-000000000252", MainRoleType.Cupid),
		Card("00000000-0000-0000-0000-000000000253", MainRoleType.Witch),
		Card("00000000-0000-0000-0000-000000000254", MainRoleType.LittleGirl),
		Card("00000000-0000-0000-0000-000000000255", MainRoleType.Defender),
		Card("00000000-0000-0000-0000-000000000256", MainRoleType.Fox),
		Card("00000000-0000-0000-0000-000000000257", MainRoleType.StutteringJudge)
	];

	private static readonly TestSubPhaseManagerKey SubPhaseKey = new();
	private static readonly TestHookSubPhaseKey HookKey = new();
	private static readonly TestGameFlowManagerKey FlowKey = new();

	[Theory]
	[InlineData(MainRoleType.Seer)]
	[InlineData(MainRoleType.Cupid)]
	[InlineData(MainRoleType.Witch)]
	[InlineData(MainRoleType.LittleGirl)]
	[InlineData(MainRoleType.Defender)]
	[InlineData(MainRoleType.Fox)]
	public void BorrowedSource_InvalidOrStaleResponseUsesNonIdentifyingError(
		MainRoleType sourceRole)
	{
		var fixture = CreateFixture(sourceRole);
		var invalidSubmission = PrepareInvalidSubmission(fixture);
		var historyCountBeforeSubmission = fixture.Session.GameHistoryLog.Count();

		invalidSubmission.Should().Throw<InvalidOperationException>()
			.WithMessage(InvalidResponseMessage);
		fixture.Session.GameHistoryLog.Should().HaveCount(
			historyCountBeforeSubmission);
	}

	[Fact]
	public void BorrowedStutteringJudge_StaleSetupReplaySkipsIdempotentlyWithoutIdentifyingOutput()
	{
		var fixture = CreateFixture(MainRoleType.StutteringJudge);
		var setup = PrepareInstructionAfterWake<ConfirmationInstruction>(fixture);
		var staleResponse = setup.CreateResponse();
		fixture.Session.CommitActorBorrowedStutteringJudgeSignalSetup(
			CreatePowerIdentity(
				fixture,
				"stuttering-judge-consecutive-vote"));
		var historyCount = fixture.Session.GameHistoryLog.Count();
		var markerCount = fixture.Session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Count();
		var commitCount = fixture.Session
			.GetActorBorrowedStutteringJudgeSignalSetupCommits().Count;
		HookListenerActionResult? replay = null;

		Action submitStaleResponse = () => replay = Advance(
			fixture.Listener,
			fixture.Session,
			staleResponse);

		submitStaleResponse.Should().NotThrow();
		replay.Should().NotBeNull();
		replay!.Outcome.Should().Be(HookListenerOutcome.Skip);
		replay.Instruction.Should().BeNull();
		fixture.Session.GameHistoryLog.Should().HaveCount(historyCount);
		fixture.Session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.HaveCount(markerCount);
		fixture.Session.GetActorBorrowedStutteringJudgeSignalSetupCommits()
			.Should().HaveCount(commitCount);
	}

	private static Action PrepareInvalidSubmission(PrivacyFixture fixture)
	{
		var selection = PrepareInstructionAfterWake<SelectPlayersInstruction>(
			fixture);
		if (fixture.SourceRole == MainRoleType.Cupid)
		{
			fixture.Session.SetPendingModeratorInstruction(FlowKey, selection);
		}

		var selectedPlayerIds = selection.SelectablePlayerIds
			.Where(playerId => playerId != fixture.ActorId)
			.Take(fixture.SourceRole == MainRoleType.Cupid ? 2 : 1)
			.ToHashSet();
		var staleResponse = selection.CreateResponse(selectedPlayerIds);
		if (fixture.SourceRole == MainRoleType.Witch)
		{
			CommitBorrowedWitchHealing(
				fixture,
				selectedPlayerIds.Single());
		}
		else
		{
			fixture.Session.EliminatePlayer(
				selectedPlayerIds.First(),
				EliminationReason.EventElimination);
		}

		return () => Advance(
			fixture.Listener,
			fixture.Session,
			staleResponse);
	}

	private static void CommitBorrowedWitchHealing(
		PrivacyFixture fixture,
		Guid targetId)
	{
		var powerIdentity = CreatePowerIdentity(fixture, "witch-potions");
		fixture.Session.CommitActorBorrowedWitchPotionUse(
			powerIdentity,
			new OneUseRolePowerResourceIdentity(
				fixture.ActorId,
				MainRoleType.Witch,
				"witch-potions",
				fixture.Activation.ActivationId,
				RolePowerInstanceOrigin.Borrowed,
				WitchRole.HealingResourceId),
			targetId);
	}

	private static RolePowerInstanceIdentity CreatePowerIdentity(
		PrivacyFixture fixture,
		string sourcePowerIdentifier) => new(
		fixture.ActorId,
		fixture.SourceRole,
		sourcePowerIdentifier,
		fixture.Activation.ActivationId,
		RolePowerInstanceOrigin.Borrowed);

	private static TInstruction PrepareInstructionAfterWake<TInstruction>(
		PrivacyFixture fixture)
		where TInstruction : ModeratorInstruction
	{
		var wake = Advance(
			fixture.Listener,
			fixture.Session,
			fixture.Start.CreateResponse()).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		return Advance(
				fixture.Listener,
				fixture.Session,
				wake.CreateResponse()).Instruction
			.Should().BeOfType<TInstruction>().Subject;
	}

	private static PrivacyFixture CreateFixture(MainRoleType sourceRole)
	{
		var sourceCard = SourceCards.Single(card =>
			card.PrintedRole == sourceRole);
		var setupCards = SourceCards
			.Where(card => card.PrintedRole != sourceRole)
			.Take(2)
			.Prepend(sourceCard)
			.ToArray();
		var config = new GameSessionConfig(
			[GameStrings.ActorRoleName, "Werewolf", "Villager 1", "Villager 2", "Villager 3"],
			[
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			new ActorSetupCards(version: 7, setupCards));
		var sessionId = Guid.NewGuid();
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		session.IdentifyRole([actorId], MainRoleType.Actor);
		SeedKnownActorBeneficiary(session, actorId);
		SeedKnownWerewolfAgentFacts(session, players[1].Id);
		session.TransitionMainPhase(GamePhase.Day);
		session.TransitionMainPhase(GamePhase.Night);
		if (sourceRole == MainRoleType.Witch)
		{
			session.PerformNightAction(
				NightActionType.WerewolfVictimSelection,
				players[^1].Id);
		}

		session.TrySpendActorSetupCard(actorId, sourceCard.Id, out var activation)
			.Should()
			.BeTrue();
		session.TryEnterSubPhaseStage(
			SubPhaseKey,
			GameHook.NightMainActionLoop.ToString()).Should().BeTrue();
		var gateway = new RolePowerAvailabilityGateway(
			AllowAllRolePowerAvailabilityPolicy.Instance);
		return new PrivacyFixture(
			sourceRole,
			session,
			start,
			actorId,
			activation!,
			CreateSourceListener(sourceRole, gateway));
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
					"test-actor-borrowed-privacy-beneficiary"),
				Facts =
				[
					FactionFact.Beneficiary(
						actorId,
						Faction.Villager,
						boundary)
				]
			});
		session.GetFactionBeneficiaryKnowledge(actorId).Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Villager));
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
		InitialBeneficiaryClosureRules.TryCommitCurrentSession(session, boundary)
			.Should().Be(InitialBeneficiaryClosureResult.Committed);
		session.GetPlayers().Should().OnlyContain(player =>
			session.GetFactionBeneficiaryKnowledge(player.Id).IsKnown);
		session.GetFactionBeneficiaryKnowledge(werewolfId).Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Werewolf));
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

	private static PhysicalCharacterCard Card(
		string id,
		MainRoleType role) => new(Guid.Parse(id), role);

	private sealed record PrivacyFixture(
		MainRoleType SourceRole,
		GameSession Session,
		StartGameConfirmationInstruction Start,
		Guid ActorId,
		ActorBorrowedRolePowerActivation Activation,
		IGameHookListener Listener);

	private sealed class TestSubPhaseManagerKey : ISubPhaseManagerKey;
	private sealed class TestHookSubPhaseKey : IHookSubPhaseKey;
	private sealed class TestGameFlowManagerKey : IGameFlowManagerKey;
}
