using FluentAssertions;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class ActorRuntimeStateTests
{
	private static readonly PhysicalCharacterCard SeerCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000141"),
		MainRoleType.Seer);
	private static readonly PhysicalCharacterCard CupidCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000142"),
		MainRoleType.Cupid);
	private static readonly PhysicalCharacterCard WitchCard = new(
		Guid.Parse("00000000-0000-0000-0000-000000000143"),
		MainRoleType.Witch);

	[Fact]
	public void NewSession_CarriesTheExactActorSetupInventoryAsRemaining()
	{
		var setup = CreateActorSetupCards();
		var session = CreateActorSession(setup);

		session.GetModeratorActorSetupCards().Should().Be(setup);
		session.GetModeratorRemainingActorSetupCards().Should().Equal(
			SeerCard,
			CupidCard,
			WitchCard);
		session.GetModeratorSpentActorSetupCards().Should().BeEmpty();
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
	}

	[Fact]
	public void Spend_MovesOneRemainingCardAndCreatesActorSpecificBorrowedLineage()
	{
		var session = CreateActorSession(CreateActorSetupCards());
		var actor = session.GetPlayers().First();
		session.AssignRole(actor.Id, MainRoleType.Actor);
		var beneficiaryBeforeSpend =
			session.GetFactionBeneficiaryKnowledge(actor.Id);

		var spent = session.TrySpendActorSetupCard(
			actor.Id,
			SeerCard.Id,
			out var activation);

		spent.Should().BeTrue();
		activation.Should().NotBeNull();
		activation!.ActivationId.Should().NotBeEmpty();
		activation.ActivationId.Should().NotBe(session.Id);
		session.GetPlayers().Select(player => player.Id).Should()
			.NotContain(activation.ActivationId);
		session.GetModeratorActorSetupCards().Cards.Select(card => card.Id).Should()
			.NotContain(activation.ActivationId);
		activation.ActingPlayerId.Should().Be(actor.Id);
		activation.ActingRole.Should().Be(MainRoleType.Actor);
		activation.SelectedCardId.Should().Be(SeerCard.Id);
		activation.SourceRole.Should().Be(MainRoleType.Seer);
		activation.Origin.Should().Be(RolePowerInstanceOrigin.Borrowed);
		session.GetModeratorRemainingActorSetupCards().Should().Equal(
			CupidCard,
			WitchCard);
		session.GetModeratorSpentActorSetupCards().Should().Equal(SeerCard);
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
		session.GetPlayerState(actor.Id).CurrentRole.Should()
			.Be(MainRoleType.Actor);
		session.GetFactionBeneficiaryKnowledge(actor.Id).Should()
			.Be(beneficiaryBeforeSpend);
	}

	[Fact]
	public void ActorSetupInventory_SurvivesProcessBoundaryWithExactVersionAndIds()
	{
		var setup = CreateActorSetupCards();
		var original = CreateActorSession(setup);

		var recovered = new GameSession(original.Serialize());

		recovered.GetModeratorActorSetupCards().Should().Be(setup);
		recovered.GetModeratorRemainingActorSetupCards().Should().Equal(
			SeerCard,
			CupidCard,
			WitchCard);
		recovered.GetModeratorSpentActorSetupCards().Should().BeEmpty();
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
	}

	[Theory]
	[InlineData(InvalidSpendCase.UnknownPlayer)]
	[InlineData(InvalidSpendCase.NonActor)]
	[InlineData(InvalidSpendCase.DeadActor)]
	[InlineData(InvalidSpendCase.OutsideInventory)]
	public void InvalidSpend_LeavesActorRuntimeStateUnchanged(
		InvalidSpendCase invalidCase)
	{
		var session = CreateActorSession(CreateActorSetupCards());
		var actor = session.GetPlayers().First();
		var actingPlayerId = actor.Id;
		var selectedCardId = SeerCard.Id;
		switch (invalidCase)
		{
			case InvalidSpendCase.UnknownPlayer:
				actingPlayerId = Guid.NewGuid();
				break;
			case InvalidSpendCase.NonActor:
				session.AssignRole(actor.Id, MainRoleType.SimpleVillager);
				break;
			case InvalidSpendCase.DeadActor:
				session.AssignRole(actor.Id, MainRoleType.Actor);
				session.EliminatePlayer(
					actor.Id,
					EliminationReason.EventElimination);
				break;
			case InvalidSpendCase.OutsideInventory:
				session.AssignRole(actor.Id, MainRoleType.Actor);
				selectedCardId = Guid.NewGuid();
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(invalidCase));
		}
		var logCountBefore = session.GameHistoryLog.Count();

		var spent = session.TrySpendActorSetupCard(
			actingPlayerId,
			selectedCardId,
			out var activation);

		spent.Should().BeFalse();
		activation.Should().BeNull();
		session.GetModeratorRemainingActorSetupCards().Should().HaveCount(3);
		session.GetModeratorSpentActorSetupCards().Should().BeEmpty();
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
		session.GameHistoryLog.Should().HaveCount(logCountBefore);
	}

	[Fact]
	public void ActiveOrAlreadySpentCard_CannotBeSpentAgain()
	{
		var session = CreateActorSession(CreateActorSetupCards());
		var actor = session.GetPlayers().First();
		session.AssignRole(actor.Id, MainRoleType.Actor);
		session.TrySpendActorSetupCard(actor.Id, SeerCard.Id, out var first)
			.Should().BeTrue();

		session.TrySpendActorSetupCard(actor.Id, CupidCard.Id, out var replacement)
			.Should().BeFalse();
		replacement.Should().BeNull();
		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(first);
		session.GetModeratorSpentActorSetupCards().Should().Equal(SeerCard);

		session.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		var logCountAfterExpiry = session.GameHistoryLog.Count();
		session.TrySpendActorSetupCard(actor.Id, SeerCard.Id, out var duplicate)
			.Should().BeFalse();
		duplicate.Should().BeNull();
		session.GameHistoryLog.Should().HaveCount(logCountAfterExpiry);

		session.TrySpendActorSetupCard(actor.Id, CupidCard.Id, out var next)
			.Should().BeTrue();
		next!.ActivationId.Should().NotBe(first!.ActivationId);
	}

	[Fact]
	public void Expiry_IsIdempotentAndNeverRefundsTheSpentCard()
	{
		var session = CreateActorSession(CreateActorSetupCards());
		var actor = session.GetPlayers().First();
		session.AssignRole(actor.Id, MainRoleType.Actor);
		session.TrySpendActorSetupCard(actor.Id, SeerCard.Id, out _)
			.Should().BeTrue();

		session.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		session.TryExpireActorBorrowedRolePowerActivation().Should().BeFalse();

		session.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
		session.GetModeratorSpentActorSetupCards().Should().Equal(SeerCard);
		session.GetModeratorRemainingActorSetupCards().Should().Equal(
			CupidCard,
			WitchCard);
		session.GameHistoryLog
			.OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
			.Should().ContainSingle();
	}

	[Fact]
	public void CommittedSpend_RoundTripPreservesSpentCardActivationAndPendingSleep()
	{
		var session = CreateActorSession(CreateActorSetupCards());
		var actor = session.GetPlayers().First();
		session.AssignRole(actor.Id, MainRoleType.Actor);
		session.TrySpendActorSetupCard(actor.Id, SeerCard.Id, out var activation)
			.Should().BeTrue();
		var sleep = new ConfirmationInstruction(
			ModeratorInstructionSemantic.PutRoleToSleep,
			publicAnnouncement: "sleep",
			affectedPlayerIds: [actor.Id]);
		session.SetPendingModeratorInstruction(RecoveryBoundaryKey.Instance, sleep);
		session.CaptureRecoveryBoundary(
			RecoveryBoundaryKey.Instance,
			domainRecoveryCursor: new DomainRecoveryCursor
			{
				Version = DomainRecoveryCursor.CurrentVersion,
				Kind = DomainRecoveryCursorKind.ActorSetupCardSpendCommit,
				CommittedActionType = NightActionType.Unknown,
				ActingPlayerId = actor.Id,
				SourceRole = MainRoleType.Seer,
				ActorSetupCardId = SeerCard.Id,
				ActorBorrowedActivationId = activation!.ActivationId,
				CommittedTargetIds = [],
				NextInstructionSemantic =
					ModeratorInstructionSemantic.PutRoleToSleep,
				NextInstructionId = sleep.InstructionId
			});

		var recovered = new GameSession(session.Serialize());

		recovered.GetModeratorActorSetupCards().Should()
			.Be(CreateActorSetupCards());
		recovered.GetModeratorRemainingActorSetupCards().Should().Equal(
			CupidCard,
			WitchCard);
		recovered.GetModeratorSpentActorSetupCards().Should().Equal(SeerCard);
		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.Be(activation);
		var recoveredSleep = recovered.PendingModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		recoveredSleep.InstructionId.Should().Be(sleep.InstructionId);
		recoveredSleep.Semantic.Should()
			.Be(ModeratorInstructionSemantic.PutRoleToSleep);
		recoveredSleep.PublicAnnouncement.Should().Be(sleep.PublicAnnouncement);
		recoveredSleep.AffectedPlayerIds.Should().Equal(actor.Id);
		recovered.TrySpendActorSetupCard(actor.Id, SeerCard.Id, out _).Should()
			.BeFalse();
		recovered.GameHistoryLog
			.OfType<ActorSetupCardSpendCommittedLogEntry>()
			.Should().ContainSingle();
		recovered.GameHistoryLog
			.OfType<OneUseRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
		recovered.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().BeEmpty();
	}

	[Fact]
	public void CompletedExpiry_RoundTripDoesNotRestoreOrReplayActivation()
	{
		var session = CreateActorSession(CreateActorSetupCards());
		var actor = session.GetPlayers().First();
		session.AssignRole(actor.Id, MainRoleType.Actor);
		session.TrySpendActorSetupCard(actor.Id, SeerCard.Id, out _)
			.Should().BeTrue();
		session.TryExpireActorBorrowedRolePowerActivation().Should().BeTrue();
		session.CaptureRecoveryBoundary(RecoveryBoundaryKey.Instance);

		var recovered = new GameSession(session.Serialize());

		recovered.GetModeratorActiveActorBorrowedRolePowerActivation().Should()
			.BeNull();
		recovered.GetModeratorSpentActorSetupCards().Should().Equal(SeerCard);
		recovered.TryExpireActorBorrowedRolePowerActivation().Should().BeFalse();
		recovered.GameHistoryLog
			.OfType<ActorBorrowedRolePowerActivationExpiredLogEntry>()
			.Should().ContainSingle();
	}

	private static ActorSetupCards CreateActorSetupCards() => new(
		version: 7,
		new[] { WitchCard, SeerCard, CupidCard });

	private static GameSession CreateActorSession(ActorSetupCards setup)
	{
		var config = new GameSessionConfig(
			new List<string>
			{
				GameStrings.ActorRoleName,
				"Werewolf",
				"Villager 1",
				"Villager 2",
				"Villager 3"
			},
			new List<MainRoleType>
			{
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			},
			setup);
		var sessionId = Guid.NewGuid();
		return new GameSession(
			sessionId,
			new StartGameConfirmationInstruction(sessionId),
			config);
	}

	public enum InvalidSpendCase
	{
		UnknownPlayer,
		NonActor,
		DeadActor,
		OutsideInventory
	}

	private sealed class RecoveryBoundaryKey : IGameFlowManagerKey
	{
		internal static RecoveryBoundaryKey Instance { get; } = new();
	}
}
