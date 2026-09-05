using System.Collections.Immutable;
using FluentAssertions;
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

public sealed class DevotedServantRoleTakeValidationTests(
	ITestOutputHelper output) : DiagnosticTestBase(output)
{
	[Fact]
	public void TryCommitDevotedServantRoleTake_ModeratorKnownRoleEstablishedAfterRequestMatchesObservedRole_ReturnsTrueAndCommitsRoleTake()
	{
		var (builder, players) = CreateDayOneScenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.DevotedServant,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var target = players[2];
		var servant = players[3];
		builder.ArrangeCurrentRole(target.Id, MainRoleType.SimpleVillager);
		var window = OpenWindow(builder, target.Id);
		builder.Process(window.CreatePublicSelfRevealResponse(servant.Id))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>();
		var session = (GameSession)builder.GetGameState()!;
		target.State.ModeratorKnownRole.Should().BeNull();
		var request = PermanentRoleSwapRules.CreateDevotedServantRoleTakeRequest(
			session,
			servant.Id,
			target.Id,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);

		builder.ArrangeKnownRole(target.Id, MainRoleType.SimpleVillager);
		target.State.ModeratorKnownRole.Should().Be(MainRoleType.SimpleVillager);
		session.TryCommitDevotedServantRoleTake(request).Should().BeTrue();
		session.GameHistoryLog
			.OfType<DevotedServantRoleTakenCommittedLogEntry>()
			.Should().ContainSingle();
		MarkTestCompleted();
	}

	[Fact]
	public void TryCommitDevotedServantRoleTake_ModeratorKnownRoleEstablishedAfterRequestContradictsObservedRole_ReturnsFalseWithoutMutation()
	{
		var (builder, players) = CreateDayOneScenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.DevotedServant,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var target = players[2];
		var servant = players[3];
		builder.ArrangeCurrentRole(target.Id, MainRoleType.SimpleVillager);
		var window = OpenWindow(builder, target.Id);
		builder.Process(window.CreatePublicSelfRevealResponse(servant.Id))
			.ModeratorInstruction.Should()
			.BeOfType<AssignRolesInstruction>();
		var session = (GameSession)builder.GetGameState()!;
		target.State.ModeratorKnownRole.Should().BeNull();
		var request = PermanentRoleSwapRules.CreateDevotedServantRoleTakeRequest(
			session,
			servant.Id,
			target.Id,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);

		builder.ArrangeKnownRole(target.Id, MainRoleType.Seer);
		target.State.ModeratorKnownRole.Should().Be(MainRoleType.Seer);
		var servantBefore = CapturePlayerState(servant);
		var targetBefore = CapturePlayerState(target);
		var historyBefore = session.GameHistoryLog.ToArray();
		var cardsBefore = session.GetModeratorPhysicalCharacterCards().ToArray();

		session.TryCommitDevotedServantRoleTake(request).Should().BeFalse();

		CapturePlayerState(servant).Should().BeEquivalentTo(servantBefore);
		CapturePlayerState(target).Should().BeEquivalentTo(targetBefore);
		session.GameHistoryLog.Should().Equal(historyBefore);
		session.GetModeratorPhysicalCharacterCards().Should().Equal(cardsBefore);
		session.GameHistoryLog
			.OfType<DevotedServantRoleTakenCommittedLogEntry>()
			.Should().BeEmpty();
		MarkTestCompleted();
	}

	private (GameTestBuilder Builder, IPlayer[] Players)
		CreateDayOneScenario(params MainRoleType[] roles)
	{
		var builder = CreateBuilder()
			.WithPlayers(roles.Length)
			.WithRoles(roles);
		builder.StartGame();
		builder.ConfirmGameStart();
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		builder.CompleteNightPhase([players[0].Id], players[1].Id);
		builder.CompleteDawnPhase(new()
		{
			[players[1].Id] = MainRoleType.SimpleVillager
		});
		return (builder, players);
	}

	private static DevotedServantVoteWindowInstruction OpenWindow(
		GameTestBuilder builder,
		Guid voteTargetId)
	{
		var debate = builder.GetCurrentInstruction()
			.Should().BeOfType<ConfirmationInstruction>().Subject;
		var vote = builder.Process(debate.CreateResponse())
			.ModeratorInstruction.Should()
			.BeOfType<SelectPlayersInstruction>().Subject;
		return builder.Process(vote.CreateResponse([voteTargetId]))
			.ModeratorInstruction.Should()
			.BeOfType<DevotedServantVoteWindowInstruction>().Subject;
	}

	private static PlayerStateSnapshot CapturePlayerState(IPlayer player) => new(
		player.State.CurrentRole,
		player.State.PhysicalCharacterCardId,
		player.State.PhysicalCharacterCardRole,
		player.State.ModeratorKnownRole,
		player.State.PubliclyRevealedRole,
		player.State.Health,
		player.State.HasVotingRight,
		player.State.DurableVotingPower,
		player.State.GetActiveStatusEffects().Order().ToImmutableArray(),
		player.State.FactionBeneficiary,
		Enum.GetValues<Faction>().ToImmutableDictionary(
			faction => faction,
			player.State.GetFactionAgentKnowledge));

	private sealed record PlayerStateSnapshot(
		MainRoleType? CurrentRole,
		Guid? PhysicalCharacterCardId,
		MainRoleType? PhysicalCharacterCardRole,
		MainRoleType? ModeratorKnownRole,
		MainRoleType? PubliclyRevealedRole,
		PlayerHealth Health,
		bool HasVotingRight,
		int DurableVotingPower,
		ImmutableArray<StatusEffectTypes> StatusEffects,
		FactionBeneficiaryKnowledge FactionBeneficiary,
		ImmutableDictionary<Faction, FactionAgentKnowledge> FactionAgents);
}
