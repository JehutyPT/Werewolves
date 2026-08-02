using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Serialization;

/// <summary>
/// Durable recovery snapshot captured at a stable main-phase boundary.
/// </summary>
internal class GameSessionDto
{
    // Session identity and setup data.
    public Guid Id { get; set; }
    public List<Guid> SeatingOrder { get; set; } = new();
    public List<MainRoleType> RolesInPlay { get; set; } = new();
	public RoleLockInDto? RoleLockIn { get; set; }
	public PublicGroupPartitionDto? PublicGroupPartition { get; set; }
	public List<PhysicalCharacterCardStateDto> PhysicalCharacterCards { get; set; } = new();
	public ActorSetupCardsDto? ActorSetupCards { get; set; }
	public List<ActorSetupCardSpendDto>? ActorSetupCardSpends { get; set; } = new();
	public ActorBorrowedRolePowerActivationDto?
		ActiveActorBorrowedRolePowerActivation { get; set; }

    // Derived state restored directly during Rehydration.
    public List<PlayerDto> Players { get; set; } = new();
    public int TurnNumber { get; set; }
    public int RoleFactSchemaVersion { get; set; }
    public int FactionFactSchemaVersion { get; set; }

    // True for the ADR-0002 payload shape that preserves a committed boundary cursor.
    public bool IsStableRecoveryBoundary { get; set; }

    // Narrow, versioned semantic cursor for an accepted observation and its
    // committed next Pending Instruction. It never carries live listener state.
    public AcceptedObservationRecoveryCursor? AcceptedObservationRecoveryCursor { get; set; }

    // Generalized, versioned semantic cursor for a committed domain operation
    // whose exact next Pending Instruction must be resumed without replay.
    public DomainRecoveryCursor? DomainRecoveryCursor { get; set; }

    // Committed boundary instruction and minimal phase cursor. Active stage/listener fields
    // remain compatibility-only and are ignored during Rehydration.
    public GamePhaseStateCacheDto PhaseStateCache { get; set; } = new();
    public ModeratorInstruction? PendingInstruction { get; set; }
    public ModeratorInstructionSemantic? PendingInstructionSemantic { get; set; }

    // Event source as of the same stable boundary as the derived state above.
    public List<GameLogEntryBase> GameHistoryLog { get; set; } = new();
}

internal static class RoleFactSchema
{
    public const int LegacyVersion = 0;
    public const int CurrentVersion = 1;
}

internal static class FactionFactSchema
{
    public const int CurrentVersion = 2;
}

/// <summary>
/// Data Transfer Object for serializing Player state.
/// </summary>
internal class PlayerDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MainRoleType? MainRole { get; set; }
	public Guid? PhysicalCharacterCardId { get; set; }
    public MainRoleType? PhysicalCharacterCardRole { get; set; }
    public MainRoleType? ModeratorKnownRole { get; set; }
    public MainRoleType? PubliclyRevealedRole { get; set; }
    public StatusEffectTypes ActiveEffects { get; set; }
    public PlayerHealth Health { get; set; }
    public bool? HasVotingRight { get; set; }
    public required int DurableVotingPower { get; set; }
    public FactionBeneficiaryKnowledge? FactionBeneficiary { get; set; }
    public Dictionary<Faction, FactionAgentKnowledge>? FactionAgentKnowledge
        { get; set; }
}

internal sealed class RoleLockInDto
{
	public long Version { get; set; }
	public int PlayerCount { get; set; }
	public List<PhysicalCharacterCard> RoleComposition { get; set; } = new();
	public List<Guid> DealPoolCardIds { get; set; } = new();
	public Guid? Offer1CardId { get; set; }
	public Guid? Offer2CardId { get; set; }

	internal static RoleLockInDto FromValue(RoleLockIn roleLockIn)
	{
		ArgumentNullException.ThrowIfNull(roleLockIn);
		return new RoleLockInDto
		{
			Version = roleLockIn.Version,
			PlayerCount = roleLockIn.PlayerCount,
			RoleComposition = roleLockIn.RoleComposition.ToList(),
			DealPoolCardIds = roleLockIn.DealPool.Select(card => card.Id).ToList(),
			Offer1CardId = roleLockIn.Offer1?.Id,
			Offer2CardId = roleLockIn.Offer2?.Id
		};
	}

	internal RoleLockIn ToValue() => new(
		Version,
		PlayerCount,
		RoleComposition,
		DealPoolCardIds,
		Offer1CardId,
		Offer2CardId);
}

internal sealed class PhysicalCharacterCardStateDto
{
	public Guid CardId { get; set; }
	public PhysicalCharacterCardZone Zone { get; set; }
	public Guid? OwnerPlayerId { get; set; }
}

internal sealed class ActorSetupCardsDto
{
	public long Version { get; set; }
	public List<PhysicalCharacterCard>? Cards { get; set; } = new();

	internal static ActorSetupCardsDto FromValue(ActorSetupCards setup) => new()
	{
		Version = setup.Version,
		Cards = setup.Cards.ToList()
	};

	internal ActorSetupCards ToValue() => new(
		Version,
		Cards ?? throw new InvalidOperationException(
			"The stable recovery snapshot has no Actor Setup Card inventory."));
}

internal sealed class ActorSetupCardSpendDto
{
	public Guid CardId { get; set; }
	public Guid ActivationId { get; set; }
}

internal sealed class ActorBorrowedRolePowerActivationDto
{
	public Guid ActivationId { get; set; }
	public Guid ActingPlayerId { get; set; }
	public MainRoleType ActingRole { get; set; }
	public Guid SelectedCardId { get; set; }
	public MainRoleType SourceRole { get; set; }

	internal static ActorBorrowedRolePowerActivationDto FromValue(
		ActorBorrowedRolePowerActivation activation) => new()
	{
		ActivationId = activation.ActivationId,
		ActingPlayerId = activation.ActingPlayerId,
		ActingRole = activation.ActingRole,
		SelectedCardId = activation.SelectedCardId,
		SourceRole = activation.SourceRole
	};

	internal ActorBorrowedRolePowerActivation ToValue() => new(
		ActivationId,
		ActingPlayerId,
		ActingRole,
		SelectedCardId,
		SourceRole);
}

internal sealed class PublicGroupPartitionDto
{
	public List<Guid> FirstGroupPlayerIds { get; set; } = new();
	public List<Guid> SecondGroupPlayerIds { get; set; } = new();

	internal static PublicGroupPartitionDto FromValue(
		PublicGroupPartition publicGroupPartition)
	{
		ArgumentNullException.ThrowIfNull(publicGroupPartition);
		return new PublicGroupPartitionDto
		{
			FirstGroupPlayerIds = publicGroupPartition.FirstGroupPlayerIds.ToList(),
			SecondGroupPlayerIds = publicGroupPartition.SecondGroupPlayerIds.ToList()
		};
	}

	internal PublicGroupPartition ToValue(IEnumerable<Guid> rosterPlayerIds) =>
		PublicGroupPartition.Create(
			rosterPlayerIds,
			FirstGroupPlayerIds,
			SecondGroupPlayerIds);
}

/// <summary>
/// Durable semantic continuation for a completed observation boundary.
/// </summary>
internal sealed class AcceptedObservationRecoveryCursor
{
    public const int CurrentVersion = 1;

    public int Version { get; set; }
    public ModeratorInstructionSemantic AcceptedObservationSemantic { get; set; }
    public MainRoleType ObservedRole { get; set; }
	public MainRoleType? ContinuationRole { get; set; }
	public bool? RetainedLittleGirlGuidanceDecision { get; set; }
    public ModeratorInstructionSemantic NextInstructionSemantic { get; set; }
    public Guid NextInstructionId { get; set; }
}

internal enum DomainRecoveryCursorKind
{
    OneUseRolePowerCommit = 1,
    RecurringNativeRolePowerCommit = 2,
	TargetPrivateRolePowerCommit = 3,
	ActorSetupCardSpendCommit = 4
}

/// <summary>
/// Durable semantic continuation for a committed domain operation.
/// It carries no live listener state.
/// </summary>
internal sealed class DomainRecoveryCursor
{
    public const int CurrentVersion = 1;

    public int Version { get; set; }
    public DomainRecoveryCursorKind Kind { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MainRoleType? SourceRole { get; set; }
    public NightActionType CommittedActionType { get; set; }
    public Guid ActingPlayerId { get; set; }
    public string SourcePowerIdentifier { get; set; } = string.Empty;
    public Guid PowerInstanceId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RolePowerInstanceOrigin? PowerInstanceOrigin { get; set; }
    public Guid OneUseResourceId { get; set; }
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public Guid ActorSetupCardId { get; set; }
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public Guid ActorBorrowedActivationId { get; set; }
    public List<Guid> CommittedTargetIds { get; set; } = new();
    public ModeratorInstructionSemantic NextInstructionSemantic { get; set; }
    public Guid NextInstructionId { get; set; }

    internal OneUseRolePowerResourceIdentity? ResourceIdentity =>
        SourceRole is { } sourceRole &&
        PowerInstanceOrigin is { } powerInstanceOrigin
            ? new(
                ActingPlayerId,
                sourceRole,
                SourcePowerIdentifier,
                PowerInstanceId,
                powerInstanceOrigin,
                OneUseResourceId)
            : null;

    internal RolePowerInstanceIdentity? PowerIdentity =>
        SourceRole is { } sourceRole &&
        PowerInstanceOrigin is { } powerInstanceOrigin
            ? new(
                ActingPlayerId,
                sourceRole,
                SourcePowerIdentifier,
                PowerInstanceId,
                powerInstanceOrigin)
            : null;
}

/// <summary>
/// Serialized phase cache shape. Stable-boundary Rehydration restores only CurrentPhase,
/// SubPhase, and CompletedSubPhaseStages.
/// </summary>
internal class GamePhaseStateCacheDto
{
    public GamePhase CurrentPhase { get; set; }
    public string? SubPhase { get; set; }
    public string? ActiveSubPhaseStage { get; set; }
    public List<string> CompletedSubPhaseStages { get; set; } = new();
    public string? CurrentListenerId { get; set; }
    public string? CurrentListenerType { get; set; }
    public string? CurrentListenerState { get; set; }
}
