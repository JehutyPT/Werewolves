using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Serialization;

namespace Werewolves.Core.StateModels.Core;

internal partial class GameSessionKernel
{
	/// <summary>
	/// Manages transient state within a single game phase, acting as a "program counter"
	/// for resumable, multi-step actions that must pause for moderator input.
	/// This becomes the single source of truth for the game's current execution point.
	/// </summary>
	private record struct GamePhaseStateCache
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

		internal GamePhaseStateCache WithExecutionCursor(ExecutionView candidate)
		{
			ArgumentNullException.ThrowIfNull(candidate);

			var previousStages = _previousSubPhaseStages;
			var completedStages = previousStages
				.Where(candidate.CompletedSubPhaseStages.Contains)
				.Concat(candidate.CompletedSubPhaseStages.Where(stage =>
					!previousStages.Contains(stage)))
				.ToList();

			return new GamePhaseStateCache(candidate.CurrentPhase)
			{
				_currentSubPhase = candidate.SubPhaseId,
				_currentSubPhaseStage = candidate.ActiveSubPhaseStage,
				_previousSubPhaseStages = completedStages,
				_currentListener = candidate.CurrentListener,
				_currentListenerState = candidate.CurrentListenerState
			};
		}

		internal void RestoreTransientContinuation(
			string activeSubPhaseStage,
			ListenerIdentifier listener,
			string listenerState)
		{
			if (_currentSubPhaseStage != null ||
				_currentListener != null ||
				_currentListenerState != null)
			{
				throw new InvalidOperationException(
					"Transient continuation restoration requires an inactive phase cache.");
			}

			_currentSubPhaseStage = activeSubPhaseStage;
			_currentListener = listener;
			_currentListenerState = listenerState;
		}

		#endregion

		internal ExecutionView CreateExecutionView(
			ModeratorInstruction? pendingInstruction,
			AcceptedObservationRecoveryCursor? acceptedObservationRecoveryCursor,
			DomainRecoveryCursor? domainRecoveryCursor) =>
			new(
				_currentPhase,
				_currentSubPhase,
				_currentSubPhaseStage,
				_previousSubPhaseStages,
				_currentListener,
				_currentListenerState,
				pendingInstruction,
				acceptedObservationRecoveryCursor,
				domainRecoveryCursor);

		#region Internal Accessors

		/// <summary>
		/// Gets the current game phase.
		/// </summary>
		/// <returns>The current game phase.</returns>
		internal GamePhase GetCurrentPhase() => _currentPhase;

		/// <summary>
		/// Gets the current GFM sub-phase as the specified enum type.
		/// </summary>
		/// <typeparam name="T">The enum type for the sub-phase.</typeparam>
		/// <returns>The sub-phase value, or null if not set or parsing fails.</returns>
		internal T? GetSubPhase<T>() where T : struct, Enum
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
		internal string? GetActiveSubPhaseStage() => _currentSubPhaseStage;

		internal bool HasSubPhaseStageCompleted(string subPhaseStageId) =>
			_previousSubPhaseStages.Contains(subPhaseStageId);

		/// <summary>
		/// Gets the state for a current listener.
		/// </summary>
		/// <typeparam name="T">The enum type for the listener state.</typeparam>
		/// <param name="listener">The identifier of the listener to check.</param>
		/// <returns>The listener's state, or null if the listener is not current or parsing fails.</returns>
		internal T? GetCurrentListenerState<T>(ListenerIdentifier listener) where T : struct, Enum
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

		internal string? GetSubPhaseId() => _currentSubPhase;

		/// <summary>
		/// Gets the identifier of the currently active listener.
		/// </summary>
		/// <returns>The current listener identifier, or null if no listener is active.</returns>
		internal ListenerIdentifier? GetCurrentListener() => _currentListener;

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
				CompletedSubPhaseStages = _previousSubPhaseStages.ToList()
			};
		}

		/// <summary>
		/// Restores a GamePhaseStateCache from a DTO.
		/// Only the main phase is restored. Sub-phase position, listener state, and other
		/// execution state are transient (ADR-0002) and intentionally not read back —
		/// rehydration resets to the beginning of the current main phase.
		/// </summary>
		internal static GamePhaseStateCache FromDto(GamePhaseStateCacheDto dto)
		{
			return new GamePhaseStateCache(dto.CurrentPhase);
		}

		/// <summary>
		/// Restores only the durable main-phase position. Active stages and listeners
		/// are transient and may be restored through the neutral continuation seam.
		/// </summary>
		internal static GamePhaseStateCache FromStableRecoveryBoundaryDto(
			GamePhaseStateCacheDto dto)
		{
			var cache = new GamePhaseStateCache(dto.CurrentPhase)
			{
				_currentSubPhase = dto.SubPhase,
				_previousSubPhaseStages = dto.CompletedSubPhaseStages.ToList()
			};

			return cache;
		}

		#endregion
	}
}
