using System.Globalization;
using FluentAssertions;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Xunit;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Services;

public class DashboardStatsSnapshotTests
{
	[Fact]
	public void FromSession_GroupsOnlyPubliclyRevealedAliveRolesWithoutLeakingPrivateKnowledge()
	{
		var players = new[]
		{
			FakePlayer.Create(
				PlayerNames.Ana,
				MainRoleType.VillagerVillager,
				physicalCharacterCardRole: MainRoleType.VillagerVillager,
				moderatorKnownRole: MainRoleType.VillagerVillager,
				publiclyRevealedRole: MainRoleType.VillagerVillager),
			FakePlayer.Create(
				PlayerNames.Bruno,
				MainRoleType.Seer,
				moderatorKnownRole: MainRoleType.Seer,
				publiclyRevealedRole: MainRoleType.Seer),
			FakePlayer.Create(
				PlayerNames.Carla,
				MainRoleType.SimpleWerewolf,
				moderatorKnownRole: MainRoleType.SimpleWerewolf,
				publiclyRevealedRole: MainRoleType.SimpleWerewolf),
			FakePlayer.Create(
				PlayerNames.Diana,
				MainRoleType.SimpleWerewolf,
				PlayerHealth.Dead,
				moderatorKnownRole: MainRoleType.SimpleWerewolf,
				publiclyRevealedRole: MainRoleType.SimpleWerewolf),
			FakePlayer.Create(
				PlayerNames.Eva,
				MainRoleType.WildChild,
				moderatorKnownRole: MainRoleType.WildChild),
			FakePlayer.Create(
				PlayerNames.Filipe,
				MainRoleType.SimpleVillager,
				physicalCharacterCardRole: MainRoleType.SimpleVillager)
		};
		var session = new FakeGameSession(players);

		var snapshot = DashboardStatsSnapshot.FromSession(session);

		snapshot.RoleGroups.Select(group => group.Group).Should().Equal(
			RoleGroup.Villagers,
			RoleGroup.Werewolves,
			RoleGroup.Ambiguous,
			RoleGroup.Loners);
		snapshot.RoleGroups.Select(group => group.RemainingCount).Should().Equal(2, 1, 0, 0);
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

	[Fact]
	public void FromSession_IgnoresModeratorPrivateDefenderCommit()
	{
		var defender = FakePlayer.Create(
			PlayerNames.Ana,
			MainRoleType.Defender,
			moderatorKnownRole: MainRoleType.Defender);
		var target = FakePlayer.Create(
			PlayerNames.Bruno,
			MainRoleType.SimpleVillager,
			moderatorKnownRole: MainRoleType.SimpleVillager);
		var session = new FakeGameSession(
			[defender, target],
			[
				new RecurringRolePowerCommittedLogEntry
				{
					Timestamp = new DateTimeOffset(
						2026,
						5,
						14,
						21,
						0,
						0,
						TimeSpan.Zero),
					TurnNumber = 1,
					CurrentPhase = GamePhase.Night,
					ActionType = NightActionType.DefenderProtect,
					TargetIds = [target.Id],
					ActingPlayerId = defender.Id,
					SourceRole = MainRoleType.Defender,
					SourcePowerIdentifier = "defender-protection",
					PowerInstanceId = defender.Id,
					PowerInstanceOrigin = RolePowerInstanceOrigin.Native
				}
			]);

		var snapshot = DashboardStatsSnapshot.FromSession(session);

		snapshot.EliminationLog.Should().BeEmpty();
		snapshot.RoleGroups.Should()
			.OnlyContain(group => group.RemainingCount == 0);
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

		public FactionBeneficiaryKnowledge GetFactionBeneficiaryKnowledge(Guid playerId) =>
			GetPlayerState(playerId).FactionBeneficiary;

		public FactionAgentKnowledge GetFactionAgentKnowledge(Guid playerId, Faction faction) =>
			GetPlayerState(playerId).GetFactionAgentKnowledge(faction);

		public bool TryGetKnownFactionAgents(Faction faction, out IReadOnlyList<IPlayer> agents)
		{
			if (!Enum.IsDefined(faction))
			{
				throw new ArgumentOutOfRangeException(nameof(faction));
			}

			agents = [];
			return false;
		}

		public Faction RequireKnownFactionBeneficiary(Guid playerId)
		{
			_ = GetFactionBeneficiaryKnowledge(playerId);
			throw FactionFactsNotReady();
		}

		public IReadOnlyList<IPlayer> RequireKnownFactionAgents(Faction faction)
		{
			_ = TryGetKnownFactionAgents(faction, out _);
			throw FactionFactsNotReady();
		}

		public int RoleInPlayCount(MainRoleType type) =>
			players.Count(player => player.State.CurrentRole == type);

		public string Serialize() => throw new NotSupportedException();

		private static InvalidOperationException FactionFactsNotReady() =>
			new("Required Faction facts are not ready.");
	}

	private sealed class FakePlayer : IPlayer
	{
		private FakePlayer(
			string name,
			MainRoleType? currentRole,
			PlayerHealth health,
			MainRoleType? physicalCharacterCardRole,
			MainRoleType? moderatorKnownRole,
			MainRoleType? publiclyRevealedRole)
		{
			Name = name;
			State = new FakePlayerState(
				currentRole,
				physicalCharacterCardRole,
				moderatorKnownRole,
				publiclyRevealedRole,
				health);
		}

		public Guid Id { get; } = Guid.NewGuid();
		public string Name { get; init; }
		public IPlayerState State { get; }

		public static FakePlayer Create(
			string name,
			MainRoleType? currentRole = null,
			PlayerHealth health = PlayerHealth.Alive,
			MainRoleType? physicalCharacterCardRole = null,
			MainRoleType? moderatorKnownRole = null,
			MainRoleType? publiclyRevealedRole = null) =>
			new(
				name,
				currentRole,
				health,
				physicalCharacterCardRole,
				moderatorKnownRole,
				publiclyRevealedRole);
	}

	private sealed class FakePlayerState(
		MainRoleType? currentRole,
		MainRoleType? physicalCharacterCardRole,
		MainRoleType? moderatorKnownRole,
		MainRoleType? publiclyRevealedRole,
		PlayerHealth health) : IPlayerState
	{
		public MainRoleType? CurrentRole { get; } = currentRole;
		public MainRoleType? MainRole => CurrentRole;
		public MainRoleType? PhysicalCharacterCardRole { get; } = physicalCharacterCardRole;
			public MainRoleType? ModeratorKnownRole { get; } = moderatorKnownRole;
			public MainRoleType? PubliclyRevealedRole { get; } = publiclyRevealedRole;
		public PlayerHealth Health { get; } = health;
		public bool HasVotingRight => true;
		public int DurableVotingPower => 1;

		public List<StatusEffectTypes> GetActiveStatusEffects() => [];

		public bool HasStatusEffect(StatusEffectTypes effect) => effect == StatusEffectTypes.None;
	}
}
