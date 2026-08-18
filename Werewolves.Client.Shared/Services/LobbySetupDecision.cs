using System.Collections.ObjectModel;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Client.Services;

internal abstract record LobbyChange
{
	private LobbyChange()
	{
	}

	internal sealed record ReplaceRoleLockIn : LobbyChange
	{
		public ReplaceRoleLockIn(
			long expectedCurrentVersion,
			RoleLockIn replacement)
		{
			ExpectedCurrentVersion = expectedCurrentVersion;
			Replacement = replacement;
		}

		public long ExpectedCurrentVersion { get; }
		public RoleLockIn Replacement { get; }
	}

	internal sealed record AcceptImplicitRoleLockIn : LobbyChange
	{
		public AcceptImplicitRoleLockIn(
			long expectedCurrentVersion,
			RoleLockIn replacement)
		{
			ExpectedCurrentVersion = expectedCurrentVersion;
			Replacement = replacement;
		}

		public long ExpectedCurrentVersion { get; }
		public RoleLockIn Replacement { get; }
	}

	internal sealed record ReplaceActorSetupCards : LobbyChange
	{
		public ReplaceActorSetupCards(
			long expectedCurrentVersion,
			ActorSetupCards replacement)
		{
			ExpectedCurrentVersion = expectedCurrentVersion;
			Replacement = replacement;
		}

		public long ExpectedCurrentVersion { get; }
		public ActorSetupCards Replacement { get; }
	}

	internal sealed record ReplacePublicGroupPartition(
		PublicGroupPartition Replacement) : LobbyChange;

	internal sealed record MovePlayer : LobbyChange
	{
		public MovePlayer(int fromIndex, int toIndex)
		{
			FromIndex = fromIndex;
			ToIndex = toIndex;
		}

		public int FromIndex { get; }
		public int ToIndex { get; }
	}

	internal sealed record AddPlayer(GameSessionPlayerConfig Player)
		: LobbyChange;

	internal sealed record RemovePlayer : LobbyChange
	{
		public RemovePlayer(int index)
		{
			Index = index;
		}

		public int Index { get; }
	}
}

internal sealed class LobbySetupAggregate
{
	private readonly ReadOnlyCollection<GameSessionPlayerConfig> _playerRoster;
	private readonly ReadOnlyCollection<Guid> _issuedPlayerIds;
	private readonly ReadOnlyDictionary<MainRoleType, int> _roleCounts;

	internal LobbySetupAggregate(
		IEnumerable<GameSessionPlayerConfig> playerRoster,
		IEnumerable<Guid> issuedPlayerIds,
		IReadOnlyDictionary<MainRoleType, int> roleCounts,
		RoleLockIn? acceptedRoleLockIn,
		ActorSetupCards acceptedActorSetupCards,
		PublicGroupPartition? acceptedPublicGroupPartition,
		bool roleLockInFinalized,
		bool acceptedRoleLockInRequiresReplacement)
	{
		ArgumentNullException.ThrowIfNull(playerRoster);
		ArgumentNullException.ThrowIfNull(issuedPlayerIds);
		ArgumentNullException.ThrowIfNull(roleCounts);
		ArgumentNullException.ThrowIfNull(acceptedActorSetupCards);

		_playerRoster = Array.AsReadOnly(playerRoster.ToArray());
		_issuedPlayerIds = Array.AsReadOnly(issuedPlayerIds.ToArray());
		_roleCounts = new ReadOnlyDictionary<MainRoleType, int>(
			roleCounts.ToDictionary(entry => entry.Key, entry => entry.Value));
		AcceptedRoleLockIn = acceptedRoleLockIn;
		AcceptedActorSetupCards = acceptedActorSetupCards;
		AcceptedPublicGroupPartition = acceptedPublicGroupPartition;
		RoleLockInFinalized = roleLockInFinalized;
		AcceptedRoleLockInRequiresReplacement =
			acceptedRoleLockInRequiresReplacement;
	}

	public IReadOnlyList<GameSessionPlayerConfig> PlayerRoster => _playerRoster;
	public IReadOnlyList<Guid> IssuedPlayerIds => _issuedPlayerIds;
	public IReadOnlyDictionary<MainRoleType, int> RoleCounts => _roleCounts;
	public RoleLockIn? AcceptedRoleLockIn { get; }
	public ActorSetupCards AcceptedActorSetupCards { get; }
	public PublicGroupPartition? AcceptedPublicGroupPartition { get; }
	public bool RoleLockInFinalized { get; }
	public bool AcceptedRoleLockInRequiresReplacement { get; }
}

internal abstract record LobbyPersistenceInstruction
{
	internal sealed record Keep : LobbyPersistenceInstruction;
	internal sealed record Clear : LobbyPersistenceInstruction;
	internal sealed record Replace(LobbySetupAggregate Aggregate)
		: LobbyPersistenceInstruction;
}

internal sealed record CanonicalSimulationScenarioDelta(
	CanonicalSimulationScenario? Before,
	CanonicalSimulationScenario? After)
{
	public bool HasIdentityChanged => !Equals(Before, After);
}

internal sealed record LobbyDecision(
	LobbySetupAggregate NextAggregate,
	LobbyPersistenceInstruction Persistence,
	CanonicalSimulationScenarioDelta CanonicalScenarioDelta,
	LobbySetupState.Commit Commit);

internal static class LobbyPersistenceExecutor
{
	public static void Execute(
		IGameSessionSaveStore saveStore,
		LobbyPersistenceInstruction instruction)
	{
		ArgumentNullException.ThrowIfNull(saveStore);
		ArgumentNullException.ThrowIfNull(instruction);
		switch (instruction)
		{
			case LobbyPersistenceInstruction.Keep:
				return;
			case LobbyPersistenceInstruction.Clear:
				saveStore.Clear();
				return;
			case LobbyPersistenceInstruction.Replace replace:
				saveStore.Save(LocalRecoveryPayloadCodec.SerializeStagedLobby(
					replace.Aggregate));
				return;
		}
	}
}
