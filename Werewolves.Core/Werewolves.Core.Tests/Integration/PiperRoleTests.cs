using FluentAssertions;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class PiperRoleTests(ITestOutputHelper output)
	: DiagnosticTestBase(output)
{
	[Fact]
	public void NightOne_UnknownHolder_IdentificationEstablishesPrivatePiperBeneficiary()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Piper,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var piper = players[1];
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					players[4].Id));

		identification.Semantic.Should().Be(
			ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.Piper);
		identification.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		identification.PublicAnnouncement.Should().BeNull();
		var afterIdentification = builder.Process(
			identification.CreateResponse([piper.Id]));

		afterIdentification.ModeratorInstruction!.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		var session = builder.GetGameState()!;
		session.RequireKnownFactionBeneficiary(piper.Id).Should().Be(
			Faction.Piper);
		session.GetFactionAgentKnowledge(piper.Id, Faction.Piper).Should().Be(
			FactionAgentKnowledge.Unknown);
		piper.State.CurrentRole.Should().Be(MainRoleType.Piper);
		piper.State.ModeratorKnownRole.Should().Be(MainRoleType.Piper);
		piper.State.PhysicalCharacterCardRole.Should().BeNull();
		piper.State.PubliclyRevealedRole.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedIdentification_RecoveryRejectsMissingPiperBeneficiaryClosureFact()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Piper,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var piper = players[1];
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.CompleteWerewolfNightAction(
					[werewolf.Id],
					players[4].Id));
		builder.Process(identification.CreateResponse([piper.Id]));
		var tampered = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize())
			.RemoveInitialBeneficiaryClosureFact(piper.Id)
			.Serialize();

		Action rehydrate = () => new GameService().RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
		MarkTestCompleted();
	}

	[Fact]
	public void NightAction_WithAtLeastTwoEligiblePlayers_CharmsExactlyTwoAndCommitsSleep()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Piper,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var piper = players[1];
		var targets = new[] { players[2].Id, players[3].Id };
		builder.ArrangeKnownRole(piper.Id, MainRoleType.Piper);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[werewolf.Id],
				players[4].Id));

		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));

		targetSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectPiperTargets);
		targetSelection.CountConstraint.Should().Be(
			NumberRangeConstraint.Exact(2));
		targetSelection.SelectablePlayerIds.Should().BeEquivalentTo(
			players.Where(player => player.Id != piper.Id)
				.Select(player => player.Id));
		targetSelection.AffectedPlayerIds.Should().Equal(piper.Id);
		targetSelection.PublicAnnouncement.Should().BeNull();
		targetSelection.PrivateInstruction.Should().NotBeNullOrWhiteSpace();

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(targetSelection.CreateResponse(
					targets.ToHashSet())));

		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(piper.Id);
		var session = builder.GetGameState()!;
		targets.Should().OnlyContain(targetId =>
			session.GetPlayerState(targetId)
				.HasStatusEffect(StatusEffectTypes.Charmed));
		session.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.PiperCharm &&
				entry.ActingPlayerId == piper.Id &&
				entry.SourceRole == MainRoleType.Piper &&
				entry.TargetIds!.ToHashSet().SetEquals(targets));
		MarkTestCompleted();
	}

	[Fact]
	public void NightAction_WithExactlyOneEligiblePlayer_RequiresOneAndStillSleepsBeforeRecognition()
	{
		var (builder, players, wake) = StartKnownPiperWake((game, roster) =>
		{
			foreach (var player in roster.Where(player =>
				         player.Id != roster[1].Id &&
				         player.Id != roster[2].Id))
			{
				game.ArrangeStatusEffect(player.Id, StatusEffectTypes.Charmed);
			}
		});
		var piper = players[1];
		var target = players[2];
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));

		selection.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		selection.SelectablePlayerIds.Should().Equal(target.Id);

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(selection.CreateResponse([target.Id])));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(piper.Id);
		builder.GetGameState()!.GetPlayerState(target.Id)
			.HasStatusEffect(StatusEffectTypes.Charmed).Should().BeTrue();
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.PiperCharm &&
				entry.TargetIds!.SequenceEqual(new[] { target.Id }));

		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));
		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeCharmedPlayers);
		MarkTestCompleted();
	}

	[Fact]
	public void NightAction_WithNoEligiblePlayersAndChangedBeneficiary_OmitsOnlySelector()
	{
		var (builder, players, wake) = StartKnownPiperWake((game, roster) =>
		{
			foreach (var player in roster.Where(player =>
				         player.Id != roster[1].Id))
			{
				game.ArrangeStatusEffect(player.Id, StatusEffectTypes.Charmed);
			}

			var session = game.GetGameState()!;
			var boundary = new FactionFactEffectiveBoundary(
				session.TurnNumber,
				session.GetCurrentPhase(),
				session.GameHistoryLog.Count());
			game.ArrangeExplicitFactionTransition(
				"piper-beneficiary-change",
				FactionFact.Beneficiary(
					roster[1].Id,
					Faction.Villager,
					boundary,
					beneficiaryPrecedence: 1));
		});
		var piper = players[1];
		var statusEffectCount = builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>().Count();

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(wake.CreateResponse()));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(piper.Id);
		builder.GetGameState()!.RequireKnownFactionBeneficiary(piper.Id)
			.Should().Be(Faction.Villager);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType == NightActionType.PiperCharm);
		builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>().Should().HaveCount(statusEffectCount);

		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));
		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeCharmedPlayers);
		recognition.AffectedPlayerIds.Should().BeEquivalentTo(
			players.Where(player => player.Id != piper.Id)
				.Select(player => player.Id));
		MarkTestCompleted();
	}

	[Fact]
	public void AvailabilityDenied_EvaluatesExactlyOnceSleepsAndOmitsEmptyRecognition()
	{
		var policy = new SequenceAvailabilityPolicy(false);
		var (builder, players, wake) = StartKnownPiperWake(
			arrange: null,
			policy);
		var piper = players[1];
		var statusEffectCount = builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>().Count();

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(wake.CreateResponse()));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(piper.Id);
		policy.Attempts.Should().ContainSingle();
		policy.Attempts.Single().ActingPlayer.Id.Should().Be(piper.Id);
		policy.Attempts.Single().SourceRole.Should().Be(MainRoleType.Piper);
		policy.Attempts.Single().OneUseResource.Should().BeNull();
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType == NightActionType.PiperCharm);
		builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>().Should().HaveCount(statusEffectCount);

		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		MarkTestCompleted();
	}

	[Fact]
	public void TargetSelection_InvalidNoLongerEligibleAndStaleResponsesAreSideEffectFree()
	{
		var (builder, players, wake) = StartKnownPiperWake((game, roster) =>
		{
			game.ArrangeStatusEffect(roster[2].Id, StatusEffectTypes.Charmed);
			game.ArrangeEliminatedPlayer(roster[3].Id);
		});
		var piper = players[1];
		var alreadyCharmed = players[2];
		var dead = players[3];
		var firstEligible = players[4];
		var secondEligible = players[5];
		var thirdEligible = players[6];
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		selection.CountConstraint.Should().Be(NumberRangeConstraint.Exact(2));
		selection.SelectablePlayerIds.Should().NotContain(
			new[] { piper.Id, alreadyCharmed.Id, dead.Id });

		var invalidResponses = new[]
		{
			new ModeratorResponse
			{
				InstructionId = selection.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds = new HashSet<Guid> { firstEligible.Id }
			},
			new ModeratorResponse
			{
				InstructionId = selection.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds =
					new HashSet<Guid> { piper.Id, firstEligible.Id }
			},
			new ModeratorResponse
			{
				InstructionId = selection.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds =
					new HashSet<Guid> { dead.Id, firstEligible.Id }
			},
			new ModeratorResponse
			{
				InstructionId = selection.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds =
					new HashSet<Guid> { alreadyCharmed.Id, firstEligible.Id }
			},
			new ModeratorResponse
			{
				InstructionId = selection.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds =
					new HashSet<Guid> { Guid.NewGuid(), firstEligible.Id }
			},
			new ModeratorResponse
			{
				InstructionId = Guid.NewGuid(),
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds =
					new HashSet<Guid> { firstEligible.Id, secondEligible.Id }
			}
		};
		foreach (var invalidResponse in invalidResponses)
		{
			AssertRejectedResponseIsSideEffectFree(
				builder,
				selection,
				invalidResponse);
		}

		builder.ArrangeStatusEffect(
			firstEligible.Id,
			StatusEffectTypes.Charmed);
		AssertRejectedResponseIsSideEffectFree(
			builder,
			selection,
			selection.CreateResponse(
				[firstEligible.Id, secondEligible.Id]));

		var accepted = selection.CreateResponse(
			[secondEligible.Id, thirdEligible.Id]);
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(accepted));
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		AssertRejectedResponseIsSideEffectFree(builder, sleep, accepted);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.PiperCharm);
		MarkTestCompleted();
	}

	[Fact]
	public void TargetSelection_HolderChangesWhilePending_IsRejectedBeforeMutation()
	{
		var (builder, players, wake) = StartKnownPiperWake();
		var originalPiper = players[1];
		var swappedInPiper = players[2];
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		builder.ArrangeKnownRole(
			originalPiper.Id,
			MainRoleType.SimpleVillager);
		builder.ArrangeKnownRole(
			swappedInPiper.Id,
			MainRoleType.Piper);

		AssertRejectedResponseIsSideEffectFree(
			builder,
			selection,
			selection.CreateResponse([players[3].Id, players[4].Id]));
		MarkTestCompleted();
	}

	[Fact]
	public void EliminatedKnownHolder_OmitsEntireCallWithoutAvailabilityEvaluation()
	{
		var policy = new SequenceAvailabilityPolicy();
		var builder = CreateBuilder()
			.WithRolePowerAvailabilityPolicy(policy)
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Piper,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var piper = players[1];
		builder.ArrangeKnownRole(piper.Id, MainRoleType.Piper);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[0].Id);
		builder.ArrangeEliminatedPlayer(piper.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id],
					players[6].Id));

		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		policy.Attempts.Should().BeEmpty();
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType == NightActionType.PiperCharm);
		MarkTestCompleted();
	}

	[Fact]
	public void PermanentRoleSwap_UsesCurrentHolderWithoutReidentificationOrBeneficiaryTransfer()
	{
		var (builder, players, wake) = StartKnownPiperWake();
		var originalPiper = players[1];
		var swappedInPiper = players[2];
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(selection.CreateResponse(
					[players[3].Id, players[4].Id])));
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(recognition.CreateResponse()));
		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[players[6].Id] = MainRoleType.SimpleVillager
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();
		builder.ArrangeKnownRole(
			originalPiper.Id,
			MainRoleType.SimpleVillager);
		builder.ArrangeKnownRole(
			swappedInPiper.Id,
			MainRoleType.Piper);
		originalPiper.State.ModeratorKnownRole.Should().Be(
			MainRoleType.SimpleVillager);
		swappedInPiper.State.ModeratorKnownRole.Should().Be(
			MainRoleType.Piper);
		var identificationCount = builder.GetGameState()!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Count(entry => entry.Role == MainRoleType.Piper);
		builder.GetGameState()!.RequireKnownFactionBeneficiary(
				originalPiper.Id).Should().Be(Faction.Piper);
		builder.GetGameState()!.RequireKnownFactionBeneficiary(
				swappedInPiper.Id).Should().Be(Faction.Villager);

		builder.ConfirmNightStart();
		var swappedWake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightActionSubsequentNight(
					players[5].Id));

		swappedWake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		swappedWake.AffectedPlayerIds.Should().Equal(swappedInPiper.Id);
		var swappedSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(swappedWake.CreateResponse()));
		swappedSelection.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectPiperTargets);
		swappedSelection.AffectedPlayerIds.Should().Equal(swappedInPiper.Id);
		swappedSelection.SelectablePlayerIds.Should().NotContain(
			swappedInPiper.Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Count(entry => entry.Role == MainRoleType.Piper)
			.Should().Be(identificationCount);
		MarkTestCompleted();
	}

	[Fact]
	public void AfterSleep_RecognitionUsesCompleteLivingCharmedRosterOnce()
	{
		var builder = CreateBuilder()
			.WithPlayers(8)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Piper,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var piper = players[1];
		var previouslyCharmed = players[2];
		var eliminatedCharmed = players[3];
		var newTargets = new[] { players[4].Id, players[5].Id };
		builder.ArrangeKnownRole(piper.Id, MainRoleType.Piper);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		builder.ArrangeStatusEffect(
			previouslyCharmed.Id,
			StatusEffectTypes.Charmed);
		builder.ArrangeStatusEffect(
			eliminatedCharmed.Id,
			StatusEffectTypes.Charmed);
		builder.ArrangeEliminatedPlayer(eliminatedCharmed.Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[werewolf.Id],
				players[6].Id));
		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(targetSelection.CreateResponse(
				newTargets.ToHashSet())));

		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));

		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeCharmedPlayers);
		recognition.AffectedPlayerIds.Should().BeEquivalentTo(
			newTargets.Append(previouslyCharmed.Id));
		recognition.AffectedPlayerIds.Should().NotContain(
			eliminatedCharmed.Id);
		recognition.PublicAnnouncement.Should().NotBeNullOrWhiteSpace();
		recognition.PrivateInstruction.Should().NotBeNullOrWhiteSpace();
		foreach (var player in new[]
		         {
			         previouslyCharmed,
			         players[4],
			         players[5],
			         eliminatedCharmed
		         })
		{
			recognition.PublicAnnouncement.Should().NotContain(player.Name);
		}

		var charmCommitCount = builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Count(entry => entry.ActionType == NightActionType.PiperCharm);
		var statusEffectCount = builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Count();
		builder.Process(recognition.CreateResponse()).IsSuccess.Should().BeTrue();
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Count(entry => entry.ActionType == NightActionType.PiperCharm)
			.Should().Be(charmCommitCount);
		builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Should().HaveCount(statusEffectCount);
		MarkTestCompleted();
	}

	[Fact]
	public void FinalUncharmedPlayerEliminated_AtFollowingDawn_PiperWins()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Piper,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var piper = players[1];
		var newTargets = new[] { players[4].Id, players[5].Id };
		var finalUncharmedPlayer = players[6];
		builder.ArrangeKnownRole(piper.Id, MainRoleType.Piper);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		foreach (var player in new[] { werewolf, players[2], players[3] })
		{
			builder.ArrangeStatusEffect(player.Id, StatusEffectTypes.Charmed);
		}

		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[werewolf.Id],
				finalUncharmedPlayer.Id));
		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(targetSelection.CreateResponse(
				newTargets.ToHashSet())));
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));

		builder.GetGameState()!.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().BeEmpty();
		var nightEnd =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(recognition.CreateResponse()));
		nightEnd.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		builder.Process(nightEnd.CreateResponse());
		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[finalUncharmedPlayer.Id] = MainRoleType.SimpleVillager
		});

		var finished = builder.GetCurrentInstruction().Should()
			.BeOfType<FinishedGameConfirmationInstruction>().Subject;
		finished.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.Piper));
		finished.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
		builder.GetGameState()!.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().ContainSingle(entry =>
				entry.GameResult.Equals(finished.GameResult) &&
				entry.VictoryCheckWindow == VictoryCheckWindow.Dawn);

		var freshService = new GameService();
		var recoveredGameId = freshService.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var recoveredFinished = freshService
			.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<FinishedGameConfirmationInstruction>().Subject;

		recoveredFinished.Should().BeEquivalentTo(finished);
		freshService.GetGameStateView(recoveredGameId)!.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().ContainSingle(entry =>
				entry.GameResult.Equals(finished.GameResult) &&
				entry.VictoryCheckWindow == VictoryCheckWindow.Dawn);
		freshService.ProcessInstruction(
				recoveredGameId,
				new ModeratorResponse
				{
					InstructionId = recoveredFinished.InstructionId,
					Type = ExpectedInputType.FinishedGame
				})
			.IsSuccess.Should().BeFalse();
		freshService.GetGameStateView(recoveredGameId)!.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void PiperEliminatedDuringDawn_PreventsPiperVictory()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Piper,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var piper = players[1];
		builder.ArrangeKnownRole(piper.Id, MainRoleType.Piper);
		builder.ArrangeKnownWerewolfFactionAgentGroup(werewolf.Id);
		foreach (var player in players.Where(player => player.Id != piper.Id))
		{
			builder.ArrangeStatusEffect(player.Id, StatusEffectTypes.Charmed);
		}

		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[werewolf.Id],
				piper.Id));
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(wake.CreateResponse()));
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		var recognition =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));
		recognition.Semantic.Should().Be(
			ModeratorInstructionSemantic.RecognizeCharmedPlayers);
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(recognition.CreateResponse()));
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();

		builder.CompleteDawnPhase(new Dictionary<Guid, MainRoleType>
		{
			[piper.Id] = MainRoleType.Piper
		}).IsSuccess.Should().BeTrue();

		builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Day);
		builder.GetGameState()!.GetPlayerState(piper.Id).Health.Should().Be(
			PlayerHealth.Dead);
		builder.GetCurrentInstruction().Should().NotBeOfType<
			FinishedGameConfirmationInstruction>();
		builder.GetGameState()!.GameHistoryLog
			.OfType<VictoryConditionMetLogEntry>()
			.Should().NotContain(entry =>
				entry.GameResult.Equals(
					new SingleFactionGameResult(Faction.Piper)));
		MarkTestCompleted();
	}

	private (
			GameTestBuilder Builder,
			IPlayer[] Players,
			ConfirmationInstruction Wake)
		StartKnownPiperWake(
			Action<GameTestBuilder, IPlayer[]>? arrange = null,
			IRolePowerAvailabilityPolicy? policy = null)
	{
		var builder = CreateBuilder()
			.WithOptionalRolePowerAvailabilityPolicy(policy)
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Piper,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeKnownRole(players[1].Id, MainRoleType.Piper);
		builder.ArrangeKnownWerewolfFactionAgentGroup(players[0].Id);
		arrange?.Invoke(builder, players);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id],
					players[6].Id));
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().Equal(players[1].Id);
		return (builder, players, wake);
	}

	private sealed class SequenceAvailabilityPolicy(params bool[] decisions)
		: IRolePowerAvailabilityPolicy
	{
		private int _nextDecision;

		internal List<RolePowerAttempt> Attempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			Attempts.Add(attempt);
			if (_nextDecision >= decisions.Length)
			{
				throw new InvalidOperationException(
					"The Piper availability policy was evaluated more often than expected.");
			}

			return decisions[_nextDecision++]
				? RolePowerAvailabilityResult.Allowed
				: RolePowerAvailabilityResult.Denied;
		}
	}

	private static void AssertRejectedResponseIsSideEffectFree(
		GameTestBuilder builder,
		ModeratorInstruction pendingInstruction,
		ModeratorResponse response)
	{
		var session = builder.GetGameState()!;
		var serializedBefore = session.Serialize();
		var logBefore = session.GameHistoryLog.ToArray();

		Action process = () => builder.Process(response);

		process.Should().ThrowExactly<InvalidOperationException>();
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			pendingInstruction.InstructionId);
		session.GameHistoryLog.Should().Equal(logBefore);
		session.Serialize().Should().Be(serializedBefore);
	}
}
