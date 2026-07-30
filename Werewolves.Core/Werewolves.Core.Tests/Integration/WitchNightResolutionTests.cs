using FluentAssertions;
using FluentAssertions.Execution;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
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
		AssertNoSuccessfulInfectionTransition(builder);
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
		AssertSuccessfulInfectionTransition(builder, elder.Id);
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
		if (expectedInfection)
		{
			AssertSuccessfulInfectionTransition(builder, elder.Id);
		}
		else
		{
			AssertNoSuccessfulInfectionTransition(builder);
		}
		AssertNoDawnVictim(builder, elder.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void PublicFlow_DefenderDoesNotBlockSuccessfulInfectionOfOrdinaryVillager()
	{
		var (builder, werewolf, wolfFather, target) =
			CreatePublicInfectionScenario(MainRoleType.SimpleVillager);
		builder.ArrangeNightAction(
			NightActionType.DefenderProtect,
			target.Id);

		CompletePublicInfectionNight(
			builder,
			werewolf,
			wolfFather,
			target);

		target.State.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)
			.Should().BeTrue();
		AssertSuccessfulInfectionTransition(builder, target.Id);
		AssertNoDawnVictim(builder, target.Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType ==
					NightActionType.WerewolfVictimSelection &&
				entry.TargetIds!.SequenceEqual(new[] { target.Id }));
		builder.GetGameState()!.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Count(entry =>
				entry.Source.Kind ==
					FactionFactSourceKind.InitialBeneficiaryClosure)
			.Should().Be(1);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, true)]
	public void PublicFlow_ElderProtectionDeterminesInfectionOutcome(
		bool elderProtectionAlreadyLost,
		bool expectedInfection)
	{
		var (builder, werewolf, wolfFather, elder) =
			CreatePublicInfectionScenario(MainRoleType.Elder);
		if (elderProtectionAlreadyLost)
		{
			builder.ArrangeStatusEffect(
				elder.Id,
				StatusEffectTypes.ElderProtectionLost);
		}

		CompletePublicInfectionNight(
			builder,
			werewolf,
			wolfFather,
			elder);

		using (new AssertionScope())
		{
			elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
				.Should().BeTrue();
			elder.State.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)
				.Should().Be(expectedInfection);
		}
		if (expectedInfection)
		{
			AssertSuccessfulInfectionTransition(builder, elder.Id);
		}
		else
		{
			AssertNoSuccessfulInfectionTransition(builder);
		}
		AssertNoDawnVictim(builder, elder.Id);
		MarkTestCompleted();
	}

	[Fact]
	public void PublicFlow_FreshElderInfectionHealedByWitchRestoresProtectionWithoutConversion()
	{
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				MainRoleType.Witch,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var wolfFather = players[1];
		var witch = players[2];
		var elder = players[3];
		builder
			.ArrangeKnownRole(witch.Id, MainRoleType.Witch)
			.ArrangeCurrentRole(elder.Id, MainRoleType.Elder);

		CompletePublicInfectionNight(
			builder,
			werewolf,
			wolfFather,
			elder,
			witch);

		using (new AssertionScope())
		{
			elder.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
				.Should().BeFalse();
			elder.State.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)
				.Should().BeFalse();
		}
		AssertNoSuccessfulInfectionTransition(builder);
		AssertNoDawnVictim(builder, elder.Id);
		builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Where(entry =>
				entry.PlayerId == elder.Id &&
				entry.EffectType == StatusEffectTypes.ElderProtectionLost)
			.Select(entry => entry.IsActive)
			.Should().Equal(true, false);
		builder.GetGameState()!.GameHistoryLog
			.OfType<StatusEffectLogEntry>()
			.Should().NotContain(entry =>
				entry.PlayerId == elder.Id &&
				entry.EffectType == StatusEffectTypes.LycanthropyInfection);
		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.SourceRole == MainRoleType.AccursedWolfFather &&
				entry.ActionType ==
					NightActionType.AccursedWolfFatherInfection &&
				entry.TargetIds!.SequenceEqual(new[] { elder.Id }));
		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.SourceRole == MainRoleType.Witch &&
				entry.ActionType == NightActionType.WitchSave &&
				entry.TargetIds!.SequenceEqual(new[] { elder.Id }));
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void InvalidInfectionIntent_ThrowsBeforeDawnConsequences(
		bool duplicateIntent)
	{
		var (builder, target) = CreateScenario(MainRoleType.SimpleVillager);
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var werewolf = players[0];
		var finishNight = PrepareNightResolution(
			builder,
			werewolf,
			target,
			afterCollective =>
			{
				afterCollective.ArrangeNightAction(
					NightActionType.AccursedWolfFatherInfection,
					duplicateIntent ? target.Id : players[2].Id);
				if (duplicateIntent)
				{
					afterCollective.ArrangeNightAction(
						NightActionType.AccursedWolfFatherInfection,
						target.Id);
				}
			});

		var act = () => builder.Process(finishNight.CreateResponse());

		act.Should().Throw<InvalidOperationException>()
			.WithMessage(
				"*does not match one retained collective victim*");
		builder.GetGameState()!.GetPlayers()
			.Should().OnlyContain(player =>
				!player.State.HasStatusEffect(
					StatusEffectTypes.LycanthropyInfection));
		AssertNoSuccessfulInfectionTransition(builder);
		builder.GetGameState()!.GameHistoryLog
			.OfType<DawnVictimDeterminedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void PublicFlow_InfectedWitchPreservesIdentityPowerRelationshipsAndDominantBeneficiary()
	{
		var (builder, werewolf, wolfFather, target) =
			CreatePublicInfectionScenario(MainRoleType.Witch);
		builder
			.ArrangeKnownRole(target.Id, MainRoleType.Witch)
			.ArrangeStatusEffect(target.Id, StatusEffectTypes.Sheriff)
			.ArrangeStatusEffect(target.Id, StatusEffectTypes.Lovers)
			.ArrangeStatusEffect(target.Id, StatusEffectTypes.Charmed);
		var sessionBefore = builder.GetGameState()!;
		var boundary = new FactionFactEffectiveBoundary(
			sessionBefore.TurnNumber,
			sessionBefore.GetCurrentPhase(),
			sessionBefore.GameHistoryLog.Count());
		builder.ArrangeExplicitFactionTransition(
			"dominant-villager-beneficiary",
			FactionFact.Beneficiary(
				target.Id,
				Faction.Villager,
				boundary,
				beneficiaryPrecedence: 1));
		var stateBefore = target.State;
		var identityBefore = (
			stateBefore.CurrentRole,
			stateBefore.PhysicalCharacterCardRole,
			stateBefore.ModeratorKnownRole,
			stateBefore.PubliclyRevealedRole);

		CompletePublicInfectionNight(
			builder,
			werewolf,
			wolfFather,
			target,
			target);

		var stateAfter = target.State;
		(
			stateAfter.CurrentRole,
			stateAfter.PhysicalCharacterCardRole,
			stateAfter.ModeratorKnownRole,
			stateAfter.PubliclyRevealedRole)
			.Should().Be(identityBefore);
		using (new AssertionScope())
		{
			stateAfter.HasStatusEffect(StatusEffectTypes.Sheriff)
				.Should().BeTrue();
			stateAfter.HasStatusEffect(StatusEffectTypes.Lovers)
				.Should().BeTrue();
			stateAfter.HasStatusEffect(StatusEffectTypes.Charmed)
				.Should().BeTrue();
		}
		stateAfter.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)
			.Should().BeTrue();
		stateAfter.FactionBeneficiary.Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Villager));
		stateAfter.GetFactionAgentKnowledge(Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.KnownAgent);
		AssertSuccessfulInfectionTransition(builder, target.Id);
		var infectionBeneficiary = builder.GetGameState()!.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Single(entry =>
				entry.Source.Identifier ==
					"accursed-wolf-father-infection")
			.Facts.Single(fact =>
				fact.Type == FactionFactType.Beneficiary);
		infectionBeneficiary.Faction.Should().Be(Faction.Werewolf);
		infectionBeneficiary.BeneficiaryPrecedence.Should().Be(0);
		builder.GetGameState()!.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActingPlayerId == target.Id &&
				entry.SourceRole == MainRoleType.Witch &&
				entry.ActionType == NightActionType.WitchSave &&
				entry.TargetIds!.SequenceEqual(new[] { target.Id }));

		var history = builder.GetGameState()!.GameHistoryLog.ToArray();
		var collectiveIndex = Array.FindIndex(
			history,
			entry => entry is NightActionLogEntry
			{
				ActionType: NightActionType.WerewolfVictimSelection
			});
		var infectionIndex = Array.FindIndex(
			history,
			entry => entry is OneUseRolePowerCommittedLogEntry
			{
				ActionType: NightActionType.AccursedWolfFatherInfection
			});
		var infectionTransition = history
			.OfType<FactionFactsCommittedLogEntry>()
			.Single(entry =>
				entry.Source.Identifier ==
					"accursed-wolf-father-infection");
		infectionTransition.CurrentPhase.Should().Be(GamePhase.Dawn);
		infectionTransition.Facts.Should().OnlyContain(fact =>
			fact.EffectiveBoundary.TurnNumber ==
				infectionTransition.TurnNumber &&
			fact.EffectiveBoundary.Phase == GamePhase.Night &&
			fact.EffectiveBoundary.Order == infectionIndex);
		infectionIndex.Should().BeGreaterThan(collectiveIndex);
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
		AssertNoSuccessfulInfectionTransition(builder);
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
		AssertSuccessfulInfectionTransition(builder, elder.Id);
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

	private (
		GameTestBuilder Builder,
		IPlayer Werewolf,
		IPlayer WolfFather,
		IPlayer Target) CreatePublicInfectionScenario(
			MainRoleType targetRole)
	{
		var configuredTargetRole = targetRole == MainRoleType.Witch
			? MainRoleType.Witch
			: MainRoleType.SimpleVillager;
		var builder = CreateBuilder()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.AccursedWolfFather,
				configuredTargetRole,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		if (targetRole != configuredTargetRole)
		{
			builder.ArrangeCurrentRole(players[2].Id, targetRole);
		}

		return (builder, players[0], players[1], players[2]);
	}

	private static void CompletePublicInfectionNight(
		GameTestBuilder builder,
		IPlayer werewolf,
		IPlayer wolfFather,
		IPlayer target,
		IPlayer? witch = null)
	{
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		builder.CompleteWerewolfNightAction(
			[werewolf.Id, wolfFather.Id],
			target.Id);
		builder.CompleteAccursedWolfFatherNightAction(
			wolfFather.Id,
			infectsVictim: true);

		if (witch != null)
		{
			var wake = InstructionAssert.ExpectType<ConfirmationInstruction>(
				builder.GetCurrentInstruction());
			wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
			var healing =
				InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
					builder.Process(wake.CreateResponse()));
			healing.Semantic.Should().Be(
				ModeratorInstructionSemantic.SelectWitchHealingTarget);
			var poison =
				InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
					builder.Process(healing.CreateResponse([target.Id])));
			poison.Semantic.Should().Be(
				ModeratorInstructionSemantic.SelectWitchPoisonTarget);
			var sleep =
				InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
					builder.Process(poison.CreateResponse([])));
			sleep.Semantic.Should().Be(
				ModeratorInstructionSemantic.PutRoleToSleep);
			builder.Process(sleep.CreateResponse());
		}

		var finishNight = InstructionAssert.ExpectType<ConfirmationInstruction>(
			builder.GetCurrentInstruction());
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		builder.Process(finishNight.CreateResponse()).IsSuccess.Should().BeTrue();
		builder.GetGameState()!.GameHistoryLog
			.OfType<PhaseTransitionLogEntry>()
			.Should().Contain(entry => entry.CurrentPhase == GamePhase.Dawn);
	}

	private static void CompleteNight(
		GameTestBuilder builder,
		IPlayer werewolf,
		IPlayer collectiveTarget,
		Action<GameTestBuilder>? arrangeAfterCollective = null)
	{
		var finishNight = PrepareNightResolution(
			builder,
			werewolf,
			collectiveTarget,
			arrangeAfterCollective);
		builder.Process(finishNight.CreateResponse());
	}

	private static ConfirmationInstruction PrepareNightResolution(
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
		return finishNight;
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

	private static void AssertSuccessfulInfectionTransition(
		GameTestBuilder builder,
		Guid playerId)
	{
		var transition = builder.GetGameState()!.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Where(entry =>
				entry.Source.Kind ==
					FactionFactSourceKind.ExplicitTransition &&
				entry.Source.Identifier ==
					"accursed-wolf-father-infection")
			.Should().ContainSingle()
			.Subject;
		transition.CurrentPhase.Should().Be(GamePhase.Dawn);
		transition.Facts.Should().HaveCount(2);
		transition.Facts.Should().ContainSingle(fact =>
			fact.PlayerId == playerId &&
			fact.Type == FactionFactType.Beneficiary &&
			fact.Faction == Faction.Werewolf);
		transition.Facts.Should().ContainSingle(fact =>
			fact.PlayerId == playerId &&
			fact.Type == FactionFactType.Agent &&
			fact.Faction == Faction.Werewolf &&
			fact.AgentKnowledge ==
				FactionAgentKnowledge.KnownAgent);
		transition.Facts
			.Select(fact => fact.EffectiveBoundary)
			.Distinct()
			.Should().ContainSingle();
	}

	private static void AssertNoSuccessfulInfectionTransition(
		GameTestBuilder builder) =>
		builder.GetGameState()!.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Should().NotContain(entry =>
				entry.Source.Kind ==
					FactionFactSourceKind.ExplicitTransition &&
				entry.Source.Identifier ==
					"accursed-wolf-father-infection");
}
