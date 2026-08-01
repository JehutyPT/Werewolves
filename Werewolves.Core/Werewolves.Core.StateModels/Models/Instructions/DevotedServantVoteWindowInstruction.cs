using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models.Instructions;

/// <summary>
/// Public Vote window in which the Moderator either continues resolution or
/// records one Player's public Devoted Servant self-reveal.
/// </summary>
public sealed record DevotedServantVoteWindowInstruction
	: ModeratorInstruction
{
	public Guid VoteTargetId { get; }
	public ImmutableHashSet<Guid> SelectablePlayerIds { get; }

	internal DevotedServantVoteWindowInstruction(
		Guid voteTargetId,
		HashSet<Guid> selectablePlayerIds,
		string publicAnnouncement)
		: this(
			voteTargetId,
			selectablePlayerIds?.ToImmutableHashSet()
				?? throw new ArgumentNullException(nameof(selectablePlayerIds)),
			publicAnnouncement,
			privateInstruction: null,
			affectedPlayerIds: [voteTargetId],
			instructionId: default)
	{
	}

	[JsonConstructor]
	internal DevotedServantVoteWindowInstruction(
		Guid voteTargetId,
		ImmutableHashSet<Guid> selectablePlayerIds,
		string? publicAnnouncement,
		string? privateInstruction,
		IReadOnlyList<Guid>? affectedPlayerIds,
		Guid instructionId = default)
		: base(
			publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			instructionId: instructionId,
			semantic:
				ModeratorInstructionSemantic.ResolveDevotedServantVoteWindow)
	{
		if (voteTargetId == Guid.Empty)
		{
			throw new ArgumentException(
				"A Devoted Servant Vote window requires one fixed Vote Target.",
				nameof(voteTargetId));
		}
		ArgumentNullException.ThrowIfNull(selectablePlayerIds);
		if (selectablePlayerIds.Count == 0 ||
			selectablePlayerIds.Contains(voteTargetId) ||
			selectablePlayerIds.Contains(Guid.Empty))
		{
			throw new ArgumentException(
				"Devoted Servant candidates must be non-empty and exclude the Vote Target.",
				nameof(selectablePlayerIds));
		}

		VoteTargetId = voteTargetId;
		SelectablePlayerIds = selectablePlayerIds;
	}

	public ModeratorResponse CreateContinueResponse() => new()
	{
		InstructionId = InstructionId,
		Type = ExpectedInputType.Continue
	};

	public ModeratorResponse CreatePublicSelfRevealResponse(Guid playerId)
	{
		if (!SelectablePlayerIds.Contains(playerId))
		{
			throw new ArgumentException(
				"The public self-reveal must identify one selectable Player.",
				nameof(playerId));
		}

		return new ModeratorResponse
		{
			InstructionId = InstructionId,
			Type = ExpectedInputType.PlayerSelection,
			SelectedPlayerIds = ImmutableHashSet.Create(playerId)
		};
	}
}
