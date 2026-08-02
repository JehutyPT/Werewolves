using FluentAssertions;
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
	public void Observer_ActorBorrowedCommitReceivesOnlyPropertyFreePublicMarker()
	{
		var observer = new RecordingStateChangeObserver();
		_ = CreateCommittedSeerSession(observer);

		var actorBorrowedEntries = observer.LogEntries
			.Where(entry => entry.GetType().Name.StartsWith(
				"ActorBorrowed",
				StringComparison.Ordinal))
			.ToArray();
		var marker = actorBorrowedEntries.Should().ContainSingle().Subject;
		marker.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		marker.ToString().Should().Be("ActorBorrowedRolePowerCommitted");
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

	private static void ArrangeCompleteWerewolfAgentFacts(GameSession session)
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
