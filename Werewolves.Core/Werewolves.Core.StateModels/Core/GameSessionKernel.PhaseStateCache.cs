using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.StateModels.Core;

//todo: move this to its own file once this is finalized.
internal interface IGamePhaseStateCache
{
	GamePhase GetCurrentPhase();
	T? GetSubPhase<T>() where T : struct, Enum;
	string? GetActiveSubPhaseStage();
	bool HasSubPhaseStageCompleted(string subPhaseStageId);
	T? GetCurrentListenerState<T>(ListenerIdentifier listener) where T : struct, Enum;
	ListenerIdentifier? GetCurrentListener();
}

internal partial class GameSessionKernel
{
	/// <summary>
	/// Manages transient state within a single game phase, acting as a "program counter"
	/// for resumable, multi-step actions that must pause for moderator input.
	/// This becomes the single source of truth for the game's current execution point.
	/// </summary>
	private record struct GamePhaseStateCache : IGamePhaseStateCache
	{
		#region Private State Fields

		// Tracks the GFM's current execution point.
		private GamePhase _currentPhase;
		private string? _currentSubPhase;

		/// <summary>
		/// Tracks the currently executing subphase stage.
		/// Essentially acts like a mutex for subphase execution, but unlike a mutex
		/// this allows us to track which stage is currently active for debugging/logging purposes.
		/// While null, any subphase stage can start execution.
		/// Otherwise, only the active subphase stage can continue or finish execution.
		/// </summary>
		private string? _currentSubPhaseStage;

		/// <summary>
		/// Tracks all previously executed subphase stages within a given sub-phase.
		/// Resets on every sub-phase transition.
		/// This is to prevent sub-phase stages from being re-entered multiple times within the same sub-phase,
		/// after they've completed once.
		/// </summary>
		private List<string> _previousSubPhaseStages = new();

		// Tracks the single listener that is currently paused awaiting input.
		private ListenerIdentifier? _currentListener;
		private string? _currentListenerState;

		#endregion

		/// <summary>
		/// Initializes a new IntraPhaseStateCache with the specified starting phase.
		/// </summary>
		/// <param name="initialPhase">The initial game phase.</param>
		internal GamePhaseStateCache(GamePhase initialPhase)
		{
			_currentPhase = initialPhase;
		}


		#region Internal State Mutators

		internal void TransitionMainPhase(SessionMutator.IStateMutatorKey key, GamePhase phase)
		{
			if (_currentPhase != phase)
			{
				ClearCurrentSubPhase();
			}

			_currentPhase = phase;
		}

		/// <summary>
		/// Sets the GFM's current state with the specified sub-phase.
		/// </summary>
		/// <typeparam name="T">The enum type for the sub-phase.</typeparam>
		/// <param name="subPhase">The optional sub-phase enum value.</param>
		internal void TransitionSubPhase(Enum subPhase)
		{
			ClearCurrentSubPhase();
			_currentSubPhase = subPhase.ToString();
		}

		/// <summary>
		/// Sets the currently active sub phase stage.
		/// </summary>
		/// <param name="subPhaseStage">The sub phase stage that is currently being processed.</param>
		internal void StartSubPhaseStage(string subPhaseStage)
		{
			_currentSubPhaseStage = subPhaseStage;
		}

		internal void CompleteSubPhaseStage()
		{
			//ok to throw if _currentSubPhaseStage is null here - indicates a logic error.
			//we shouldn't be able to attempt to complete subphase stages when none are active.
			_previousSubPhaseStages.Add(_currentSubPhaseStage!);
			ClearSubPhaseStage();
		}


		/// <summary>
		/// Sets the state for a current listener.
		/// </summary>
		/// <param name="listener">The identifier of the current listener.</param>
		/// <param name="state">The state value for the listener.</param>
		internal void TransitionListenerAndState(ListenerIdentifier listener, string state)
		{
			_currentListener = listener;
			_currentListenerState = state;
		}

		#endregion

		#region Public Interface Accessors

		/// <summary>
		/// Gets the current game phase.
		/// </summary>
		/// <returns>The current game phase.</returns>
		public GamePhase GetCurrentPhase() => _currentPhase;

		/// <summary>
		/// Gets the current GFM sub-phase as the specified enum type.
		/// </summary>
		/// <typeparam name="T">The enum type for the sub-phase.</typeparam>
		/// <returns>The sub-phase value, or null if not set or parsing fails.</returns>
		public T? GetSubPhase<T>() where T : struct, Enum
		{
			if (_currentSubPhase != null)
			{
				if (Enum.TryParse<T>(_currentSubPhase, out var result))
				{
					return result;
				}
			}

			return null;
		}

		/// <summary>
		/// Gets the currently active sub phase stage.
		/// </summary>
		/// <returns>The active sub phase stage, or null if none is active.</returns>
		public string? GetActiveSubPhaseStage() => _currentSubPhaseStage;

		public bool HasSubPhaseStageCompleted(string subPhaseStageId) =>
			_previousSubPhaseStages.Contains(subPhaseStageId);

		/// <summary>
		/// Gets the state for a current listener.
		/// </summary>
		/// <typeparam name="T">The enum type for the listener state.</typeparam>
		/// <param name="listener">The identifier of the listener to check.</param>
		/// <returns>The listener's state, or null if the listener is not current or parsing fails.</returns>
		public T? GetCurrentListenerState<T>(ListenerIdentifier listener) where T : struct, Enum
		{
			if (_currentListener?.Equals(listener) == true && _currentListenerState != null)
			{
				if (Enum.TryParse<T>(_currentListenerState, out var result))
				{
					return result;
				}
			}

			return null;
		}

		/// <summary>
		/// Gets the identifier of the currently active listener.
		/// </summary>
		/// <returns>The current listener identifier, or null if no listener is active.</returns>
		public ListenerIdentifier? GetCurrentListener() => _currentListener;

		#endregion

		#region Internal State Cleanup

		/// <summary>
		/// Clears the current listener and its state.
		/// </summary>
		internal void ClearCurrentListener()
		{
			_currentListener = null;
			_currentListenerState = null;
		}

		#endregion

		#region Private Helpers

		/// <summary>
		/// Marks the current hook as completed and clears it.
		/// </summary>

		private void ClearSubPhaseStage()
		{
			_currentSubPhaseStage = null;
			ClearCurrentListener();
		}

		/// <summary>
		/// Central cleanup method that must be called when transitioning between main GamePhases.
		/// Guarantees transient state is never leaked across phases.
		/// </summary>
		private void ClearCurrentSubPhase()
		{
			_currentSubPhase = null;
			_previousSubPhaseStages = [];
			ClearSubPhaseStage();
		}

		#endregion

		#region Serialization

		/// <summary>
		/// Creates a DTO representation of this cache for serialization.
		/// </summary>
		internal GamePhaseStateCacheDto ToDto()
		{
			return new GamePhaseStateCacheDto
			{
				CurrentPhase = _currentPhase,
				SubPhase = _currentSubPhase,
				ActiveSubPhaseStage = _currentSubPhaseStage,
				CompletedSubPhaseStages = _previousSubPhaseStages.ToList(),
				CurrentListenerId = _currentListener?.ListenerId,
				CurrentListenerType = _currentListener?.ListenerType.ToString(),
				CurrentListenerState = _currentListenerState
			};
		}

		/// <summary>
		/// Restores a GamePhaseStateCache from a DTO.
		/// </summary>
		internal static GamePhaseStateCache FromDto(GamePhaseStateCacheDto dto)
		{
			ListenerIdentifier? listener = null;
			if (dto.CurrentListenerId != null && dto.CurrentListenerType != null)
			{
				if (Enum.TryParse<GameHookListenerType>(dto.CurrentListenerType, out var listenerType))
				{
					// Recreate the listener identifier based on type
					listener = listenerType switch
					{
						GameHookListenerType.MainRole when Enum.TryParse<MainRoleType>(dto.CurrentListenerId, out var role) 
							=> ListenerIdentifier.Listener(role),
						GameHookListenerType.StatusEffect when Enum.TryParse<StatusEffectTypes>(dto.CurrentListenerId, out var effect) 
							=> ListenerIdentifier.Listener(effect),
						_ => null
					};
				}
			}

			return new GamePhaseStateCache
			{
				_currentPhase = dto.CurrentPhase,
				_currentSubPhase = dto.SubPhase,
				_currentSubPhaseStage = dto.ActiveSubPhaseStage,
				_previousSubPhaseStages = dto.CompletedSubPhaseStages.ToList(),
				_currentListener = listener,
				_currentListenerState = dto.CurrentListenerState
			};
		}

		#endregion
	}
}