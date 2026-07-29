using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

public enum FactionFactSourceKind
{
	ScheduledObservation = 0,
	ExplicitTransition = 1,
	InitialBeneficiaryClosure = 2,
	SimulationStartState = 3
}

public sealed record FactionFactSource
{
	[JsonConstructor]
	public FactionFactSource(FactionFactSourceKind kind, string identifier)
	{
		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
		Kind = kind;
		Identifier = identifier;
	}

	public FactionFactSourceKind Kind { get; }

	public string Identifier { get; }
}

public sealed record FactionFactEffectiveBoundary
{
	[JsonConstructor]
	public FactionFactEffectiveBoundary(
		int turnNumber,
		GamePhase phase,
		int order)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(turnNumber, 1);
		if (!Enum.IsDefined(phase))
		{
			throw new ArgumentOutOfRangeException(nameof(phase));
		}

		ArgumentOutOfRangeException.ThrowIfNegative(order);
		TurnNumber = turnNumber;
		Phase = phase;
		Order = order;
	}

	public int TurnNumber { get; }

	public GamePhase Phase { get; }

	public int Order { get; }
}

public enum FactionFactType
{
	Beneficiary = 0,
	Agent = 1
}

/// <summary>
/// One final Faction fact established by an accepted observation or Core rule.
/// Unknown and provisional candidate values are never committed as facts.
/// </summary>
public sealed record FactionFact
{
	[JsonConstructor]
	public FactionFact(
		Guid playerId,
		FactionFactType type,
		Faction faction,
		FactionAgentKnowledge? agentKnowledge,
		FactionFactEffectiveBoundary effectiveBoundary,
		int? beneficiaryPrecedence)
	{
		if (playerId == Guid.Empty)
		{
			throw new ArgumentException(
				"A Faction fact requires a Player.",
				nameof(playerId));
		}

		if (!Enum.IsDefined(type))
		{
			throw new ArgumentOutOfRangeException(nameof(type));
		}

		if (!Enum.IsDefined(faction))
		{
			throw new ArgumentOutOfRangeException(nameof(faction));
		}

		ArgumentNullException.ThrowIfNull(effectiveBoundary);
		if (type == FactionFactType.Beneficiary)
		{
			if (agentKnowledge is not null)
			{
				throw new ArgumentException(
					"A Beneficiary fact cannot carry Agent knowledge.",
					nameof(agentKnowledge));
			}

			if (beneficiaryPrecedence is null or < 0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(beneficiaryPrecedence));
			}
		}
		else
		{
			if (agentKnowledge is null or FactionAgentKnowledge.Unknown ||
			    !Enum.IsDefined(agentKnowledge.Value))
			{
				throw new ArgumentException(
					"An Agent fact must establish a known state.",
					nameof(agentKnowledge));
			}

			if (beneficiaryPrecedence is not null)
			{
				throw new ArgumentException(
					"An Agent fact cannot carry Beneficiary precedence.",
					nameof(beneficiaryPrecedence));
			}
		}

		PlayerId = playerId;
		Type = type;
		Faction = faction;
		AgentKnowledge = agentKnowledge;
		EffectiveBoundary = effectiveBoundary;
		BeneficiaryPrecedence = beneficiaryPrecedence;
	}

	public Guid PlayerId { get; }

	public FactionFactType Type { get; }

	public Faction Faction { get; }

	public FactionAgentKnowledge? AgentKnowledge { get; }

	public FactionFactEffectiveBoundary EffectiveBoundary { get; }

	public int? BeneficiaryPrecedence { get; }

	public static FactionFact Beneficiary(
		Guid playerId,
		Faction faction,
		FactionFactEffectiveBoundary effectiveBoundary,
		int beneficiaryPrecedence = 0) =>
		new(
			playerId,
			FactionFactType.Beneficiary,
			faction,
			agentKnowledge: null,
			effectiveBoundary,
			beneficiaryPrecedence);

	public static FactionFact Agent(
		Guid playerId,
		Faction faction,
		FactionAgentKnowledge knowledge,
		FactionFactEffectiveBoundary effectiveBoundary) =>
		new(
			playerId,
			FactionFactType.Agent,
			faction,
			knowledge,
			effectiveBoundary,
			beneficiaryPrecedence: null);
}

internal static class FactionFactSourceIdentifiers
{
	internal const string InitialBeneficiaryClosure =
		"initial-beneficiary-closure";
	internal const string SimulationStartState = "simulation-start-state";
	internal const string WildChildModelEliminated =
		"wild-child-model-eliminated";
}
