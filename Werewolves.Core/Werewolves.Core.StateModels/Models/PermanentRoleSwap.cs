using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

public enum PermanentRoleSwapDisposition
{
	Unknown = 0,
	Preserve = 1,
	Change = 2,
	Clear = 3
}

public sealed record PermanentRoleSwapPolicy(
	PermanentRoleSwapDisposition PrivateRoleKnowledge,
	PermanentRoleSwapDisposition PublicRevealHistory,
	PermanentRoleSwapDisposition FactionBeneficiary,
	PermanentRoleSwapDisposition FactionAgents,
	PermanentRoleSwapDisposition Relationships,
	PermanentRoleSwapDisposition StatusEffects,
	PermanentRoleSwapDisposition VotingState,
	PermanentRoleSwapDisposition Restrictions,
	PermanentRoleSwapDisposition Assignments,
	PermanentRoleSwapDisposition RolePowerState)
{
	public bool IsExplicit =>
		new[]
		{
			PrivateRoleKnowledge,
			PublicRevealHistory,
			FactionBeneficiary,
			FactionAgents,
			Relationships,
			StatusEffects,
			VotingState,
			Restrictions,
			Assignments,
			RolePowerState
		}.All(disposition =>
			Enum.IsDefined(disposition) &&
			disposition != PermanentRoleSwapDisposition.Unknown);

}

public sealed record PermanentRoleSwapCardMovement
{
	public Guid OutgoingOwnedCardId { get; }
	public Guid AcquiredCardId { get; }
	public IReadOnlyList<Guid> AdditionalSetAsideCardIds { get; }
	public Guid? ExpectedAcquiredCardOwnerPlayerId { get; }

	public PermanentRoleSwapCardMovement(
		Guid outgoingOwnedCardId,
		Guid acquiredCardId,
		IReadOnlyList<Guid> additionalSetAsideCardIds,
		Guid? expectedAcquiredCardOwnerPlayerId = null)
	{
		ArgumentNullException.ThrowIfNull(additionalSetAsideCardIds);
		var cardIds = new[] { outgoingOwnedCardId, acquiredCardId }
			.Concat(additionalSetAsideCardIds)
			.ToArray();
		if (cardIds.Any(cardId => cardId == Guid.Empty) ||
			cardIds.Distinct().Count() != cardIds.Length)
		{
			throw new ArgumentException(
				"Permanent Role Swap card movement requires distinct physical card identities.");
		}
		if (expectedAcquiredCardOwnerPlayerId == Guid.Empty)
		{
			throw new ArgumentException(
				"An expected acquired-card owner must have a stable Player identity.",
				nameof(expectedAcquiredCardOwnerPlayerId));
		}

		OutgoingOwnedCardId = outgoingOwnedCardId;
		AcquiredCardId = acquiredCardId;
		AdditionalSetAsideCardIds = Array.AsReadOnly(
			additionalSetAsideCardIds.ToArray());
		ExpectedAcquiredCardOwnerPlayerId = expectedAcquiredCardOwnerPlayerId;
	}
}

public sealed record PermanentRoleSwapFactionReplacement
{
	public Faction BeneficiaryCandidate { get; }
	public IReadOnlyDictionary<Faction, FactionAgentKnowledge> AgentFacts { get; }

	public PermanentRoleSwapFactionReplacement(
		Faction beneficiaryCandidate,
		IReadOnlyDictionary<Faction, FactionAgentKnowledge> agentFacts)
	{
		ArgumentNullException.ThrowIfNull(agentFacts);
		if (!Enum.IsDefined(beneficiaryCandidate))
		{
			throw new ArgumentOutOfRangeException(nameof(beneficiaryCandidate));
		}
		var factions = Enum.GetValues<Faction>();
		if (agentFacts.Count != factions.Length ||
			factions.Any(faction =>
				!agentFacts.TryGetValue(faction, out var knowledge) ||
				!Enum.IsDefined(knowledge) ||
				knowledge == FactionAgentKnowledge.Unknown))
		{
			throw new ArgumentException(
				"Permanent Role Swap requires complete known Agent facts.",
				nameof(agentFacts));
		}

		BeneficiaryCandidate = beneficiaryCandidate;
		AgentFacts = new Dictionary<Faction, FactionAgentKnowledge>(agentFacts);
	}
}

internal static class PermanentRoleSwapFactionFacts
{
	internal static FactionFactSource CreateSource(
		Guid playerId,
		Guid powerInstanceId) =>
		new(
			FactionFactSourceKind.ExplicitTransition,
			$"permanent-role-swap:{playerId:N}:{powerInstanceId:N}");

	internal static ImmutableArray<FactionFact> CreateBatch(
		Guid playerId,
		PermanentRoleSwapPolicy policy,
		PermanentRoleSwapFactionReplacement replacement,
		FactionFactEffectiveBoundary boundary)
	{
		ArgumentNullException.ThrowIfNull(policy);
		ArgumentNullException.ThrowIfNull(replacement);
		ArgumentNullException.ThrowIfNull(boundary);

		var facts = ImmutableArray.CreateBuilder<FactionFact>();
		if (policy.FactionBeneficiary == PermanentRoleSwapDisposition.Change)
		{
			facts.Add(FactionFact.Beneficiary(
				playerId,
				replacement.BeneficiaryCandidate,
				boundary));
		}
		if (policy.FactionAgents == PermanentRoleSwapDisposition.Change)
		{
			facts.AddRange(Enum.GetValues<Faction>().Select(faction =>
				FactionFact.Agent(
					playerId,
					faction,
					replacement.AgentFacts[faction],
					boundary)));
		}
		return facts.ToImmutable();
	}

	internal static bool IsCanonicalSource(
		FactionFactSource source,
		Guid playerId,
		Guid powerInstanceId) =>
		source == CreateSource(playerId, powerInstanceId);

	internal static bool IsValidCommittedBatch(
		Guid playerId,
		PermanentRoleSwapPolicy policy,
		ImmutableArray<FactionFact> facts,
		int turnNumber,
		GamePhase phase,
		int? expectedOrder)
	{
		ArgumentNullException.ThrowIfNull(policy);
		if (facts.IsDefault ||
			facts.Any(fact =>
				fact.PlayerId != playerId ||
				fact.EffectiveBoundary.TurnNumber != turnNumber ||
				fact.EffectiveBoundary.Phase != phase ||
				expectedOrder is { } order &&
					fact.EffectiveBoundary.Order != order))
		{
			return false;
		}

		var beneficiaryFacts = facts
			.Where(fact => fact.Type == FactionFactType.Beneficiary)
			.ToArray();
		var agentFacts = facts
			.Where(fact => fact.Type == FactionFactType.Agent)
			.ToArray();
		if (facts.Length != beneficiaryFacts.Length + agentFacts.Length)
		{
			return false;
		}

		var beneficiaryValid = policy.FactionBeneficiary switch
		{
			PermanentRoleSwapDisposition.Preserve => beneficiaryFacts.Length == 0,
			PermanentRoleSwapDisposition.Change =>
				beneficiaryFacts is [var beneficiary] &&
				beneficiary.BeneficiaryPrecedence == 0,
			_ => false
		};
		var agentValid = policy.FactionAgents switch
		{
			PermanentRoleSwapDisposition.Preserve => agentFacts.Length == 0,
			PermanentRoleSwapDisposition.Change =>
				agentFacts.Length == Enum.GetValues<Faction>().Length &&
				Enum.GetValues<Faction>().All(faction =>
					agentFacts.Count(fact => fact.Faction == faction) == 1),
			_ => false
		};
		return beneficiaryValid && agentValid;
	}
}

public sealed record PermanentRoleSwapVotingState(
	bool HasVotingRight,
	int DurableVotingPower)
{
	public PermanentRoleSwapVotingState Validate()
	{
		ArgumentOutOfRangeException.ThrowIfNegative(DurableVotingPower);
		return this;
	}
}

public sealed record PermanentRoleSwapStateChanges
{
	public ImmutableArray<StatusEffectTypes> RelationshipEffectsToClear { get; }
	public ImmutableArray<StatusEffectTypes> StatusEffectsToClear { get; }
	public PermanentRoleSwapVotingState? VotingStateAfterSwap { get; }
	public ImmutableArray<string> RestrictionScopeIdsToClear { get; }
	public ImmutableArray<string> AssignmentIdsToClear { get; }

	public PermanentRoleSwapStateChanges(
		IReadOnlySet<StatusEffectTypes> relationshipEffectsToClear,
		IReadOnlySet<StatusEffectTypes> statusEffectsToClear,
		PermanentRoleSwapVotingState? votingStateAfterSwap,
		IReadOnlySet<string> restrictionScopeIdsToClear,
		IReadOnlySet<string> assignmentIdsToClear)
		: this(
			relationshipEffectsToClear is null
				? throw new ArgumentNullException(nameof(relationshipEffectsToClear))
				: relationshipEffectsToClear.Order().ToImmutableArray(),
			statusEffectsToClear is null
				? throw new ArgumentNullException(nameof(statusEffectsToClear))
				: statusEffectsToClear.Order().ToImmutableArray(),
			votingStateAfterSwap,
			restrictionScopeIdsToClear is null
				? throw new ArgumentNullException(nameof(restrictionScopeIdsToClear))
				: restrictionScopeIdsToClear
					.OrderBy(value => value, StringComparer.Ordinal)
					.ToImmutableArray(),
			assignmentIdsToClear is null
				? throw new ArgumentNullException(nameof(assignmentIdsToClear))
				: assignmentIdsToClear
					.OrderBy(value => value, StringComparer.Ordinal)
					.ToImmutableArray())
	{
	}

	[JsonConstructor]
	internal PermanentRoleSwapStateChanges(
		ImmutableArray<StatusEffectTypes> relationshipEffectsToClear,
		ImmutableArray<StatusEffectTypes> statusEffectsToClear,
		PermanentRoleSwapVotingState? votingStateAfterSwap,
		ImmutableArray<string> restrictionScopeIdsToClear,
		ImmutableArray<string> assignmentIdsToClear)
	{
		if (relationshipEffectsToClear.IsDefault ||
			statusEffectsToClear.IsDefault ||
			restrictionScopeIdsToClear.IsDefault ||
			assignmentIdsToClear.IsDefault ||
			relationshipEffectsToClear.Distinct().Count() !=
				relationshipEffectsToClear.Length ||
			statusEffectsToClear.Distinct().Count() !=
				statusEffectsToClear.Length ||
			restrictionScopeIdsToClear.Distinct(StringComparer.Ordinal).Count() !=
				restrictionScopeIdsToClear.Length ||
			assignmentIdsToClear.Distinct(StringComparer.Ordinal).Count() !=
				assignmentIdsToClear.Length ||
			relationshipEffectsToClear.Any(effect =>
				effect != StatusEffectTypes.Lovers) ||
			statusEffectsToClear.Any(effect =>
				effect is StatusEffectTypes.None or StatusEffectTypes.Lovers ||
				!Enum.IsDefined(effect)) ||
			restrictionScopeIdsToClear.Any(string.IsNullOrWhiteSpace) ||
			assignmentIdsToClear.Any(string.IsNullOrWhiteSpace))
		{
			throw new ArgumentException(
				"Permanent Role Swap state-change targets are invalid.");
		}

		RelationshipEffectsToClear = relationshipEffectsToClear;
		StatusEffectsToClear = statusEffectsToClear;
		VotingStateAfterSwap = votingStateAfterSwap?.Validate();
		RestrictionScopeIdsToClear = restrictionScopeIdsToClear;
		AssignmentIdsToClear = assignmentIdsToClear;
	}

	public static PermanentRoleSwapStateChanges None { get; } = new(
		new HashSet<StatusEffectTypes>(),
		new HashSet<StatusEffectTypes>(),
		votingStateAfterSwap: null,
		new HashSet<string>(),
		new HashSet<string>());
	public bool IsEmpty =>
		RelationshipEffectsToClear.Length == 0 &&
		StatusEffectsToClear.Length == 0 &&
		VotingStateAfterSwap is null &&
		RestrictionScopeIdsToClear.Length == 0 &&
		AssignmentIdsToClear.Length == 0;

	public bool IsCoherentWith(PermanentRoleSwapPolicy policy)
	{
		ArgumentNullException.ThrowIfNull(policy);
		return policy.IsExplicit &&
			(policy.Relationships == PermanentRoleSwapDisposition.Preserve
				? RelationshipEffectsToClear.Length == 0
				: policy.Relationships == PermanentRoleSwapDisposition.Clear) &&
			(policy.StatusEffects == PermanentRoleSwapDisposition.Preserve
				? StatusEffectsToClear.Length == 0
				: policy.StatusEffects is PermanentRoleSwapDisposition.Clear or
					PermanentRoleSwapDisposition.Change) &&
			(policy.VotingState switch
			{
				PermanentRoleSwapDisposition.Preserve => VotingStateAfterSwap is null,
				PermanentRoleSwapDisposition.Change => VotingStateAfterSwap is not null,
				PermanentRoleSwapDisposition.Clear => VotingStateAfterSwap is null,
				_ => false
			}) &&
			policy.Restrictions == PermanentRoleSwapDisposition.Preserve &&
			RestrictionScopeIdsToClear.Length == 0 &&
			policy.Assignments == PermanentRoleSwapDisposition.Preserve &&
			AssignmentIdsToClear.Length == 0;
	}
}

public sealed record PermanentRoleSwapRequest(
	long ExpectedRoleLockInVersion,
	Guid PlayerId,
	MainRoleType ExpectedCurrentRole,
	MainRoleType NewCurrentRole,
	PermanentRoleSwapCardMovement PhysicalCards,
	PermanentRoleSwapPolicy Policy,
	PermanentRoleSwapFactionReplacement Factions,
	PermanentRoleSwapStateChanges StateChanges);
