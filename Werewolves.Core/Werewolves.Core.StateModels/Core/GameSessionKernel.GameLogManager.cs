using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Core;

internal sealed partial class GameSessionKernel
{
    private class GameLogManager
    {
        private readonly List<GameLogEntryBase> _logEntries = new();

        internal void PreflightLogEntry(
            GameLogEntryBase entry,
            IReadOnlyCollection<Guid> playerIds)
        {
            ArgumentNullException.ThrowIfNull(entry);
            ArgumentNullException.ThrowIfNull(playerIds);

            entry.EnforceValidity();
            ValidateOneUseResourceCommit(entry);
            ValidateEliminationCascadeBatchResolution(entry);
            ValidateEliminationCascadeCompletion(entry);
            ValidateEliminationCascadeReactionCompletion(entry);
            ValidateFactionFacts(entry, playerIds);
        }

        internal void AddLogEntry(SessionMutator.IStateMutatorKey key, GameLogEntryBase entry)
        {
            entry.EnforceValidity();
            ValidateOneUseResourceCommit(entry);
            ValidateEliminationCascadeBatchResolution(entry);
            ValidateEliminationCascadeCompletion(entry);
            ValidateEliminationCascadeReactionCompletion(entry);
            ValidateFactionFacts(entry, playerIds: null);
            _logEntries.Add(entry);
        }

        /// <summary>
        /// Restores a log entry from deserialization without requiring a mutator key.
        /// This is only used during deserialization when rebuilding the log history.
        /// </summary>
        internal void RestoreLogEntry(
            GameLogEntryBase entry,
            IReadOnlyCollection<Guid> playerIds)
        {
            PreflightLogEntry(entry, playerIds);
            _logEntries.Add(entry);
        }

        private void ValidateFactionFacts(
            GameLogEntryBase entry,
            IReadOnlyCollection<Guid>? playerIds)
        {
            if (entry is not FactionFactsCommittedLogEntry commit)
            {
                return;
            }

            if (playerIds != null
                && commit.Facts.Any(fact => !playerIds.Contains(fact.PlayerId)))
            {
                throw new InvalidOperationException(
                    "Faction history references a Player outside the Game Session.");
            }

            var existingCommits = _logEntries
                .OfType<FactionFactsCommittedLogEntry>()
                .ToArray();

            if (existingCommits.Any(existing => existing.HasSameBatch(commit)))
            {
                throw new InvalidOperationException(
                    "The Faction fact batch is already committed.");
            }

            if (commit.Source.Kind is
                    FactionFactSourceKind.InitialBeneficiaryClosure or
                    FactionFactSourceKind.SimulationStartState
                && existingCommits.Any(existing =>
                    existing.Source.Kind == commit.Source.Kind))
            {
                throw new InvalidOperationException(
                    "The one-time Faction fact source is already committed.");
            }

            var existingBoundaryKeys = existingCommits
                .SelectMany(existing => existing.Facts)
                .Select(FactionFactProjection.FactBoundaryKey)
                .ToHashSet();
            if (commit.Facts
                .Select(FactionFactProjection.FactBoundaryKey)
                .Any(existingBoundaryKeys.Contains))
            {
                throw new InvalidOperationException(
                    "Faction history already contains a fact at this boundary.");
            }
        }

        private void ValidateOneUseResourceCommit(GameLogEntryBase entry)
        {
            if (entry is not IOneUseRolePowerCommittedLogEntry commit)
            {
                return;
            }

            if (_logEntries
                .OfType<IOneUseRolePowerCommittedLogEntry>()
                .Any(existing =>
                    existing.ResourceIdentity == commit.ResourceIdentity))
            {
                throw new InvalidOperationException(
                    "The One-Use Role Power Resource is already spent by its owning power instance.");
            }
        }

        private void ValidateEliminationCascadeReactionCompletion(
            GameLogEntryBase entry)
        {
            if (entry is not EliminationCascadeReactionCompletedLogEntry
                completion)
            {
                return;
            }

            if (_logEntries
                .OfType<EliminationCascadeReactionCompletedLogEntry>()
                .Any(existing => existing.HasSameCompletionKey(completion)))
            {
                throw new InvalidOperationException(
                    "The Elimination Cascade reaction already completed for this scope and trigger batch.");
            }
        }

        private void ValidateEliminationCascadeBatchResolution(
            GameLogEntryBase entry)
        {
            if (entry is not EliminationCascadeBatchResolvedLogEntry resolution)
            {
                return;
            }

            if (_logEntries
                .OfType<EliminationCascadeBatchResolvedLogEntry>()
                .Any(existing => existing.HasSameResolutionKey(resolution)))
            {
                throw new InvalidOperationException(
                    "The Elimination Cascade batch is already resolved for this scope.");
            }
        }

        private void ValidateEliminationCascadeCompletion(
            GameLogEntryBase entry)
        {
            if (entry is not EliminationCascadeCompletedLogEntry completion)
            {
                return;
            }

            if (_logEntries
                .OfType<EliminationCascadeCompletedLogEntry>()
                .Any(existing => existing.ScopeId == completion.ScopeId))
            {
                throw new InvalidOperationException(
                    "The Elimination Cascade scope is already complete.");
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
