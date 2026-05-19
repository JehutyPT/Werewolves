using System.Globalization;
using FluentAssertions;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Xunit;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Services;

public class DashboardStatsSnapshotTests
{
	[Fact]
	public void FromSession_GroupsKnownAliveRolesByRoleGroup()
	{
		var players = new[]
		{
			FakePlayer.Create(PlayerNames.Ana, MainRoleType.SimpleVillager),
			FakePlayer.Create(PlayerNames.Bruno, MainRoleType.Seer),
			FakePlayer.Create(PlayerNames.Carla, MainRoleType.SimpleWerewolf),
			FakePlayer.Create(PlayerNames.Diana, MainRoleType.SimpleWerewolf, PlayerHealth.Dead),
			FakePlayer.Create(PlayerNames.Eva, MainRoleType.WildChild),
			FakePlayer.Create(PlayerNames.Filipe)
		};
		var session = new FakeGameSession(players);

		var snapshot = DashboardStatsSnapshot.FromSession(session);

		snapshot.RoleGroups.Select(group => group.Group).Should().Equal(
			RoleGroup.Villagers,
			RoleGroup.Werewolves,
			RoleGroup.Ambiguous,
			RoleGroup.Loners);
		snapshot.RoleGroups.Select(group => group.RemainingCount).Should().Equal(2, 1, 1, 0);
	}

	[Fact]
	public void FromSession_ListsEliminationsInChronologicalOrderWithTurnAndPhase()
	{
		var ana = FakePlayer.Create(PlayerNames.Ana, MainRoleType.SimpleVillager, PlayerHealth.Dead);
		var bruno = FakePlayer.Create(PlayerNames.Bruno, MainRoleType.SimpleWerewolf, PlayerHealth.Dead);
		var session = new FakeGameSession(
			[ana, bruno],
			[
				new PlayerEliminatedLogEntry
				{
					Timestamp = new DateTimeOffset(2026, 5, 14, 21, 0, 0, TimeSpan.Zero),
					TurnNumber = 1,
					CurrentPhase = GamePhase.Dawn,
					PlayerId = ana.Id,
					Reason = EliminationReason.WerewolfAttack
				},
				new PlayerEliminatedLogEntry
				{
					Timestamp = new DateTimeOffset(2026, 5, 14, 21, 20, 0, TimeSpan.Zero),
					TurnNumber = 2,
					CurrentPhase = GamePhase.Day,
					PlayerId = bruno.Id,
					Reason = EliminationReason.DayVote
				}
			]);

		var snapshot = DashboardStatsSnapshot.FromSession(session);

		snapshot.EliminationLog.Select(entry => entry.PlayerName).Should().Equal(PlayerNames.DefaultTwo);
		snapshot.EliminationLog.Select(entry => entry.TurnPhaseLabel).Should().Equal(
			PhaseTurnLabel(GamePhase.Dawn, 1),
			PhaseTurnLabel(GamePhase.Day, 2));
		snapshot.EliminationLog.Select(entry => entry.ReasonLabel).Should().Equal(
			DashboardStatsSnapshot.EliminationReasonLabel(EliminationReason.WerewolfAttack),
			DashboardStatsSnapshot.EliminationReasonLabel(EliminationReason.DayVote));
	}

	private static string PhaseTurnLabel(GamePhase phase, int turnNumber) =>
		string.Format(
			CultureInfo.CurrentCulture,
			ClientStrings.Dashboard_PhaseTurnFormat,
			DashboardStatsSnapshot.PhaseLabel(phase),
			turnNumber);

	private sealed class FakeGameSession(IReadOnlyList<IPlayer> players, IReadOnlyList<GameLogEntryBase>? log = null) : IGameSession
	{
		public IEnumerable<GameLogEntryBase> GameHistoryLog => log ?? [];
		public Guid Id { get; } = Guid.NewGuid();
		public int TurnNumber => 1;

		public GamePhase GetCurrentPhase() => GamePhase.Day;

		public IPlayer GetPlayer(Guid playerId) => players.Single(player => player.Id == playerId);

		public IPlayerState GetPlayerState(Guid playerId) => GetPlayer(playerId).State;

		public IEnumerable<IPlayer> GetPlayers() => players;

		public int RoleInPlayCount(MainRoleType type) =>
			players.Count(player => player.State.MainRole == type);

		public string Serialize() => throw new NotSupportedException();
	}

	private sealed class FakePlayer : IPlayer
	{
		private FakePlayer(string name, MainRoleType? role, PlayerHealth health)
		{
			Name = name;
			State = new FakePlayerState(role, health);
		}

		public Guid Id { get; } = Guid.NewGuid();
		public string Name { get; init; }
		public IPlayerState State { get; }

		public static FakePlayer Create(
			string name,
			MainRoleType? role = null,
			PlayerHealth health = PlayerHealth.Alive) =>
			new(name, role, health);
	}

	private sealed class FakePlayerState(MainRoleType? role, PlayerHealth health) : IPlayerState
	{
		public MainRoleType? MainRole { get; } = role;
		public PlayerHealth Health { get; } = health;
		public bool IsImmuneToLynching => false;
		public string? LynchingImmunityAnnouncement => null;

		public List<StatusEffectTypes> GetActiveStatusEffects() => [];

		public bool HasStatusEffect(StatusEffectTypes effect) => effect == StatusEffectTypes.None;
	}
}
