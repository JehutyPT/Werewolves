using System.Runtime.CompilerServices;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Models.EliminationCascades;

internal static class EliminationCascadeReactionIds
{
	internal const string RustySwordDiseaseAnnouncement =
		"rusty-sword-disease-announcement";
	internal const string WildChildModelEliminated =
		"wild-child-model-eliminated";
	internal const string LoversHeartbreak = "lovers-heartbreak";
	internal const string HunterFinalShot = "hunter-final-shot";
	internal const string DevotedServantVoteWindow =
		"devoted-servant-vote-window";
}

internal enum EliminationCascadeReactionBoundary
{
	PreReveal,
	Forced,
	Interactive
}

internal readonly record struct EliminationRequest(
	Guid PlayerId,
	EliminationReason Reason);

internal interface IEliminationCascadeReaction
{
	string ReactionId { get; }

	EliminationCascadeReactionResult Advance(
		GameSession session,
		IReadOnlyCollection<Guid> eliminatedPlayerIds,
		ModeratorResponse input);
}

internal sealed record EliminationCascadeReactionRegistration(
	string ReactionId,
	EliminationCascadeReactionBoundary Boundary,
	ListenerIdentifier Listener);

internal sealed record EliminationCascadeReactionBinding(
	IEliminationCascadeReaction Reaction,
	EliminationCascadeReactionBoundary Boundary);

internal sealed record EliminationCascadeReactionResult(
	bool IsComplete,
	ModeratorInstruction? Instruction,
	IReadOnlyCollection<EliminationRequest> Eliminations)
{
	internal static EliminationCascadeReactionResult Complete(
		IReadOnlyCollection<EliminationRequest>? eliminations = null) =>
		new(
			IsComplete: true,
			Instruction: null,
			eliminations ?? []);

	internal static EliminationCascadeReactionResult NeedInput(
		ModeratorInstruction instruction) =>
		new(
			IsComplete: false,
			instruction,
			Eliminations: []);
}

internal sealed record EliminationCascadeSeed(
	string ScopeId,
	int ScopeStartLogIndex,
	IReadOnlyCollection<EliminationRequest> InitialEliminations);

internal static class EliminationCascadeRuntimeStore
{
	private sealed class SessionRuntime(
		IReadOnlyList<EliminationCascadeReactionBinding> reactions)
	{
		internal IReadOnlyList<EliminationCascadeReactionBinding> Reactions
			{ get; } =
			reactions;
	}

	private static readonly ConditionalWeakTable<GameSession, SessionRuntime>
		SessionRuntimes = new();

	internal static void Configure(
		GameSession session,
		IReadOnlyList<EliminationCascadeReactionBinding> reactions)
	{
		var duplicateIds = reactions
			.GroupBy(
				binding => binding.Reaction.ReactionId,
				StringComparer.Ordinal)
			.Where(group => group.Count() > 1)
			.Select(group => group.Key)
			.ToArray();
		if (duplicateIds.Length > 0)
		{
			throw new InvalidOperationException(
				$"Elimination Cascade reaction IDs must be unique: {string.Join(", ", duplicateIds)}.");
		}

		SessionRuntimes.Remove(session);
		SessionRuntimes.Add(
			session,
			new SessionRuntime(reactions.ToArray()));
	}

	internal static IReadOnlyList<EliminationCascadeReactionBinding>
		GetReactions(
		GameSession session) =>
		SessionRuntimes.TryGetValue(session, out var runtime)
			? runtime.Reactions
			: [];
}
