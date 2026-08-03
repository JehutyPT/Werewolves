using System.Runtime.CompilerServices;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using static Werewolves.Core.GameLogic.Models.InternalMessages.StayInSubPhaseHandlerResult;

namespace Werewolves.Core.GameLogic.Models.StateMachine;

internal sealed record EliminationBatchCommitDecision(
	IReadOnlyCollection<EliminationRequest> Eliminations,
	ModeratorInstruction? ConsequenceInstruction)
{
	internal static EliminationBatchCommitDecision Proceed(
		IReadOnlyCollection<EliminationRequest> eliminations) =>
		new(eliminations, ConsequenceInstruction: null);
}

internal sealed record EliminationCascadePostCommitInteractionResult(
	bool IsComplete,
	ModeratorInstruction? Instruction)
{
	internal static EliminationCascadePostCommitInteractionResult Complete() =>
		new(IsComplete: true, Instruction: null);

	internal static EliminationCascadePostCommitInteractionResult NeedInput(
		ModeratorInstruction instruction) =>
		new(IsComplete: false, instruction);
}

/// <summary>
/// Owns one resolution-scoped Elimination Cascade. The stage can pause for
/// public Role Reveal or a Role-owned extension, but it never navigates.
/// </summary>
internal sealed class EliminationCascadeStage : SubPhaseStage
{
	private enum BatchProgress
	{
		PreReveal,
		RequestReveal,
		AwaitingReveal,
		Commit,
		AwaitingCommitConsequence,
		RequestPostCommitInteraction,
		AwaitingPostCommitInteraction,
		ForcedReactions,
		InteractiveReactions
	}

	private sealed class BatchFrame(
		IReadOnlyCollection<EliminationRequest> eliminations,
		bool isInitial,
		BatchProgress progress)
	{
		internal IReadOnlyCollection<EliminationRequest> PreRevealEliminations { get; } =
			eliminations.ToArray();
		internal IReadOnlyCollection<EliminationRequest> Eliminations { get; set; } =
			eliminations;
		internal bool IsInitial { get; } = isInitial;
		internal BatchProgress Progress { get; set; } = progress;
		internal IReadOnlyCollection<Guid> CommittedPlayerIds { get; set; } = [];
		internal int PreRevealReactionIndex { get; set; }
		internal int ForcedReactionIndex { get; set; }
		internal int InteractiveReactionIndex { get; set; }
	}

	private sealed class CascadeExecution(
		string stageId,
		EliminationCascadeSeed seed)
	{
		private readonly Queue<BatchFrame> _pendingBatches = new();
		private readonly Queue<BatchFrame> _interactiveFrames = new();
		private readonly HashSet<Guid> _admittedPlayerIds = [];

		internal string StageId { get; } = stageId;
		internal string ScopeId { get; } = seed.ScopeId;
		internal int ScopeStartLogIndex { get; } = seed.ScopeStartLogIndex;
		internal BatchFrame? CurrentFrame { get; set; }

		internal void Seed(
			GameSession session,
			ModeratorInstructionSemantic initialRevealSemantic,
			Func<ModeratorInstruction, bool>
				matchesPostCommitInteractionInstruction)
		{
			var initial = Normalize(seed.InitialEliminations);
			foreach (var elimination in initial)
			{
				_admittedPlayerIds.Add(elimination.PlayerId);
			}

			EnqueueFrame(
				session,
				initial,
				isInitial: true,
				initialRevealSemantic,
				matchesPostCommitInteractionInstruction);
		}

		internal bool TryTakeNextFrame(out BatchFrame? frame)
		{
			if (_pendingBatches.TryDequeue(out frame))
			{
				return true;
			}

			return _interactiveFrames.TryDequeue(out frame);
		}

		internal void QueueInteractive(BatchFrame frame) =>
			_interactiveFrames.Enqueue(frame);

		internal IReadOnlyCollection<EliminationRequest>
			AdmitReactionEliminations(
			GameSession session,
			IEnumerable<EliminationRequest> candidates)
		{
			var candidateArray = candidates.ToArray();
			if (candidateArray.Length == 0)
			{
				return [];
			}

			var admitted = new List<EliminationRequest>();
			foreach (var candidate in Normalize(candidateArray))
			{
				var health = session.GetPlayerState(candidate.PlayerId).Health;
				if ((health != PlayerHealth.Alive &&
					 !WasEliminatedInsideScope(session, candidate)) ||
					!_admittedPlayerIds.Add(candidate.PlayerId))
				{
					continue;
				}

				admitted.Add(candidate);
			}

			return admitted;
		}

		internal void QueueReactionBatch(
			GameSession session,
			IReadOnlyCollection<EliminationRequest> eliminations,
			ModeratorInstructionSemantic initialRevealSemantic,
			Func<ModeratorInstruction, bool>
				matchesPostCommitInteractionInstruction)
		{
			if (eliminations.Count == 0)
			{
				return;
			}

			EnqueueFrame(
				session,
				eliminations,
				isInitial: false,
				initialRevealSemantic,
				matchesPostCommitInteractionInstruction);
		}

		private void EnqueueFrame(
			GameSession session,
			IReadOnlyCollection<EliminationRequest> eliminations,
			bool isInitial,
			ModeratorInstructionSemantic initialRevealSemantic,
			Func<ModeratorInstruction, bool>
				matchesPostCommitInteractionInstruction)
		{
			_pendingBatches.Enqueue(CreateFrame(
				session,
				eliminations,
				isInitial,
				initialRevealSemantic,
				matchesPostCommitInteractionInstruction));
		}

		internal void RebuildCompletedPreRevealReactionEliminations(
			GameSession session,
			BatchFrame frame,
			ModeratorInstructionSemantic initialRevealSemantic,
			Func<ModeratorInstruction, bool>
				matchesPostCommitInteractionInstruction)
		{
			if (frame.Progress == BatchProgress.PreReveal)
			{
				return;
			}

			var admittedEliminations = EliminationCascadeRuntimeStore
				.GetReactions(session)
				.Where(binding =>
					binding.Boundary ==
					EliminationCascadeReactionBoundary.PreReveal)
				.Select(binding => FindReactionCompletion(
					session,
					this,
					binding.Reaction.ReactionId,
					frame.PreRevealEliminations))
				.Where(completion => completion != null)
				.SelectMany(completion =>
					completion!.AdmittedEliminations)
				.Select(ToEliminationRequest)
				.ToArray();
			var restoredEliminations = AdmitReactionEliminations(
				session,
				admittedEliminations);

			QueueReactionBatch(
				session,
				restoredEliminations,
				initialRevealSemantic,
				matchesPostCommitInteractionInstruction);
		}

		private BatchFrame CreateFrame(
			GameSession session,
			IReadOnlyCollection<EliminationRequest> eliminations,
			bool isInitial,
			ModeratorInstructionSemantic initialRevealSemantic,
			Func<ModeratorInstruction, bool>
				matchesPostCommitInteractionInstruction)
		{
			var requestedFacts = eliminations
				.Select(ToEliminationFact)
				.ToArray();
			var durableResolution =
				GameSessionQueries.GetEliminationCascadeBatchResolution(
					session,
					ScopeId,
					ScopeStartLogIndex,
					requestedFacts);
			if (durableResolution != null)
			{
				var committedEliminations = durableResolution
					.CommittedEliminations
					.Select(ToEliminationRequest)
					.ToArray();
				return new BatchFrame(
					eliminations,
					isInitial,
					session.PendingModeratorInstruction is { } pending &&
					matchesPostCommitInteractionInstruction(pending)
						? BatchProgress.AwaitingPostCommitInteraction
						: BatchProgress.AwaitingCommitConsequence)
				{
					Eliminations = committedEliminations,
					CommittedPlayerIds = committedEliminations
						.Select(elimination => elimination.PlayerId)
						.ToArray()
				};
			}

			var historical = eliminations.All(elimination =>
				WasEliminatedInsideScope(session, elimination));
			if (historical)
			{
				return new BatchFrame(
					eliminations,
					isInitial,
					BatchProgress.ForcedReactions)
				{
					CommittedPlayerIds = eliminations
						.Select(elimination => elimination.PlayerId)
						.ToArray()
				};
			}

			if (eliminations.Any(elimination =>
				session.GetPlayerState(elimination.PlayerId).Health !=
				PlayerHealth.Alive))
			{
				throw new InvalidOperationException(
					$"Elimination Cascade scope '{ScopeId}' mixed committed and uncommitted members in one concurrent batch.");
			}

			var frame = new BatchFrame(
				eliminations,
				isInitial,
				BatchProgress.PreReveal);
			var revealSemantic = isInitial
				? initialRevealSemantic
				: ModeratorInstructionSemantic.AssignEliminationCascadeRoles;
			if (PendingRevealMatches(
					session,
					eliminations,
					revealSemantic,
					isInitial))
			{
				frame.Progress = BatchProgress.AwaitingReveal;
			}

			return frame;
		}

		private bool WasEliminatedInsideScope(
			GameSession session,
			EliminationRequest elimination) =>
			session.GameHistoryLog
				.Skip(ScopeStartLogIndex + 1)
				.OfType<PlayerEliminatedLogEntry>()
				.Any(entry =>
					entry.PlayerId == elimination.PlayerId &&
					entry.Reason == elimination.Reason);

		private static IReadOnlyCollection<EliminationRequest> Normalize(
			IEnumerable<EliminationRequest> eliminations)
		{
			var normalized = new List<EliminationRequest>();
			foreach (var group in eliminations.GroupBy(
				elimination => elimination.PlayerId))
			{
				var reasons = group
					.Select(elimination => elimination.Reason)
					.Distinct()
					.ToArray();
				if (reasons.Length != 1)
				{
					throw new InvalidOperationException(
						$"Player {group.Key} was admitted to one concurrent Elimination batch with contradictory reasons.");
				}

				normalized.Add(new EliminationRequest(group.Key, reasons[0]));
			}

			if (normalized.Count == 0)
			{
				throw new InvalidOperationException(
					"An Elimination Cascade cannot begin an empty batch.");
			}

			return normalized;
		}
	}

	private static readonly ConditionalWeakTable<GameSession, CascadeExecution>
		Executions = new();

	private readonly Func<GameSession, EliminationCascadeSeed> _createSeed;
	private readonly ModeratorInstructionSemantic _initialRevealSemantic;
	private readonly Func<
		GameSession,
		IReadOnlyCollection<EliminationRequest>,
		string?> _createPublicRevealAnnouncement;
	private readonly Func<
		GameSession,
		IReadOnlyCollection<EliminationRequest>,
		EliminationBatchCommitDecision> _interceptBeforeCommit;
	private readonly Func<
		GameSession,
		IReadOnlyCollection<EliminationRequest>,
		ModeratorInstruction?> _createPostCommitInstruction;
	private readonly Func<
		GameSession,
		IReadOnlyCollection<EliminationRequest>,
		ModeratorResponse?,
		EliminationCascadePostCommitInteractionResult>
		_advancePostCommitInteraction;
	private readonly Func<ModeratorInstruction, bool>
		_matchesPostCommitInteractionInstruction;

	private EliminationCascadeStage(
		Enum id,
		Func<GameSession, EliminationCascadeSeed> createSeed,
		ModeratorInstructionSemantic initialRevealSemantic,
		Func<
			GameSession,
			IReadOnlyCollection<EliminationRequest>,
			string?>? createPublicRevealAnnouncement,
		Func<
			GameSession,
			IReadOnlyCollection<EliminationRequest>,
			EliminationBatchCommitDecision>? interceptBeforeCommit,
			Func<
				GameSession,
				IReadOnlyCollection<EliminationRequest>,
				ModeratorInstruction?>? createPostCommitInstruction,
			Func<
				GameSession,
				IReadOnlyCollection<EliminationRequest>,
				ModeratorResponse?,
				EliminationCascadePostCommitInteractionResult>?
				advancePostCommitInteraction,
			Func<ModeratorInstruction, bool>?
				matchesPostCommitInteractionInstruction)
		: base(id)
	{
		_createSeed = createSeed;
		_initialRevealSemantic = initialRevealSemantic;
		_createPublicRevealAnnouncement =
			createPublicRevealAnnouncement ?? ((_, _) => null);
		_interceptBeforeCommit =
			interceptBeforeCommit ?? ((_, eliminations) =>
				EliminationBatchCommitDecision.Proceed(eliminations));
		_createPostCommitInstruction =
			createPostCommitInstruction ?? ((_, _) => null);
		_advancePostCommitInteraction =
			advancePostCommitInteraction ??
			((_, _, _) =>
				EliminationCascadePostCommitInteractionResult.Complete());
		_matchesPostCommitInteractionInstruction =
			matchesPostCommitInteractionInstruction ?? (_ => false);
	}

	internal static SubPhaseStage CascadeStage<TEnum>(
		TEnum id,
		Func<GameSession, EliminationCascadeSeed> createSeed,
		ModeratorInstructionSemantic initialRevealSemantic,
		Func<
			GameSession,
			IReadOnlyCollection<EliminationRequest>,
			string?>? createPublicRevealAnnouncement = null,
		Func<
			GameSession,
			IReadOnlyCollection<EliminationRequest>,
			EliminationBatchCommitDecision>? interceptBeforeCommit = null,
			Func<
				GameSession,
				IReadOnlyCollection<EliminationRequest>,
				ModeratorInstruction?>? createPostCommitInstruction = null,
			Func<
				GameSession,
				IReadOnlyCollection<EliminationRequest>,
				ModeratorResponse?,
				EliminationCascadePostCommitInteractionResult>?
				advancePostCommitInteraction = null,
			Func<ModeratorInstruction, bool>?
				matchesPostCommitInteractionInstruction = null)
		where TEnum : struct, Enum =>
		new EliminationCascadeStage(
			id,
			createSeed,
			initialRevealSemantic,
				createPublicRevealAnnouncement,
				interceptBeforeCommit,
				createPostCommitInstruction,
				advancePostCommitInteraction,
				matchesPostCommitInteractionInstruction);

	protected override PhaseHandlerResult InnerExecute(
		GameSession session,
		ModeratorResponse input)
	{
		var execution = GetOrCreateExecution(session);
		var reactions = EliminationCascadeRuntimeStore.GetReactions(session);

		while (true)
		{
			if (execution.CurrentFrame == null)
			{
				if (!execution.TryTakeNextFrame(out var nextFrame))
				{
					if (!session.GameHistoryLog
						.OfType<EliminationCascadeCompletedLogEntry>()
						.Any(entry =>
							entry.ScopeId == execution.ScopeId))
					{
						session.RecordEliminationCascadeCompletion(
							execution.ScopeId);
					}
					Executions.Remove(session);
					return CompleteSubPhaseStage(null);
				}

				execution.CurrentFrame = nextFrame;
				execution.RebuildCompletedPreRevealReactionEliminations(
					session,
					nextFrame!,
					_initialRevealSemantic,
					_matchesPostCommitInteractionInstruction);
			}

			var frame = execution.CurrentFrame!;
			switch (frame.Progress)
			{
				case BatchProgress.PreReveal:
				{
					var boundary = AdvanceBoundary(
						session,
						input,
						frame,
						reactions,
						EliminationCascadeReactionBoundary.PreReveal);
					if (boundary != null)
					{
						return boundary;
					}

					frame.Progress = BatchProgress.RequestReveal;
					continue;
				}

				case BatchProgress.RequestReveal:
				{
					var instruction = RequestPublicRoleReveal(
						session,
						frame);
					if (instruction != null)
					{
						frame.Progress = BatchProgress.AwaitingReveal;
						return PauseSubPhaseStage(instruction);
					}

					frame.Progress = BatchProgress.Commit;
					continue;
				}

				case BatchProgress.AwaitingReveal:
					RoleKnowledgeHandlers.RecordPublicRoleReveal(
						session,
						GetPlayersForPublicRoleReveal(
							session,
							frame.Eliminations),
						input);
					frame.Progress = BatchProgress.Commit;
					continue;

				case BatchProgress.Commit:
					{
						var requestedEliminations =
							frame.Eliminations.ToArray();
						var decision = _interceptBeforeCommit(
							session,
							frame.Eliminations);
						frame.Eliminations = decision.Eliminations;
						var committed = new List<Guid>();
						foreach (var elimination in frame.Eliminations)
						{
							if (session.GetPlayerState(elimination.PlayerId).Health !=
								PlayerHealth.Alive)
							{
								continue;
							}

							session.EliminatePlayer(
								elimination.PlayerId,
								elimination.Reason);
							committed.Add(elimination.PlayerId);
						}

						frame.CommittedPlayerIds = committed;
						if (requestedEliminations.Length > 0)
						{
							var committedPlayerIds = committed.ToHashSet();
							session.RecordEliminationCascadeBatchResolution(
								execution.ScopeId,
								requestedEliminations
									.Select(ToEliminationFact)
									.ToArray(),
								frame.Eliminations
									.Where(elimination =>
										committedPlayerIds.Contains(
											elimination.PlayerId))
									.Select(ToEliminationFact)
									.ToArray());
						}
						var postCommitInstruction =
							decision.ConsequenceInstruction ??
							_createPostCommitInstruction(
								session,
								frame.Eliminations);
						if (postCommitInstruction != null)
						{
							frame.Progress =
								BatchProgress.AwaitingCommitConsequence;
							return PauseSubPhaseStage(postCommitInstruction);
						}

						frame.Progress =
							BatchProgress.RequestPostCommitInteraction;
						continue;
					}

				case BatchProgress.AwaitingCommitConsequence:
					if (frame.CommittedPlayerIds.Count == 0)
					{
						execution.CurrentFrame = null;
						continue;
					}

					frame.Progress =
						BatchProgress.RequestPostCommitInteraction;
					continue;

				case BatchProgress.RequestPostCommitInteraction:
					{
						var pause = HandlePostCommitInteraction(
							session,
							execution,
							frame,
							null);
						if (pause != null)
						{
							return pause;
						}

						continue;
					}

				case BatchProgress.AwaitingPostCommitInteraction:
					{
						var pause = HandlePostCommitInteraction(
							session,
							execution,
							frame,
							input);
						if (pause != null)
						{
							return pause;
						}

						continue;
					}

				case BatchProgress.ForcedReactions:
					{
						var beforeCount = frame.ForcedReactionIndex;
						var boundary = AdvanceBoundary(
							session,
							input,
							frame,
							reactions,
							EliminationCascadeReactionBoundary.Forced);
						if (boundary != null)
						{
							return boundary;
						}

						if (frame.ForcedReactionIndex < beforeCount)
						{
							throw new InvalidOperationException(
								"Elimination Cascade forced-reaction progress regressed.");
						}

						frame.Progress = BatchProgress.InteractiveReactions;
						execution.QueueInteractive(frame);
						execution.CurrentFrame = null;
						continue;
					}

				case BatchProgress.InteractiveReactions:
					{
						var boundary = AdvanceBoundary(
							session,
							input,
							frame,
							reactions,
							EliminationCascadeReactionBoundary.Interactive);
						if (boundary != null)
						{
							return boundary;
						}

						execution.CurrentFrame = null;
						continue;
					}

				default:
					throw new InvalidOperationException(
						$"Unknown Elimination Cascade progress '{frame.Progress}'.");
			}
		}
	}

	private PhaseHandlerResult? HandlePostCommitInteraction(
		GameSession session,
		CascadeExecution execution,
		BatchFrame frame,
		ModeratorResponse? input)
	{
		var interaction = _advancePostCommitInteraction(
			session,
			frame.Eliminations,
			input);
		if (interaction.Instruction != null)
		{
			if (interaction.IsComplete)
			{
				throw new InvalidOperationException(
					"A completed Elimination Cascade post-commit interaction returned an instruction.");
			}

			frame.Progress = BatchProgress.AwaitingPostCommitInteraction;
			return PauseSubPhaseStage(interaction.Instruction);
		}

		if (!interaction.IsComplete)
		{
			throw new InvalidOperationException(
				"An Elimination Cascade post-commit interaction paused without an instruction.");
		}

		if (frame.CommittedPlayerIds.Count == 0)
		{
			execution.CurrentFrame = null;
			return null;
		}

		frame.Progress = BatchProgress.ForcedReactions;
		return null;
	}

	private PhaseHandlerResult? AdvanceBoundary(
		GameSession session,
		ModeratorResponse input,
		BatchFrame frame,
		IReadOnlyList<EliminationCascadeReactionBinding> reactions,
		EliminationCascadeReactionBoundary boundary)
	{
		var boundaryReactions = reactions
			.Where(binding => binding.Boundary == boundary)
			.ToArray();
		var nextIndex = GetReactionIndex(frame, boundary);
		var scheduledEliminations = new List<EliminationRequest>();
		var execution = GetExecution(session);
		var triggeringEliminations =
			GetTriggeringEliminations(frame);

		while (nextIndex < boundaryReactions.Length)
		{
			var reaction = boundaryReactions[nextIndex].Reaction;
			var completion = FindReactionCompletion(
				session,
				execution,
				reaction.ReactionId,
				triggeringEliminations);
			IReadOnlyCollection<EliminationRequest> admittedEliminations;
			if (completion != null)
			{
				var completedEliminations = completion.AdmittedEliminations
					.Select(ToEliminationRequest)
					.ToArray();
				var newlyAdmittedEliminations =
					execution.AdmitReactionEliminations(
						session,
						completedEliminations);
				// PreReveal restarts after input and must rebuild its local
				// concurrent wave even when these IDs were admitted before pausing.
				admittedEliminations =
					boundary == EliminationCascadeReactionBoundary.PreReveal
						? completedEliminations
						: newlyAdmittedEliminations;
			}
			else
			{
				var result = reaction.Advance(
					session,
					triggeringEliminations
						.Select(elimination => elimination.PlayerId)
						.ToArray(),
					input);
				if (result.Instruction != null)
				{
					if (boundary ==
						EliminationCascadeReactionBoundary.Forced)
					{
						throw new InvalidOperationException(
							$"Forced Elimination reaction '{reaction.ReactionId}' requested Moderator input.");
					}

					if (result.IsComplete)
					{
						throw new InvalidOperationException(
							$"Elimination reaction '{reaction.ReactionId}' returned an instruction after completing.");
					}

					if (boundary ==
						EliminationCascadeReactionBoundary.PreReveal)
					{
						// Re-scan durable completions after input so earlier and
						// later admissions retain one concurrent next wave.
						SetReactionIndex(frame, boundary, 0);
					}
					return PauseSubPhaseStage(result.Instruction);
				}

				if (!result.IsComplete)
				{
					throw new InvalidOperationException(
						$"Elimination reaction '{reaction.ReactionId}' paused without an instruction.");
				}

				admittedEliminations =
					execution.AdmitReactionEliminations(
						session,
						result.Eliminations);
				if (result.Disposition ==
					EliminationCascadeReactionResultDisposition.PublicCompletion)
				{
					session.RecordEliminationCascadeReactionCompletion(
						execution.ScopeId,
						reaction.ReactionId,
						triggeringEliminations
							.Select(ToEliminationFact)
							.ToArray(),
						admittedEliminations
							.Select(ToEliminationFact)
							.ToArray());
				}
			}

			nextIndex++;
			SetReactionIndex(frame, boundary, nextIndex);
			scheduledEliminations.AddRange(admittedEliminations);

			if (boundary == EliminationCascadeReactionBoundary.Interactive &&
				scheduledEliminations.Count > 0)
			{
					execution.QueueReactionBatch(
						session,
						scheduledEliminations,
						_initialRevealSemantic,
						_matchesPostCommitInteractionInstruction);
				if (nextIndex < boundaryReactions.Length)
				{
					execution.QueueInteractive(frame);
				}
				return null;
			}
		}

		if (scheduledEliminations.Count > 0)
		{
			execution.QueueReactionBatch(
				session,
				scheduledEliminations,
				_initialRevealSemantic,
				_matchesPostCommitInteractionInstruction);
		}

		return null;
	}

	private static IReadOnlyCollection<EliminationRequest>
		GetTriggeringEliminations(BatchFrame frame)
	{
		if (frame.CommittedPlayerIds.Count == 0)
		{
			return frame.Eliminations.ToArray();
		}

		var committedPlayerIds = frame.CommittedPlayerIds.ToHashSet();
		return frame.Eliminations
			.Where(elimination =>
				committedPlayerIds.Contains(elimination.PlayerId))
			.ToArray();
	}

	private static EliminationCascadeReactionCompletedLogEntry?
		FindReactionCompletion(
			GameSession session,
			CascadeExecution execution,
			string reactionId,
			IReadOnlyCollection<EliminationRequest>
				triggeringEliminations)
	{
		var triggerFacts = triggeringEliminations
			.Select(ToEliminationFact)
			.ToArray();
		return session.GameHistoryLog
			.Skip(execution.ScopeStartLogIndex + 1)
			.OfType<EliminationCascadeReactionCompletedLogEntry>()
			.SingleOrDefault(entry =>
				entry.ScopeId == execution.ScopeId &&
				entry.ReactionId == reactionId &&
				entry.TriggeringEliminations.SequenceEqual(triggerFacts));
	}

	private static EliminationCascadeElimination ToEliminationFact(
		EliminationRequest elimination) =>
		new(elimination.PlayerId, elimination.Reason);

	private static EliminationRequest ToEliminationRequest(
		EliminationCascadeElimination elimination) =>
		new(elimination.PlayerId, elimination.Reason);

	private ModeratorInstruction? RequestPublicRoleReveal(
		GameSession session,
		BatchFrame frame)
	{
		var players = GetPlayersForPublicRoleReveal(
			session,
			frame.Eliminations);
		var semantic = frame.IsInitial
			? _initialRevealSemantic
			: ModeratorInstructionSemantic.AssignEliminationCascadeRoles;
		var publicAnnouncement = _createPublicRevealAnnouncement(
			session,
			frame.Eliminations);
		var reveal = RoleKnowledgeHandlers.RequestPublicRoleReveal(
			session,
			players,
			semantic,
			publicAnnouncement);
		if (reveal != null)
		{
			return reveal;
		}

		if (publicAnnouncement == null)
		{
			return null;
		}

		return new ConfirmationInstruction(
			frame.IsInitial
				? ModeratorInstructionSemantic.AnnounceDawnVictims
				: ModeratorInstructionSemantic.AnnounceEliminationCascadeVictims,
			publicAnnouncement: publicAnnouncement,
			affectedPlayerIds: frame.Eliminations
				.Select(elimination => elimination.PlayerId)
				.ToArray());
	}

	private CascadeExecution GetOrCreateExecution(GameSession session)
	{
		if (Executions.TryGetValue(session, out var existing))
		{
			if (existing.StageId != Id)
			{
				throw new InvalidOperationException(
					$"Elimination Cascade '{existing.ScopeId}' is still active while stage '{Id}' tried to begin.");
			}

			return existing;
		}

		var created = new CascadeExecution(Id, _createSeed(session));
		created.Seed(
			session,
			_initialRevealSemantic,
			_matchesPostCommitInteractionInstruction);
		Executions.Add(session, created);
		return created;
	}

	private static CascadeExecution GetExecution(GameSession session) =>
		Executions.TryGetValue(session, out var execution)
			? execution
			: throw new InvalidOperationException(
				"No Elimination Cascade execution is active.");

	internal static string GetActiveScopeId(GameSession session) =>
		GetExecution(session).ScopeId;

	internal static bool IsActiveInteractiveReactionBatch(
		GameSession session,
		string scopeId,
		IReadOnlyList<Guid> triggeringPlayerIds)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
		ArgumentNullException.ThrowIfNull(triggeringPlayerIds);
		return Executions.TryGetValue(session, out var execution) &&
			StringComparer.Ordinal.Equals(execution.ScopeId, scopeId) &&
			execution.CurrentFrame is
			{
				Progress: BatchProgress.InteractiveReactions
			} frame &&
			GetTriggeringEliminations(frame)
				.Select(elimination => elimination.PlayerId)
				.SequenceEqual(triggeringPlayerIds);
	}

	private static int GetReactionIndex(
		BatchFrame frame,
		EliminationCascadeReactionBoundary boundary)
		=> boundary switch
		{
			EliminationCascadeReactionBoundary.PreReveal =>
				frame.PreRevealReactionIndex,
			EliminationCascadeReactionBoundary.Forced =>
				frame.ForcedReactionIndex,
			EliminationCascadeReactionBoundary.Interactive =>
				frame.InteractiveReactionIndex,
			_ => throw new InvalidOperationException(
				$"Unknown Elimination Cascade boundary '{boundary}'.")
		};

	private static void SetReactionIndex(
		BatchFrame frame,
		EliminationCascadeReactionBoundary boundary,
		int value)
	{
		switch (boundary)
		{
			case EliminationCascadeReactionBoundary.PreReveal:
				frame.PreRevealReactionIndex = value;
				break;
			case EliminationCascadeReactionBoundary.Forced:
				frame.ForcedReactionIndex = value;
				break;
			case EliminationCascadeReactionBoundary.Interactive:
				frame.InteractiveReactionIndex = value;
				break;
			default:
				throw new InvalidOperationException(
					$"Unknown Elimination Cascade boundary '{boundary}'.");
		}
	}

	private static IReadOnlyCollection<IPlayer> GetPlayers(
		GameSession session,
		IReadOnlyCollection<EliminationRequest> eliminations) =>
		eliminations
			.Select(elimination => session.GetPlayer(elimination.PlayerId))
			.ToArray();

	private static IReadOnlyCollection<IPlayer> GetPlayersForPublicRoleReveal(
		GameSession session,
		IReadOnlyCollection<EliminationRequest> eliminations) =>
		GetPlayers(session, eliminations)
			.Where(player =>
				!GameSessionQueries.HasDevotedServantRoleTakeForTarget(
					session,
					player.Id))
			.ToArray();

	private static bool PendingRevealMatches(
		GameSession session,
		IReadOnlyCollection<EliminationRequest> eliminations,
		ModeratorInstructionSemantic revealSemantic,
		bool isInitial)
	{
		var pending = session.PendingModeratorInstruction;
		if (pending is not AssignRolesInstruction &&
			pending is not ConfirmationInstruction)
		{
			return false;
		}

		var unrevealedPlayerIds = eliminations
			.Select(elimination => session.GetPlayer(elimination.PlayerId))
			.Where(player => player.State.PubliclyRevealedRole == null)
			.Select(player => player.Id)
			.ToHashSet();
		if (unrevealedPlayerIds.Count > 0)
		{
			return pending.Semantic == revealSemantic &&
				pending.AffectedPlayerIds?.ToHashSet()
					.SetEquals(unrevealedPlayerIds) == true;
		}

		var announcedPlayerIds = eliminations
			.Select(elimination => elimination.PlayerId)
			.ToHashSet();
		var announcementSemantic = isInitial
			? ModeratorInstructionSemantic.AnnounceDawnVictims
			: ModeratorInstructionSemantic.AnnounceEliminationCascadeVictims;
		return pending is ConfirmationInstruction &&
			pending.Semantic == announcementSemantic &&
			pending.AffectedPlayerIds?.ToHashSet()
				.SetEquals(announcedPlayerIds) == true;
	}
}
