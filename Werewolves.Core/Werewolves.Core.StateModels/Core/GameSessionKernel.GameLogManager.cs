using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Core;

internal sealed partial class GameSessionKernel
{
	private class GameLogManager
	{
		private readonly List<GameLogEntryBase> _logEntries = new();

		internal void AddLogEntry(SessionMutator.IStateMutatorKey key, GameLogEntryBase entry)
		{
			ValidateOneUseResourceCommit(entry);
			_logEntries.Add(entry);
		}

		/// <summary>
		/// Restores a log entry from deserialization without requiring a mutator key.
		/// This is only used during deserialization when rebuilding the log history.
		/// </summary>
		internal void RestoreLogEntry(GameLogEntryBase entry)
		{
			ValidateOneUseResourceCommit(entry);
			_logEntries.Add(entry);
		}

		private void ValidateOneUseResourceCommit(GameLogEntryBase entry)
		{
			if (entry is not OneUseRolePowerCommittedLogEntry commit)
			{
				return;
			}

			commit.EnforceValidity();
			if (_logEntries
			    .OfType<OneUseRolePowerCommittedLogEntry>()
			    .Any(existing =>
				    existing.ResourceIdentity == commit.ResourceIdentity))
			{
				throw new InvalidOperationException(
					"The One-Use Role Power Resource is already spent by its owning power instance.");
			}
		}

		internal IReadOnlyList<GameLogEntryBase> GetAllLogEntries() => _logEntries.AsReadOnly();

		/// <summary>
		/// Searches the game history log for entries of a specific type, with optional filters.
		/// </summary>
		internal IEnumerable<TLogEntry> FindLogEntries<TLogEntry>(NumberRangeConstraint turnIntervalConstraint,
			GamePhase? phase = null,
			Func<TLogEntry, bool>? filter = null) where TLogEntry : GameLogEntryBase
		{
			IEnumerable<TLogEntry> query = _logEntries.OfType<TLogEntry>();

			var turnsAgo = turnIntervalConstraint;
			if (turnsAgo.Minimum < 0 || turnsAgo.Maximum < 0)
				throw new ArgumentOutOfRangeException(nameof(turnIntervalConstraint), "turnsAgo cannot be negative.");

			query = query.Where(log =>
				log.TurnNumber >= turnsAgo.Minimum &&
				log.TurnNumber <= turnsAgo.Maximum);

			if (phase.HasValue)
			{
				query = query.Where(log => log.CurrentPhase == phase.Value);
			}

			if (filter != null)
			{
				query = query.Where(filter);
			}

			return query;
		}
	}
}
