using FluentAssertions;
using FluentAssertions.Execution;
using Werewolves.Core.GameLogic.Roles.MainRoles;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class WitchRoleTests : DiagnosticTestBase
{
	public enum ForeignPotionIdentityDimension
	{
		ActingPlayer,
		SourceRole,
		SourcePower
	}

	public enum OmittedPotionIdentityDimension
	{
		SourceRole,
		PowerInstanceOrigin
	}

	public WitchRoleTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void FirstNight_UnknownWitch_IdentifiesThenOffersHealingForPhysicalAttackTarget()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Witch,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var witch = players[1];
		var attackTarget = players[4];
		builder.CompleteWerewolfNightAction([werewolf.Id], attackTarget.Id);
		var identification = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			builder.GetCurrentInstruction());

		identification.RoleIdentification.Should().Be(MainRoleType.Witch);
		identification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.Single);
		identification.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.WitchRoleName));
		identification.PrivateInstruction.Should().Be(
			GameStrings.RoleSingleIdentificationPrompt.Format(
				GameStrings.WitchRoleName));

		var healing = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(identification.CreateResponse([witch.Id])));

		healing.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.SingleOptional);
		healing.SelectablePlayerIds.Should().Equal(attackTarget.Id);
		healing.PublicAnnouncement.Should().BeNull();
		healing.PrivateInstruction.Should().Contain(attackTarget.Name);
		healing.AffectedPlayerIds.Should().Equal(witch.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void HealingRoster_IsDistinctUnionOfAllCurrentNightPhysicalAttackTargets()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Witch,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.ArrangeNightAction(
			NightActionType.DefenderProtect,
			players[4].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction(
			[players[0].Id, players[2].Id],
			players[4].Id);
		builder
			.ArrangeNightAction(
				NightActionType.AccursedWolfFatherInfection,
				players[4].Id)
			.ArrangeNightAction(
				NightActionType.WhiteWerewolfVictimSelection,
				players[2].Id)
			.ArrangeNightAction(
				NightActionType.BigBadWolfVictimSelection,
				players[3].Id);
		var identification = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			builder.GetCurrentInstruction());

		var healing = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(identification.CreateResponse([players[1].Id])));

		healing.SelectablePlayerIds.Should().BeEquivalentTo(
			new[]
			{
				players[2].Id,
				players[3].Id,
				players[4].Id
			});
		healing.PrivateInstruction.Should().ContainAll(
			players[2].Name,
			players[3].Name,
			players[4].Name);
		healing.PublicAnnouncement.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_KnownWitch_UsesOrdinaryPublicWakeBeforePrivatePotionFlow()
	{
		var (builder, players) = CreateStartedWitchGame();
		builder.ArrangeKnownRole(players[1].Id, MainRoleType.Witch);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction([players[0].Id], players[4].Id);

		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());

		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.WitchRoleName));
		wake.PrivateInstruction.Should().BeNull();
		wake.AffectedPlayerIds.Should().Equal(players[1].Id);
		var healing = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(wake.CreateResponse()));
		healing.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchHealingTarget);
		healing.PublicAnnouncement.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void SecondNight_LivingKnownWitchWithOnlyPoisonRemaining_WakesAndOffersPoison()
	{
		var (builder, players, healing) = StartWitchCall();
		var witch = players[1];
		var firstNightAttackTarget = players[4];
		var secondNightAttackTarget = players[3];

		var poison = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(healing.CreateResponse([firstNightAttackTarget.Id])));
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(poison.CreateResponse([])));
		var finishNight = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(sleep.CreateResponse()));
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		builder.CompleteDawnPhase().IsSuccess.Should().BeTrue();
		builder.CompleteDayPhaseWithTie().IsSuccess.Should().BeTrue();

		var nightTwoState = builder.GetGameState()!;
		using (new AssertionScope())
		{
			nightTwoState.TurnNumber.Should().Be(2);
			nightTwoState.GetCurrentPhase().Should().Be(GamePhase.Night);
			nightTwoState.GetPlayer(witch.Id).State.Health.Should().Be(PlayerHealth.Alive);
			nightTwoState.GameHistoryLog
				.OfType<OneUseRolePowerCommittedLogEntry>()
				.Select(entry => entry.OneUseResourceId)
				.Should().Equal(WitchRole.HealingResourceId);
		}

		builder.ConfirmNightStart();
		var afterWerewolves = builder.CompleteWerewolfNightActionSubsequentNight(
			secondNightAttackTarget.Id);
		var witchWake = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			afterWerewolves);

		using (new AssertionScope())
		{
			witchWake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
			witchWake.PublicAnnouncement.Should().Be(
				GameStrings.RoleWakesUp.Format(GameStrings.WitchRoleName));
			witchWake.PrivateInstruction.Should().BeNull();
			witchWake.AffectedPlayerIds.Should().Equal(witch.Id);
		}

		var remainingPoison =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(witchWake.CreateResponse()));
		remainingPoison.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchPoisonTarget);
		remainingPoison.AffectedPlayerIds.Should().Equal(witch.Id);
		remainingPoison.PrivateInstruction.Should().Contain(
			GameStrings.WitchPoisonSelectionInstruction);
		remainingPoison.PrivateInstruction.Should().Contain(
			secondNightAttackTarget.Name);
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_KnownEmptyWitch_OmitsTheWholeCall()
	{
		var policy = new SequenceAvailabilityPolicy();
		var (builder, players) = CreateStartedWitchGame(policy);
		builder
			.ArrangeKnownRole(players[1].Id, MainRoleType.Witch)
			.ArrangeKnownWerewolfFactionAgentGroup(players[0].Id)
			.ArrangeEliminatedPlayer(players[1].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var afterWerewolves = builder.CompleteWerewolfNightAction(
			[players[0].Id],
			players[4].Id);

		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterWerewolves);
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		policy.Attempts.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstNight_BothPotionResourcesSpent_OmitsTheWholeCall()
	{
		var policy = new SequenceAvailabilityPolicy();
		var (builder, players) = CreateStartedWitchGame(policy);
		builder
			.ArrangeKnownRole(players[1].Id, MainRoleType.Witch)
			.ArrangeCommittedWitchPotion(
				players[1].Id,
				WitchRole.HealingResourceId,
				NightActionType.WitchSave,
				players[3].Id)
			.ArrangeCommittedWitchPotion(
				players[1].Id,
				WitchRole.PoisonResourceId,
				NightActionType.WitchKill,
				players[2].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();

		var afterWerewolves = builder.CompleteWerewolfNightAction(
			[players[0].Id],
			players[4].Id);

		InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				afterWerewolves)
			.Semantic.Should().Be(
				ModeratorInstructionSemantic.FinishNightActions);
		policy.Attempts.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void FirstNight_OnePotionSpent_OffersOnlyTheRemainingResource(
		bool healingAlreadySpent)
	{
		var policy = new SequenceAvailabilityPolicy(true);
		var (builder, players) = CreateStartedWitchGame(policy);
		var spentResource = healingAlreadySpent
			? WitchRole.HealingResourceId
			: WitchRole.PoisonResourceId;
		var spentAction = healingAlreadySpent
			? NightActionType.WitchSave
			: NightActionType.WitchKill;
		builder
			.ArrangeKnownRole(players[1].Id, MainRoleType.Witch)
			.ArrangeCommittedWitchPotion(
				players[1].Id,
				spentResource,
				spentAction,
				players[3].Id);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction([players[0].Id], players[4].Id);
		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());

		var remainingSelector =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));

		remainingSelector.Semantic.Should().Be(
			healingAlreadySpent
				? ModeratorInstructionSemantic.SelectWitchPoisonTarget
				: ModeratorInstructionSemantic.SelectWitchHealingTarget);
		remainingSelector.PrivateInstruction.Should().Contain(players[4].Name);
		policy.Attempts.Should().ContainSingle();
		AssertPotionAttempt(
			policy.Attempts.Single(),
			players[1],
			healingAlreadySpent
				? WitchRole.PoisonResourceId
				: WitchRole.HealingResourceId);
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(remainingSelector.CreateResponse([])));
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		policy.Attempts.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void NoPhysicalAttackTargets_OmitsHealingAndEvaluatesOnlyPoison()
	{
		var policy = new SequenceAvailabilityPolicy(true);
		var (builder, players) = CreateStartedWitchGame(policy);
		ArrangeKnownWerewolfAgentGroup(
			builder,
			new HashSet<Guid> { players[0].Id });
		builder
			.ArrangeKnownRole(players[0].Id, MainRoleType.SimpleWerewolf)
			.ArrangeEliminatedPlayer(players[0].Id)
			.ArrangeKnownRole(players[1].Id, MainRoleType.Witch);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());

		var poison = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(wake.CreateResponse()));

		poison.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchPoisonTarget);
		poison.PrivateInstruction.Should().Be(
			GameStrings.WitchPoisonSelectionInstruction);
		policy.Attempts.Should().ContainSingle();
		AssertPotionAttempt(
			policy.Attempts.Single(),
			players[1],
			WitchRole.PoisonResourceId);
		MarkTestCompleted();
	}

	[Fact]
	public void NoLegalPotionCandidates_OmitsBothSelectorsWithoutAvailabilityEvaluation()
	{
		var policy = new SequenceAvailabilityPolicy();
		var (builder, players) = CreateStartedWitchGame(policy);
		ArrangeKnownWerewolfAgentGroup(
			builder,
			new HashSet<Guid> { players[0].Id });
		builder
			.ArrangeKnownRole(players[0].Id, MainRoleType.SimpleWerewolf)
			.ArrangeEliminatedPlayer(players[0].Id)
			.ArrangeKnownRole(players[1].Id, MainRoleType.Witch);
		foreach (var player in players.Skip(2))
		{
			builder.ArrangeEliminatedPlayer(player.Id);
		}

		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());

		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(wake.CreateResponse()));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PrivateInstruction.Should().BeNull();
		policy.Attempts.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void PhysicalAttackOnWitch_AllowsSelfHealing()
	{
		var (builder, players, healing) = StartWitchCall(attackTargetIndex: 1);

		healing.SelectablePlayerIds.Should().Equal(players[1].Id);
		var poison = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(healing.CreateResponse([players[1].Id])));
		poison.SelectablePlayerIds.Should().NotContain(players[1].Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.WitchSave &&
				entry.TargetIds!.SequenceEqual(new[] { players[1].Id }));
		MarkTestCompleted();
	}

	[Fact]
	public void PoisonCandidates_RejectActingWitchAndSameNightHealedTarget()
	{
		var (builder, players, healing) = StartWitchCall();
		var poison = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(healing.CreateResponse([players[4].Id])));

		poison.SelectablePlayerIds.Should().NotContain(players[1].Id);
		poison.SelectablePlayerIds.Should().NotContain(players[4].Id);
		Action selfPoison = () => poison.CreateResponse([players[1].Id]);
		Action poisonHealedTarget = () =>
			poison.CreateResponse([players[4].Id]);
		selfPoison.Should().Throw<ArgumentException>();
		poisonHealedTarget.Should().Throw<ArgumentException>();
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public void FirstNight_SequentialOptionalPotions_CommitOnlySelectedResources(
		bool useHealing,
		bool usePoison)
	{
		var (builder, players, healing) = StartWitchCall();
		var witch = players[1];
		var attackTarget = players[4];
		var poisonTarget = players[2];

		healing.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchHealingTarget);
		healing.EmptySelectionOptionLabel.Should().Be(GameStrings.DeclineOption);
		var afterHealing = builder.Process(healing.CreateResponse(
			useHealing ? [attackTarget.Id] : []));
		var poison = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			afterHealing);

		using (new AssertionScope())
		{
			poison.Semantic.Should().Be(
				ModeratorInstructionSemantic.SelectWitchPoisonTarget);
			poison.CountConstraint.Should().BeEquivalentTo(
				NumberRangeConstraint.SingleOptional);
			poison.EmptySelectionOptionLabel.Should().Be(GameStrings.DeclineOption);
			poison.SelectablePlayerIds.Should().NotContain(witch.Id);
			if (useHealing)
			{
				poison.SelectablePlayerIds.Should().NotContain(attackTarget.Id);
			}
			else
			{
				poison.SelectablePlayerIds.Should().Contain(attackTarget.Id);
			}
		}

		var afterPoison = builder.Process(poison.CreateResponse(
			usePoison ? [poisonTarget.Id] : []));
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			afterPoison);
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);

		var committedResources = builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.ToArray();
		committedResources.Should().HaveCount(
			(useHealing ? 1 : 0) + (usePoison ? 1 : 0));
		if (committedResources.Length > 0)
		{
			committedResources.Should().OnlyContain(entry =>
				entry.ActingPlayerId == witch.Id &&
				entry.SourceRole == MainRoleType.Witch &&
				entry.SourcePowerIdentifier == "witch-potions" &&
				entry.PowerInstanceId == witch.Id &&
				entry.PowerInstanceOrigin == RolePowerInstanceOrigin.Native);
		}
		committedResources.Select(entry => entry.OneUseResourceId)
			.Should().OnlyHaveUniqueItems();

		committedResources.Any(
				entry => entry.ActionType == NightActionType.WitchSave)
			.Should().Be(useHealing);
		committedResources.Any(
				entry => entry.ActionType == NightActionType.WitchKill)
			.Should().Be(usePoison);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(RolePowerInstanceOrigin.Borrowed)]
	[InlineData(RolePowerInstanceOrigin.Swapped)]
	public void OwnerQualifiedPotionResource_RejectsExactDuplicateAndSurvivesDistinctOriginCollision(
		RolePowerInstanceOrigin distinctOrigin)
	{
		var (builder, players) = CreateStartedWitchGame();
		var witch = players[1];
		builder.ArrangeCommittedWitchPotion(
			witch.Id,
			WitchRole.HealingResourceId,
			NightActionType.WitchSave,
			players[4].Id);

		Action duplicateNativeSpend = () =>
			builder.ArrangeCommittedWitchPotion(
				witch.Id,
				WitchRole.HealingResourceId,
				NightActionType.WitchSave,
				players[3].Id);
		duplicateNativeSpend.Should().Throw<InvalidOperationException>()
			.WithMessage("*already spent*");

		builder.ArrangeCommittedWitchPotion(
			witch.Id,
			WitchRole.HealingResourceId,
			NightActionType.WitchSave,
			players[3].Id,
			powerInstanceId: witch.Id,
			distinctOrigin);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction([players[0].Id], players[4].Id);
		var identification = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			builder.GetCurrentInstruction());
		var poison = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(identification.CreateResponse([witch.Id])));
		poison.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchPoisonTarget);
		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(poison.CreateResponse([])));
		var finishNight = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(sleep.CreateResponse()));
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		var service = new GameService();

		var gameId = service.RehydrateSession(
			builder.GetGameState()!.Serialize());

		var reconstructed = service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Where(entry =>
				entry.OneUseResourceId == WitchRole.HealingResourceId)
			.ToArray();
		reconstructed.Should().HaveCount(2);
		reconstructed.Select(entry => entry.PowerInstanceOrigin).Should()
			.BeEquivalentTo(new[]
			{
				RolePowerInstanceOrigin.Native,
				distinctOrigin
			});
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(ForeignPotionIdentityDimension.ActingPlayer)]
	[InlineData(ForeignPotionIdentityDimension.SourceRole)]
	[InlineData(ForeignPotionIdentityDimension.SourcePower)]
	public void ForeignActorRoleOrPowerIdentity_DoesNotSpendNativeWitchHealingPotion(
		ForeignPotionIdentityDimension differingDimension)
	{
		var (builder, players) = CreateStartedWitchGame();
		var witch = players[1];
		builder.ArrangeCommittedWitchPotion(
			witch.Id,
			WitchRole.HealingResourceId,
			NightActionType.WitchSave,
			players[3].Id,
			powerInstanceId: witch.Id,
			RolePowerInstanceOrigin.Native,
			actingPlayerId:
				differingDimension == ForeignPotionIdentityDimension.ActingPlayer
					? players[2].Id
					: witch.Id,
			sourceRole:
				differingDimension == ForeignPotionIdentityDimension.SourceRole
					? MainRoleType.Seer
					: MainRoleType.Witch,
			sourcePowerIdentifier:
				differingDimension == ForeignPotionIdentityDimension.SourcePower
					? "foreign-one-use-power"
					: "witch-potions");
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction([players[0].Id], players[4].Id);
		var identification = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			builder.GetCurrentInstruction());

		var healing = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(identification.CreateResponse([witch.Id])));
		healing.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchHealingTarget);
		var poison = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.Process(healing.CreateResponse([players[4].Id])));

		poison.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchPoisonTarget);
		var service = new GameService();
		var gameId = service.RehydrateSession(
			builder.GetGameState()!.Serialize());
		var committedHealingIdentities = service.GetGameStateView(gameId)!
			.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Where(entry =>
				entry.OneUseResourceId == WitchRole.HealingResourceId)
			.Select(entry => entry.ResourceIdentity)
			.ToArray();
		committedHealingIdentities.Should().HaveCount(2);
		committedHealingIdentities.Should().OnlyHaveUniqueItems();
		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedWitchIdentification_RehydrateResumesExactHealingWithoutRedisclosingAttackRoster()
	{
		var (builder, players) = CreateStartedWitchGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction([players[0].Id], players[4].Id);
		var identification = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			builder.GetCurrentInstruction());
		var expectedHealing =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(identification.CreateResponse([players[1].Id])));
		var service = new GameService();

		var gameId = service.RehydrateSession(
			builder.GetGameState()!.Serialize());

		var recoveredHealing = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			service.GetCurrentInstruction(gameId));
		using (new AssertionScope())
		{
			recoveredHealing.InstructionId.Should().Be(expectedHealing.InstructionId);
			recoveredHealing.Semantic.Should().Be(
				ModeratorInstructionSemantic.SelectWitchHealingTarget);
			recoveredHealing.SelectablePlayerIds.Should()
				.BeEquivalentTo(expectedHealing.SelectablePlayerIds);
			recoveredHealing.PrivateInstruction.Should()
				.Be(expectedHealing.PrivateInstruction);
		}

		Action replayIdentification = () =>
			service.ProcessInstruction(
				gameId,
				identification.CreateResponse([players[1].Id]));
		replayIdentification.Should().Throw<InvalidOperationException>();

		var poison = service.ProcessInstruction(
				gameId,
				recoveredHealing.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		poison.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchPoisonTarget);
		poison.SelectablePlayerIds.Should().Contain(players[4].Id);
		poison.PrivateInstruction.Should().Be(
			GameStrings.WitchPoisonSelectionInstruction,
			"the recovered healing selector already disclosed the attack roster");
		service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty("declining healing must preserve its resource");
		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedHealingCommit_RehydrateResumesAtExactPoisonInstructionWithoutReapplying()
	{
		var (builder, players, healing) = StartWitchCall();
		var healingResponse = healing.CreateResponse([players[4].Id]);
		var expectedPoison =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(healingResponse));
		var service = new GameService();

		var gameId = service.RehydrateSession(
			builder.GetGameState()!.Serialize());

		var recoveredPoison = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			service.GetCurrentInstruction(gameId));
		using (new AssertionScope())
		{
			recoveredPoison.InstructionId.Should().Be(expectedPoison.InstructionId);
			recoveredPoison.Semantic.Should().Be(
				ModeratorInstructionSemantic.SelectWitchPoisonTarget);
			recoveredPoison.SelectablePlayerIds.Should()
				.BeEquivalentTo(expectedPoison.SelectablePlayerIds);
			service.GetGameStateView(gameId)!.GameHistoryLog
				.OfType<OneUseRolePowerCommittedLogEntry>()
				.Should().ContainSingle(entry =>
					entry.ActionType == NightActionType.WitchSave &&
					entry.OneUseResourceId == WitchRole.HealingResourceId);
		}

		Action replayHealing = () =>
			service.ProcessInstruction(gameId, healingResponse);
		replayHealing.Should().Throw<InvalidOperationException>();
		service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle();

		var sleep = service.ProcessInstruction(
				gameId,
				recoveredPoison.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PrivateInstruction.Should().BeNull(
			"the attack roster was already disclosed by the healing selector");
		MarkTestCompleted();
	}

	[Fact]
	public void AcceptedPoisonCommit_RehydrateResumesAtExactSleepWithoutReapplying()
	{
		var (builder, players, healing) = StartWitchCall();
		var poison =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(healing.CreateResponse([players[4].Id])));
		var poisonResponse = poison.CreateResponse([players[2].Id]);
		var expectedSleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(poisonResponse));
		var service = new GameService();

		var gameId = service.RehydrateSession(
			builder.GetGameState()!.Serialize());

		var recoveredSleep = InstructionAssert.ExpectType<ConfirmationInstruction>(
			service.GetCurrentInstruction(gameId));
		recoveredSleep.InstructionId.Should().Be(expectedSleep.InstructionId);
		recoveredSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		recoveredSleep.PrivateInstruction.Should().BeNull();
		service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().HaveCount(2);
		Action replayPoison = () =>
			service.ProcessInstruction(gameId, poisonResponse);
		replayPoison.Should().Throw<InvalidOperationException>();

		var finishNight = service.ProcessInstruction(
				gameId,
				recoveredSleep.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		service.GetGameStateView(gameId)!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().HaveCount(2);
		MarkTestCompleted();
	}

	[Fact]
	public void RehydrateSession_MismatchedOneUseResourceIdentity_RejectsBeforeContinuation()
	{
		var (builder, players, healing) = StartWitchCall();
		builder.Process(healing.CreateResponse([players[4].Id]));
		var recoveryPayload = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize())
			.MismatchOneUseResource(Guid.NewGuid())
			.Serialize();
		var service = new GameService();

		Action rehydrate = () =>
			service.RehydrateSession(recoveryPayload);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*latest committed One-Use Resource action*");
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(OmittedPotionIdentityDimension.SourceRole)]
	[InlineData(OmittedPotionIdentityDimension.PowerInstanceOrigin)]
	public void RehydrateSession_OmittedEnumIdentityField_RejectsBeforeContinuation(
		OmittedPotionIdentityDimension omittedDimension)
	{
		var (builder, players, healing) = StartWitchCall();
		builder.Process(healing.CreateResponse([players[4].Id]));
		var payloadDriver = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize());
		switch (omittedDimension)
		{
			case OmittedPotionIdentityDimension.SourceRole:
				payloadDriver.OmitSourceRole();
				break;
			case OmittedPotionIdentityDimension.PowerInstanceOrigin:
				payloadDriver.OmitPowerInstanceOrigin();
				break;
			default:
				throw new ArgumentOutOfRangeException(
					nameof(omittedDimension),
					omittedDimension,
					null);
		}

		var service = new GameService();
		Action rehydrate = () =>
			service.RehydrateSession(payloadDriver.Serialize());

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*structurally invalid*");
		MarkTestCompleted();
	}

	[Fact]
	public void RehydrateSession_ImpossibleCommittedActionContinuation_Rejects()
	{
		var (builder, players, healing) = StartWitchCall();
		builder.Process(healing.CreateResponse([players[4].Id]));
		var recoveryPayload = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize())
			.RewriteLatestOneUseAction(NightActionType.WitchKill)
			.Serialize();
		var service = new GameService();

		Action rehydrate = () =>
			service.RehydrateSession(recoveryPayload);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*invalid One-Use Role Power identity*");
		MarkTestCompleted();
	}

	[Fact]
	public void RehydrateSession_ContradictoryInstructionShape_Rejects()
	{
		var (builder, players, healing) = StartWitchCall();
		builder.Process(healing.CreateResponse([players[4].Id]));
		var recoveryPayload = RecoveryPayloadTestDriver
			.Parse(builder.GetGameState()!.Serialize())
			.ReplacePendingInstructionWithConfirmation()
			.Serialize();
		var service = new GameService();

		Action rehydrate = () =>
			service.RehydrateSession(recoveryPayload);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*invalid type 'ConfirmationInstruction'*");
		MarkTestCompleted();
	}

	[Fact]
	public void PerResourceAvailability_DeniedHealingAllowedPoison_EvaluatesEachOpportunityOnce()
	{
		var policy = new SequenceAvailabilityPolicy(false, true);
		var (builder, players, instruction) = StartWitchCall(policy);

		instruction.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchPoisonTarget);
		instruction.PrivateInstruction.Should().Contain(players[4].Name);
		policy.Attempts.Should().HaveCount(2);
		AssertPotionAttempt(
			policy.Attempts[0],
			players[1],
			WitchRole.HealingResourceId);
		AssertPotionAttempt(
			policy.Attempts[1],
			players[1],
			WitchRole.PoisonResourceId);

		builder.Process(instruction.CreateResponse([]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>();
		policy.Attempts.Should().HaveCount(2);
		MarkTestCompleted();
	}

	[Fact]
	public void PerResourceAvailability_AllowedHealingDeniedPoison_OmitsOnlyPoisonWithoutReevaluation()
	{
		var policy = new SequenceAvailabilityPolicy(true, false);
		var (builder, players, healing) = StartWitchCall(policy);
		policy.Attempts.Should().ContainSingle();

		var sleep = InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
			builder.Process(healing.CreateResponse([])));

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PrivateInstruction.Should().BeNull(
			"the healing selector already disclosed the attack roster");
		policy.Attempts.Should().HaveCount(2);
		AssertPotionAttempt(
			policy.Attempts[0],
			players[1],
			WitchRole.HealingResourceId);
		AssertPotionAttempt(
			policy.Attempts[1],
			players[1],
			WitchRole.PoisonResourceId);
		MarkTestCompleted();
	}

	[Fact]
	public void PerResourceAvailability_BothDenied_SleepsWithPrivateAttackRoster()
	{
		var policy = new SequenceAvailabilityPolicy(false, false);
		var (builder, players, instruction) =
			StartWitchCall<ConfirmationInstruction>(policy);

		var sleep = instruction;
		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.WitchRoleName));
		sleep.PrivateInstruction.Should().Contain(players[4].Name);
		sleep.AffectedPlayerIds.Should().Equal(players[1].Id);
		policy.Attempts.Should().HaveCount(2);
		MarkTestCompleted();
	}

	private (GameTestBuilder Builder, IPlayer[] Players, TInstruction Instruction)
		StartWitchCall<TInstruction>(
			IRolePowerAvailabilityPolicy? policy = null,
			int attackTargetIndex = 4)
		where TInstruction : ModeratorInstruction
	{
		var (builder, players) = CreateStartedWitchGame(policy);
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction(
			[players[0].Id],
			players[attackTargetIndex].Id);
		var identification = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			builder.GetCurrentInstruction());
		var instruction = InstructionAssert.ExpectSuccessWithType<TInstruction>(
			builder.Process(identification.CreateResponse([players[1].Id])));

		return (builder, players, instruction);
	}

	private (GameTestBuilder Builder, IPlayer[] Players, SelectPlayersInstruction Healing)
		StartWitchCall(
			IRolePowerAvailabilityPolicy? policy = null,
			int attackTargetIndex = 4) =>
		StartWitchCall<SelectPlayersInstruction>(policy, attackTargetIndex);

	private (GameTestBuilder Builder, IPlayer[] Players)
		CreateStartedWitchGame(
			IRolePowerAvailabilityPolicy? policy = null)
	{
		var builder = CreateBuilder()
			.WithOptionalRolePowerAvailabilityPolicy(policy)
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Witch,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		return (builder, builder.GetGameState()!.GetPlayers().ToArray());
	}

	private static void ArrangeKnownWerewolfAgentGroup(
		GameTestBuilder builder,
		IReadOnlySet<Guid> werewolfAgentIds)
	{
		var session = builder.GetGameState()!;
		var boundary = new FactionFactEffectiveBoundary(
			session.TurnNumber,
			session.GetCurrentPhase(),
			session.GameHistoryLog.Count());
		var facts = session.GetPlayers()
			.Select(player => FactionFact.Agent(
				player.Id,
				Faction.Werewolf,
				werewolfAgentIds.Contains(player.Id)
					? FactionAgentKnowledge.KnownAgent
					: FactionAgentKnowledge.KnownNonAgent,
				boundary))
			.ToArray();
		builder.ArrangeExplicitFactionTransition(
			"witch-test-known-werewolf-agent-group",
			facts);
	}

	private static void AssertPotionAttempt(
		RolePowerAttempt attempt,
		IPlayer witch,
		Guid resourceId)
	{
		attempt.ActingPlayer.Id.Should().Be(witch.Id);
		attempt.SourceRole.Should().Be(MainRoleType.Witch);
		attempt.SourcePower.Identifier.Should().Be(
			new RolePowerIdentifier("witch-potions"));
		attempt.PowerInstance.Id.Should().Be(witch.Id);
		attempt.PowerInstance.Origin.Should().Be(RolePowerInstanceOrigin.Native);
		attempt.OneUseResource.Should().NotBeNull();
		attempt.OneUseResource!.Id.Should().Be(resourceId);
		attempt.OneUseResource.OwningPowerInstance.Should()
			.Be(attempt.PowerInstance);
	}

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
