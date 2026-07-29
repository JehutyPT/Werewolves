namespace Werewolves.Core.StateModels.Models;

public enum InitialBeneficiaryClosureReadiness
{
	Incomplete = 0,
	Ready = 1,
	AlreadyCommitted = 2
}

public enum InitialBeneficiaryClosureResult
{
	Incomplete = 0,
	Committed = 1,
	AlreadyCommitted = 2
}

public sealed record InitialBeneficiaryClosurePrerequisite
{
	public InitialBeneficiaryClosurePrerequisite(
		string identifier,
		bool isComplete)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
		Identifier = identifier;
		IsComplete = isComplete;
	}

	public string Identifier { get; }

	public bool IsComplete { get; }
}

public sealed class InitialBeneficiaryClosureDeferredResult
{
	private InitialBeneficiaryClosureDeferredResult(
		string identifier,
		bool isComplete,
		IReadOnlyCollection<FactionFact> facts)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
		ArgumentNullException.ThrowIfNull(facts);
		if (facts.Any(fact =>
			fact is null || fact.Type != FactionFactType.Beneficiary))
		{
			throw new ArgumentException(
				"Deferred Initial Beneficiary Closure results may establish only Beneficiary facts.",
				nameof(facts));
		}

		if (!isComplete && facts.Count != 0)
		{
			throw new ArgumentException(
				"An incomplete deferred result cannot contain facts.",
				nameof(facts));
		}

		Identifier = identifier;
		IsComplete = isComplete;
		Facts = Array.AsReadOnly(facts.ToArray());
	}

	public string Identifier { get; }

	public bool IsComplete { get; }

	public IReadOnlyList<FactionFact> Facts { get; }

	public static InitialBeneficiaryClosureDeferredResult Pending(
		string identifier) =>
		new(identifier, isComplete: false, []);

	public static InitialBeneficiaryClosureDeferredResult Complete(
		string identifier,
		IReadOnlyCollection<FactionFact> facts) =>
		new(identifier, isComplete: true, facts);
}

public sealed class InitialBeneficiaryClosureRequest
{
	public InitialBeneficiaryClosureRequest(
		FactionFactEffectiveBoundary initialAgentGroupBoundary,
		IReadOnlyCollection<InitialBeneficiaryClosurePrerequisite>
			applicableExceptionPrerequisites,
		IReadOnlyCollection<InitialBeneficiaryClosureDeferredResult>
			deferredResults)
	{
		ArgumentNullException.ThrowIfNull(initialAgentGroupBoundary);
		ArgumentNullException.ThrowIfNull(applicableExceptionPrerequisites);
		ArgumentNullException.ThrowIfNull(deferredResults);
		if (applicableExceptionPrerequisites.Any(item => item is null)
			|| applicableExceptionPrerequisites
				.GroupBy(item => item.Identifier, StringComparer.Ordinal)
				.Any(group => group.Count() > 1))
		{
			throw new ArgumentException(
				"Initial Beneficiary Closure prerequisites must be non-null and uniquely identified.",
				nameof(applicableExceptionPrerequisites));
		}

		if (deferredResults.Any(item => item is null)
			|| deferredResults
				.GroupBy(item => item.Identifier, StringComparer.Ordinal)
				.Any(group => group.Count() > 1))
		{
			throw new ArgumentException(
				"Initial Beneficiary Closure deferred results must be non-null and uniquely identified.",
				nameof(deferredResults));
		}

		InitialAgentGroupBoundary = initialAgentGroupBoundary;
		ApplicableExceptionPrerequisites = Array.AsReadOnly(
			applicableExceptionPrerequisites.ToArray());
		DeferredResults = Array.AsReadOnly(deferredResults.ToArray());
	}

	public FactionFactEffectiveBoundary InitialAgentGroupBoundary { get; }

	public IReadOnlyList<InitialBeneficiaryClosurePrerequisite>
		ApplicableExceptionPrerequisites { get; }

	public IReadOnlyList<InitialBeneficiaryClosureDeferredResult>
		DeferredResults { get; }
}
