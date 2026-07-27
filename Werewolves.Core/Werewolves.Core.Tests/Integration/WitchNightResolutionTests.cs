using FluentAssertions;
using FluentAssertions.Execution;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public sealed class WitchNightResolutionTests : DiagnosticTestBase
{
	public WitchNightResolutionTests(ITestOutputHelper output) : base(output) { }

	[Fact]
	public void MultipleTargets_ResolveCollectiveWhiteAndBigBadInCanonicalGlobalOrder()
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder
			.ArrangeCurrentRole(players[0].Id, MainRoleType.SimpleWerewolf)
			.ArrangeCurrentRole(players[1].Id, MainRoleType.SimpleVillager)
			.ArrangeCurrentRole(players[3].Id, MainRoleType.Elder)
			.ArrangeCurrentRole(players[4].Id, MainRoleType.SimpleVillager);

		CompleteNight(
			builder,
			players[0],
			players[3],
			afterCollective => afterCollective
				.ArrangeNightAction(
					NightActionType.WhiteWerewolfVictimSelection,
					players[0].Id)
				.ArrangeNightAction(
					NightActionType.BigBadWolfVictimSelection,
					players[1].Id));

		builder.GetGameState()!.GameHistoryLog
			.Select(entry => entry switch
			{
				StatusEffectLogEntry
				{
					PlayerId: var playerId,
					EffectType: StatusEffectTypes.ElderProtectionLost,
					IsActive: true
				} when playerId == players[3].Id => "collective",
				DawnVictimDeterminedLogEntry { PlayerId: var playerId }
					when playerId == players[0].Id => "white",
				DawnVictimDeterminedLogEntry { PlayerId: var playerId }
					when playerId == players[1].Id => "big-bad",
				_ => null
			})
			.Where(operation => operation != null)
			.Should().Equal("collective", "white", "big-bad");
		MarkTestCompleted();
	}

	[Fact]
	public void FreshElder_ResistsCollectiveInfection()
	{
		var (builder, elder) = CreateScenario(MainRoleType.Elder);
		var werewolf = builder.GetGameState()!.GetPlayers().First();

		CompleteNight(
			builder,
			werewolf,
			elder,
			afterCollective => afterCollective.ArrangeNightAction(
				NightActionType.AccursedWolfFatherInfection,
				elder.Id));

		elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeTrue();
		elder.State.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)
			.Should().BeFalse();
		AssertNoDawnVictim(builder, elder.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void SpentElder_IsInfectedByCollectiveReplacementWithoutDawnVictim()
	{
		var (builder, elder) = CreateScenario(
			MainRoleType.Elder,
			elderProtectionAlreadyLost: true);
		var werewolf = builder.GetGameState()!.GetPlayers().First();

		CompleteNight(
			builder,
			werewolf,
			elder,
			afterCollective => afterCollective.ArrangeNightAction(
				NightActionType.AccursedWolfFatherInfection,
				elder.Id));

		elder.State.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)
			.Should().BeTrue();
		AssertNoDawnVictim(builder, elder.Id);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(NightActionType.WerewolfVictimSelection)]
	[InlineData(NightActionType.WhiteWerewolfVictimSelection)]
	[InlineData(NightActionType.BigBadWolfVictimSelection)]
	public void Defender_BlocksRulesValidPhysicalAttackWithoutConsumingElderProtection(
		NightActionType attackType)
	{
		var (builder, elder) = CreateScenario(MainRoleType.Elder);
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var collectiveTarget = attackType ==
			NightActionType.WerewolfVictimSelection
				? elder
				: players[2];
		builder
			.ArrangeCurrentRole(
				players[2].Id,
				MainRoleType.SimpleVillager)
			.ArrangeNightAction(NightActionType.DefenderProtect, elder.Id);
		if (attackType == NightActionType.WhiteWerewolfVictimSelection)
		{
			builder.ArrangeStatusEffect(
				elder.Id,
				StatusEffectTypes.LycanthropyInfection);
		}

		CompleteNight(
			builder,
			werewolf,
			collectiveTarget,
			afterCollective =>
			{
				if (attackType != NightActionType.WerewolfVictimSelection)
				{
					afterCollective.ArrangeNightAction(attackType, elder.Id);
				}
			});

		elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeFalse();
		AssertNoDawnVictim(builder, elder.Id);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, true)]
	public void Defender_DoesNotBlockInfection(
		bool elderProtectionAlreadyLost,
		bool expectedInfection)
	{
		var (builder, elder) = CreateScenario(
			MainRoleType.Elder,
			elderProtectionAlreadyLost);
		var werewolf = builder.GetGameState()!.GetPlayers().First();
		builder.ArrangeNightAction(NightActionType.DefenderProtect, elder.Id);

		CompleteNight(
			builder,
			werewolf,
			elder,
			afterCollective => afterCollective.ArrangeNightAction(
				NightActionType.AccursedWolfFatherInfection,
				elder.Id));

		elder.State.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)
			.Should().Be(expectedInfection);
		elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeTrue();
		AssertNoDawnVictim(builder, elder.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void Healing_FreshElderAfterOnePhysicalHit_RestoresProtection()
	{
		var (builder, elder) = CreateScenario(MainRoleType.Elder);
		var werewolf = builder.GetGameState()!.GetPlayers().First();

		CompleteNight(
			builder,
			werewolf,
			elder,
			afterCollective => afterCollective.ArrangeNightAction(
				NightActionType.WitchSave,
				elder.Id));

		elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeFalse();
		AssertNoDawnVictim(builder, elder.Id);
		var elderProtectionLogs = builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Where(entry =>
				entry.PlayerId == elder.Id &&
				entry.EffectType == StatusEffectTypes.ElderProtectionLost)
			.ToArray();
		elderProtectionLogs.Should().HaveCount(2);
		elderProtectionLogs.Select(entry => entry.IsActive)
			.Should().Equal(true, false);
		MarkTestCompleted();
	}

	[Fact]
	public void Healing_SpentElderAfterPhysicalHit_PreventsDeathWithoutRestoringProtection()
	{
		var (builder, elder) = CreateScenario(
			MainRoleType.Elder,
			elderProtectionAlreadyLost: true);
		var werewolf = builder.GetGameState()!.GetPlayers().First();

		CompleteNight(
			builder,
			werewolf,
			elder,
			afterCollective => afterCollective.ArrangeNightAction(
				NightActionType.WitchSave,
				elder.Id));

		elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeTrue();
		AssertNoDawnVictim(builder, elder.Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Where(entry =>
				entry.PlayerId == elder.Id &&
				entry.EffectType == StatusEffectTypes.ElderProtectionLost)
			.Should().ContainSingle(entry => entry.IsActive);
		MarkTestCompleted();
	}

	[Fact]
	public void Healing_ResistedInfection_RestoresProtectionWithoutInfection()
	{
		var (builder, elder) = CreateScenario(MainRoleType.Elder);
		var werewolf = builder.GetGameState()!.GetPlayers().First();

		CompleteNight(
			builder,
			werewolf,
			elder,
			afterCollective => afterCollective
				.ArrangeNightAction(
					NightActionType.AccursedWolfFatherInfection,
					elder.Id)
				.ArrangeNightAction(NightActionType.WitchSave, elder.Id));

		using (new AssertionScope())
		{
			elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
				.Should().BeFalse();
			elder.State.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)
				.Should().BeFalse();
		}
		AssertNoDawnVictim(builder, elder.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void Healing_SuccessfulInfection_DoesNotCureInfectionOrRefundPriorProtection()
	{
		var (builder, elder) = CreateScenario(
			MainRoleType.Elder,
			elderProtectionAlreadyLost: true);
		var werewolf = builder.GetGameState()!.GetPlayers().First();

		CompleteNight(
			builder,
			werewolf,
			elder,
			afterCollective => afterCollective
				.ArrangeNightAction(
					NightActionType.AccursedWolfFatherInfection,
					elder.Id)
				.ArrangeNightAction(NightActionType.WitchSave, elder.Id));

		elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
			.Should().BeTrue();
		elder.State.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)
			.Should().BeTrue();
		AssertNoDawnVictim(builder, elder.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void Poison_RemainsLethalDespiteDefenderWhileHealingSavesDifferentPhysicalTarget()
	{
		var (builder, protectedTarget) = CreateScenario(
			MainRoleType.SimpleVillager);
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var poisonTarget = players[2];
		builder
			.ArrangeCurrentRole(
				poisonTarget.Id,
				MainRoleType.SimpleVillager)
			.ArrangeNightAction(
				NightActionType.DefenderProtect,
				poisonTarget.Id);

		CompleteNight(
			builder,
			werewolf,
			protectedTarget,
			afterCollective => afterCollective
				.ArrangeNightAction(
					NightActionType.WitchSave,
					protectedTarget.Id)
				.ArrangeNightAction(
					NightActionType.WitchKill,
					poisonTarget.Id));

		AssertNoDawnVictim(builder, protectedTarget.Id);
		AssertDawnVictim(
			builder,
			poisonTarget.Id,
			EliminationReason.WitchKill);
		MarkTestCompleted();
	}

	[Fact]
	public void SameTarget_RustySwordTakesPrecedenceOverLaterWitchKill()
	{
		var (builder, target) = CreateScenario(MainRoleType.SimpleVillager);
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var collectiveTarget = players[2];
		builder.ArrangeCurrentRole(
			collectiveTarget.Id,
			MainRoleType.SimpleVillager);

		CompleteNight(
			builder,
			werewolf,
			collectiveTarget,
			afterCollective => afterCollective
				.ArrangeNightAction(NightActionType.RustySword, target.Id)
				.ArrangeNightAction(NightActionType.WitchKill, target.Id));

		AssertDawnVictim(builder, target.Id, EliminationReason.RustySword);
		MarkTestCompleted();
	}

	private (GameTestBuilder Builder, IPlayer Target) CreateScenario(
		MainRoleType targetRole,
		bool elderProtectionAlreadyLost = false)
	{
		var builder = CreateBuilder()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder
			.ArrangeCurrentRole(players[0].Id, MainRoleType.SimpleWerewolf)
			.ArrangeCurrentRole(players[1].Id, targetRole);
		if (elderProtectionAlreadyLost)
		{
			builder.ArrangeStatusEffect(
				players[1].Id,
				StatusEffectTypes.ElderProtectionLost);
		}

		return (builder, players[1]);
	}

	private static void CompleteNight(
		GameTestBuilder builder,
		IPlayer werewolf,
		IPlayer collectiveTarget,
		Action<GameTestBuilder>? arrangeAfterCollective = null)
	{
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var identification = InstructionAssert.ExpectType<SelectPlayersInstruction>(
			builder.GetCurrentInstruction());
		var targetSelection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(
					identification.CreateResponse([werewolf.Id])));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(
					targetSelection.CreateResponse([collectiveTarget.Id])));
		arrangeAfterCollective?.Invoke(builder);
		var finishNight =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(sleep.CreateResponse()));
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		builder.Process(finishNight.CreateResponse());
	}

	private static void AssertDawnVictim(
		GameTestBuilder builder,
		Guid playerId,
		EliminationReason reason) =>
		builder.GetGameState()!.GameHistoryLog
			.OfType<DawnVictimDeterminedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.PlayerId == playerId &&
				entry.Reason == reason);

	private static void AssertNoDawnVictim(
		GameTestBuilder builder,
		Guid playerId) =>
		builder.GetGameState()!.GameHistoryLog
			.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == playerId);
}
