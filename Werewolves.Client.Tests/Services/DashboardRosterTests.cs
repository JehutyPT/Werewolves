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

public class DashboardRosterTests
{
	[Fact]
	public void FromSession_DistinguishesUnknownPrivateAndPublicRoleKnowledgeWithoutLeakingHiddenFacts()
	{
		var brunoStatusEffects = new[]
		{
			StatusEffectTypes.Sheriff,
			StatusEffectTypes.Lovers,
			StatusEffectTypes.Charmed,
			StatusEffectTypes.LycanthropyInfection
		};
		var brunoStatusLabels = brunoStatusEffects.Select(DashboardRoster.StatusEffectLabel).ToArray();
		var session = new TestGameSession([
			new TestPlayer(
				PlayerNames.Ana,
				currentRole: MainRoleType.Seer,
				physicalCharacterCardRole: MainRoleType.Seer),
			new TestPlayer(
				PlayerNames.Bruno,
				currentRole: MainRoleType.SimpleWerewolf,
				physicalCharacterCardRole: MainRoleType.SimpleWerewolf,
				moderatorKnownRole: MainRoleType.SimpleWerewolf,
				health: PlayerHealth.Dead,
				activeEffects: brunoStatusEffects),
			new TestPlayer(
				PlayerNames.Carla,
				currentRole: MainRoleType.VillagerVillager,
				physicalCharacterCardRole: MainRoleType.VillagerVillager,
				moderatorKnownRole: MainRoleType.VillagerVillager,
				publiclyRevealedRole: MainRoleType.VillagerVillager),
			new TestPlayer(
				PlayerNames.Diana,
				currentRole: MainRoleType.WildChild,
				physicalCharacterCardRole: MainRoleType.SimpleVillager,
				moderatorKnownRole: MainRoleType.WildChild,
				publiclyRevealedRole: MainRoleType.SimpleVillager)
		]);

		var roster = DashboardRoster.FromSession(session);

		roster.Should().HaveCount(4);
		roster[0].Should().BeEquivalentTo(new
		{
			SeatNumber = 1,
			Name = PlayerNames.Ana,
			RoleLabel = DashboardRoster.RoleLabel(null),
			IsRoleKnown = false,
			RoleVisibility = DashboardRoleVisibility.Unknown,
			RoleVisibilityLabel = DashboardRoster.RoleVisibilityLabel(DashboardRoleVisibility.Unknown),
			HealthLabel = DashboardRoster.HealthLabel(PlayerHealth.Alive),
			IsDead = false,
			StatusEffectsLabel = DashboardRoster.NoStatusEffectsLabel,
			StatusEffects = Array.Empty<string>()
		});
		roster[1].Should().BeEquivalentTo(new
		{
			SeatNumber = 2,
			Name = PlayerNames.Bruno,
			RoleLabel = MainRoleType.SimpleWerewolf.GetPublicName(),
			IsRoleKnown = true,
			RoleVisibility = DashboardRoleVisibility.ModeratorPrivate,
			RoleVisibilityLabel = DashboardRoster.RoleVisibilityLabel(DashboardRoleVisibility.ModeratorPrivate),
			HealthLabel = DashboardRoster.HealthLabel(PlayerHealth.Dead),
			IsDead = true,
			StatusEffectsLabel = string.Join(ClientStrings.Common_ListSeparator, brunoStatusLabels),
			StatusEffects = brunoStatusLabels
		});
		roster[2].Should().BeEquivalentTo(new
		{
			SeatNumber = 3,
			Name = PlayerNames.Carla,
			RoleLabel = MainRoleType.VillagerVillager.GetPublicName(),
			IsRoleKnown = true,
			RoleVisibility = DashboardRoleVisibility.Public,
			RoleVisibilityLabel = DashboardRoster.RoleVisibilityLabel(DashboardRoleVisibility.Public),
			HealthLabel = DashboardRoster.HealthLabel(PlayerHealth.Alive),
			IsDead = false,
			StatusEffectsLabel = DashboardRoster.NoStatusEffectsLabel,
			StatusEffects = Array.Empty<string>()
		});
		roster[3].Should().BeEquivalentTo(new
		{
			SeatNumber = 4,
			Name = PlayerNames.Diana,
			RoleLabel = MainRoleType.WildChild.GetPublicName(),
			IsRoleKnown = true,
			RoleVisibility = DashboardRoleVisibility.ModeratorPrivate,
			RoleVisibilityLabel = DashboardRoster.RoleVisibilityLabel(DashboardRoleVisibility.ModeratorPrivate),
			HealthLabel = DashboardRoster.HealthLabel(PlayerHealth.Alive),
			IsDead = false,
			StatusEffectsLabel = DashboardRoster.NoStatusEffectsLabel,
			StatusEffects = Array.Empty<string>()
		});
	}

	[Fact]
	public void FromSession_DistinguishesPermanentZeroVotingPowerFromDeathAndTemporaryRestriction()
	{
		var session = new TestGameSession([
			new TestPlayer(
				PlayerNames.Ana,
				hasVotingRight: false,
				durableVotingPower: 0),
			new TestPlayer(
				PlayerNames.Bruno,
				health: PlayerHealth.Dead,
				hasVotingRight: false,
				durableVotingPower: 0),
			new TestPlayer(
				PlayerNames.Carla,
				hasVotingRight: false,
				durableVotingPower: 1)
		]);

		var roster = DashboardRoster.FromSession(session);

		roster[0].VotingGuidanceLabel.Should()
			.Be(ClientStrings.Dashboard_VotingPowerLostPermanently);
		roster[1].VotingGuidanceLabel.Should().BeNull();
		roster[2].VotingGuidanceLabel.Should()
			.Be(ClientStrings.Dashboard_VotingRightTemporarilyRestricted);
	}

	private sealed class TestGameSession(IReadOnlyList<IPlayer> players) : IGameSession
	{
		public IEnumerable<GameLogEntryBase> GameHistoryLog => [];
		public Guid Id { get; } = Guid.NewGuid();
		public int TurnNumber => 1;
		public GamePhase GetCurrentPhase() => GamePhase.Night;
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
		public int RoleInPlayCount(MainRoleType type) => players.Count(player => player.State.CurrentRole == type);
		public string Serialize() => string.Empty;

		private static InvalidOperationException FactionFactsNotReady() =>
			new("Required Faction facts are not ready.");
	}

	private sealed class TestPlayer(
		string name,
		MainRoleType? currentRole = null,
		MainRoleType? physicalCharacterCardRole = null,
		MainRoleType? moderatorKnownRole = null,
		MainRoleType? publiclyRevealedRole = null,
		PlayerHealth health = PlayerHealth.Alive,
		bool hasVotingRight = true,
		int durableVotingPower = 1,
		IReadOnlyList<StatusEffectTypes>? activeEffects = null) : IPlayer
	{
		public Guid Id { get; } = Guid.NewGuid();
		public string Name { get; init; } = name;
		public IPlayerState State { get; } = new TestPlayerState(
			currentRole,
			physicalCharacterCardRole,
			moderatorKnownRole,
			publiclyRevealedRole,
			health,
			hasVotingRight,
			durableVotingPower,
			activeEffects ?? []);
	}

	private sealed class TestPlayerState(
		MainRoleType? currentRole,
		MainRoleType? physicalCharacterCardRole,
		MainRoleType? moderatorKnownRole,
		MainRoleType? publiclyRevealedRole,
		PlayerHealth health,
		bool hasVotingRight,
		int durableVotingPower,
		IReadOnlyList<StatusEffectTypes> activeEffects) : IPlayerState
	{
		public MainRoleType? CurrentRole => currentRole;
		public MainRoleType? MainRole => CurrentRole;
		public MainRoleType? PhysicalCharacterCardRole => physicalCharacterCardRole;
			public MainRoleType? ModeratorKnownRole => moderatorKnownRole;
			public MainRoleType? PubliclyRevealedRole => publiclyRevealedRole;
		public PlayerHealth Health => health;
		public bool HasVotingRight => hasVotingRight;
		public int DurableVotingPower => durableVotingPower;
		public Team Team => currentRole == MainRoleType.SimpleWerewolf ? Team.Werewolves : Team.Villagers;
		public List<StatusEffectTypes> GetActiveStatusEffects() => activeEffects.ToList();
		public bool HasStatusEffect(StatusEffectTypes effect) => activeEffects.Contains(effect);
	}
}
