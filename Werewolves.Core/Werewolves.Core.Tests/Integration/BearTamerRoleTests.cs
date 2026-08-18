using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
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

public sealed class BearTamerRoleTests : DiagnosticTestBase
{
	public BearTamerRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void FirstNight_UnknownHolder_IdentifiesExactRoleWithoutAssigningOrRevealingACard()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.BearTamer,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();

		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var bearTamer = builder.GetGameState()!.GetPlayers().First();

		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.BearTamer);
		identification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.Single);

		var werewolfObservation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(
					identification.CreateResponse([bearTamer.Id])));

		werewolfObservation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().ContainSingle(entry =>
				entry.Role == MainRoleType.BearTamer &&
				entry.PlayerIds.SetEquals(new[] { bearTamer.Id }));
		bearTamer.State.PhysicalCharacterCardRole.Should().BeNull();
		bearTamer.State.PubliclyRevealedRole.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void Dawn_LivingNeighborIsKnownWerewolfAgent_RequiresOneGenericGrowlAcknowledgmentThenCommitsOneIdentityFreeFact()
	{
		var builder = CreateBuilder()
			.WithPlayers(
				"Bear Tamer",
				"Werewolf",
				"Villager A",
				"Victim",
				"Villager B")
			.WithRoles(
				MainRoleType.BearTamer,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		builder.Process(identification.CreateResponse([players[0].Id]));
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[1].Id],
				players[3].Id));
		builder.Process(nightEnd.CreateResponse());

		var growl = AdvanceDawnToBearTamerGrowl(
			builder,
			new Dictionary<Guid, MainRoleType>
			{
				[players[3].Id] = MainRoleType.SimpleVillager
			});

		growl.GetType().Should().Be<ConfirmationInstruction>();
		growl.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceBearTamerGrowl);
		growl.PublicAnnouncement.Should().BeNull();
		growl.PrivateInstruction.Should().Be(
			GameStrings.BearTamerGrowlInstruction);
		growl.AffectedPlayerIds.Should().BeNull();
		growl.SoundEffects.Should().Equal(SoundEffectsEnum.BearGrowl);
		builder.GetGameState()!.GameHistoryLog
			.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().BeEmpty();

		var afterGrowl = builder.Process(growl.CreateResponse());

		afterGrowl.IsSuccess.Should().BeTrue();
		builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Day);
		var fact = builder.GetGameState()!.GameHistoryLog
			.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().ContainSingle().Subject;
		fact.CurrentPhase.Should().Be(GamePhase.Dawn);
		fact.ToString().Should().Be("BearTamerGrowlOccurred");
		MarkTestCompleted();
	}

	[Fact]
	public void DawnGrowl_RecoveryIgnoresPresentationChangesAndDoesNotReplayCommittedFact()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.BearTamer,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(players[0].Id, MainRoleType.BearTamer);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[1].Id],
				players[3].Id));
		builder.Process(nightEnd.CreateResponse());
		var growl = AdvanceDawnToBearTamerGrowl(
			builder,
			new Dictionary<Guid, MainRoleType>
			{
				[players[3].Id] = MainRoleType.SimpleVillager
			});
		const string changedPrivateInstruction =
			"Changed copy must not become recovery identity.";
		var changedSnapshot = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize())
			.RewritePendingConfirmationPresentation(
				changedPrivateInstruction,
				soundEffects: null)
			.Serialize();

		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			changedSnapshot);
		var recoveredGrowl = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recoveredGrowl.InstructionId.Should().Be(growl.InstructionId);
		recoveredGrowl.Semantic.Should().Be(growl.Semantic);
		recoveredGrowl.PrivateInstruction.Should().Be(changedPrivateInstruction);
		recoveredGrowl.PublicAnnouncement.Should().BeNull();
		recoveredGrowl.AffectedPlayerIds.Should().BeNull();
		recoveredGrowl.SoundEffects.Should().BeEmpty();

		var growlResponse = recoveredGrowl.CreateResponse();
		recoveredService.ProcessInstruction(recoveredGameId, growlResponse)
			.IsSuccess.Should().BeTrue();
		var committedState = recoveredService.GetGameStateView(recoveredGameId)!;
		committedState.GameHistoryLog
			.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().ContainSingle();

		var secondRecoveredService = new GameService();
		var secondRecoveredGameId = secondRecoveredService.RehydrateSession(
			committedState.Serialize());
		var postCommitInstruction =
			secondRecoveredService.GetCurrentInstruction(secondRecoveredGameId);

		postCommitInstruction!.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.AnnounceBearTamerGrowl);
		secondRecoveredService.GetGameStateView(secondRecoveredGameId)!
			.GameHistoryLog
			.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().ContainSingle();

		var serializedBeforeDuplicate =
			secondRecoveredService.GetGameStateView(secondRecoveredGameId)!
				.Serialize();
		Action duplicate = () => secondRecoveredService.ProcessInstruction(
			secondRecoveredGameId,
			growlResponse);

		duplicate.Should().Throw<InvalidOperationException>()
			.WithMessage("*pending Moderator Instruction*");
		secondRecoveredService.GetGameStateView(secondRecoveredGameId)!
			.Serialize().Should().Be(serializedBeforeDuplicate);
		secondRecoveredService.GetGameStateView(secondRecoveredGameId)!
			.GameHistoryLog
			.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void DawnGrowl_RecoveryWithAffectedAudience_IsRejectedBeforeSessionExposure()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.BearTamer,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(players[0].Id, MainRoleType.BearTamer);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[1].Id],
				players[3].Id));
		builder.Process(nightEnd.CreateResponse());
		_ = AdvanceDawnToBearTamerGrowl(
			builder,
			new Dictionary<Guid, MainRoleType>
			{
				[players[3].Id] = MainRoleType.SimpleVillager
			});
		var tampered = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize())
			.RewritePendingConfirmationAffectedPlayer(players[0].Id)
			.Serialize();

		var service = new GameService();
		Action rehydrate = () => service.RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
		MarkTestCompleted();
	}

	[Fact]
	public void Dawn_AutomaticPowerIsDenied_EvaluatesOnceAndSkipsWithoutGrowlOrFact()
	{
		var policy = new RecordingBearAvailabilityPolicy(isAvailable: false);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.BearTamer,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(players[0].Id, MainRoleType.BearTamer);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[1].Id],
				players[3].Id));
		builder.Process(nightEnd.CreateResponse());

		var result = builder.CompleteDawnPhase(
			new Dictionary<Guid, MainRoleType>
			{
				[players[3].Id] = MainRoleType.SimpleVillager
			});

		result.IsSuccess.Should().BeTrue();
		builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Day);
		policy.BearAttempts.Should().ContainSingle();
		var attempt = policy.BearAttempts.Single();
		attempt.ActingPlayer.Id.Should().Be(players[0].Id);
		attempt.SourceRole.Should().Be(MainRoleType.BearTamer);
		attempt.SourcePower.Identifier.Value.Should().Be("bear-tamer-growl");
		attempt.SourcePower.Category.Should().Be(RolePowerCategory.Automatic);
		attempt.PowerInstance.Id.Should().Be(players[0].Id);
		attempt.PowerInstance.Origin.Should().Be(
			RolePowerInstanceOrigin.Native);
		attempt.OneUseResource.Should().BeNull();
		builder.GetGameState()!.GameHistoryLog
			.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void Dawn_ActorBorrowsDifferentSource_EvaluatesNativeBearTamer()
	{
		var policy = new RecordingBearAvailabilityPolicy(isAvailable: true);
		var actorSetupCards = new ActorSetupCards(
			[
				MainRoleType.Seer,
				MainRoleType.Cupid,
				MainRoleType.Witch
			]);
		var seerCard = actorSetupCards.Cards.Single(card =>
			card.PrintedRole == MainRoleType.Seer);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithActorSetupCards(actorSetupCards)
			.WithPlayers(
				GameStrings.ActorRoleName,
				"Bear Tamer",
				"Werewolf",
				"Victim",
				"Villager")
			.WithRoles(
				MainRoleType.Actor,
				MainRoleType.BearTamer,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(players[0].Id, MainRoleType.Actor);
		builder.ArrangeKnownRole(players[1].Id, MainRoleType.BearTamer);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[2].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteActorNightAction(players[0].Id, seerCard.Id);
		var seerWake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[2].Id],
				players[3].Id));
		seerWake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		seerWake.AffectedPlayerIds.Should().Equal(players[0].Id);
		var seerTarget = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(seerWake.CreateResponse()));
		var seerResult = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(seerTarget.CreateResponse([players[2].Id])));
		var seerSleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(seerResult.CreateResponse()));
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(seerSleep.CreateResponse()));
		builder.Process(nightEnd.CreateResponse()).IsSuccess.Should().BeTrue();
		builder.GetGameState()!
			.GetModeratorActiveActorBorrowedRolePowerActivation()!
			.SourceRole.Should().Be(MainRoleType.Seer);

		var growl = AdvanceDawnToBearTamerGrowl(
			builder,
			new Dictionary<Guid, MainRoleType>
			{
				[players[3].Id] = MainRoleType.SimpleVillager
			});

		growl.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceBearTamerGrowl);
		var attempt = policy.BearAttempts.Should().ContainSingle().Subject;
		attempt.ActingPlayer.Id.Should().Be(players[1].Id);
		attempt.PowerInstance.Id.Should().Be(players[1].Id);
		attempt.PowerInstance.Origin.Should().Be(
			RolePowerInstanceOrigin.Native);
		MarkTestCompleted();
	}

	[Fact]
	public void Dawn_AfterKnownRoleSwap_EvaluatesTheCurrentLivingHolder()
	{
		var policy = new RecordingBearAvailabilityPolicy(isAvailable: true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.BearTamer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeCurrentRole(players[0].Id, MainRoleType.SimpleVillager);
		builder.ArrangeKnownRole(players[2].Id, MainRoleType.BearTamer);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[3].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[3].Id],
				players[4].Id));
		builder.Process(nightEnd.CreateResponse());

		var growl = AdvanceDawnToBearTamerGrowl(
			builder,
			new Dictionary<Guid, MainRoleType>
			{
				[players[4].Id] = MainRoleType.SimpleVillager
			});

		growl.Semantic.Should().Be(
			ModeratorInstructionSemantic.AnnounceBearTamerGrowl);
		policy.BearAttempts.Should().ContainSingle();
		policy.BearAttempts.Single().ActingPlayer.Id.Should().Be(players[2].Id);
		players[0].State.CurrentRole.Should().Be(MainRoleType.SimpleVillager);
		players[2].State.CurrentRole.Should().Be(MainRoleType.BearTamer);
		MarkTestCompleted();
	}

	[Fact]
	public void Dawn_GrowlPrecedesImmediateWerewolfVictory()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.BearTamer,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(players[3].Id, MainRoleType.BearTamer);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			players[0].Id,
			players[1].Id,
			players[2].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var werewolves = players
			.Take(3)
			.Select(player => player.Id)
			.ToHashSet();
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				werewolves,
				players[4].Id));
		builder.Process(nightEnd.CreateResponse());
		var growl = AdvanceDawnToBearTamerGrowl(
			builder,
			new Dictionary<Guid, MainRoleType>
			{
				[players[4].Id] = MainRoleType.SimpleVillager
			});

		var terminal = InstructionAssert
			.ExpectSuccessWithType<FinishedGameConfirmationInstruction>(
				builder.Process(growl.CreateResponse()));

		terminal.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
		terminal.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.Werewolf));
		builder.GetGameState()!.GameHistoryLog
			.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().ContainSingle();
		var recoveredService = new GameService();
		var recoveredGameId = recoveredService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredTerminal = recoveredService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<FinishedGameConfirmationInstruction>()
			.Subject;

		recoveredTerminal.InstructionId.Should().Be(terminal.InstructionId);
		recoveredService.GetGameStateView(recoveredGameId)!.GameHistoryLog
			.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void Dawn_NoLivingNeighborIsKnownWerewolfAgent_EvaluatesOnceAndSkips()
	{
		var policy = new RecordingBearAvailabilityPolicy(isAvailable: true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.BearTamer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(players[0].Id, MainRoleType.BearTamer);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[2].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[2].Id],
				players[3].Id));
		builder.Process(nightEnd.CreateResponse());

		builder.CompleteDawnPhase(
			new Dictionary<Guid, MainRoleType>
			{
				[players[3].Id] = MainRoleType.SimpleVillager
			}).IsSuccess.Should().BeTrue();

		policy.BearAttempts.Should().ContainSingle();
		builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Day);
		builder.GetGameState()!.GameHistoryLog
			.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void Dawn_BearTamerWasEliminatedDuringVictimResolution_SkipsBeforeAvailability()
	{
		var policy = new RecordingBearAvailabilityPolicy(isAvailable: true);
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.BearTamer,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(players[0].Id, MainRoleType.BearTamer);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[1].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var nightEnd = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[1].Id],
				players[0].Id));
		builder.Process(nightEnd.CreateResponse());

		builder.CompleteDawnPhase(
			new Dictionary<Guid, MainRoleType>
			{
				[players[0].Id] = MainRoleType.BearTamer
			}).IsSuccess.Should().BeTrue();

		players[0].State.Health.Should().Be(PlayerHealth.Dead);
		policy.BearAttempts.Should().BeEmpty();
		builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Day);
		builder.GetGameState()!.GameHistoryLog
			.OfType<BearTamerGrowlOccurredLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	private static ConfirmationInstruction AdvanceDawnToBearTamerGrowl(
		GameTestBuilder builder,
		IReadOnlyDictionary<Guid, MainRoleType> roleAssignments)
	{
		for (var step = 0; step < 20; step++)
		{
			switch (builder.GetCurrentInstruction())
			{
				case ConfirmationInstruction
				{
					Semantic:
						ModeratorInstructionSemantic.AnnounceBearTamerGrowl
				} growl:
					return growl;
				case ConfirmationInstruction confirmation:
					builder.Process(confirmation.CreateResponse());
					break;
				case AssignRolesInstruction assignRoles:
					var assignments = assignRoles.PlayersForAssignment.ToDictionary(
						playerId => playerId,
						playerId => roleAssignments.GetValueOrDefault(
							playerId,
							MainRoleType.SimpleVillager));
					builder.Process(assignRoles.CreateResponse(assignments));
					break;
				case null:
					throw new InvalidOperationException(
						"Dawn did not expose a pending Moderator Instruction.");
				case var instruction:
					throw new InvalidOperationException(
						$"Unexpected Dawn instruction {instruction.GetType().Name}.");
			}
		}

		throw new InvalidOperationException(
			"Dawn did not reach the Bear Tamer growl.");
	}

	private sealed class RecordingBearAvailabilityPolicy(bool isAvailable)
		: IRolePowerAvailabilityPolicy
	{
		internal List<RolePowerAttempt> BearAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			if (attempt.SourceRole != MainRoleType.BearTamer)
			{
				return RolePowerAvailabilityResult.Allowed;
			}

			BearAttempts.Add(attempt);
			return isAvailable
				? RolePowerAvailabilityResult.Allowed
				: RolePowerAvailabilityResult.Denied;
		}
	}
}
