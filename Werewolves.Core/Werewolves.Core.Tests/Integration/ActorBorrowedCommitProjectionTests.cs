using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorBorrowedCommitProjectionTests
{
	private static readonly PhysicalCharacterCard DefenderCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000241"),
		MainRoleType.Defender);
	private static readonly PhysicalCharacterCard SeerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000242"),
		MainRoleType.Seer);
	private static readonly PhysicalCharacterCard FoxCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000243"),
		MainRoleType.Fox);
	private static readonly PhysicalCharacterCard WitchCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000244"),
		MainRoleType.Witch);
	private static readonly PhysicalCharacterCard CupidCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000245"),
		MainRoleType.Cupid);
	private static readonly PhysicalCharacterCard StutteringJudgeCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000246"),
		MainRoleType.StutteringJudge);

	[Theory]
	[InlineData(PublicBorrowedEntryKind.Recurring)]
	[InlineData(PublicBorrowedEntryKind.TargetPrivate)]
	[InlineData(PublicBorrowedEntryKind.OneUse)]
	public void RehydrateSession_PublicBorrowedRolePowerEntryIsRejected(
		PublicBorrowedEntryKind entryKind)
	{
		var fixture = CreateCommittedSeerSession();
		var forgedEntry = CreateForgedPublicEntry(
			entryKind,
			fixture.Session,
			fixture.PowerIdentity,
			fixture.TargetPlayerId);
		var tampered = RecoveryPayloadTestDriver
			.Parse(fixture.Session.Serialize())
			.AppendPublicRolePowerCommit(forgedEntry)
			.Serialize();

		Action rehydrate = () => new GameSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*Actor borrowed Role Power state*");
	}

	[Fact]
	public void RehydrateSession_ActorBorrowedMarkerAndPrivateCommitCountMismatchIsRejected()
	{
		var fixture = CreateCommittedSeerSession();
		var tampered = RecoveryPayloadTestDriver
			.Parse(fixture.Session.Serialize())
			.RemoveLatestActorBorrowedRolePowerMarker()
			.Serialize();

		Action rehydrate = () => new GameSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*Actor borrowed Role Power state*");
	}

	[Fact]
	public void RehydrateSession_CursorlessActorBorrowedDefenderRetargetIsRejected()
	{
		var fixture = CreateCommittedDefenderSession();
		var tampered = RecoveryPayloadTestDriver
			.Parse(fixture.Session.Serialize())
			.RetargetActorBorrowedDefenderCommit(fixture.AlternateTargetPlayerId)
			.Serialize();

		Action rehydrate = () => new GameSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*Actor borrowed Role Power state*");
	}

	[Theory]
	[InlineData(ActorBorrowedPrivateCommitMutation.SeerTarget)]
	[InlineData(ActorBorrowedPrivateCommitMutation.SeerResult)]
	[InlineData(ActorBorrowedPrivateCommitMutation.DefenderTarget)]
	[InlineData(ActorBorrowedPrivateCommitMutation.FoxCenter)]
	[InlineData(ActorBorrowedPrivateCommitMutation.FoxResultAndResource)]
	[InlineData(ActorBorrowedPrivateCommitMutation.WitchUseTarget)]
	[InlineData(ActorBorrowedPrivateCommitMutation.WitchUseResource)]
	[InlineData(ActorBorrowedPrivateCommitMutation.WitchDeclineResource)]
	[InlineData(ActorBorrowedPrivateCommitMutation.CupidPair)]
	[InlineData(ActorBorrowedPrivateCommitMutation.CupidDisposition)]
	[InlineData(ActorBorrowedPrivateCommitMutation.JudgeSetupPowerLineage)]
	[InlineData(ActorBorrowedPrivateCommitMutation.JudgeObservationSignalAndResource)]
	public void RehydrateSession_CursorlessStableBoundaryWithMutatedActorPrivateCommitIsRejected(
		ActorBorrowedPrivateCommitMutation mutation)
	{
		var session = CreateCommittedActorSession(mutation);
		var stableSnapshot = session.Serialize();
		Action rehydrateUntampered = () => new GameSession(stableSnapshot);
		rehydrateUntampered.Should().NotThrow();
		var tampered = RecoveryPayloadTestDriver
			.Parse(stableSnapshot)
			.MutateActorBorrowedPrivateCommit(mutation)
			.Serialize();

		GameSession? recovered = null;
		Action rehydrateTampered = () => recovered = new GameSession(tampered);

		rehydrateTampered.Should().Throw<InvalidOperationException>();
		recovered.Should().BeNull();
		session.Serialize().Should().Be(stableSnapshot);
	}

	[Fact]
	public void Observer_ActorBorrowedCommitReceivesOnlyPropertyFreePublicMarker()
	{
		var observer = new RecordingStateChangeObserver();
		_ = CreateCommittedSeerSession(observer);

		var actorBorrowedEntries = observer.LogEntries
			.Where(entry => entry.GetType().Name.StartsWith(
				"ActorBorrowed",
				StringComparison.Ordinal))
			.ToArray();
		var marker = actorBorrowedEntries.Should().ContainSingle().Subject
			.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>().Subject;
		marker.ToString().Should().Be("ActorBorrowedRolePowerCommitted");
		Convert.FromBase64String(marker.IntegrityCommitment).Should().HaveCount(32);
		marker.IntegrityCommitment.Should()
			.NotContain(SeerCard.Id.ToString())
			.And.NotContain("seer-werewolf-detection");
		marker.GetType().GetProperties(
			System.Reflection.BindingFlags.Instance |
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.DeclaredOnly).Should().BeEmpty();
	}

	private static CommittedSeerFixture CreateCommittedSeerSession(
		IStateChangeObserver? observer = null)
	{
		var setup = new ActorSetupCards(
			version: 7,
			[DefenderCard, SeerCard, FoxCard]);
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
		var session = new GameSession(
			sessionId,
			new StartGameConfirmationInstruction(sessionId),
			config,
			observer);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		var targetId = players[1].Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		ArrangeCompleteWerewolfAgentFacts(session);
		session.TrySpendActorSetupCard(actorId, SeerCard.Id, out var activation)
			.Should().BeTrue();
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.Seer,
			"seer-werewolf-detection",
			activation!.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		session.CommitActorBorrowedSeerCheck(
			powerIdentity,
			targetId,
			FactionAgentKnowledge.KnownNonAgent);
		session.CaptureRecoveryBoundary(RecoveryBoundaryKey.Instance);
		return new CommittedSeerFixture(session, powerIdentity, targetId);
	}

	private static CommittedDefenderFixture CreateCommittedDefenderSession()
	{
		var setup = new ActorSetupCards(
			version: 7,
			[DefenderCard, SeerCard, FoxCard]);
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
		var session = new GameSession(
			sessionId,
			new StartGameConfirmationInstruction(sessionId),
			config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		session.TrySpendActorSetupCard(actorId, DefenderCard.Id, out var activation)
			.Should().BeTrue();
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.Defender,
			"defender-protection",
			activation!.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		session.CommitActorBorrowedDefenderProtection(
			powerIdentity,
			players[1].Id);
		session.CaptureRecoveryBoundary(RecoveryBoundaryKey.Instance);
		return new CommittedDefenderFixture(session, players[2].Id);
	}

	private static GameSession CreateCommittedActorSession(
		ActorBorrowedPrivateCommitMutation mutation)
	{
		if (mutation == ActorBorrowedPrivateCommitMutation
			.JudgeObservationSignalAndResource)
		{
			return ActorBorrowedStutteringJudgeTests
				.CreateCursorlessCommittedJudgeObservationSession();
		}

		var sourceCard = mutation switch
		{
			ActorBorrowedPrivateCommitMutation.SeerTarget or
			ActorBorrowedPrivateCommitMutation.SeerResult => SeerCard,
			ActorBorrowedPrivateCommitMutation.DefenderTarget => DefenderCard,
			ActorBorrowedPrivateCommitMutation.FoxCenter or
			ActorBorrowedPrivateCommitMutation.FoxResultAndResource => FoxCard,
			ActorBorrowedPrivateCommitMutation.WitchUseTarget or
			ActorBorrowedPrivateCommitMutation.WitchUseResource or
			ActorBorrowedPrivateCommitMutation.WitchDeclineResource => WitchCard,
			ActorBorrowedPrivateCommitMutation.CupidPair or
			ActorBorrowedPrivateCommitMutation.CupidDisposition => CupidCard,
			ActorBorrowedPrivateCommitMutation.JudgeSetupPowerLineage =>
				StutteringJudgeCard,
			_ => throw new ArgumentOutOfRangeException(nameof(mutation))
		};
		var setupCards = new[]
			{
				sourceCard,
				DefenderCard,
				SeerCard,
				FoxCard
			}
			.GroupBy(card => card.PrintedRole)
			.Select(group => group.First())
			.Take(3)
			.ToArray();
		var setup = new ActorSetupCards(version: 8, setupCards);
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
		var session = new GameSession(
			sessionId,
			new StartGameConfirmationInstruction(sessionId),
			config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		FactionFactEffectiveBoundary? initialAgentGroupBoundary = null;
		if (sourceCard.PrintedRole == MainRoleType.Seer ||
			mutation == ActorBorrowedPrivateCommitMutation.CupidDisposition)
		{
			initialAgentGroupBoundary =
				ArrangeCompleteWerewolfAgentFacts(session);
		}

		session.TrySpendActorSetupCard(actorId, sourceCard.Id, out var activation)
			.Should().BeTrue();
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			sourceCard.PrintedRole,
			SourcePowerIdentifier(sourceCard.PrintedRole),
			activation!.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		switch (mutation)
		{
			case ActorBorrowedPrivateCommitMutation.SeerTarget:
			case ActorBorrowedPrivateCommitMutation.SeerResult:
				session.CommitActorBorrowedSeerCheck(
					powerIdentity,
					players[1].Id,
					FactionAgentKnowledge.KnownNonAgent);
				break;
			case ActorBorrowedPrivateCommitMutation.DefenderTarget:
				session.CommitActorBorrowedDefenderProtection(
					powerIdentity,
					players[1].Id);
				break;
			case ActorBorrowedPrivateCommitMutation.FoxCenter:
			case ActorBorrowedPrivateCommitMutation.FoxResultAndResource:
				session.CommitActorBorrowedFoxCheck(
					powerIdentity,
					players[1].Id,
					FactionAgentKnowledge.KnownAgent,
					spentResourceIdentity: null);
				break;
			case ActorBorrowedPrivateCommitMutation.WitchUseTarget:
			case ActorBorrowedPrivateCommitMutation.WitchUseResource:
				session.CommitActorBorrowedWitchPotionUse(
					powerIdentity,
					CreateResourceIdentity(
						powerIdentity,
						ActorBorrowedWitchPotionUseCommit.PoisonResourceId),
					players[1].Id);
				break;
			case ActorBorrowedPrivateCommitMutation.WitchDeclineResource:
				session.CommitActorBorrowedWitchPotionDecline(
					powerIdentity,
					CreateResourceIdentity(
						powerIdentity,
						ActorBorrowedWitchPotionUseCommit.HealingResourceId));
				break;
			case ActorBorrowedPrivateCommitMutation.CupidPair:
			case ActorBorrowedPrivateCommitMutation.CupidDisposition:
				var lovers = players.Skip(1).Take(2)
					.Select(player => player.Id)
					.Order()
					.ToArray();
				session.CommitActorBorrowedCupidLovers(
					powerIdentity,
					lovers,
					ActorBorrowedCupidLoversDisposition
						.DeferredToInitialBeneficiaryClosure);
				break;
			case ActorBorrowedPrivateCommitMutation.JudgeSetupPowerLineage:
				session.CommitActorBorrowedStutteringJudgeSignalSetup(powerIdentity);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(mutation));
		}
		if (mutation == ActorBorrowedPrivateCommitMutation.CupidDisposition)
		{
			InitialBeneficiaryClosureRules.TryCommitCurrentSession(
					session,
					initialAgentGroupBoundary)
				.Should().Be(InitialBeneficiaryClosureResult.Committed);
			session.GetActorBorrowedCupidLoversCommits().Single().Disposition
				.Should().Be(ActorBorrowedCupidLoversDisposition.SameFaction);
		}

		session.CaptureRecoveryBoundary(RecoveryBoundaryKey.Instance);
		return session;
	}

	private static string SourcePowerIdentifier(MainRoleType sourceRole) =>
		sourceRole switch
		{
			MainRoleType.Seer => "seer-werewolf-detection",
			MainRoleType.Defender => "defender-protection",
			MainRoleType.Fox => "fox-neighborhood-check",
			MainRoleType.Witch =>
				ActorBorrowedWitchPotionUseCommit.ExpectedSourcePowerIdentifier,
			MainRoleType.Cupid =>
				ActorBorrowedCupidLoversCommit.ExpectedSourcePowerIdentifier,
			MainRoleType.StutteringJudge =>
				ActorBorrowedStutteringJudgeSignalSetupCommit
					.ExpectedSourcePowerIdentifier,
			_ => throw new ArgumentOutOfRangeException(nameof(sourceRole))
		};

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		RolePowerInstanceIdentity powerIdentity,
		Guid resourceId) => new(
			powerIdentity.ActingPlayerId,
			powerIdentity.SourceRole,
			powerIdentity.SourcePowerIdentifier,
			powerIdentity.PowerInstanceId,
			powerIdentity.PowerInstanceOrigin,
			resourceId);

	private static FactionFactEffectiveBoundary
		ArrangeCompleteWerewolfAgentFacts(GameSession session)
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
						FactionAgentKnowledge.KnownNonAgent,
						boundary))
				]
			});
		return boundary;
	}

	private static GameLogEntryBase CreateForgedPublicEntry(
		PublicBorrowedEntryKind entryKind,
		GameSession session,
		RolePowerInstanceIdentity powerIdentity,
		Guid targetPlayerId)
	{
		var timestamp = session.GameHistoryLog.Last().Timestamp.AddTicks(1);
		return entryKind switch
		{
			PublicBorrowedEntryKind.Recurring =>
				new RecurringRolePowerCommittedLogEntry
				{
					Timestamp = timestamp,
					TurnNumber = session.TurnNumber,
					CurrentPhase = GamePhase.Night,
					ActionType = NightActionType.SeerCheck,
					TargetIds = [targetPlayerId],
					ActingPlayerId = powerIdentity.ActingPlayerId,
					SourceRole = powerIdentity.SourceRole,
					SourcePowerIdentifier =
						powerIdentity.SourcePowerIdentifier,
					PowerInstanceId = powerIdentity.PowerInstanceId,
					PowerInstanceOrigin = RolePowerInstanceOrigin.Borrowed
				},
			PublicBorrowedEntryKind.TargetPrivate =>
				new TargetPrivateRolePowerCommittedLogEntry
				{
					Timestamp = timestamp,
					TurnNumber = session.TurnNumber,
					CurrentPhase = GamePhase.Night,
					ActionType = NightActionType.SeerCheck,
					TargetIds = [],
					ActingPlayerId = powerIdentity.ActingPlayerId,
					SourceRole = powerIdentity.SourceRole,
					SourcePowerIdentifier =
						powerIdentity.SourcePowerIdentifier,
					PowerInstanceId = powerIdentity.PowerInstanceId,
					PowerInstanceOrigin = RolePowerInstanceOrigin.Borrowed
				},
			PublicBorrowedEntryKind.OneUse =>
				new OneUseRolePowerCommittedLogEntry
				{
					Timestamp = timestamp,
					TurnNumber = session.TurnNumber,
					CurrentPhase = GamePhase.Night,
					ActionType = NightActionType.SeerCheck,
					TargetIds = [targetPlayerId],
					ActingPlayerId = powerIdentity.ActingPlayerId,
					SourceRole = powerIdentity.SourceRole,
					SourcePowerIdentifier =
						powerIdentity.SourcePowerIdentifier,
					PowerInstanceId = powerIdentity.PowerInstanceId,
					PowerInstanceOrigin = RolePowerInstanceOrigin.Borrowed,
					OneUseResourceId = Guid.NewGuid()
				},
			_ => throw new ArgumentOutOfRangeException(nameof(entryKind))
		};
	}

	public enum PublicBorrowedEntryKind
	{
		Recurring,
		TargetPrivate,
		OneUse
	}

	private sealed record CommittedSeerFixture(
		GameSession Session,
		RolePowerInstanceIdentity PowerIdentity,
		Guid TargetPlayerId);

	private sealed record CommittedDefenderFixture(
		GameSession Session,
		Guid AlternateTargetPlayerId);

	private sealed class RecoveryBoundaryKey : IGameFlowManagerKey
	{
		internal static RecoveryBoundaryKey Instance { get; } = new();
	}

	private sealed class RecordingStateChangeObserver : IStateChangeObserver
	{
		internal List<GameLogEntryBase> LogEntries { get; } = [];

		public void OnLogEntryApplied(GameLogEntryBase entry) =>
			LogEntries.Add(entry);

		public void OnMainPhaseChanged(GamePhase newPhase)
		{
		}

		public void OnSubPhaseChanged(string? newSubPhase)
		{
		}

		public void OnSubPhaseStageChanged(string? newSubPhaseStage)
		{
		}

		public void OnListenerChanged(
			ListenerIdentifier? listener,
			string? listenerState)
		{
		}

		public void OnTurnNumberChanged(int newTurnNumber)
		{
		}

		public void OnPendingInstructionChanged(
			ModeratorInstruction? instruction)
		{
		}
	}
}
