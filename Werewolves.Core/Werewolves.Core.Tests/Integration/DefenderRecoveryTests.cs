using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public sealed class DefenderRecoveryTests
{
	[Theory]
	[InlineData(RecurringTamper.Actor)]
	[InlineData(RecurringTamper.SourceRole)]
	[InlineData(RecurringTamper.SourcePower)]
	[InlineData(RecurringTamper.PowerInstance)]
	[InlineData(RecurringTamper.PowerOrigin)]
	[InlineData(RecurringTamper.Action)]
	public void CommittedProtection_OwnerIdentityTamperIsRejected(
		RecurringTamper tamper)
	{
		var recovery = CreateCommittedProtection();
		var driver = RecoveryPayloadTestDriver.Parse(recovery.SerializedSession);
		switch (tamper)
		{
			case RecurringTamper.Actor:
				driver.RewriteRecurringActorAndCursor(recovery.OtherPlayerId);
				break;
			case RecurringTamper.SourceRole:
				driver.RewriteRecurringSourceRoleAndCursor(
					MainRoleType.Seer);
				break;
			case RecurringTamper.SourcePower:
				driver.RewriteRecurringPowerAndCursor(
					"not-defender-protection");
				break;
			case RecurringTamper.PowerInstance:
				driver.RewriteRecurringInstanceAndCursor(
					recovery.OtherPlayerId);
				break;
			case RecurringTamper.PowerOrigin:
				driver.RewriteRecurringOriginAndCursor(
					RolePowerInstanceOrigin.Borrowed);
				break;
			case RecurringTamper.Action:
				driver.RewriteRecurringActionAndCursor(
					NightActionType.BigBadWolfVictimSelection);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(tamper));
		}
		var service = new GameService();

		Action rehydrate = () =>
			service.RehydrateSession(driver.Serialize());

		rehydrate.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void CommittedProtection_RetargetedToCurrentLittleGirlIsRejected()
	{
		var recovery = CreateCommittedProtection();
		var tampered = RecoveryPayloadTestDriver
			.Parse(recovery.SerializedSession)
			.RetargetLatestRecurringNightActionAndCursor(
				recovery.LittleGirlId)
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>()
			.WithMessage("*legal target*");
	}

	[Fact]
	public void CommittedProtection_WrongContinuationSemanticIsRejected()
	{
		var recovery = CreateCommittedProtection();
		var tampered = RecoveryPayloadTestDriver
			.Parse(recovery.SerializedSession)
			.RewriteRecurringNextSemantic(
				ModeratorInstructionSemantic.WakeRole)
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void CommittedProtection_SleepForDifferentPlayerIsRejected()
	{
		var recovery = CreateCommittedProtection();
		var tampered = RecoveryPayloadTestDriver
			.Parse(recovery.SerializedSession)
			.RewritePendingConfirmationAffectedPlayer(
				recovery.OtherPlayerId)
			.Serialize();
		var service = new GameService();

		Action rehydrate = () => service.RehydrateSession(tampered);

		rehydrate.Should().Throw<InvalidOperationException>();
	}

	private static CommittedProtectionRecovery CreateCommittedProtection()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(6)
			.WithRoles(
				MainRoleType.Defender,
				MainRoleType.LittleGirl,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var defender = players[0];
		var littleGirl = players[1];
		var target = players[3];
		builder.ArrangeKnownRole(defender.Id, MainRoleType.Defender);
		builder.ArrangeKnownRole(littleGirl.Id, MainRoleType.LittleGirl);
		builder.ConfirmGameStart();
		var wake =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.ConfirmNightStart());
		var selection =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.Process(wake.CreateResponse()));
		var sleep =
			InstructionAssert.ExpectSuccessWithType<ConfirmationInstruction>(
				builder.Process(selection.CreateResponse([target.Id])));
		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		return new CommittedProtectionRecovery(
			builder.GetGameState()!.Serialize(),
			littleGirl.Id,
			players[4].Id);
	}

	public enum RecurringTamper
	{
		Actor,
		SourceRole,
		SourcePower,
		PowerInstance,
		PowerOrigin,
		Action
	}

	private sealed record CommittedProtectionRecovery(
		string SerializedSession,
		Guid LittleGirlId,
		Guid OtherPlayerId);
}
