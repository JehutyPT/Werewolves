using FluentAssertions;
using FluentAssertions.Execution;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class AccursedWolfFatherRoleTests : DiagnosticTestBase
{
	public AccursedWolfFatherRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void FirstNight_UnknownHolder_IdentifiesThenOffersRetainedVictimOptions()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var wolfFather = players[1];
		var victim = players[4];

		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id, wolfFather.Id],
					victim.Id));

		identification.RoleIdentification.Should().Be(
			MainRoleType.AccursedWolfFather);
		identification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.Single);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Should().NotContain(entry =>
				entry.Role == MainRoleType.AccursedWolfFather);

		var choice =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				builder.Process(
					identification.CreateResponse([wolfFather.Id])));

		choice.Semantic.Should().NotBe(
			ModeratorInstructionSemantic.Unspecified);
		choice.SelectionRange.Should().BeEquivalentTo(
			NumberRangeConstraint.Single);
		choice.Options.Select(option => option.Id).Should().Equal(
			"accursed-wolf-father-infect",
			"accursed-wolf-father-decline");
		choice.PublicAnnouncement.Should().BeNull();
		choice.PrivateInstruction.Should().NotBeNullOrWhiteSpace();
		choice.AffectedPlayerIds.Should().Equal(wolfFather.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void CompleteNightPhase_AccursedWolfFatherInputUsesPublicFlowAndResolvesDawn()
	{
		var (builder, players) = CreateStartedGame();
		var wolfFather = players[1];
		var victim = players[4];
		builder.ConfirmGameStart();

		var result = builder.CompleteNightPhase(new NightActionInputs
		{
			WerewolfIds = [players[0].Id, wolfFather.Id],
			WerewolfVictimId = victim.Id,
			AccursedWolfFatherId = wolfFather.Id,
			AccursedWolfFatherInfectsVictim = true
		});

		result.IsSuccess.Should().BeTrue();
		builder.GetGameState()!.GetCurrentPhase().Should().Be(GamePhase.Day);
		builder.GetGameState()!.GameHistoryLog
			.OfType<PhaseTransitionLogEntry>()
			.Should().Contain(entry => entry.CurrentPhase == GamePhase.Dawn);
		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType ==
					NightActionType.AccursedWolfFatherInfection &&
				entry.TargetIds!.SequenceEqual(new[] { victim.Id }));
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_KnownLivingHolder_UsesPublicWakeBeforePrivateChoice()
	{
		var (builder, players) = CreateStartedGame();
		var wolfFather = players[1];
		var victim = players[4];
		builder.ArrangeKnownRole(
			wolfFather.Id,
			MainRoleType.AccursedWolfFather);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[0].Id, wolfFather.Id],
				victim.Id));

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(
				GameStrings.AccursedWolfFatherRoleName));
		wake.PrivateInstruction.Should().BeNull();
		wake.AffectedPlayerIds.Should().Equal(wolfFather.Id);

		var choice =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				builder.Process(wake.CreateResponse()));
		choice.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseAccursedWolfFatherInfection);
		choice.PublicAnnouncement.Should().BeNull();
		choice.PrivateInstruction.Should().Contain(victim.Name);
		choice.AffectedPlayerIds.Should().Equal(wolfFather.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void Infect_CommitsOwnerQualifiedIntentImmediately_AndReturnsPublicSleep()
	{
		var (builder, players, choice) = StartKnownChoice();
		var wolfFather = players[1];
		var victim = players[4];

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					choice.CreateResponse(
						AccursedWolfFatherInfectionOptionIds.Infect)));

		using (new AssertionScope())
		{
			sleep.Semantic.Should().Be(
				ModeratorInstructionSemantic.PutRoleToSleep);
			sleep.PublicAnnouncement.Should().Be(
				GameStrings.RoleGoesToSleepSingle.Format(
					GameStrings.AccursedWolfFatherRoleName));
			sleep.PrivateInstruction.Should().BeNull();
			sleep.AffectedPlayerIds.Should().Equal(wolfFather.Id);
		}

		var commit = builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle()
			.Subject;
		using (new AssertionScope())
		{
			commit.ActionType.Should().Be(
				NightActionType.AccursedWolfFatherInfection);
			commit.TargetIds.Should().Equal(victim.Id);
			commit.ActingPlayerId.Should().Be(wolfFather.Id);
			commit.SourceRole.Should().Be(MainRoleType.AccursedWolfFather);
			commit.SourcePowerIdentifier.Should().Be(
				"accursed-wolf-father-infection");
			commit.PowerInstanceId.Should().Be(wolfFather.Id);
			commit.PowerInstanceOrigin.Should().Be(
				RolePowerInstanceOrigin.Native);
			commit.OneUseResourceId.Should().Be(
				AccursedWolfFatherRole.InfectionResourceId);
		}

		builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.WerewolfVictimSelection &&
				entry.TargetIds!.SequenceEqual(new[] { victim.Id }));
		MarkTestCompleted();
	}

	[Fact]
	public void Decline_PreservesResource_AndOffersChoiceOnNextEligibleNight()
	{
		var (builder, players, choice) = StartKnownChoice();
		var wolfFather = players[1];

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					choice.CreateResponse(
						AccursedWolfFatherInfectionOptionIds.Decline)));

		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase(new()
		{
			[players[4].Id] = MainRoleType.SimpleVillager
		}).IsSuccess.Should().BeTrue();
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();

		builder.ConfirmNightStart();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightActionSubsequentNight(
					players[3].Id));
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.AffectedPlayerIds.Should().Equal(wolfFather.Id);

		var secondChoice =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				builder.Process(wake.CreateResponse()));
		secondChoice.Semantic.Should().Be(
			ModeratorInstructionSemantic.ChooseAccursedWolfFatherInfection);
		secondChoice.PrivateInstruction.Should().Contain(players[3].Name);
		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void AvailabilityDenial_IsEvaluatedOnce_AndReturnsSleepWithoutSpend()
	{
		var policy = new SequenceAvailabilityPolicy(false);
		var (builder, players) = CreateStartedGame(policy);
		var wolfFather = players[1];
		builder.ArrangeKnownRole(
			wolfFather.Id,
			MainRoleType.AccursedWolfFather);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[0].Id, wolfFather.Id],
				players[4].Id));

		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(wake.CreateResponse()));

		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		policy.Attempts.Should().ContainSingle();
		var attempt = policy.Attempts.Single();
		using (new AssertionScope())
		{
			attempt.ActingPlayer.Id.Should().Be(wolfFather.Id);
			attempt.SourceRole.Should().Be(MainRoleType.AccursedWolfFather);
			attempt.SourcePower.Identifier.Should().Be(
				new RolePowerIdentifier(
					"accursed-wolf-father-infection"));
			attempt.PowerInstance.Id.Should().Be(wolfFather.Id);
			attempt.PowerInstance.Origin.Should().Be(
				RolePowerInstanceOrigin.Native);
			attempt.OneUseResource.Should().NotBeNull();
			attempt.OneUseResource!.Id.Should().Be(
				AccursedWolfFatherRole.InfectionResourceId);
			attempt.OneUseResource.OwningPowerInstance.Should().Be(
				attempt.PowerInstance);
		}
		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void KnownEmptyHolder_OmitsEntireCallWithoutAvailabilityEvaluation()
	{
		var policy = new SequenceAvailabilityPolicy();
		var (builder, players) = CreateStartedGame(policy);
		builder
			.ArrangeKnownRole(
				players[1].Id,
				MainRoleType.AccursedWolfFather)
			.ArrangeKnownWerewolfFactionAgentGroup(
				players[0].Id,
				players[1].Id)
			.ArrangeEliminatedPlayer(players[1].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id],
					players[4].Id));

		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		policy.Attempts.Should().BeEmpty();
		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void SpentResource_OmitsEntireCallWithoutAvailabilityEvaluation()
	{
		var policy = new SequenceAvailabilityPolicy();
		var (builder, players) = CreateStartedGame(policy);
		var wolfFather = players[1];
		builder
			.ArrangeKnownRole(
				wolfFather.Id,
				MainRoleType.AccursedWolfFather)
			.ArrangeCommittedOneUseRolePower(
				CreateResourceIdentity(wolfFather.Id),
				NightActionType.AccursedWolfFatherInfection,
				players[3].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.CompleteWerewolfNightAction(
					[players[0].Id, wolfFather.Id],
					players[4].Id));

		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		policy.Attempts.Should().BeEmpty();
		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void InfectionChoice_InvalidPayloadsAreSideEffectFree()
	{
		var (builder, players, choice) = StartKnownChoice();
		var invalidCases = new (string Name, ModeratorResponse Response)[]
		{
			("missing", new ModeratorResponse
			{
				InstructionId = choice.InstructionId,
				Type = ExpectedInputType.OptionSelection
			}),
			("empty", new ModeratorResponse
			{
				InstructionId = choice.InstructionId,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds = []
			}),
			("both", new ModeratorResponse
			{
				InstructionId = choice.InstructionId,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds =
				[
					AccursedWolfFatherInfectionOptionIds.Infect,
					AccursedWolfFatherInfectionOptionIds.Decline
				]
			}),
			("duplicate", new ModeratorResponse
			{
				InstructionId = choice.InstructionId,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds =
				[
					AccursedWolfFatherInfectionOptionIds.Infect,
					AccursedWolfFatherInfectionOptionIds.Infect
				]
			}),
			("unknown", new ModeratorResponse
			{
				InstructionId = choice.InstructionId,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds = ["unknown-infection-choice"]
			}),
			("wrong-type", new ModeratorResponse
			{
				InstructionId = choice.InstructionId,
				Type = ExpectedInputType.PlayerSelection,
				SelectedPlayerIds = new HashSet<Guid> { players[3].Id }
			}),
			("different-target-payload", new ModeratorResponse
			{
				InstructionId = choice.InstructionId,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds =
					[AccursedWolfFatherInfectionOptionIds.Infect],
				SelectedPlayerIds = new HashSet<Guid> { players[3].Id }
			}),
			("stale-correlation", new ModeratorResponse
			{
				InstructionId = Guid.Empty,
				Type = ExpectedInputType.OptionSelection,
				SelectedOptionIds =
					[AccursedWolfFatherInfectionOptionIds.Infect]
			})
		};

		foreach (var invalidCase in invalidCases)
		{
			var before = builder.GetGameState()!.Serialize();
			var beforeHistory =
				builder.GetGameState()!.GameHistoryLog.ToArray();

			var act = () => builder.Process(invalidCase.Response);

			act.Should().Throw<InvalidOperationException>(
				invalidCase.Name);
			builder.GetCurrentInstruction()!.InstructionId.Should().Be(
				choice.InstructionId,
				invalidCase.Name);
			builder.GetGameState()!.Serialize().Should().Be(
				before,
				invalidCase.Name);
			builder.GetGameState()!.GameHistoryLog.Should().Equal(
				beforeHistory,
				invalidCase.Name);
		}

		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedInfectionReplay_IsSideEffectFreeAfterSpend()
	{
		var (builder, _, choice) = StartKnownChoice();
		var accepted = choice.CreateResponse(
			AccursedWolfFatherInfectionOptionIds.Infect);
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(accepted));
		var beforeReplay = builder.GetGameState()!.Serialize();
		var beforeHistory =
			builder.GetGameState()!.GameHistoryLog.ToArray();

		var replay = () => builder.Process(accepted);

		replay.Should().Throw<InvalidOperationException>();
		builder.GetCurrentInstruction()!.InstructionId.Should().Be(
			sleep.InstructionId);
		builder.GetGameState()!.Serialize().Should().Be(beforeReplay);
		builder.GetGameState()!.GameHistoryLog.Should().Equal(beforeHistory);
		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void SuccessfulInfection_JoinsCollectiveOnlyOnSubsequentNight()
	{
		var (builder, players, choice) = StartKnownChoice();
		var originalWerewolf = players[0];
		var wolfFather = players[1];
		var infected = players[4];
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					choice.CreateResponse(
						AccursedWolfFatherInfectionOptionIds.Infect)));

		infected.State.GetFactionAgentKnowledge(Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownNonAgent);
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));
		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		infected.State.GetFactionAgentKnowledge(Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();

		builder.ConfirmNightStart();
		var collectiveWake =
			InstructionAssert.ExpectType<ConfirmationInstruction>(
				builder.GetCurrentInstruction());
		collectiveWake.Semantic.Should().Be(
			ModeratorInstructionSemantic.WakeRole);
		collectiveWake.AffectedPlayerIds.Should().BeEquivalentTo(
			[originalWerewolf.Id, wolfFather.Id, infected.Id]);

		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(collectiveWake.CreateResponse()));
		targetSelection.AffectedPlayerIds.Should().BeEquivalentTo(
			[originalWerewolf.Id, wolfFather.Id, infected.Id]);
		targetSelection.SelectablePlayerIds.Should()
			.NotContain(originalWerewolf.Id)
			.And.NotContain(wolfFather.Id)
			.And.NotContain(infected.Id);
		MarkTestCompleted();
	}

	private (GameTestBuilder Builder, IPlayer[] Players,
		SelectOptionsInstruction Choice) StartKnownChoice()
	{
		var (builder, players) = CreateStartedGame();
		builder.ArrangeKnownRole(
			players[1].Id,
			MainRoleType.AccursedWolfFather);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.CompleteWerewolfNightAction(
				[players[0].Id, players[1].Id],
				players[4].Id));
		var choice =
			InstructionAssert.ExpectSuccessWithType<SelectOptionsInstruction>(
				builder.Process(wake.CreateResponse()));
		return (builder, players, choice);
	}

	private (GameTestBuilder Builder, IPlayer[] Players) CreateStartedGame(
		IRolePowerAvailabilityPolicy? policy = null)
	{
		var builder = CreateBuilder()
			.WithOptionalRolePowerAvailabilityPolicy(policy)
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		return (builder, builder.GetGameState()!.GetPlayers().ToArray());
	}

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		Guid wolfFatherId) => new(
		wolfFatherId,
		MainRoleType.AccursedWolfFather,
		"accursed-wolf-father-infection",
		wolfFatherId,
		RolePowerInstanceOrigin.Native,
		AccursedWolfFatherRole.InfectionResourceId);

	private sealed class SequenceAvailabilityPolicy(params bool[] decisions)
		: IRolePowerAvailabilityPolicy
	{
		private int _nextDecision;

		public List<RolePowerAttempt> Attempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			Attempts.Add(attempt);
			if (_nextDecision >= decisions.Length)
			{
				throw new InvalidOperationException(
					"The availability policy was evaluated more often than expected.");
			}

			return decisions[_nextDecision++]
				? RolePowerAvailabilityResult.Allowed
				: RolePowerAvailabilityResult.Denied;
		}
	}
}
