using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class DashboardRosterTests
{
	[Fact]
	public void FromSession_ProjectsKnownRosterInformationInPortuguese()
	{
		var session = new TestGameSession([
			new TestPlayer("Ana"),
			new TestPlayer(
				"Bruno",
				MainRoleType.SimpleWerewolf,
				PlayerHealth.Dead,
				[
					StatusEffectTypes.Sheriff,
					StatusEffectTypes.Lovers,
					StatusEffectTypes.Charmed,
					StatusEffectTypes.LycanthropyInfection
				])
		]);

		var roster = DashboardRoster.FromSession(session);

		roster.Should().HaveCount(2);
		roster[0].Should().BeEquivalentTo(new
		{
			SeatNumber = 1,
			Name = "Ana",
			RoleLabel = "Desconhecido",
			IsRoleKnown = false,
			HealthLabel = "Vivo",
			IsDead = false,
			StatusEffectsLabel = "Sem efeitos",
			StatusEffects = Array.Empty<string>()
		});
		roster[1].Should().BeEquivalentTo(new
		{
			SeatNumber = 2,
			Name = "Bruno",
			RoleLabel = "Lobisomem",
			IsRoleKnown = true,
			HealthLabel = "Morto",
			IsDead = true,
			StatusEffectsLabel = "Capitão, Apaixonado, Encantado, Infetado",
			StatusEffects = new[] { "Capitão", "Apaixonado", "Encantado", "Infetado" }
		});
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
		public int RoleInPlayCount(MainRoleType type) => players.Count(player => player.State.MainRole == type);
		public string Serialize() => string.Empty;
	}

	private sealed class TestPlayer(
		string name,
		MainRoleType? role = null,
		PlayerHealth health = PlayerHealth.Alive,
		IReadOnlyList<StatusEffectTypes>? activeEffects = null) : IPlayer
	{
		public Guid Id { get; } = Guid.NewGuid();
		public string Name { get; init; } = name;
		public IPlayerState State { get; } = new TestPlayerState(role, health, activeEffects ?? []);
	}

	private sealed class TestPlayerState(
		MainRoleType? role,
		PlayerHealth health,
		IReadOnlyList<StatusEffectTypes> activeEffects) : IPlayerState
	{
		public MainRoleType? MainRole => role;
		public PlayerHealth Health => health;
		public bool IsImmuneToLynching => false;
		public string? LynchingImmunityAnnouncement => null;
		public Team Team => role == MainRoleType.SimpleWerewolf ? Team.Werewolves : Team.Villagers;
		public List<StatusEffectTypes> GetActiveStatusEffects() => activeEffects.ToList();
		public bool HasStatusEffect(StatusEffectTypes effect) => activeEffects.Contains(effect);
	}
}
