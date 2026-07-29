using System.Collections.Immutable;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.StateModels.Log;

/// <summary>
/// One append-only, atomic batch of final Faction facts.
/// </summary>
public sealed record FactionFactsCommittedLogEntry
	: GameLogEntryBase
{
	public required FactionFactSource Source { get; init; }

	public required ImmutableArray<FactionFact> Facts { get; init; }

	internal override void EnforceValidity()
	{
		ArgumentNullException.ThrowIfNull(Source);
		if (Facts.IsDefault || Facts.Any(fact => fact is null))
		{
			throw new InvalidOperationException(
				"A Faction fact batch is structurally invalid.");
		}

		if (Facts.IsEmpty &&
		    Source.Kind != FactionFactSourceKind.InitialBeneficiaryClosure)
		{
			throw new InvalidOperationException(
				"Only Initial Beneficiary Closure may commit an empty Faction fact batch.");
		}

		if (Facts.Distinct().Count() != Facts.Length)
		{
			throw new InvalidOperationException(
				"A Faction fact batch cannot contain duplicate facts.");
		}

		var duplicateBoundaries = Facts
			.GroupBy(FactionFactProjection.FactBoundaryKey)
			.Any(group => group.Count() > 1);
		if (duplicateBoundaries)
		{
			throw new InvalidOperationException(
				"A Faction fact batch contains contradictory facts at one boundary.");
		}

		foreach (var fact in Facts)
		{
			var effective = fact.EffectiveBoundary;
			if (effective.TurnNumber > TurnNumber ||
			    effective.TurnNumber == TurnNumber &&
			    FactionFactProjection.PhaseOrder(effective.Phase) >
			    FactionFactProjection.PhaseOrder(CurrentPhase))
			{
				throw new InvalidOperationException(
					"A Faction fact cannot be effective after its commit boundary.");
			}
		}
	}

	protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
	{
		mutator.ApplyFactionFacts(this);
		return this;
	}

	internal bool HasSameBatch(FactionFactsCommittedLogEntry other) =>
		Source == other.Source &&
		Facts.SequenceEqual(other.Facts);

	public override string ToString() =>
		$"FactionFactsCommitted: {Source.Kind} ({Facts.Length} fact(s))";
}

internal sealed class FactionFactProjection
{
	private FactionFactProjection(
		IReadOnlyDictionary<Guid, FactionBeneficiaryKnowledge> beneficiaries,
		IReadOnlyDictionary<
			Guid,
			IReadOnlyDictionary<Faction, FactionAgentKnowledge>> agents)
	{
		Beneficiaries = beneficiaries;
		Agents = agents;
	}

	internal IReadOnlyDictionary<Guid, FactionBeneficiaryKnowledge>
		Beneficiaries { get; }

	internal IReadOnlyDictionary<
		Guid,
		IReadOnlyDictionary<Faction, FactionAgentKnowledge>> Agents { get; }

	internal static FactionFactProjection Create(
		IEnumerable<FactionFactsCommittedLogEntry> entries,
		IReadOnlyCollection<Guid> playerIds,
		FactionFactEffectiveBoundary? inclusiveBoundary = null)
	{
		ArgumentNullException.ThrowIfNull(entries);
		ArgumentNullException.ThrowIfNull(playerIds);
		var players = playerIds.ToHashSet();
		var beneficiaries = players.ToDictionary(
			playerId => playerId,
			_ => FactionBeneficiaryKnowledge.Unknown);
		var beneficiaryPrecedence = players.ToDictionary(
			playerId => playerId,
			_ => -1);
		var agents = players.ToDictionary(
			playerId => playerId,
			_ => Enum.GetValues<Faction>().ToDictionary(
				faction => faction,
				_ => FactionAgentKnowledge.Unknown));

		var orderedFacts = entries
			.SelectMany((entry, entryIndex) =>
				entry.Facts.Select((fact, factIndex) =>
					new OrderedFactionFact(fact, entryIndex, factIndex)))
			.Where(ordered =>
				inclusiveBoundary is null ||
				CompareBoundaries(
					ordered.Fact.EffectiveBoundary,
					inclusiveBoundary) <= 0)
			.OrderBy(ordered => ordered.Fact.EffectiveBoundary.TurnNumber)
			.ThenBy(ordered =>
				PhaseOrder(ordered.Fact.EffectiveBoundary.Phase))
			.ThenBy(ordered => ordered.Fact.EffectiveBoundary.Order)
			.ThenBy(ordered => ordered.EntryIndex)
			.ThenBy(ordered => ordered.FactIndex);

		foreach (var ordered in orderedFacts)
		{
			var fact = ordered.Fact;
			if (!players.Contains(fact.PlayerId))
			{
				throw new InvalidOperationException(
					"Faction history references a Player outside the Game Session.");
			}

			if (fact.Type == FactionFactType.Agent)
			{
				agents[fact.PlayerId][fact.Faction] =
					fact.AgentKnowledge!.Value;
				continue;
			}

			var incomingPrecedence = fact.BeneficiaryPrecedence!.Value;
			if (incomingPrecedence < beneficiaryPrecedence[fact.PlayerId])
			{
				continue;
			}

			beneficiaryPrecedence[fact.PlayerId] = incomingPrecedence;
			beneficiaries[fact.PlayerId] =
				FactionBeneficiaryKnowledge.Known(fact.Faction);
		}

		return new FactionFactProjection(
			beneficiaries,
			agents.ToDictionary(
				pair => pair.Key,
				pair =>
					(IReadOnlyDictionary<Faction, FactionAgentKnowledge>)
					new Dictionary<Faction, FactionAgentKnowledge>(
						pair.Value)));
	}

	internal static object FactBoundaryKey(FactionFact fact) =>
		fact.Type == FactionFactType.Beneficiary
			? new
			{
				fact.PlayerId,
				fact.Type,
				AgentFaction = (Faction?)null,
				fact.EffectiveBoundary,
				fact.BeneficiaryPrecedence
			}
			: new
			{
				fact.PlayerId,
				fact.Type,
				AgentFaction = (Faction?)fact.Faction,
				fact.EffectiveBoundary,
				BeneficiaryPrecedence = (int?)null
			};

	internal static int CompareBoundaries(
		FactionFactEffectiveBoundary left,
		FactionFactEffectiveBoundary right)
	{
		var turnComparison = left.TurnNumber.CompareTo(right.TurnNumber);
		if (turnComparison != 0)
		{
			return turnComparison;
		}

		var phaseComparison =
			PhaseOrder(left.Phase).CompareTo(PhaseOrder(right.Phase));
		return phaseComparison != 0
			? phaseComparison
			: left.Order.CompareTo(right.Order);
	}

	internal static int PhaseOrder(GamePhase phase) => phase switch
	{
		GamePhase.Night => 0,
		GamePhase.Dawn => 1,
		GamePhase.Day => 2,
		_ => throw new ArgumentOutOfRangeException(nameof(phase))
	};

	private sealed record OrderedFactionFact(
		FactionFact Fact,
		int EntryIndex,
		int FactIndex);
}
