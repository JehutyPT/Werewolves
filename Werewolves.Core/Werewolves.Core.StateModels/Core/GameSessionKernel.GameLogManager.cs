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
			ValidatePermanentRoleSwapPowerInstance(entry, playerIds);
            ValidateOneUseResourceCommit(entry);
            ValidateEliminationCascadeBatchResolution(entry);
            ValidateEliminationCascadeCompletion(entry);
            ValidateEliminationCascadeReactionCompletion(entry);
            ValidateLoversPairCommitment(entry, playerIds);
            ValidateFactionFacts(entry, playerIds);
			ValidatePermanentRoleSwapFactionBoundary(entry);
			ValidateThiefOfferDecline(entry, playerIds);
        }

        internal void AddLogEntry(SessionMutator.IStateMutatorKey key, GameLogEntryBase entry)
        {
            entry.EnforceValidity();
			ValidatePermanentRoleSwapPowerInstance(entry, playerIds: null);
            ValidateOneUseResourceCommit(entry);
            ValidateEliminationCascadeBatchResolution(entry);
            ValidateEliminationCascadeCompletion(entry);
            ValidateEliminationCascadeReactionCompletion(entry);
            ValidateLoversPairCommitment(entry, playerIds: null);
            ValidateFactionFacts(entry, playerIds: null);
			ValidatePermanentRoleSwapFactionBoundary(entry);
			ValidateThiefOfferDecline(entry, playerIds: null);
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
			if (entry is not IFactionFactBatchLogEntry commit)
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
				.OfType<IFactionFactBatchLogEntry>()
                .ToArray();

			if (existingCommits.Any(existing =>
				existing.Source == commit.Source &&
				existing.Facts.SequenceEqual(commit.Facts)))
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

		private void ValidatePermanentRoleSwapFactionBoundary(
			GameLogEntryBase entry)
		{
			if (entry is IPermanentRoleSwapCommittedLogEntry swap &&
				!PermanentRoleSwapFactionFacts.IsValidCommittedBatch(
					swap.PlayerId,
					swap.Policy,
					swap.Facts,
					entry.TurnNumber,
					entry.CurrentPhase,
					_logEntries.Count))
			{
				throw new InvalidOperationException(
					"Permanent Role Swap Faction fact boundary does not match its committed history position.");
			}
		}

		private void ValidateThiefOfferDecline(
			GameLogEntryBase entry,
			IReadOnlyCollection<Guid>? playerIds)
		{
			if (entry is not ThiefOfferDeclinedLogEntry decline)
			{
				return;
			}

			if (playerIds?.Contains(decline.PlayerId) == false ||
			    _logEntries.OfType<ThiefOfferDeclinedLogEntry>().Any() ||
			    _logEntries.OfType<PermanentRoleSwapCommittedLogEntry>().Any(swap =>
				    swap.ExpectedCurrentRole == MainRoleType.Thief))
			{
				throw new InvalidOperationException(
					"The Thief offer opportunity is already committed or invalid.");
			}
		}

		private void ValidatePermanentRoleSwapPowerInstance(
			GameLogEntryBase entry,
			IReadOnlyCollection<Guid>? playerIds)
		{
			if (entry is IPermanentRoleSwapCommittedLogEntry swap &&
				(playerIds?.Contains(swap.NewPowerInstanceId) == true ||
				 _logEntries
					 .OfType<IPermanentRoleSwapCommittedLogEntry>()
					 .Any(existing =>
						 existing.NewPowerInstanceId == swap.NewPowerInstanceId)))
			{
				throw new InvalidOperationException(
					"The Permanent Role Swap power-instance identity is not fresh.");
			}
		}

        private void ValidateLoversPairCommitment(
            GameLogEntryBase entry,
            IReadOnlyCollection<Guid>? playerIds)
        {
            if (entry is not LoversPairCommittedLogEntry pair)
            {
                return;
            }

            if (_logEntries.OfType<LoversPairCommittedLogEntry>().Any())
            {
                throw new InvalidOperationException(
                    "The Lovers pair is already committed.");
            }

            if (pair.LinkBoundary.Order != _logEntries.Count)
            {
                throw new InvalidOperationException(
                    "The Lovers pair link boundary does not match its committed history position.");
            }

            if (playerIds is not null &&
                (!playerIds.Contains(pair.ActingPlayerId) ||
                 pair.PlayerIds.Any(playerId => !playerIds.Contains(playerId))))
            {
                throw new InvalidOperationException(
                    "The Lovers pair references a Player outside the Game Session.");
            }
        }

        private void ValidateOneUseResourceCommit(GameLogEntryBase entry)
        {
            var committedResource = GetCommittedResourceIdentity(entry);
            if (committedResource == null)
            {
                return;
            }

            if (_logEntries
                .Select(GetCommittedResourceIdentity)
                .Any(existing => existing == committedResource))
            {
                throw new InvalidOperationException(
                    "The One-Use Role Power Resource is already spent by its owning power instance.");
            }
        }

        private static OneUseRolePowerResourceIdentity?
            GetCommittedResourceIdentity(GameLogEntryBase entry) =>
            entry switch
            {
                IOneUseRolePowerCommittedLogEntry oneUse =>
                    oneUse.ResourceIdentity,
                TargetPrivateRolePowerCommittedLogEntry targetPrivate =>
                    targetPrivate.SpentResourceIdentity,
                _ => null
            };

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
