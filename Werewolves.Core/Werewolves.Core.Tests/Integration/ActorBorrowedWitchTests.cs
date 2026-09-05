using FluentAssertions;
using Werewolves.Core.GameLogic;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.StateMachine;
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
using Werewolves.Core.StateModels.Serialization;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorBorrowedWitchTests
{
	private sealed class TestExecutionCommitKey : IGameFlowManagerKey;
	private static readonly TestExecutionCommitKey ExecutionCommitKey = new();

	private static readonly PhysicalCharacterCard WitchCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000157"),
		MainRoleType.Witch);
	private static readonly PhysicalCharacterCard SeerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000158"),
		MainRoleType.Seer);
	private static readonly PhysicalCharacterCard FoxCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000159"),
		MainRoleType.Fox);
	private static readonly SubPhaseManager<NightSubPhases> NightActionLoop = new(
		NightSubPhases.Start,
		[
			HookSubPhaseStage.HookStage(GameHook.NightMainActionLoop),
			NavigationSubPhaseStage.NavigationEndStageSilent(NightSubPhases.Start)
		]);

	[Theory]
	[InlineData(WitchRecoveryPresentationTamper.HealingPrivateInstruction)]
	[InlineData(WitchRecoveryPresentationTamper.HealingDeclineLabel)]
	[InlineData(WitchRecoveryPresentationTamper.PoisonPrivateInstruction)]
	[InlineData(WitchRecoveryPresentationTamper.PoisonDeclineLabel)]
	[InlineData(WitchRecoveryPresentationTamper.SleepUndisclosedRosterInstruction)]
	[InlineData(WitchRecoveryPresentationTamper.SleepDisclosedRosterInstruction)]
	public void BorrowedWitch_TamperedPendingPresentationIsRejectedDuringStableRecovery(
		WitchRecoveryPresentationTamper tamper)
	{
		var (session, start, _, attackTargetId) = CreateActorSession();
		IRolePowerAvailabilityPolicy policy = tamper switch
		{
			WitchRecoveryPresentationTamper.PoisonPrivateInstruction or
				WitchRecoveryPresentationTamper.PoisonDeclineLabel =>
				new HealingUnavailablePolicy(),
			WitchRecoveryPresentationTamper.SleepUndisclosedRosterInstruction =>
				new AllUnavailablePolicy(),
			_ => AllowAllRolePowerAvailabilityPolicy.Instance
		};
		IGameHookListener witch = new WitchRole(
			new RolePowerAvailabilityGateway(policy));
		var (_, wake) = PerformSpendOpening(
			CreateActorRole(),
			witch,
			session,
			start,
			WitchCard.Id);
		var pending = Advance(witch, session, wake.CreateResponse())
			.ModeratorInstruction
			?? throw new InvalidOperationException(
				"Expected a borrowed Witch instruction after wake.");

		if (tamper is WitchRecoveryPresentationTamper.HealingPrivateInstruction or
		    WitchRecoveryPresentationTamper.HealingDeclineLabel)
		{
			var healing = pending.Should()
				.BeOfType<SelectPlayersInstruction>().Subject;
			healing.Semantic.Should().Be(
				ModeratorInstructionSemantic.SelectWitchHealingTarget);
			healing.PrivateInstruction.Should().Be(
				GameStrings.WitchHealingSelectionInstruction.Format(
					session.GetPlayer(attackTargetId).Name));
			healing.EmptySelectionOptionLabel.Should().Be(
				GameStrings.DeclineOption);
		}
		else if (tamper is
		         WitchRecoveryPresentationTamper.PoisonPrivateInstruction or
		         WitchRecoveryPresentationTamper.PoisonDeclineLabel)
		{
			var poison = pending.Should()
				.BeOfType<SelectPlayersInstruction>().Subject;
			poison.Semantic.Should().Be(
				ModeratorInstructionSemantic.SelectWitchPoisonTarget);
			poison.PrivateInstruction.Should().Be(
				GameStrings.WitchAttackTargetsAndPoisonSelectionInstruction.Format(
					session.GetPlayer(attackTargetId).Name));
			poison.EmptySelectionOptionLabel.Should().Be(
				GameStrings.DeclineOption);
		}
		else if (tamper ==
		         WitchRecoveryPresentationTamper.SleepUndisclosedRosterInstruction)
		{
			var sleep = pending.Should()
				.BeOfType<ConfirmationInstruction>().Subject;
			sleep.Semantic.Should().Be(
				ModeratorInstructionSemantic.PutRoleToSleep);
			sleep.PrivateInstruction.Should().Be(
				GameStrings.WitchAttackTargetsInstruction.Format(
					session.GetPlayer(attackTargetId).Name));
		}
		else
		{
			var healing = pending.Should()
				.BeOfType<SelectPlayersInstruction>().Subject;
			var poison = Advance(
				witch,
				session,
				healing.CreateResponse([])).ModeratorInstruction
				.Should().BeOfType<SelectPlayersInstruction>().Subject;
			pending = Advance(
				witch,
				session,
				poison.CreateResponse([])).ModeratorInstruction
				.Should().BeOfType<ConfirmationInstruction>().Subject;
			pending.PrivateInstruction.Should().BeNull();
		}

		var driver = RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(pending);
		if (pending is SelectPlayersInstruction selection)
		{
			driver.RewritePendingPlayerSelectionPresentation(
				selection.PublicAnnouncement,
				tamper is
					WitchRecoveryPresentationTamper.HealingPrivateInstruction or
					WitchRecoveryPresentationTamper.PoisonPrivateInstruction
						? "Tampered source-identifying private instruction."
						: selection.PrivateInstruction,
				tamper is
					WitchRecoveryPresentationTamper.HealingDeclineLabel or
					WitchRecoveryPresentationTamper.PoisonDeclineLabel
						? "Tampered decline label"
						: selection.EmptySelectionOptionLabel);
		}
		else
		{
			driver.RewritePendingConfirmationPresentation(
				"Tampered source-identifying private instruction.",
				pending.SoundEffects);
		}

		var service = new GameService();
		Action restore = () => service.RehydrateSession(driver.Serialize());

		restore.Should().Throw<InvalidOperationException>()
			.WithMessage(GameStrings.ActorBorrowedRolePowerInvalidResponse);
	}

	[Fact]
	public void BorrowedWitch_SourceSlotOffersActorQualifiedHealingAndPoisonWithoutSourceLeak()
	{
		var (session, start, actorId, attackTargetId) = CreateActorSession();
		var policy = new RecordingPolicy();
		IGameHookListener witch = new WitchRole(
			new RolePowerAvailabilityGateway(policy));
		var (activation, wake) = PerformSpendOpening(
			CreateActorRole(),
			witch,
			session,
			start,
			WitchCard.Id);

		policy.ObservedAttempts.Should().BeEmpty();
		session.GameHistoryLog.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		session.GameHistoryLog.OfType<NightActionLogEntry>().Should()
			.NotContain(entry =>
				entry.ActionType == NightActionType.WitchSave ||
				entry.ActionType == NightActionType.WitchKill);
		wake.Semantic.Should().Be(ModeratorInstructionSemantic.WakeRole);
		wake.PublicAnnouncement.Should().Be(
			GameStrings.RoleWakesUp.Format(GameStrings.ActorRoleName));
		wake.PublicAnnouncement.Should().NotContain(GameStrings.WitchRoleName);
		wake.PrivateInstruction.Should().BeNull();
		wake.AffectedPlayerIds.Should().Equal(actorId);

		var healing = Advance(witch, session, wake.CreateResponse())
			.ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		healing.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchHealingTarget);
		healing.CountConstraint.Should().Be(NumberRangeConstraint.SingleOptional);
		healing.SelectablePlayerIds.Should().Equal(attackTargetId);
		healing.PublicAnnouncement.Should().BeNull();
		healing.PrivateInstruction.Should().Be(
			GameStrings.WitchHealingSelectionInstruction.Format(
				session.GetPlayer(attackTargetId).Name));
		healing.AffectedPlayerIds.Should().Equal(actorId);
		healing.EmptySelectionOptionLabel.Should().Be(GameStrings.DeclineOption);

		var poison = Advance(
			witch,
			session,
			healing.CreateResponse([])).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		poison.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchPoisonTarget);
		poison.CountConstraint.Should().Be(NumberRangeConstraint.SingleOptional);
		poison.SelectablePlayerIds.Should()
			.NotContain(actorId)
			.And.Contain(attackTargetId);
		poison.PublicAnnouncement.Should().BeNull();
		poison.PrivateInstruction.Should().Be(
			GameStrings.WitchPoisonSelectionInstruction);
		poison.AffectedPlayerIds.Should().Equal(actorId);
		poison.EmptySelectionOptionLabel.Should().Be(GameStrings.DeclineOption);

		policy.ObservedAttempts.Select(CreateResourceIdentity).Should().Equal(
			new OneUseRolePowerResourceIdentity(
				actorId,
				MainRoleType.Witch,
				"witch-potions",
				activation.ActivationId,
				RolePowerInstanceOrigin.Borrowed,
				WitchRole.HealingResourceId),
			new OneUseRolePowerResourceIdentity(
				actorId,
				MainRoleType.Witch,
				"witch-potions",
				activation.ActivationId,
				RolePowerInstanceOrigin.Borrowed,
				WitchRole.PoisonResourceId));
		session.GetPlayerState(actorId).CurrentRole.Should().Be(MainRoleType.Actor);
		session.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.Witch);
		session.GameHistoryLog.Select(entry => entry.ToString()).Should()
			.NotContain(text =>
				text.Contains(WitchCard.Id.ToString(), StringComparison.Ordinal) ||
				text.Contains(activation.ActivationId.ToString(), StringComparison.Ordinal) ||
				text.Contains(MainRoleType.Witch.ToString(), StringComparison.Ordinal));
		session.GameHistoryLog.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void BorrowedWitch_SpentResourcesSurviveNextOpeningExpiryAndCannotReactivate()
	{
		var (session, start, actorId, attackTargetId) = CreateActorSession();
		var poisonTargetId = session.GetPlayers().Single(player =>
			player.Name == "Villager 1").Id;
		session.AssignRole(attackTargetId, MainRoleType.SimpleVillager);
		session.AssignRole(poisonTargetId, MainRoleType.SimpleVillager);
		IGameHookListener witch = new WitchRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var (activation, wake) = PerformSpendOpening(
			CreateActorRole(),
			witch,
			session,
			start,
			WitchCard.Id);
		var healing = Advance(witch, session, wake.CreateResponse())
			.ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.Witch,
			"witch-potions",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		var healingResourceIdentity = new OneUseRolePowerResourceIdentity(
			actorId,
			MainRoleType.Witch,
			"witch-potions",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed,
			WitchRole.HealingResourceId);
		var poisonResourceIdentity = healingResourceIdentity with
		{
			OneUseResourceId = WitchRole.PoisonResourceId
		};
		var historyCountBeforeHealing = session.GameHistoryLog.Count();
		session = RehydrateAtPendingInstruction(session, healing);

		var poison = GameFlowManager.HandleInput(
				session,
				healing.CreateResponse([attackTargetId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		poison.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchPoisonTarget);
		poison.CountConstraint.Should().Be(NumberRangeConstraint.SingleOptional);
		poison.SelectablePlayerIds.Should()
			.NotContain(actorId)
			.And.NotContain(attackTargetId)
			.And.Contain(poisonTargetId);
		poison.PrivateInstruction.Should().Be(
			GameStrings.WitchPoisonSelectionInstruction);
		poison.AffectedPlayerIds.Should().Equal(actorId);
		var healingUse = session.GetActorBorrowedWitchPotionUseCommits()
			.Should().ContainSingle().Subject;
		healingUse.PowerIdentity.Should().Be(powerIdentity);
		healingUse.ActorSetupCardId.Should().Be(WitchCard.Id);
		healingUse.SpentResourceIdentity.Should().Be(healingResourceIdentity);
		healingUse.TargetPlayerId.Should().Be(attackTargetId);
		healingUse.PublicMarkerLogIndex.Should().Be(historyCountBeforeHealing);
		GetPotionAction(healingUse.SpentResourceIdentity).Should().Be(
			NightActionType.WitchSave);
		var healingMarker = session.GameHistoryLog.Skip(historyCountBeforeHealing)
			.Should().ContainSingle().Subject;
		healingMarker.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		healingMarker.Should().NotBeAssignableTo<NightActionLogEntry>();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			session,
			healingResourceIdentity).Should().BeTrue();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			session,
			poisonResourceIdentity).Should().BeFalse();

		var recoveredAtPoison = RecoveryPayloadTestDriver.Parse(
				session.SerializeRecoverySnapshot())
			.RehydrateGameSession();
		var recoveredPoison = RecoveryPayloadTestDriver.Capture(
				recoveredAtPoison)
			.PendingInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		recoveredPoison.InstructionId.Should().Be(poison.InstructionId);
		recoveredPoison.SelectablePlayerIds.Should()
			.BeEquivalentTo(poison.SelectablePlayerIds);
		recoveredAtPoison.GetActorBorrowedWitchPotionUseCommits().Should()
			.Equal(healingUse);
		recoveredAtPoison.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
		var historyCountBeforePoison = recoveredAtPoison.GameHistoryLog.Count();

		var sleep = GameFlowManager.HandleInput(
				recoveredAtPoison,
				recoveredPoison.CreateResponse([poisonTargetId]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		sleep.PrivateInstruction.Should().BeNull();
		sleep.AffectedPlayerIds.Should().Equal(actorId);
		var uses = recoveredAtPoison.GetActorBorrowedWitchPotionUseCommits();
		uses.Should().HaveCount(2);
		var poisonUse = uses.Single(use =>
			use.SpentResourceIdentity == poisonResourceIdentity);
		poisonUse.PowerIdentity.Should().Be(powerIdentity);
		poisonUse.ActorSetupCardId.Should().Be(WitchCard.Id);
		poisonUse.TargetPlayerId.Should().Be(poisonTargetId);
		poisonUse.PublicMarkerLogIndex.Should().Be(historyCountBeforePoison);
		GetPotionAction(poisonUse.SpentResourceIdentity).Should().Be(
			NightActionType.WitchKill);
		var poisonMarker = recoveredAtPoison.GameHistoryLog
			.Skip(historyCountBeforePoison).Should().ContainSingle().Subject;
		poisonMarker.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		poisonMarker.Should().NotBeAssignableTo<NightActionLogEntry>();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			recoveredAtPoison,
			poisonResourceIdentity).Should().BeTrue();

		var recoveredAtSleep = RecoveryPayloadTestDriver.Parse(
				recoveredAtPoison.SerializeRecoverySnapshot())
			.RehydrateGameSession();
		var recoveredSleep = RecoveryPayloadTestDriver.Capture(
				recoveredAtSleep)
			.PendingInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recoveredSleep.InstructionId.Should().Be(sleep.InstructionId);
		recoveredSleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		recoveredSleep.PublicAnnouncement.Should().Be(
			GameStrings.RoleGoesToSleepSingle.Format(GameStrings.ActorRoleName));
		recoveredSleep.AffectedPlayerIds.Should().Equal(actorId);
		recoveredAtSleep.GetActorBorrowedWitchPotionUseCommits().Should()
			.Equal(uses);
		recoveredAtSleep.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should().HaveCount(2);

		GameFlowManager.HandleInput(
			recoveredAtSleep,
			recoveredSleep.CreateResponse(),
			SupportedRoleCatalog.Admissions);
		recoveredAtSleep.GetActorBorrowedWitchPotionUseCommits().Should()
			.HaveCount(2);
		recoveredAtSleep.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should().HaveCount(2);
		recoveredAtSleep.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>().Should().BeEmpty();
		recoveredAtSleep.GameHistoryLog.OfType<NightActionLogEntry>().Should()
			.NotContain(entry =>
				entry.ActionType == NightActionType.WitchSave ||
				entry.ActionType == NightActionType.WitchKill);
		recoveredAtSleep.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Select(entry => entry.ToString()).Should().OnlyContain(text =>
				!text.Contains(MainRoleType.Witch.ToString(), StringComparison.Ordinal) &&
				!text.Contains(WitchCard.Id.ToString(), StringComparison.Ordinal) &&
				!text.Contains(activation.ActivationId.ToString(), StringComparison.Ordinal) &&
				!text.Contains(WitchRole.HealingResourceId.ToString(), StringComparison.Ordinal) &&
				!text.Contains(WitchRole.PoisonResourceId.ToString(), StringComparison.Ordinal) &&
				!text.Contains(attackTargetId.ToString(), StringComparison.Ordinal) &&
				!text.Contains(poisonTargetId.ToString(), StringComparison.Ordinal));
		recoveredAtSleep.GetPlayerState(actorId).CurrentRole.Should().Be(
			MainRoleType.Actor);
		recoveredAtSleep.GameHistoryLog.OfType<RoleIdentificationLogEntry>().Should()
			.NotContain(entry => entry.Role == MainRoleType.Witch);

		NightInteractionResolver.ResolveNightPhase(recoveredAtSleep);
		recoveredAtSleep.GameHistoryLog.OfType<DawnVictimDeterminedLogEntry>()
			.Should().NotContain(entry => entry.PlayerId == attackTargetId)
			.And.ContainSingle(entry =>
				entry.PlayerId == poisonTargetId &&
				entry.Reason == EliminationReason.WitchKill);
		recoveredAtSleep.TransitionMainPhase(GamePhase.Dawn);
		recoveredAtSleep.TransitionMainPhase(GamePhase.Day);
		recoveredAtSleep.TransitionMainPhase(GamePhase.Night);

		IGameHookListener nextActor = CreateActorRole();
		var nextActorWake = Advance(
			nextActor,
			recoveredAtSleep,
			start.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recoveredAtSleep.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().BeNull();
		var nextActorChoice = Advance(
			nextActor,
			recoveredAtSleep,
			nextActorWake.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var nextActorSleep = Advance(
			nextActor,
			recoveredAtSleep,
			nextActorChoice.CreateResponse()).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		CompleteCadence(
			nextActor,
			recoveredAtSleep,
			nextActorSleep.CreateResponse());

		recoveredAtSleep.GetActorBorrowedWitchPotionUseCommits().Should()
			.Equal(uses);
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			recoveredAtSleep,
			healingResourceIdentity).Should().BeTrue();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			recoveredAtSleep,
			poisonResourceIdentity).Should().BeTrue();
		recoveredAtSleep.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should().HaveCount(2);
		recoveredAtSleep.GetModeratorSpentActorSetupCards().Should()
			.Equal(WitchCard);
	}

	[Fact]
	public void BorrowedWitch_ExplicitPotionDeclinesAreDurableRecoverableUnspentAndPrivate()
	{
		var (session, start, actorId, attackTargetId) = CreateActorSession();
		IGameHookListener witch = new WitchRole(
			new RolePowerAvailabilityGateway(
				AllowAllRolePowerAvailabilityPolicy.Instance));
		var (activation, wake) = PerformSpendOpening(
			CreateActorRole(),
			witch,
			session,
			start,
			WitchCard.Id);
		var healing = Advance(witch, session, wake.CreateResponse())
			.ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var powerIdentity = new RolePowerInstanceIdentity(
			actorId,
			MainRoleType.Witch,
			"witch-potions",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed);
		var healingResourceIdentity = new OneUseRolePowerResourceIdentity(
			actorId,
			MainRoleType.Witch,
			"witch-potions",
			activation.ActivationId,
			RolePowerInstanceOrigin.Borrowed,
			WitchRole.HealingResourceId);
		var poisonResourceIdentity = healingResourceIdentity with
		{
			OneUseResourceId = WitchRole.PoisonResourceId
		};
		var historyCountBeforeHealingDecline = session.GameHistoryLog.Count();
		session = RehydrateAtPendingInstruction(session, healing);

		var poison = GameFlowManager.HandleInput(
				session,
				healing.CreateResponse([]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		poison.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchPoisonTarget);
		var healingDecline = session.GetActorBorrowedWitchPotionDeclineCommits()
			.Should().ContainSingle().Subject;
		healingDecline.PowerIdentity.Should().Be(powerIdentity);
		healingDecline.ActorSetupCardId.Should().Be(WitchCard.Id);
		healingDecline.OfferedResourceIdentity.Should().Be(
			healingResourceIdentity);
		healingDecline.PublicMarkerLogIndex.Should().Be(
			historyCountBeforeHealingDecline);
		session.GetActorBorrowedWitchPotionUseCommits().Should().BeEmpty();
		session.GameHistoryLog.Skip(historyCountBeforeHealingDecline).Should()
			.ContainSingle()
			.Which.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			session,
			healingResourceIdentity).Should().BeFalse();
		var healingDeclineCursor = RecoveryPayloadTestDriver.Capture(session)
			.DomainRecoveryCursor!;
		healingDeclineCursor.Kind.Should().Be(
			DomainRecoveryCursorKind.ActorBorrowedWitchPotionDeclineCommit);
		healingDeclineCursor.ResourceIdentity.Should().Be(
			healingResourceIdentity);
		healingDeclineCursor.CommittedTargetIds.Should().BeEmpty();

		var recoveredAtPoison = RecoveryPayloadTestDriver.Parse(
				session.SerializeRecoverySnapshot())
			.RehydrateGameSession();
		var recoveredPoison = RecoveryPayloadTestDriver.Capture(
				recoveredAtPoison)
			.PendingInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		recoveredPoison.InstructionId.Should().Be(poison.InstructionId);
		recoveredAtPoison.GetActorBorrowedWitchPotionDeclineCommits().Should()
			.Equal(healingDecline);
		var historyCountBeforePoisonDecline =
			recoveredAtPoison.GameHistoryLog.Count();

		var sleep = GameFlowManager.HandleInput(
				recoveredAtPoison,
				recoveredPoison.CreateResponse([]),
				SupportedRoleCatalog.Admissions).ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		sleep.Semantic.Should().Be(ModeratorInstructionSemantic.PutRoleToSleep);
		var declines = recoveredAtPoison
			.GetActorBorrowedWitchPotionDeclineCommits();
		declines.Should().HaveCount(2);
		var poisonDecline = declines.Single(commit =>
			commit.OfferedResourceIdentity == poisonResourceIdentity);
		poisonDecline.PowerIdentity.Should().Be(powerIdentity);
		poisonDecline.ActorSetupCardId.Should().Be(WitchCard.Id);
		poisonDecline.PublicMarkerLogIndex.Should().Be(
			historyCountBeforePoisonDecline);
		recoveredAtPoison.GameHistoryLog.Skip(historyCountBeforePoisonDecline)
			.Should().ContainSingle()
			.Which.Should().BeOfType<ActorBorrowedRolePowerCommittedLogEntry>();
		GameSessionQueries.IsOneUseRolePowerResourceCommitted(
			recoveredAtPoison,
			poisonResourceIdentity).Should().BeFalse();
		var poisonDeclineCursor = RecoveryPayloadTestDriver.Capture(
				recoveredAtPoison)
			.DomainRecoveryCursor!;
		poisonDeclineCursor.Kind.Should().Be(
			DomainRecoveryCursorKind.ActorBorrowedWitchPotionDeclineCommit);
		poisonDeclineCursor.ResourceIdentity.Should().Be(poisonResourceIdentity);
		poisonDeclineCursor.CommittedTargetIds.Should().BeEmpty();

		var recoveredAtSleep = RecoveryPayloadTestDriver.Parse(
				recoveredAtPoison.SerializeRecoverySnapshot())
			.RehydrateGameSession();
		var recoveredSleep = RecoveryPayloadTestDriver.Capture(
				recoveredAtSleep)
			.PendingInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recoveredSleep.InstructionId.Should().Be(sleep.InstructionId);
		recoveredAtSleep.GetActorBorrowedWitchPotionDeclineCommits().Should()
			.Equal(declines);
		recoveredAtSleep.GetActorBorrowedWitchPotionUseCommits().Should().BeEmpty();
		recoveredAtSleep.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should().HaveCount(2);
		recoveredAtSleep.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>().Should().BeEmpty();
		recoveredAtSleep.GameHistoryLog.OfType<NightActionLogEntry>().Should()
			.NotContain(entry =>
				entry.ActionType == NightActionType.WitchSave ||
				entry.ActionType == NightActionType.WitchKill);
		recoveredAtSleep.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Select(entry => entry.ToString()).Should().OnlyContain(text =>
				!text.Contains(MainRoleType.Witch.ToString(), StringComparison.Ordinal) &&
				!text.Contains(WitchCard.Id.ToString(), StringComparison.Ordinal) &&
				!text.Contains(activation.ActivationId.ToString(), StringComparison.Ordinal) &&
				!text.Contains(WitchRole.HealingResourceId.ToString(), StringComparison.Ordinal) &&
				!text.Contains(WitchRole.PoisonResourceId.ToString(), StringComparison.Ordinal));
	}

	[Fact]
	public void BorrowedWitch_UnavailableHealingSlotCreatesNoImplicitDeclineOrMarker()
	{
		var (session, start, _, attackTargetId) = CreateActorSession();
		IGameHookListener witch = new WitchRole(
			new RolePowerAvailabilityGateway(
				new HealingUnavailablePolicy()));
		var (_, wake) = PerformSpendOpening(
			CreateActorRole(),
			witch,
			session,
			start,
			WitchCard.Id);

		var poison = Advance(witch, session, wake.CreateResponse())
			.ModeratorInstruction
			.Should().BeOfType<SelectPlayersInstruction>().Subject;

		poison.Semantic.Should().Be(
			ModeratorInstructionSemantic.SelectWitchPoisonTarget);
		session.GetActorBorrowedWitchPotionDeclineCommits().Should().BeEmpty();
		session.GetActorBorrowedWitchPotionUseCommits().Should().BeEmpty();
		session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should().BeEmpty();

		session = RehydrateAtPendingInstruction(session, poison);
		GameFlowManager.HandleInput(
			session,
			poison.CreateResponse([]),
			SupportedRoleCatalog.Admissions);

		var decline = session.GetActorBorrowedWitchPotionDeclineCommits()
			.Should().ContainSingle().Subject;
		decline.OfferedResourceIdentity.OneUseResourceId.Should().Be(
			WitchRole.PoisonResourceId);
		session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.ContainSingle();
	}

	private static ActorRole CreateActorRole() => new(
		new RolePowerAvailabilityGateway(
			new VillagerRolePowerSuppressionPolicy(
				AllowAllRolePowerAvailabilityPolicy.Instance)));

	private static (
		ActorBorrowedRolePowerActivation Activation,
		ConfirmationInstruction SourceWake) PerformSpendOpening(
		IGameHookListener actorListener,
		IGameHookListener sourceListener,
		GameSession session,
		StartGameConfirmationInstruction start,
		Guid selectedCardId)
	{
		var wake = Advance(actorListener, session, start.CreateResponse())
			.ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(actorListener, session, wake.CreateResponse())
			.ModeratorInstruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var sleep = Advance(
			actorListener,
			session,
			choice.CreateResponse(selectedCardId.ToString("D")))
			.ModeratorInstruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation()!;
		session.GetOrCreateListener(sourceListener.Id, () => sourceListener);
		var sourceWake = AdvanceToBorrowedRoleWake(
			actorListener,
			session,
			sleep.CreateResponse(),
			activation.ActingPlayerId);
		return (activation, sourceWake);
	}

	private static ConfirmationInstruction AdvanceToBorrowedRoleWake(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response,
		Guid actorId)
	{
		var instruction = Advance(listener, session, response)
			.ModeratorInstruction;
		for (var step = 0; step < 20; step++)
		{
			if (instruction is ConfirmationInstruction
			    {
				    Semantic: ModeratorInstructionSemantic.WakeRole
			    } wake &&
			    wake.AffectedPlayerIds?.SequenceEqual([actorId]) == true)
			{
				return wake;
			}

			instruction = Advance(
				listener,
				session,
				CreateCadenceResponse(session, instruction))
					.ModeratorInstruction;
		}

		throw new InvalidOperationException(
			"The test cadence did not reach the borrowed Role wake within 20 steps.");
	}

	private static ModeratorResponse CreateCadenceResponse(
		GameSession session,
		ModeratorInstruction? instruction) => instruction switch
		{
			SelectPlayersInstruction
			{
				Semantic:
					ModeratorInstructionSemantic
						.ObserveWerewolfFactionAgentGroup
			} selection => CreateSingleSelectionResponse(
				session,
				selection,
				"Werewolf"),
			SelectPlayersInstruction
			{
				Semantic: ModeratorInstructionSemantic.SelectWerewolfVictim
			} selection => CreateSingleSelectionResponse(
				session,
				selection,
				"Villager 3"),
			ConfirmationInstruction confirmation =>
				confirmation.CreateResponse(),
			_ => throw new InvalidOperationException(
				$"The test cadence cannot answer '{instruction?.Semantic}'.")
		};

	private static ModeratorResponse CreateSingleSelectionResponse(
		GameSession session,
		SelectPlayersInstruction instruction,
		string preferredPlayerName)
	{
		var preferredPlayerId = session.GetPlayers()
			.Where(player => player.Name == preferredPlayerName)
			.Select(player => player.Id)
			.SingleOrDefault();
		var selectedPlayerId = instruction.SelectablePlayerIds.Contains(
			preferredPlayerId)
			? preferredPlayerId
			: instruction.SelectablePlayerIds.First();
		return instruction.CreateResponse([selectedPlayerId]);
	}

	private static void CompleteCadence(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response)
	{
		var instruction = Advance(listener, session, response)
			.ModeratorInstruction;
		for (var step = 0; step < 20; step++)
		{
			if (instruction == null)
			{
				return;
			}

			instruction = Advance(
				listener,
				session,
				CreateCadenceResponse(session, instruction))
				.ModeratorInstruction;
		}

		throw new InvalidOperationException(
			"The test cadence did not complete the Night hook within 20 steps.");
	}

	private static OneUseRolePowerResourceIdentity CreateResourceIdentity(
		RolePowerAttempt attempt)
	{
		var resource = attempt.OneUseResource
			?? throw new InvalidOperationException(
				"The Witch availability attempt requires a one-use Resource.");
		return new OneUseRolePowerResourceIdentity(
			attempt.ActingPlayer.Id,
			attempt.SourceRole,
			attempt.SourcePower.Identifier.Value,
			attempt.PowerInstance.Id,
			attempt.PowerInstance.Origin,
			resource.Id);
	}

	private static NightActionType GetPotionAction(
		OneUseRolePowerResourceIdentity resourceIdentity) =>
		resourceIdentity.OneUseResourceId switch
		{
			var id when id == WitchRole.HealingResourceId =>
				NightActionType.WitchSave,
			var id when id == WitchRole.PoisonResourceId =>
				NightActionType.WitchKill,
			_ => NightActionType.Unknown
		};

	private static (
		GameSession Session,
		StartGameConfirmationInstruction Start,
		Guid ActorId,
		Guid AttackTargetId) CreateActorSession()
	{
		var setup = new ActorSetupCards(
			version: 7,
			new[] { WitchCard, SeerCard, FoxCard });
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
		var start = new StartGameConfirmationInstruction(sessionId);
		var session = new GameSession(sessionId, start, config);
		var players = session.GetPlayers().ToArray();
		var actorId = players[0].Id;
		session.AssignRole(actorId, MainRoleType.Actor);
		RoleFactionKnowledge.CommitRoleIdentification(
			session,
			new HashSet<Guid> { actorId },
			MainRoleType.Actor);
		return (session, start, actorId, players[4].Id);
	}

	private static PhaseHandlerResult Advance(
		IGameHookListener listener,
		GameSession session,
		ModeratorResponse response)
	{
		var consumedInstruction = session.Execution.PendingInstruction
			?? throw new InvalidOperationException(
				"The Actor borrowed Witch test workflow requires one Pending Instruction.");
		session.GetOrCreateListener(listener.Id, () => listener);
		var result = NightActionLoop.Execute(session, response);
		if (result.ModeratorInstruction is { } nextInstruction)
		{
			var publicationResponse =
				response.InstructionId == consumedInstruction.InstructionId
					? response
					: new ModeratorResponse
					{
						InstructionId = consumedInstruction.InstructionId,
						Type = response.Type,
						SelectedPlayerIds = response.SelectedPlayerIds,
						AssignedPlayerRoles = response.AssignedPlayerRoles,
						SelectedOptionIds = response.SelectedOptionIds
					};
			session.CommitExecution(
				ExecutionCommitKey,
				ExecutionCommit.RetainRecoveryBoundary(
					session.Execution,
					consumedInstruction,
					publicationResponse,
					nextInstruction));
		}

		return result;
	}

	private static GameSession RehydrateAtPendingInstruction(
		GameSession session,
		ModeratorInstruction instruction) =>
		RecoveryPayloadTestDriver.Capture(session)
			.WithPendingInstruction(instruction)
			.RehydrateGameSession();

	private sealed class RecordingPolicy : IRolePowerAvailabilityPolicy
	{
		internal List<RolePowerAttempt> ObservedAttempts { get; } = [];

		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt)
		{
			ObservedAttempts.Add(attempt);
			return RolePowerAvailabilityResult.Allowed;
		}
	}

	private sealed class HealingUnavailablePolicy : IRolePowerAvailabilityPolicy
	{
		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt) =>
			attempt.OneUseResource?.Id == WitchRole.HealingResourceId
				? RolePowerAvailabilityResult.Denied
				: RolePowerAvailabilityResult.Allowed;
	}

	private sealed class AllUnavailablePolicy : IRolePowerAvailabilityPolicy
	{
		public RolePowerAvailabilityResult Evaluate(RolePowerAttempt attempt) =>
			RolePowerAvailabilityResult.Denied;
	}

	public enum WitchRecoveryPresentationTamper
	{
		HealingPrivateInstruction,
		HealingDeclineLabel,
		PoisonPrivateInstruction,
		PoisonDeclineLabel,
		SleepUndisclosedRosterInstruction,
		SleepDisclosedRosterInstruction
	}

}
