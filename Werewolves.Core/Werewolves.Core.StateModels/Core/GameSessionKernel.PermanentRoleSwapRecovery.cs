using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Core;

internal sealed partial class GameSessionKernel
{
	private void ValidatePermanentRoleSwapPlayerProjectionMatchesHistory()
	{
		var history = _gameHistoryLog.GetAllLogEntries();
		if (!history.OfType<IPermanentRoleSwapCommittedLogEntry>().Any())
		{
			return;
		}

		var projection = new PermanentRoleSwapRecoveryProjection(
			_playerSeatingOrder);
		foreach (var entry in history)
		{
			entry.Apply(projection);
		}

		foreach (var playerId in projection.SwappedPlayerIds)
		{
			var expected = projection.GetPlayer(playerId);
			var actual = GetPlayer(playerId).GetMutableState(
				new DeserializationKey());
			if (actual.CurrentRole != expected.CurrentRole ||
				actual.ModeratorKnownRole != expected.ModeratorKnownRole ||
				actual.PubliclyRevealedRole != expected.PubliclyRevealedRole ||
				actual.ActiveEffects != expected.ActiveEffects ||
				actual.HasVotingRight != expected.HasVotingRight ||
				actual.DurableVotingPower != expected.DurableVotingPower)
			{
				throw new InvalidOperationException(
					"The Permanent Role Swap Player projection does not match committed history.");
			}
		}
	}

	/// <summary>
	/// Replays the canonical log-entry application path into a disposable Player
	/// projection. Physical cards and Faction facts have their own recovery
	/// projections; phase and health are outside the Permanent Role Swap policy.
	/// </summary>
	private sealed class PermanentRoleSwapRecoveryProjection(
		IReadOnlyCollection<Guid> playerIds)
		: ISessionMutator,
		  IDevotedServantSessionMutator
	{
		private readonly Dictionary<Guid, ProjectedPlayer> _players =
			playerIds.ToDictionary(
				playerId => playerId,
				_ => new ProjectedPlayer());
		private IReadOnlyList<Guid>? _loversPairPlayerIds;

		internal HashSet<Guid> SwappedPlayerIds { get; } = [];

		public int CurrentTurnNumber { get; private set; } = 1;

		internal ProjectedPlayer GetPlayer(Guid playerId) =>
			_players.TryGetValue(playerId, out var player)
				? player
				: throw new InvalidOperationException(
					"Permanent Role Swap history contains an unknown Player.");

		public void SetModeratorKnownRole(Guid playerId, MainRoleType role) =>
			GetPlayer(playerId).ModeratorKnownRole = role;

		public void SetPhysicalCharacterCardOwnership(
			long roleLockInVersion,
			Guid playerId,
			Guid cardId,
			MainRoleType printedRole)
		{
			// The existing physical-card recovery projection validates this fact.
		}

		public void SetPhysicalCharacterCardRole(
			Guid playerId,
			MainRoleType role)
		{
			// The existing physical-card recovery projection validates this cache.
		}

		public void SetPlayerHealth(Guid playerId, PlayerHealth health)
		{
			// Health is not a Permanent Role Swap transaction category.
		}

		public void SetVotingRight(Guid playerId, bool hasVotingRight) =>
			GetPlayer(playerId).HasVotingRight = hasVotingRight;

		public void SetDurableVotingPower(
			Guid playerId,
			int durableVotingPower) =>
			GetPlayer(playerId).DurableVotingPower = durableVotingPower;

		public void SetPlayerRole(Guid playerId, MainRoleType role) =>
			GetPlayer(playerId).CurrentRole = role;

		public void SetPubliclyRevealedRole(
			Guid playerId,
			MainRoleType role) =>
			GetPlayer(playerId).PubliclyRevealedRole = role;

		public void SetCurrentPhase(GamePhase newPhase)
		{
			// Phase recovery is validated by the existing phase-state cache.
		}

		public void SetStatusEffect(
			Guid playerId,
			StatusEffectTypes effect,
			bool isActive)
		{
			var player = GetPlayer(playerId);
			player.ActiveEffects = isActive
				? player.ActiveEffects | effect
				: player.ActiveEffects & ~effect;
		}

		public void ApplyFactionFacts(IFactionFactBatchLogEntry entry)
		{
			// FactionFactProjection remains the single Faction recovery authority.
		}

		public void ApplyPermanentRoleSwap(
			PermanentRoleSwapCommittedLogEntry entry)
		{
			var player = GetPlayer(entry.PlayerId);
			if (player.CurrentRole != entry.ExpectedCurrentRole)
			{
				throw new InvalidOperationException(
					"Permanent Role Swap history does not match the prior current Role.");
			}

			SwappedPlayerIds.Add(entry.PlayerId);
			player.CurrentRole = entry.NewCurrentRole;
			player.ModeratorKnownRole = entry.Policy.PrivateRoleKnowledge switch
			{
				PermanentRoleSwapDisposition.Preserve =>
					player.ModeratorKnownRole,
				PermanentRoleSwapDisposition.Change => entry.NewCurrentRole,
				PermanentRoleSwapDisposition.Clear => null,
				_ => throw new InvalidOperationException(
					"Permanent Role Swap private-knowledge history is invalid.")
			};

			if (entry.StateChanges.RelationshipEffectsToClear.Contains(
					StatusEffectTypes.Lovers))
			{
				if (_loversPairPlayerIds is null ||
					!_loversPairPlayerIds.Contains(entry.PlayerId) ||
					_loversPairPlayerIds.Any(playerId =>
						!GetPlayer(playerId).ActiveEffects.HasFlag(
							StatusEffectTypes.Lovers)))
				{
					throw new InvalidOperationException(
						"Permanent Role Swap relationship history is invalid.");
				}

				foreach (var playerId in _loversPairPlayerIds)
				{
					GetPlayer(playerId).ActiveEffects &= ~StatusEffectTypes.Lovers;
				}
			}

			foreach (var effect in entry.StateChanges.StatusEffectsToClear)
			{
				player.ActiveEffects &= ~effect;
			}

			switch (entry.Policy.VotingState)
			{
				case PermanentRoleSwapDisposition.Preserve:
					break;
				case PermanentRoleSwapDisposition.Clear:
					player.HasVotingRight = true;
					player.DurableVotingPower = 1;
					break;
				case PermanentRoleSwapDisposition.Change:
					player.HasVotingRight = entry.StateChanges
						.VotingStateAfterSwap!.HasVotingRight;
					player.DurableVotingPower = entry.StateChanges
						.VotingStateAfterSwap.DurableVotingPower;
					break;
				default:
					throw new InvalidOperationException(
						"Permanent Role Swap voting history is invalid.");
			}
		}

		public void ApplyDevotedServantRoleTake(
			DevotedServantRoleTakenCommittedLogEntry entry)
		{
			var actor = GetPlayer(entry.ActingPlayerId);
			var target = GetPlayer(entry.VoteTargetId);
			if (actor.CurrentRole != MainRoleType.DevotedServant ||
				target.CurrentRole != entry.ExpectedTargetCurrentRole)
			{
				throw new InvalidOperationException(
					"Devoted Servant Role-take history does not match the prior current Roles.");
			}

			SwappedPlayerIds.Add(entry.ActingPlayerId);
			SwappedPlayerIds.Add(entry.VoteTargetId);
			actor.CurrentRole = entry.NewCurrentRole;
			actor.ModeratorKnownRole = entry.NewCurrentRole;
			target.CurrentRole = null;
			target.ModeratorKnownRole = null;
			foreach (var effect in entry.StateChanges.StatusEffectsToClear)
			{
				actor.ActiveEffects &= ~effect;
			}

			switch (entry.Policy.VotingState)
			{
				case PermanentRoleSwapDisposition.Preserve:
					break;
				case PermanentRoleSwapDisposition.Clear:
					actor.HasVotingRight = true;
					actor.DurableVotingPower = 1;
					break;
				case PermanentRoleSwapDisposition.Change:
					actor.HasVotingRight = entry.StateChanges
						.VotingStateAfterSwap!.HasVotingRight;
					actor.DurableVotingPower = entry.StateChanges
						.VotingStateAfterSwap.DurableVotingPower;
					break;
				default:
					throw new InvalidOperationException(
						"Devoted Servant voting-state history is invalid.");
			}
		}

		public void AddLogEntry<T>(T entry) where T : GameLogEntryBase
		{
			CurrentTurnNumber = entry.TurnNumber;
			if (entry is LoversPairCommittedLogEntry lovers)
			{
				_loversPairPlayerIds = lovers.PlayerIds;
			}
		}
	}

	private sealed class ProjectedPlayer
	{
		internal MainRoleType? CurrentRole { get; set; }
		internal MainRoleType? ModeratorKnownRole { get; set; }
		internal MainRoleType? PubliclyRevealedRole { get; set; }
		internal StatusEffectTypes ActiveEffects { get; set; }
		internal bool HasVotingRight { get; set; } = true;
		internal int DurableVotingPower { get; set; } = 1;
	}
}
