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
	public string? ActorBorrowedRolePowerCommitmentKey { get; set; }
	public ActorBorrowedRolePowerActivationDto?
		ActiveActorBorrowedRolePowerActivation { get; set; }
	public List<ActorBorrowedSeerCheckCommitDto> ActorBorrowedSeerCheckCommits
		{ get; set; } = new();
	public List<ActorBorrowedDefenderProtectionCommitDto>
		ActorBorrowedDefenderProtectionCommits { get; set; } = new();
	public List<ActorBorrowedFoxCheckCommitDto> ActorBorrowedFoxCheckCommits
		{ get; set; } = new();
	public List<ActorBorrowedBearTamerGrowlCommitDto>
		ActorBorrowedBearTamerGrowlCommits { get; set; } = new();
	public List<ActorBorrowedKnightRustySwordScheduleCommitDto>
		ActorBorrowedKnightRustySwordScheduleCommits { get; set; } = new();
	public List<ActorBorrowedHunterFinalShotCommitDto>
		ActorBorrowedHunterFinalShotCommits { get; set; } = new();
	public List<ActorBorrowedElderResistanceCommitDto>
		ActorBorrowedElderResistanceCommits { get; set; } = new();
	public List<ActorBorrowedElderSuppressionCommitDto>
		ActorBorrowedElderSuppressionCommits { get; set; } = new();
	public List<ActorBorrowedScapegoatTieReplacementCommitDto>
		ActorBorrowedScapegoatTieReplacementCommits { get; set; } = new();
	public List<ActorBorrowedScapegoatVoterRestrictionCommitDto>
		ActorBorrowedScapegoatVoterRestrictionCommits { get; set; } = new();
	public List<ActorBorrowedVillageIdiotPardonCommitDto>
		ActorBorrowedVillageIdiotPardonCommits { get; set; } = new();
	public List<ActorBorrowedWitchPotionUseCommitDto>
		ActorBorrowedWitchPotionUseCommits { get; set; } = new();
	public List<ActorBorrowedWitchPotionDeclineCommitDto>
		ActorBorrowedWitchPotionDeclineCommits { get; set; } = new();
	public List<ActorBorrowedCupidLoversCommitDto> ActorBorrowedCupidLoversCommits
		{ get; set; } = new();
	public List<ActorBorrowedStutteringJudgeSignalSetupCommitDto>
		ActorBorrowedStutteringJudgeSignalSetupCommits { get; set; } = new();
	public List<ActorBorrowedStutteringJudgeSignalObservationCommitDto>
		ActorBorrowedStutteringJudgeSignalObservationCommits { get; set; } = new();

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

internal sealed class ActorBorrowedSeerCheckCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public Guid TargetPlayerId { get; set; }
	public FactionAgentKnowledge TargetAgentKnowledge { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedSeerCheckCommitDto FromValue(
		ActorBorrowedSeerCheckCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		TargetPlayerId = commit.TargetPlayerId,
		TargetAgentKnowledge = commit.TargetAgentKnowledge,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedSeerCheckCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		TargetPlayerId,
		TargetAgentKnowledge,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedDefenderProtectionCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public Guid TargetPlayerId { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedDefenderProtectionCommitDto FromValue(
		ActorBorrowedDefenderProtectionCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		TargetPlayerId = commit.TargetPlayerId,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedDefenderProtectionCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		TargetPlayerId,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedFoxCheckCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public Guid CenterPlayerId { get; set; }
	public FactionAgentKnowledge NeighborhoodAgentKnowledge { get; set; }
	public OneUseRolePowerResourceIdentity? SpentResourceIdentity { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedFoxCheckCommitDto FromValue(
		ActorBorrowedFoxCheckCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		CenterPlayerId = commit.CenterPlayerId,
		NeighborhoodAgentKnowledge = commit.NeighborhoodAgentKnowledge,
		SpentResourceIdentity = commit.SpentResourceIdentity,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedFoxCheckCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		CenterPlayerId,
		NeighborhoodAgentKnowledge,
		SpentResourceIdentity,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedVillageIdiotPardonCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public OneUseRolePowerResourceIdentity SpentResourceIdentity { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedVillageIdiotPardonCommitDto FromValue(
		ActorBorrowedVillageIdiotPardonCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		SpentResourceIdentity = commit.SpentResourceIdentity,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedVillageIdiotPardonCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		SpentResourceIdentity,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedHunterFinalShotCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public string CascadeScopeId { get; set; } = string.Empty;
	public List<Guid> TriggeringPlayerIds { get; set; } = new();
	public Guid TargetPlayerId { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedHunterFinalShotCommitDto FromValue(
		ActorBorrowedHunterFinalShotCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		CascadeScopeId = commit.CascadeScopeId,
		TriggeringPlayerIds = commit.TriggeringPlayerIds.ToList(),
		TargetPlayerId = commit.TargetPlayerId,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedHunterFinalShotCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		CascadeScopeId,
		TriggeringPlayerIds,
		TargetPlayerId,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedElderResistanceCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public Guid TargetPlayerId { get; set; }
	public int TriggeringNightActionLogIndex { get; set; }
	public int? RestoringWitchSaveLogIndex { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedElderResistanceCommitDto FromValue(
		ActorBorrowedElderResistanceCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		TargetPlayerId = commit.TargetPlayerId,
		TriggeringNightActionLogIndex =
			commit.TriggeringNightActionLogIndex,
		RestoringWitchSaveLogIndex = commit.RestoringWitchSaveLogIndex,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedElderResistanceCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		TargetPlayerId,
		TriggeringNightActionLogIndex,
		RestoringWitchSaveLogIndex,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedElderSuppressionCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public int TriggeringVoteOutcomeLogIndex { get; set; }
	public string CascadeScopeId { get; set; } = string.Empty;
	public Guid AnnouncementInstructionId { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedElderSuppressionCommitDto FromValue(
		ActorBorrowedElderSuppressionCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		TriggeringVoteOutcomeLogIndex =
			commit.TriggeringVoteOutcomeLogIndex,
		CascadeScopeId = commit.CascadeScopeId,
		AnnouncementInstructionId = commit.AnnouncementInstructionId,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedElderSuppressionCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		TriggeringVoteOutcomeLogIndex,
		CascadeScopeId,
		AnnouncementInstructionId,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedBearTamerGrowlCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedBearTamerGrowlCommitDto FromValue(
		ActorBorrowedBearTamerGrowlCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedBearTamerGrowlCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedKnightRustySwordScheduleCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public Guid TargetPlayerId { get; set; }
	public int WerewolfAttackEliminationLogIndex { get; set; }
	public string CascadeScopeId { get; set; } = string.Empty;
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedKnightRustySwordScheduleCommitDto FromValue(
		ActorBorrowedKnightRustySwordScheduleCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		TargetPlayerId = commit.TargetPlayerId,
		WerewolfAttackEliminationLogIndex =
			commit.WerewolfAttackEliminationLogIndex,
		CascadeScopeId = commit.CascadeScopeId,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedKnightRustySwordScheduleCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		TargetPlayerId,
		WerewolfAttackEliminationLogIndex,
		CascadeScopeId,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedScapegoatTieReplacementCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public int TriggeringVoteOutcomeLogIndex { get; set; }
	public int VoteOrdinal { get; set; }
	public string CascadeScopeId { get; set; } = string.Empty;
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedScapegoatTieReplacementCommitDto FromValue(
		ActorBorrowedScapegoatTieReplacementCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		TriggeringVoteOutcomeLogIndex =
			commit.TriggeringVoteOutcomeLogIndex,
		VoteOrdinal = commit.VoteOrdinal,
		CascadeScopeId = commit.CascadeScopeId,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedScapegoatTieReplacementCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		TriggeringVoteOutcomeLogIndex,
		VoteOrdinal,
		CascadeScopeId,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedScapegoatVoterRestrictionCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public int TieReplacementPublicMarkerLogIndex { get; set; }
	public string CascadeScopeId { get; set; } = string.Empty;
	public List<Guid> CandidatePlayerIds { get; set; } = new();
	public List<Guid> PermittedVoterIds { get; set; } = new();
	public int AppliesOnTurnNumber { get; set; }
	public Guid AnnouncementInstructionId { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedScapegoatVoterRestrictionCommitDto FromValue(
		ActorBorrowedScapegoatVoterRestrictionCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		TieReplacementPublicMarkerLogIndex =
			commit.TieReplacementPublicMarkerLogIndex,
		CascadeScopeId = commit.CascadeScopeId,
		CandidatePlayerIds = commit.CandidatePlayerIds.ToList(),
		PermittedVoterIds = commit.PermittedVoterIds.ToList(),
		AppliesOnTurnNumber = commit.AppliesOnTurnNumber,
		AnnouncementInstructionId = commit.AnnouncementInstructionId,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedScapegoatVoterRestrictionCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		TieReplacementPublicMarkerLogIndex,
		CascadeScopeId,
		CandidatePlayerIds,
		PermittedVoterIds,
		AppliesOnTurnNumber,
		AnnouncementInstructionId,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedWitchPotionUseCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public OneUseRolePowerResourceIdentity SpentResourceIdentity { get; set; }
	public Guid TargetPlayerId { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedWitchPotionUseCommitDto FromValue(
		ActorBorrowedWitchPotionUseCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		SpentResourceIdentity = commit.SpentResourceIdentity,
		TargetPlayerId = commit.TargetPlayerId,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedWitchPotionUseCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		SpentResourceIdentity,
		TargetPlayerId,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedWitchPotionDeclineCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public OneUseRolePowerResourceIdentity OfferedResourceIdentity { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedWitchPotionDeclineCommitDto FromValue(
		ActorBorrowedWitchPotionDeclineCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		OfferedResourceIdentity = commit.OfferedResourceIdentity,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedWitchPotionDeclineCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		OfferedResourceIdentity,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedCupidLoversCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public Guid FirstPlayerId { get; set; }
	public Guid SecondPlayerId { get; set; }
	public ActorBorrowedCupidLoversDisposition Disposition { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedCupidLoversCommitDto FromValue(
		ActorBorrowedCupidLoversCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		FirstPlayerId = commit.FirstPlayerId,
		SecondPlayerId = commit.SecondPlayerId,
		Disposition = commit.Disposition,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedCupidLoversCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		FirstPlayerId,
		SecondPlayerId,
		Disposition,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedStutteringJudgeSignalSetupCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedStutteringJudgeSignalSetupCommitDto FromValue(
		ActorBorrowedStutteringJudgeSignalSetupCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedStutteringJudgeSignalSetupCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
}

internal sealed class ActorBorrowedStutteringJudgeSignalObservationCommitDto
{
	public RolePowerInstanceIdentity PowerIdentity { get; set; }
	public Guid ActorSetupCardId { get; set; }
	public bool SignalOccurred { get; set; }
	public OneUseRolePowerResourceIdentity? SpentResourceIdentity { get; set; }
	public DateTimeOffset Timestamp { get; set; }
	public int TurnNumber { get; set; }
	public GamePhase CurrentPhase { get; set; }
	public int PublicMarkerLogIndex { get; set; }

	internal static ActorBorrowedStutteringJudgeSignalObservationCommitDto FromValue(
		ActorBorrowedStutteringJudgeSignalObservationCommit commit) => new()
	{
		PowerIdentity = commit.PowerIdentity,
		ActorSetupCardId = commit.ActorSetupCardId,
		SignalOccurred = commit.SignalOccurred,
		SpentResourceIdentity = commit.SpentResourceIdentity,
		Timestamp = commit.Timestamp,
		TurnNumber = commit.TurnNumber,
		CurrentPhase = commit.CurrentPhase,
		PublicMarkerLogIndex = commit.PublicMarkerLogIndex
	};

	internal ActorBorrowedStutteringJudgeSignalObservationCommit ToValue() => new(
		PowerIdentity,
		ActorSetupCardId,
		SignalOccurred,
		SpentResourceIdentity,
		Timestamp,
		TurnNumber,
		CurrentPhase,
		PublicMarkerLogIndex);
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
	ActorSetupCardSpendCommit = 4,
	ActorBorrowedStutteringJudgeSignalObservationCommit = 5,
	ActorBorrowedWitchPotionUseCommit = 6,
	ActorBorrowedWitchPotionDeclineCommit = 7
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
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public DayPowerType? CommittedDayActionType { get; set; }
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
