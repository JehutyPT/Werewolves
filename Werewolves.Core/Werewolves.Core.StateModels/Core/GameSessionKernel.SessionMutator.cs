using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;

namespace Werewolves.Core.StateModels.Core;

//todo: move this to its own file once this is finalized.
public interface ISessionMutator
{
	int CurrentTurnNumber { get; }
	void SetPlayerHealth(Guid playerId, PlayerHealth health);
	void SetPlayerRole(Guid playerId, MainRoleType role);
	void SetCurrentPhase(GamePhase newPhase);
	
	/// <summary>
	/// Sets or clears a status effect on a player.
	/// </summary>
	/// <param name="playerId">The player to modify.</param>
	/// <param name="effect">The status effect to set or clear.</param>
	/// <param name="isActive">True to add the effect, false to remove it.</param>
	void SetStatusEffect(Guid playerId, StatusEffectTypes effect, bool isActive);
	
	void AddLogEntry<T>(T entry) where T : GameLogEntryBase;
}

internal partial class GameSessionKernel
{
	private class SessionMutator(GameSessionKernel kernel) : ISessionMutator
	{
		/// <summary>
		/// Represents a key used to allow access to mutate persistent state, player's, game state (i.e. main phase) or game logs.
		/// </summary>
		internal interface IStateMutatorKey{}
		/// <summary>
		/// Private implementation of the state mutator key to restrict access.
		/// </summary>
		private class StateMutatorKey : IStateMutatorKey{}
		private static readonly StateMutatorKey Key = new();

		private PlayerState GetMutablePlayerState(Guid playerId) =>
			kernel.GetMutablePlayerState(Key, playerId);

		public int CurrentTurnNumber => kernel.TurnNumber;

		public void SetPlayerHealth(Guid playerId, PlayerHealth health) 
            => GetMutablePlayerState(playerId).Health = health;
        public void SetPlayerRole(Guid playerId, MainRoleType role) 

            => GetMutablePlayerState(playerId).MainRole = role;

		public void SetCurrentPhase(GamePhase newPhase)
		{
			kernel._phaseStateCache.TransitionMainPhase(Key, newPhase);
			kernel._stateChangeObserver?.OnMainPhaseChanged(newPhase);

			if (newPhase == GamePhase.Night)
			{
				kernel.IncrementTurnNumber(Key);
				kernel._stateChangeObserver?.OnTurnNumberChanged(kernel.TurnNumber);
			}
		}

		public void SetStatusEffect(Guid playerId, StatusEffectTypes effect, bool isActive)
		{
			var playerState = GetMutablePlayerState(playerId);
			if (isActive)
			{
				playerState.AddEffect(effect);
			}
			else
			{
				playerState.RemoveEffect(effect);
			}
		}

		public void AddLogEntry<T>(T entry) where T : GameLogEntryBase
		{
			kernel._gameHistoryLog.AddLogEntry(Key, entry);
			kernel._stateChangeObserver?.OnLogEntryApplied(entry);
		}
    }
}