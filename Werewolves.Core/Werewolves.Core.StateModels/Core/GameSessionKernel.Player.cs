using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Core;

public interface IPlayer : IEquatable<IPlayer>
{
	public Guid Id { get; }
	public string Name { get; init; }
	public IPlayerState State { get; }
	bool IEquatable<IPlayer>.Equals(IPlayer? other)
	{
		return other is not null && Id == other.Id;
	}
}

public interface IPlayerState
{
	public MainRoleType? MainRole { get; }
	public PlayerHealth Health { get; }
	
	/// <summary>
	/// Returns a list of all currently active status effects for this player.
	/// Intended for UI consumption to display status effect icons.
	/// </summary>
	public List<StatusEffectTypes> GetActiveStatusEffects();
	
	/// <summary>
	/// Checks if this player has a specific status effect active.
	/// This is the single method that performs bitwise flag checks.
	/// </summary>
	public bool HasStatusEffect(StatusEffectTypes effect);

	/// <summary>
	/// Returns true if the player is immune to lynching (e.g., Village Idiot who hasn't been voted for yet).
	/// </summary>
    public bool IsImmuneToLynching { get; }

    /// <summary>
    /// Returns null if not immune to lynching, or the specific localized string if they are.
    /// </summary>
    public string? LynchingImmunityAnnouncement 
    {
        get
        {
            if (MainRole == MainRoleType.VillageIdiot && IsImmuneToLynching)
            {
                return "The Village Idiot is saved by their foolishness! They survive, but lose their vote for the rest of the game.";
            }
            return null;
        }
    }

	public Team Team
	{
		get
		{
			switch (MainRole)
			{
				case MainRoleType.SimpleWerewolf:
					return Team.Werewolves;
                case null:
				default:
					return Team.Villagers;
			}
		}
	}
}

internal partial class GameSessionKernel
{
	/// <summary>
	/// Represents a participant in the game and the tracked information about them.
	/// </summary>
	private class Player : IPlayer
	{
		public Player(string name)
		{
			Name = name;
		}

		/// <summary>
		/// Constructor for deserialization - allows specifying an existing ID.
		/// </summary>
		internal Player(string name, Guid id)
		{
			Name = name;
			Id = id;
		}

		public Guid Id { get; } = Guid.NewGuid();
		public string Name { get; init; }
		private PlayerState State { get; } = new();
		IPlayerState IPlayer.State => State;

		/// <summary>
		/// Ensure only the SessionMutator can get a mutable reference to the PlayerState.
		/// </summary>
		/// <param name="key"></param>
		/// <returns></returns>
		public PlayerState GetMutableState(SessionMutator.IStateMutatorKey key) => State;
	}

	/// <summary>
	/// Wrapper record holding all dynamic state information for a Player.
	/// Properties use internal set, they are meant to only be managed through GameSession,
	/// as they represent a purely derived state cache of the game.
	/// </summary>
	private class PlayerState : IPlayerState
	{
		public MainRoleType? MainRole { get; internal set; } = null;

		public PlayerHealth Health { get; internal set; } = PlayerHealth.Alive;

		/// <summary>
		/// Internal flags field for all status effects - not exposed on interface.
		/// </summary>
		internal StatusEffectTypes ActiveEffects { get; set; } = StatusEffectTypes.None;

		/// <summary>
		/// Checks if a specific status effect is active.
		/// For None: returns true only if the player has zero active effects.
		/// For other effects: performs standard HasFlag check.
		/// </summary>
		public bool HasStatusEffect(StatusEffectTypes effect) => 
			effect == StatusEffectTypes.None 
				? ActiveEffects == StatusEffectTypes.None
				: ActiveEffects.HasFlag(effect);

		/// <summary>
		/// Returns all active status effects as a list for UI consumption.
		/// </summary>
		public List<StatusEffectTypes> GetActiveStatusEffects()
		{
			var effects = new List<StatusEffectTypes>();
			foreach (StatusEffectTypes effect in Enum.GetValues<StatusEffectTypes>())
			{
				if (effect != StatusEffectTypes.None && HasStatusEffect(effect))
				{
					effects.Add(effect);
				}
			}
			return effects;
		}

		/// <summary>
		/// Village Idiot is immune to lynching until they've used their immunity.
		/// </summary>
		public bool IsImmuneToLynching => 
			MainRole == MainRoleType.VillageIdiot && !HasStatusEffect(StatusEffectTypes.LynchingImmunityUsed);

		// Internal-only mutation methods (accessible only by SessionMutator)
		internal void AddEffect(StatusEffectTypes effect) => 
			ActiveEffects |= effect;

		internal void RemoveEffect(StatusEffectTypes effect) => 
			ActiveEffects &= ~effect;
	}
}