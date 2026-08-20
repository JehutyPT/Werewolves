using System.Globalization;
using FluentAssertions;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Models.StateMachine;
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
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorBorrowedPrivacyTests
{
	private sealed class TestExecutionCommitKey : IGameFlowManagerKey;
	private static readonly TestExecutionCommitKey ExecutionCommitKey = new();

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

	[Theory]
	[InlineData(MainRoleType.Seer)]
	[InlineData(MainRoleType.Cupid)]
	[InlineData(MainRoleType.Witch)]
	[InlineData(MainRoleType.LittleGirl)]
	[InlineData(MainRoleType.Defender)]
	[InlineData(MainRoleType.Fox)]
	public void BorrowedSource_InvalidOrStaleResponseUsesLocalizedNonIdentifyingError(
		MainRoleType sourceRole)
	{
		var originalCulture = CultureInfo.CurrentUICulture;
		try
		{
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-PT");
			var expected = GameStrings.ActorBorrowedRolePowerInvalidResponse;
			var english = GameStrings.ResourceManager.GetString(
				nameof(GameStrings.ActorBorrowedRolePowerInvalidResponse),
				CultureInfo.GetCultureInfo("en-US"));
			var fixture = CreateFixture(sourceRole);
			var invalidSubmission = PrepareInvalidSubmission(fixture);
			var historyCountBeforeSubmission = fixture.Session.GameHistoryLog.Count();

			expected.Should().NotBeNullOrWhiteSpace().And.NotBe(english);
			invalidSubmission.Should().Throw<InvalidOperationException>()
				.WithMessage(expected);
			fixture.Session.GameHistoryLog.Should().HaveCount(
				historyCountBeforeSubmission);
		}
		finally
		{
			CultureInfo.CurrentUICulture = originalCulture;
		}
	}

	[Theory]
	[InlineData(BorrowedValidationBranch.SeerMissingSelection)]
	[InlineData(BorrowedValidationBranch.SeerMultipleSelection)]
	[InlineData(BorrowedValidationBranch.CupidTamperedPendingSelection)]
	[InlineData(BorrowedValidationBranch.CupidMalformedSelection)]
	[InlineData(BorrowedValidationBranch.CupidAlreadyCommitted)]
	[InlineData(BorrowedValidationBranch.WitchHealingMissingSelection)]
	[InlineData(BorrowedValidationBranch.WitchHealingMultipleSelection)]
	[InlineData(BorrowedValidationBranch.WitchHealingInvalidTarget)]
	[InlineData(BorrowedValidationBranch.WitchPoisonMissingSelection)]
	[InlineData(BorrowedValidationBranch.WitchPoisonMultipleSelection)]
	[InlineData(BorrowedValidationBranch.WitchPoisonInvalidTarget)]
	public void BorrowedSource_ReachableInvalidResponseBranchesUseLocalizedNonIdentifyingErrorWithoutMutation(
		BorrowedValidationBranch branch)
	{
		var originalCulture = CultureInfo.CurrentUICulture;
		try
		{
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-PT");
			var expected = GameStrings.ActorBorrowedRolePowerInvalidResponse;
			var english = GameStrings.ResourceManager.GetString(
				nameof(GameStrings.ActorBorrowedRolePowerInvalidResponse),
				CultureInfo.GetCultureInfo("en-US"));
			var submission = PrepareValidationSubmission(branch);
			var serializedBefore = submission.Session.Serialize();

			expected.Should().NotBeNullOrWhiteSpace().And.NotBe(english);
			submission.Submit.Should().Throw<InvalidOperationException>()
				.WithMessage(expected);
			submission.Session.Serialize().Should().Be(serializedBefore);
		}
		finally
		{
			CultureInfo.CurrentUICulture = originalCulture;
		}
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
			fixture,
			staleResponse);

		submitStaleResponse.Should().NotThrow();
		replay.Should().NotBeNull();
		replay!.Outcome.Should().Be(HookListenerOutcome.Complete);
		replay.Instruction.Should().BeNull();
		fixture.Session.GameHistoryLog.Should().HaveCount(historyCount);
		fixture.Session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>().Should()
			.HaveCount(markerCount);
		fixture.Session.GetActorBorrowedStutteringJudgeSignalSetupCommits()
			.Should().HaveCount(commitCount);
	}

	[Fact]
	public void RehydrateSession_BorrowedHunterTamperedPendingSelectorPresentationIsRejectedBeforeRegistrationWithoutObserverExposure()
	{
		var sourceObserver = new CountingStateChangeObserver();
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedHunterPendingSelectorSnapshot(sourceObserver);
		var sourceNotificationCount = sourceObserver.NotificationCount;
		var tampered = RecoveryPayloadTestDriver
			.Parse(snapshot.SerializedSession)
			.RewriteActorBorrowedHunterPendingSelectorPrivateInstruction(
				"Tampered private Hunter selector presentation.")
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		var failure = rehydrate.Should().Throw<InvalidOperationException>().Which;
		AssertHunterRecoveryTextIsSourceSafe(failure.Message, snapshot);
		service.GetCurrentInstruction(snapshot.SessionId).Should().BeNull();
		service.GetGameStateView(snapshot.SessionId).Should().BeNull();
		var unavailable = service.ProcessInstruction(
			snapshot.SessionId,
			snapshot.Selector.CreateResponse(
				[snapshot.Selector.SelectablePlayerIds.First()]));
		unavailable.IsSuccess.Should().BeFalse();
		var publicError = unavailable.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		publicError.Semantic.Should().Be(
			ModeratorInstructionSemantic.GameSessionNotFound);
		AssertHunterRecoveryTextIsSourceSafe(
			string.Concat(
				publicError.PublicAnnouncement,
				"\n",
				publicError.PrivateInstruction),
			snapshot);
		sourceNotificationCount.Should().BeGreaterThan(0);
		sourceObserver.NotificationCount.Should().Be(sourceNotificationCount);
	}

	[Fact]
	public void RehydrateSession_BorrowedElderTamperedPendingSuppressionAnnouncementPresentationIsRejectedBeforeRegistrationWithoutObserverExposure()
	{
		var sourceObserver = new CountingStateChangeObserver();
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedElderPendingSuppressionAnnouncementSnapshot(
				sourceObserver);
		var sourceNotificationCount = sourceObserver.NotificationCount;
		var tampered = RecoveryPayloadTestDriver
			.Parse(snapshot.SerializedSession)
			.RewriteActorBorrowedElderPendingSuppressionPublicAnnouncement(
				"Tampered public suppression announcement.")
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		var failure = rehydrate.Should().Throw<InvalidOperationException>().Which;
		AssertElderRecoveryTextIsSourceSafe(failure.Message, snapshot);
		service.GetCurrentInstruction(snapshot.SessionId).Should().BeNull();
		service.GetGameStateView(snapshot.SessionId).Should().BeNull();
		var unavailable = service.ProcessInstruction(
			snapshot.SessionId,
			snapshot.Announcement.CreateResponse());
		unavailable.IsSuccess.Should().BeFalse();
		var publicError = unavailable.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		publicError.Semantic.Should().Be(
			ModeratorInstructionSemantic.GameSessionNotFound);
		AssertElderRecoveryTextIsSourceSafe(
			string.Concat(
				publicError.PublicAnnouncement,
				"\n",
				publicError.PrivateInstruction),
			snapshot);
		sourceNotificationCount.Should().BeGreaterThan(0);
		sourceObserver.NotificationCount.Should().Be(sourceNotificationCount);
	}

	[Theory]
	[InlineData(ActorBorrowedScapegoatRecoveryStep.Reveal)]
	[InlineData(ActorBorrowedScapegoatRecoveryStep.PermittedVoterSelection)]
	[InlineData(ActorBorrowedScapegoatRecoveryStep.PermittedVoterAnnouncement)]
	public void RehydrateSession_BorrowedScapegoatTamperedPendingPresentationIsRejectedBeforeRegistrationWithoutObserverExposure(
		ActorBorrowedScapegoatRecoveryStep step)
	{
		var sourceObserver = new CountingStateChangeObserver();
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedScapegoatPendingSnapshot(step, sourceObserver);
		var sourceNotificationCount = sourceObserver.NotificationCount;
		var tampered = RecoveryPayloadTestDriver
			.Parse(snapshot.SerializedSession)
			.RewriteActorBorrowedScapegoatPendingPresentation(step)
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		var failure = rehydrate.Should().Throw<InvalidOperationException>().Which;
		AssertScapegoatRecoveryTextIsSourceSafe(failure.Message, snapshot);
		service.GetCurrentInstruction(snapshot.SessionId).Should().BeNull();
		service.GetGameStateView(snapshot.SessionId).Should().BeNull();
		var unavailable = service.ProcessInstruction(
			snapshot.SessionId,
			CreateScapegoatRecoveryResponse(snapshot.PendingInstruction));
		unavailable.IsSuccess.Should().BeFalse();
		var publicError = unavailable.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		publicError.Semantic.Should().Be(
			ModeratorInstructionSemantic.GameSessionNotFound);
		AssertScapegoatRecoveryTextIsSourceSafe(
			string.Concat(
				publicError.PublicAnnouncement,
				"\n",
				publicError.PrivateInstruction),
			snapshot);
		sourceNotificationCount.Should().BeGreaterThan(0);
		sourceObserver.NotificationCount.Should().Be(sourceNotificationCount);
	}

	[Fact]
	public void RehydrateSession_BorrowedScapegoatPendingRevealWithoutCurrentActivationIsRejectedBeforeRegistrationWithoutObserverExposure()
	{
		var sourceObserver = new CountingStateChangeObserver();
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedScapegoatPendingSnapshot(
				ActorBorrowedScapegoatRecoveryStep.Reveal,
				sourceObserver);
		var sourceNotificationCount = sourceObserver.NotificationCount;
		var tampered = RecoveryPayloadTestDriver
			.Parse(snapshot.SerializedSession)
			.ExpireActorBorrowedScapegoatPendingRevealActivation()
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		var failure = rehydrate.Should().Throw<InvalidOperationException>().Which;
		failure.Message.Should().Be(
			"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		AssertScapegoatRecoveryTextIsSourceSafe(failure.Message, snapshot);
		service.GetCurrentInstruction(snapshot.SessionId).Should().BeNull();
		service.GetGameStateView(snapshot.SessionId).Should().BeNull();
		var unavailable = service.ProcessInstruction(
			snapshot.SessionId,
			CreateScapegoatRecoveryResponse(snapshot.PendingInstruction));
		unavailable.IsSuccess.Should().BeFalse();
		var publicError = unavailable.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		publicError.Semantic.Should().Be(
			ModeratorInstructionSemantic.GameSessionNotFound);
		AssertScapegoatRecoveryTextIsSourceSafe(
			string.Concat(
				publicError.PublicAnnouncement,
				"\n",
				publicError.PrivateInstruction),
			snapshot);
		sourceNotificationCount.Should().BeGreaterThan(0);
		sourceObserver.NotificationCount.Should().Be(sourceNotificationCount);
	}

	[Fact]
	public void RehydrateSession_BorrowedScapegoatPendingRevealWithDifferentCurrentActivationIsRejectedBeforeRegistrationWithoutObserverExposure()
	{
		var sourceObserver = new CountingStateChangeObserver();
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedScapegoatPendingSnapshot(
				ActorBorrowedScapegoatRecoveryStep.Reveal,
				sourceObserver);
		var sourceNotificationCount = sourceObserver.NotificationCount;
		var tampered = RecoveryPayloadTestDriver
			.Parse(snapshot.SerializedSession)
			.ReplaceActorBorrowedScapegoatPendingRevealActivation()
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		var failure = rehydrate.Should().Throw<InvalidOperationException>().Which;
		failure.Message.Should().Be(
			"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		AssertScapegoatRecoveryTextIsSourceSafe(failure.Message, snapshot);
		service.GetCurrentInstruction(snapshot.SessionId).Should().BeNull();
		service.GetGameStateView(snapshot.SessionId).Should().BeNull();
		var unavailable = service.ProcessInstruction(
			snapshot.SessionId,
			CreateScapegoatRecoveryResponse(snapshot.PendingInstruction));
		unavailable.IsSuccess.Should().BeFalse();
		var publicError = unavailable.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		publicError.Semantic.Should().Be(
			ModeratorInstructionSemantic.GameSessionNotFound);
		AssertScapegoatRecoveryTextIsSourceSafe(
			string.Concat(
				publicError.PublicAnnouncement,
				"\n",
				publicError.PrivateInstruction),
			snapshot);
		sourceNotificationCount.Should().BeGreaterThan(0);
		sourceObserver.NotificationCount.Should().Be(sourceNotificationCount);
	}

	[Fact]
	public void RehydrateSession_BorrowedVillageIdiotExpiredPendingPardonWithTamperedPresentationIsRejectedBeforeRegistrationWithoutObserverExposure()
	{
		var sourceObserver = new CountingStateChangeObserver();
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedVillageIdiotPendingPardonSnapshot(sourceObserver);
		var sourceNotificationCount = sourceObserver.NotificationCount;
		var tampered = RecoveryPayloadTestDriver
			.Parse(snapshot.SerializedSession)
			.ExpireActorBorrowedVillageIdiotPendingPardonActivation()
			.RewriteActorBorrowedVillageIdiotPendingPardonPublicAnnouncement(
				"Tampered public borrowed pardon announcement.")
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		var failure = rehydrate.Should().Throw<InvalidOperationException>().Which;
		AssertVillageIdiotRecoveryTextIsSourceSafe(failure.Message, snapshot);
		service.GetCurrentInstruction(snapshot.SessionId).Should().BeNull();
		service.GetGameStateView(snapshot.SessionId).Should().BeNull();
		var unavailable = service.ProcessInstruction(
			snapshot.SessionId,
			snapshot.Pardon.CreateResponse());
		unavailable.IsSuccess.Should().BeFalse();
		var publicError = unavailable.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		publicError.Semantic.Should().Be(
			ModeratorInstructionSemantic.GameSessionNotFound);
		AssertVillageIdiotRecoveryTextIsSourceSafe(
			string.Concat(
				publicError.PublicAnnouncement,
				"\n",
				publicError.PrivateInstruction),
			snapshot);
		sourceNotificationCount.Should().BeGreaterThan(0);
		sourceObserver.NotificationCount.Should().Be(sourceNotificationCount);
	}

	[Fact]
	public void RehydrateSession_BorrowedVillageIdiotExpiredPendingPardonWithAuthenticatedHistoricalLineageIsRestoredWithoutObserverExposure()
	{
		var sourceObserver = new CountingStateChangeObserver();
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedVillageIdiotPendingPardonSnapshot(sourceObserver);
		var sourceNotificationCount = sourceObserver.NotificationCount;
		var expired = RecoveryPayloadTestDriver
			.Parse(snapshot.SerializedSession)
			.ExpireActorBorrowedVillageIdiotPendingPardonActivation()
			.Serialize();
		var service = new GameService();

		var recoveredGameId = service.RehydrateSession(expired);

		recoveredGameId.Should().Be(snapshot.SessionId);
		var recoveredPardon = service.GetCurrentInstruction(recoveredGameId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		recoveredPardon.Should().BeEquivalentTo(snapshot.Pardon);
		AssertVillageIdiotRecoveryTextIsSourceSafe(
			string.Concat(
				recoveredPardon.PublicAnnouncement,
				"\n",
				recoveredPardon.PrivateInstruction),
			snapshot);
		var recovered = service.GetGameStateView(recoveredGameId)
			.Should().BeOfType<GameSession>().Subject;
		var actorId = snapshot.Pardon.AffectedPlayerIds.Should()
			.ContainSingle().Subject;
		var actor = recovered.GetPlayerState(actorId);
		actor.HasVotingRight.Should().BeFalse();
		actor.DurableVotingPower.Should().Be(0);
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation()
			.Should().BeNull();
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
			.Should().ContainSingle();
		recovered.GetActorBorrowedVillageIdiotPardonCommits()
			.Should().ContainSingle();
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerCommittedLogEntry>()
			.Should().ContainSingle();
		sourceNotificationCount.Should().BeGreaterThan(0);
		sourceObserver.NotificationCount.Should().Be(sourceNotificationCount);
	}

	[Fact]
	public void RehydrateSession_BorrowedVillageIdiotExpiredPendingPardonWithoutHistoricalLineageIsRejectedBeforeRegistrationWithoutObserverExposure()
	{
		var sourceObserver = new CountingStateChangeObserver();
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedVillageIdiotPendingPardonSnapshot(sourceObserver);
		var sourceNotificationCount = sourceObserver.NotificationCount;
		var stripped = RecoveryPayloadTestDriver
			.Parse(snapshot.SerializedSession)
			.ExpireActorBorrowedVillageIdiotPendingPardonActivation()
			.RemoveActorBorrowedVillageIdiotPendingPardonLineage()
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(stripped);

		var failure = rehydrate.Should().Throw<InvalidOperationException>().Which;
		failure.Message.Should().Be(
			"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		AssertVillageIdiotRecoveryTextIsSourceSafe(failure.Message, snapshot);
		service.GetCurrentInstruction(snapshot.SessionId).Should().BeNull();
		service.GetGameStateView(snapshot.SessionId).Should().BeNull();
		var unavailable = service.ProcessInstruction(
			snapshot.SessionId,
			snapshot.Pardon.CreateResponse());
		unavailable.IsSuccess.Should().BeFalse();
		var publicError = unavailable.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		publicError.Semantic.Should().Be(
			ModeratorInstructionSemantic.GameSessionNotFound);
		AssertVillageIdiotRecoveryTextIsSourceSafe(
			string.Concat(
				publicError.PublicAnnouncement,
				"\n",
				publicError.PrivateInstruction),
			snapshot);
		sourceNotificationCount.Should().BeGreaterThan(0);
		sourceObserver.NotificationCount.Should().Be(sourceNotificationCount);
	}

	[Theory]
	[InlineData(ActorBorrowedBearTamerRecoveryTamper.PublicAnnouncement)]
	[InlineData(ActorBorrowedBearTamerRecoveryTamper.PrivateGuidance)]
	[InlineData(ActorBorrowedBearTamerRecoveryTamper.SoundEffect)]
	public void RehydrateSession_BorrowedBearTamerTamperedPendingGrowlPresentationIsRejectedBeforeRegistrationWithoutObserverExposure(
		ActorBorrowedBearTamerRecoveryTamper tamper)
	{
		var sourceObserver = new CountingStateChangeObserver();
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedBearTamerPendingGrowlSnapshot(sourceObserver);
		var sourceNotificationCount = sourceObserver.NotificationCount;
		var tampered = RecoveryPayloadTestDriver
			.Parse(snapshot.SerializedSession)
			.RewriteActorBorrowedBearTamerPendingGrowlPresentation(tamper)
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		var failure = rehydrate.Should().Throw<InvalidOperationException>().Which;
		AssertBearTamerRecoveryTextIsSourceSafe(failure.Message, snapshot);
		service.GetCurrentInstruction(snapshot.SessionId).Should().BeNull();
		service.GetGameStateView(snapshot.SessionId).Should().BeNull();
		var unavailable = service.ProcessInstruction(
			snapshot.SessionId,
			snapshot.Growl.CreateResponse());
		unavailable.IsSuccess.Should().BeFalse();
		var publicError = unavailable.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		publicError.Semantic.Should().Be(
			ModeratorInstructionSemantic.GameSessionNotFound);
		AssertBearTamerRecoveryTextIsSourceSafe(
			string.Concat(
				publicError.PublicAnnouncement,
				"\n",
				publicError.PrivateInstruction),
			snapshot);
		sourceNotificationCount.Should().BeGreaterThan(0);
		sourceObserver.NotificationCount.Should().Be(sourceNotificationCount);
	}

	[Fact]
	public void RehydrateSession_BorrowedBearTamerPendingGrowlWithoutCurrentActivationIsRejectedBeforeRegistrationWithoutObserverExposure()
	{
		var sourceObserver = new CountingStateChangeObserver();
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedBearTamerPendingGrowlSnapshot(sourceObserver);
		var sourceNotificationCount = sourceObserver.NotificationCount;
		var tampered = RecoveryPayloadTestDriver
			.Parse(snapshot.SerializedSession)
			.ExpireActorBorrowedBearTamerPendingGrowlActivation()
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		var failure = rehydrate.Should().Throw<InvalidOperationException>().Which;
		failure.Message.Should().Be(
			"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		AssertBearTamerRecoveryTextIsSourceSafe(failure.Message, snapshot);
		service.GetCurrentInstruction(snapshot.SessionId).Should().BeNull();
		service.GetGameStateView(snapshot.SessionId).Should().BeNull();
		var unavailable = service.ProcessInstruction(
			snapshot.SessionId,
			snapshot.Growl.CreateResponse());
		unavailable.IsSuccess.Should().BeFalse();
		var publicError = unavailable.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		publicError.Semantic.Should().Be(
			ModeratorInstructionSemantic.GameSessionNotFound);
		AssertBearTamerRecoveryTextIsSourceSafe(
			string.Concat(
				publicError.PublicAnnouncement,
				"\n",
				publicError.PrivateInstruction),
			snapshot);
		sourceNotificationCount.Should().BeGreaterThan(0);
		sourceObserver.NotificationCount.Should().Be(sourceNotificationCount);
	}

	[Fact]
	public void RehydrateSession_BorrowedBearTamerPendingGrowlWithDifferentCurrentActivationIsRejectedBeforeRegistrationWithoutObserverExposure()
	{
		var sourceObserver = new CountingStateChangeObserver();
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedBearTamerPendingGrowlSnapshot(sourceObserver);
		var sourceNotificationCount = sourceObserver.NotificationCount;
		var tampered = RecoveryPayloadTestDriver
			.Parse(snapshot.SerializedSession)
			.ReplaceActorBorrowedBearTamerPendingGrowlActivation()
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		var failure = rehydrate.Should().Throw<InvalidOperationException>().Which;
		failure.Message.Should().Be(
			"The pending Actor borrowed Role Power instruction does not match its recovery context.");
		AssertBearTamerRecoveryTextIsSourceSafe(failure.Message, snapshot);
		service.GetCurrentInstruction(snapshot.SessionId).Should().BeNull();
		service.GetGameStateView(snapshot.SessionId).Should().BeNull();
		var unavailable = service.ProcessInstruction(
			snapshot.SessionId,
			snapshot.Growl.CreateResponse());
		unavailable.IsSuccess.Should().BeFalse();
		var publicError = unavailable.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		publicError.Semantic.Should().Be(
			ModeratorInstructionSemantic.GameSessionNotFound);
		AssertBearTamerRecoveryTextIsSourceSafe(
			string.Concat(
				publicError.PublicAnnouncement,
				"\n",
				publicError.PrivateInstruction),
			snapshot);
		sourceNotificationCount.Should().BeGreaterThan(0);
		sourceObserver.NotificationCount.Should().Be(sourceNotificationCount);
	}

	[Theory]
	[InlineData(ActorBorrowedKnightRecoveryTamper.PublicAnnouncement)]
	[InlineData(ActorBorrowedKnightRecoveryTamper.PrivateGuidance)]
	[InlineData(ActorBorrowedKnightRecoveryTamper.AffectedPlayer)]
	[InlineData(ActorBorrowedKnightRecoveryTamper.SoundEffect)]
	public void RehydrateSession_BorrowedKnightTamperedPendingRustySwordAnnouncementPresentationIsRejectedBeforeRegistrationWithoutObserverExposure(
		ActorBorrowedKnightRecoveryTamper tamper)
	{
		var sourceObserver = new CountingStateChangeObserver();
		var snapshot = RecoveryPayloadTestDriver
			.CreateActorBorrowedKnightPendingRustySwordAnnouncementSnapshot(
				sourceObserver);
		var sourceNotificationCount = sourceObserver.NotificationCount;
		var tampered = RecoveryPayloadTestDriver
			.Parse(snapshot.SerializedSession)
			.RewriteActorBorrowedKnightPendingRustySwordAnnouncementPresentation(
				tamper)
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		var failure = rehydrate.Should().Throw<InvalidOperationException>().Which;
		AssertKnightRecoveryTextIsSourceSafe(failure.Message, snapshot);
		service.GetCurrentInstruction(snapshot.SessionId).Should().BeNull();
		service.GetGameStateView(snapshot.SessionId).Should().BeNull();
		var unavailable = service.ProcessInstruction(
			snapshot.SessionId,
			snapshot.Announcement.CreateResponse());
		unavailable.IsSuccess.Should().BeFalse();
		var publicError = unavailable.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		publicError.Semantic.Should().Be(
			ModeratorInstructionSemantic.GameSessionNotFound);
		AssertKnightRecoveryTextIsSourceSafe(
			string.Concat(
				publicError.PublicAnnouncement,
				"\n",
				publicError.PrivateInstruction),
			snapshot);
		sourceNotificationCount.Should().BeGreaterThan(0);
		sourceObserver.NotificationCount.Should().Be(sourceNotificationCount);
	}

	private static void AssertHunterRecoveryTextIsSourceSafe(
		string text,
		ActorBorrowedHunterPendingRecoverySnapshot snapshot)
	{
		text.Should().NotContain(GameStrings.HunterRoleName)
			.And.NotContain(MainRoleType.Hunter.ToString())
			.And.NotContain(snapshot.ActorSetupCardId.ToString())
			.And.NotContain(snapshot.ActivationId.ToString());
	}

	private static void AssertElderRecoveryTextIsSourceSafe(
		string text,
		ActorBorrowedElderPendingRecoverySnapshot snapshot)
	{
		text.Should().NotContain(GameStrings.ElderRoleName)
			.And.NotContain(MainRoleType.Elder.ToString())
			.And.NotContain("elder-village-vote-suppression")
			.And.NotContain(snapshot.ActorSetupCardId.ToString())
			.And.NotContain(snapshot.ActivationId.ToString())
			.And.NotContain(StatusEffectTypes.ElderProtectionLost.ToString());
	}

	private static ModeratorResponse CreateScapegoatRecoveryResponse(
		ModeratorInstruction instruction) => instruction switch
	{
		ConfirmationInstruction confirmation => confirmation.CreateResponse(),
		SelectPlayersInstruction selection => selection.CreateResponse(
			[selection.SelectablePlayerIds.First()]),
		_ => throw new InvalidOperationException(
			"The borrowed Scapegoat recovery fixture has an unsupported pending instruction.")
	};

	private static void AssertScapegoatRecoveryTextIsSourceSafe(
		string text,
		ActorBorrowedScapegoatPendingRecoverySnapshot snapshot)
	{
		text.Should().NotContain(GameStrings.ScapegoatRoleName)
			.And.NotContain(MainRoleType.Scapegoat.ToString())
			.And.NotContain("scapegoat-tie-replacement")
			.And.NotContain(snapshot.ActorSetupCardId.ToString())
			.And.NotContain(snapshot.ActivationId.ToString());
	}

	private static void AssertVillageIdiotRecoveryTextIsSourceSafe(
		string text,
		ActorBorrowedVillageIdiotPendingRecoverySnapshot snapshot)
	{
		text.Should().NotContain(GameStrings.VillageIdiotRoleName)
			.And.NotContain(MainRoleType.VillageIdiot.ToString())
			.And.NotContain("village-idiot-pardon")
			.And.NotContain(snapshot.ActorSetupCardId.ToString())
			.And.NotContain(snapshot.ActivationId.ToString())
			.And.NotContain(snapshot.PardonResourceId.ToString());
	}

	private static void AssertBearTamerRecoveryTextIsSourceSafe(
		string text,
		ActorBorrowedBearTamerPendingRecoverySnapshot snapshot)
	{
		text.Should().NotContain(GameStrings.BearTamerRoleName)
			.And.NotContain(MainRoleType.BearTamer.ToString())
			.And.NotContain(GameStrings.BearTamerGrowlInstruction)
			.And.NotContain("bear-tamer-growl")
			.And.NotContain(SoundEffectsEnum.BearGrowl.ToString())
			.And.NotContain(snapshot.ActorId.ToString())
			.And.NotContain(snapshot.ActorSetupCardId.ToString())
			.And.NotContain(snapshot.ActivationId.ToString());
	}

	private static void AssertKnightRecoveryTextIsSourceSafe(
		string text,
		ActorBorrowedKnightPendingRecoverySnapshot snapshot)
	{
		text.Should().NotContain(GameStrings.KnightWithRustySwordRoleName)
			.And.NotContain(MainRoleType.KnightWithRustySword.ToString())
			.And.NotContain("knight-rusty-sword-disease")
			.And.NotContain(snapshot.ActorId.ToString())
			.And.NotContain(snapshot.ActorSetupCardId.ToString())
			.And.NotContain(snapshot.ActivationId.ToString());
	}

	private static Action PrepareInvalidSubmission(PrivacyFixture fixture)
	{
		var selection = PrepareInstructionAfterWake<SelectPlayersInstruction>(
			fixture);
		if (fixture.SourceRole == MainRoleType.Cupid)
		{
			fixture.RestoreAt(selection);
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

		return () => Advance(fixture, staleResponse);
	}

	private static ValidationSubmission PrepareValidationSubmission(
		BorrowedValidationBranch branch)
	{
		var sourceRole = branch switch
		{
			BorrowedValidationBranch.SeerMissingSelection or
				BorrowedValidationBranch.SeerMultipleSelection => MainRoleType.Seer,
			BorrowedValidationBranch.CupidTamperedPendingSelection or
				BorrowedValidationBranch.CupidMalformedSelection or
				BorrowedValidationBranch.CupidAlreadyCommitted => MainRoleType.Cupid,
			BorrowedValidationBranch.WitchHealingMissingSelection or
				BorrowedValidationBranch.WitchHealingMultipleSelection or
				BorrowedValidationBranch.WitchHealingInvalidTarget or
				BorrowedValidationBranch.WitchPoisonMissingSelection or
				BorrowedValidationBranch.WitchPoisonMultipleSelection or
				BorrowedValidationBranch.WitchPoisonInvalidTarget => MainRoleType.Witch,
			_ => throw new ArgumentOutOfRangeException(nameof(branch))
		};
		var fixture = CreateFixture(sourceRole);
		var selection = PrepareInstructionAfterWake<SelectPlayersInstruction>(
			fixture);
		var nonActorPlayerIds = fixture.Session.GetPlayers()
			.Select(player => player.Id)
			.Where(playerId => playerId != fixture.ActorId)
			.ToArray();

		if (sourceRole == MainRoleType.Seer)
		{
			var response = branch == BorrowedValidationBranch.SeerMissingSelection
				? CreateUncheckedResponse(selection, null)
				: CreateUncheckedResponse(
					selection,
					nonActorPlayerIds.Take(2).ToHashSet());
			return CreateSubmission(fixture, response);
		}

		if (sourceRole == MainRoleType.Cupid)
		{
			var selectedPlayerIds = selection.SelectablePlayerIds
				.Take(2)
				.ToHashSet();
			var response = branch ==
			               BorrowedValidationBranch.CupidMalformedSelection
				? CreateUncheckedResponse(
					selection,
					selectedPlayerIds.Take(1).ToHashSet())
				: CreateUncheckedResponse(selection, selectedPlayerIds);
			fixture.RestoreAt(selection);
			if (branch ==
			    BorrowedValidationBranch.CupidTamperedPendingSelection)
			{
				var tamperedSession = fixture.Session;
				return new ValidationSubmission(
					tamperedSession,
					() => RecoveryPayloadTestDriver.Capture(tamperedSession)
						.RewritePendingPlayerSelectionCountConstraint(
							NumberRangeConstraint.Single)
						.RehydrateGameSession());
			}

			if (branch == BorrowedValidationBranch.CupidAlreadyCommitted)
			{
				var beneficiaries = selectedPlayerIds
					.Select(fixture.Session.RequireKnownFactionBeneficiary)
					.ToArray();
				fixture.Session.CommitActorBorrowedCupidLovers(
					CreatePowerIdentity(fixture, "cupid-link-lovers"),
					selectedPlayerIds,
					beneficiaries[0] == beneficiaries[1]
						? ActorBorrowedCupidLoversDisposition.SameFaction
						: ActorBorrowedCupidLoversDisposition.CrossFaction);
			}

			return CreateSubmission(fixture, response);
		}

		if (branch is BorrowedValidationBranch.WitchPoisonMissingSelection or
		    BorrowedValidationBranch.WitchPoisonMultipleSelection or
		    BorrowedValidationBranch.WitchPoisonInvalidTarget)
		{
			selection = Advance(fixture, selection.CreateResponse([])).Instruction
				.Should().BeOfType<SelectPlayersInstruction>().Subject;
		}

		IReadOnlySet<Guid>? selectedForWitch = branch switch
		{
			BorrowedValidationBranch.WitchHealingMissingSelection or
				BorrowedValidationBranch.WitchPoisonMissingSelection => null,
			BorrowedValidationBranch.WitchHealingMultipleSelection or
				BorrowedValidationBranch.WitchPoisonMultipleSelection =>
				nonActorPlayerIds.Take(2).ToHashSet(),
			BorrowedValidationBranch.WitchHealingInvalidTarget or
				BorrowedValidationBranch.WitchPoisonInvalidTarget =>
				new HashSet<Guid> { fixture.ActorId },
			_ => throw new ArgumentOutOfRangeException(nameof(branch))
		};
		return CreateSubmission(
			fixture,
			CreateUncheckedResponse(selection, selectedForWitch));
	}

	private static ValidationSubmission CreateSubmission(
		PrivacyFixture fixture,
		ModeratorResponse response) => new(
		fixture.Session,
		() => Advance(fixture, response));

	private static ModeratorResponse CreateUncheckedResponse(
		SelectPlayersInstruction selection,
		IReadOnlySet<Guid>? selectedPlayerIds) => new()
	{
		InstructionId = selection.InstructionId,
		Type = ExpectedInputType.PlayerSelection,
		SelectedPlayerIds = selectedPlayerIds
	};

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

	private static SpendOpening PerformSpendOpening(
		GameSession session,
		StartGameConfirmationInstruction start,
		Guid selectedCardId)
	{
		var wake = Advance(
			session,
			start.CreateResponse(),
			publishInstruction: true).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var choice = Advance(
			session,
			wake.CreateResponse(),
			publishInstruction: true).Instruction
			.Should().BeOfType<SelectOptionsInstruction>().Subject;
		var sleep = Advance(
			session,
			choice.CreateResponse(selectedCardId.ToString("D")),
			publishInstruction: true).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var activation = session
			.GetModeratorActiveActorBorrowedRolePowerActivation();
		activation.Should().NotBeNull();

		return new SpendOpening(activation!, sleep.CreateResponse());
	}

	private static TInstruction PrepareInstructionAfterWake<TInstruction>(
		PrivacyFixture fixture)
		where TInstruction : ModeratorInstruction
	{
		var wake = Advance(fixture, fixture.OpeningResponse).Instruction
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		TInstruction instruction;
		if (fixture.SourceRole is MainRoleType.Fox or MainRoleType.Defender
			or MainRoleType.Seer)
		{
			fixture.RestoreAt(wake);
			instruction = GameFlowManager.HandleInput(
					fixture.Session,
					wake.CreateResponse(),
					SupportedRoleCatalog.Admissions)
				.ModeratorInstruction.Should()
				.BeOfType<TInstruction>().Subject;
		}
		else
		{
			instruction = Advance(fixture, wake.CreateResponse()).Instruction
				.Should().BeOfType<TInstruction>().Subject;
		}

		if (fixture.SourceRole is MainRoleType.Seer or MainRoleType.Fox)
		{
			SeedKnownWerewolfAgentFacts(
				fixture.Session,
				fixture.Session.GetPlayers().Skip(1).First().Id);
		}

		return instruction;
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
		SeedKnownWerewolfAgentFacts(
			session,
			sourceRole is MainRoleType.Seer or MainRoleType.Fox or
				MainRoleType.Witch
				? null
				: players[1].Id);
		session.TransitionMainPhase(GamePhase.Day);
		session.TransitionMainPhase(GamePhase.Night);
		if (sourceRole == MainRoleType.Witch)
		{
			session.PerformNightAction(
				NightActionType.WerewolfVictimSelection,
				players[^1].Id);
		}

		var opening = PerformSpendOpening(
			session,
			start,
			sourceCard.Id);
		return new PrivacyFixture(
			sourceRole,
			session,
			actorId,
			opening.Activation,
			opening.SourceOpeningResponse);
	}

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
		Guid? werewolfId)
	{
		var hadCompleteAgentKnowledge = session.GetPlayers().All(player =>
			session.GetFactionAgentKnowledge(player.Id, Faction.Werewolf) !=
			FactionAgentKnowledge.Unknown);
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
		if (werewolfId is not { } knownWerewolfId ||
			hadCompleteAgentKnowledge)
		{
			return;
		}

		InitialBeneficiaryClosureRules.TryCommitCurrentSession(session, boundary)
			.Should().Be(InitialBeneficiaryClosureResult.Committed);
		session.GetPlayers().Should().OnlyContain(player =>
			session.GetFactionBeneficiaryKnowledge(player.Id).IsKnown);
		session.GetFactionBeneficiaryKnowledge(knownWerewolfId).Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Werewolf));
	}

	private static HookListenerActionResult Advance(
		PrivacyFixture fixture,
		ModeratorResponse response) => Advance(
		fixture.Session,
		response,
		publishInstruction: fixture.SourceRole is
			MainRoleType.LittleGirl or MainRoleType.Witch
			or MainRoleType.StutteringJudge or MainRoleType.Cupid);

	private static HookListenerActionResult Advance(
		GameSession session,
		ModeratorResponse response,
		bool publishInstruction = false)
	{
		var consumedInstruction = publishInstruction
			? session.Execution.PendingInstruction ??
			  throw new InvalidOperationException(
				  "The Actor borrowed privacy harness requires one Pending Instruction.")
			: null;
		var manager = new SubPhaseManager<HookHarnessSubPhase>(
			HookHarnessSubPhase.Active,
			[
				HookSubPhaseStage.HookStage(GameHook.NightMainActionLoop),
				NavigationSubPhaseStage.NavigationEndStageSilent(GamePhase.Dawn)
			]);
		var result = manager.Execute(session, response).Should()
			.BeOfType<StayInSubPhaseHandlerResult>().Subject;
		if (!result.StageComplete)
		{
			result.ModeratorInstruction.Should().NotBeNull();
			var nextInstruction = result.ModeratorInstruction!;
			if (consumedInstruction != null)
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
			return HookListenerActionResult.NeedInput(
				nextInstruction,
				HookHarnessListenerState.AwaitingInput);
		}

		result.ModeratorInstruction.Should().BeNull();
		return HookListenerActionResult.Complete(
			HookHarnessListenerState.Complete);
	}

	private static PhysicalCharacterCard Card(
		string id,
		MainRoleType role) => new(Guid.Parse(id), role);

	private sealed record SpendOpening(
		ActorBorrowedRolePowerActivation Activation,
		ModeratorResponse SourceOpeningResponse);

	private sealed class PrivacyFixture
	{
		internal PrivacyFixture(
			MainRoleType sourceRole,
			GameSession session,
			Guid actorId,
			ActorBorrowedRolePowerActivation activation,
			ModeratorResponse openingResponse)
		{
			SourceRole = sourceRole;
			Session = session;
			ActorId = actorId;
			Activation = activation;
			OpeningResponse = openingResponse;
		}

		internal MainRoleType SourceRole { get; }
		internal GameSession Session { get; private set; }
		internal Guid ActorId { get; }
		internal ActorBorrowedRolePowerActivation Activation { get; }
		internal ModeratorResponse OpeningResponse { get; }

		internal void RestoreAt(ModeratorInstruction instruction)
		{
			Session = RecoveryPayloadTestDriver.Capture(Session)
				.RecordActorSetupCardSpend(Activation)
				.WithPendingInstruction(instruction)
				.RehydrateGameSession();
		}
	}

	private sealed record ValidationSubmission(
		GameSession Session,
		Action Submit);

	private sealed class CountingStateChangeObserver : IStateChangeObserver
	{
		internal int NotificationCount { get; private set; }

		public void OnLogEntryApplied(GameLogEntryBase entry) =>
			NotificationCount++;

		public void OnMainPhaseChanged(GamePhase newPhase) =>
			NotificationCount++;

		public void OnSubPhaseChanged(string? newSubPhase) =>
			NotificationCount++;

		public void OnSubPhaseStageChanged(string? newSubPhaseStage) =>
			NotificationCount++;

		public void OnListenerChanged(
			ListenerIdentifier? listener,
			string? listenerState) => NotificationCount++;

		public void OnTurnNumberChanged(int newTurnNumber) =>
			NotificationCount++;

		public void OnPendingInstructionChanged(
			ModeratorInstruction? instruction) => NotificationCount++;
	}

	public enum BorrowedValidationBranch
	{
		SeerMissingSelection,
		SeerMultipleSelection,
		CupidTamperedPendingSelection,
		CupidMalformedSelection,
		CupidAlreadyCommitted,
		WitchHealingMissingSelection,
		WitchHealingMultipleSelection,
		WitchHealingInvalidTarget,
		WitchPoisonMissingSelection,
		WitchPoisonMultipleSelection,
		WitchPoisonInvalidTarget
	}

	private enum HookHarnessSubPhase
	{
		Active
	}

	private enum HookHarnessListenerState
	{
		AwaitingInput,
		Complete
	}
}
