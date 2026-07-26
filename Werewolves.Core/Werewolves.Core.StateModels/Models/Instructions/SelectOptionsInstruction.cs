using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models.Instructions;

/// <summary>
/// One machine-stable semantic option and its separately rendered label.
/// </summary>
public sealed record ModeratorOption
{
	public string Id { get; }

	public string Label { get; }

	[JsonConstructor]
	public ModeratorOption(string id, string label)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			throw new ArgumentException("Option ID cannot be empty.", nameof(id));
		}

		if (string.IsNullOrWhiteSpace(label))
		{
			throw new ArgumentException("Option label cannot be empty.", nameof(label));
		}

		Id = id;
		Label = label;
	}
}

/// <summary>
/// Instruction that requires the moderator to select from an ordered list of
/// semantic options.
/// </summary>
public record SelectOptionsInstruction : ModeratorInstruction
{
	/// <summary>
	/// Ordered semantic options. IDs drive behavior; labels are presentation only.
	/// </summary>
	public IReadOnlyList<ModeratorOption> Options { get; }

	public NumberRangeConstraint SelectionRange { get; }

	[JsonConstructor]
	internal SelectOptionsInstruction(
		IReadOnlyList<ModeratorOption> options,
		NumberRangeConstraint selectionRange,
		string? publicAnnouncement = null,
		string? privateInstruction = null,
		IReadOnlyList<Guid>? affectedPlayerIds = null,
		Guid instructionId = default)
		: this(
			ModeratorInstructionSemantic.Unspecified,
			options,
			selectionRange,
			publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			instructionId)
	{
	}

	internal SelectOptionsInstruction(
		ModeratorInstructionSemantic semantic,
		IReadOnlyList<ModeratorOption> options,
		NumberRangeConstraint selectionRange,
		string? publicAnnouncement = null,
		string? privateInstruction = null,
		IReadOnlyList<Guid>? affectedPlayerIds = null,
		Guid instructionId = default)
		: base(
			publicAnnouncement,
			privateInstruction,
			affectedPlayerIds,
			instructionId: instructionId,
			semantic: semantic)
	{
		ArgumentNullException.ThrowIfNull(options);

		if (options.Count == 0)
		{
			throw new ArgumentException("Options cannot be empty.", nameof(options));
		}

		if (options.Any(option => option is null))
		{
			throw new ArgumentException("Options cannot contain null entries.", nameof(options));
		}

		var copiedOptions = options.ToImmutableArray();
		var duplicateId = copiedOptions
			.GroupBy(option => option.Id, StringComparer.Ordinal)
			.FirstOrDefault(group => group.Count() > 1);
		if (duplicateId is not null)
		{
			throw new ArgumentException(
				$"Options contains duplicate ID '{duplicateId.Key}'.",
				nameof(options));
		}

		Options = copiedOptions;
		SelectionRange = selectionRange;
	}

	public ModeratorResponse CreateResponse(params string[] selectedOptionIds)
		=> CreateResponse((IReadOnlyCollection<string>)selectedOptionIds);

	public ModeratorResponse CreateResponse(IReadOnlyCollection<string> selectedOptionIds)
	{
		ArgumentNullException.ThrowIfNull(selectedOptionIds);

		var selectedIdSet = selectedOptionIds.ToImmutableHashSet(StringComparer.Ordinal);
		if (selectedIdSet.Count != selectedOptionIds.Count)
		{
			throw new ArgumentException(
				"Selected option IDs cannot contain duplicates.",
				nameof(selectedOptionIds));
		}

		if (selectedIdSet.Any(selectedId =>
			Options.All(option => !StringComparer.Ordinal.Equals(option.Id, selectedId))))
		{
			throw new ArgumentException("Selected option IDs are not valid.", nameof(selectedOptionIds));
		}

		SelectionRange.Enforce(selectedIdSet);

		var orderedSelectedIds = Options
			.Where(option => selectedIdSet.Contains(option.Id))
			.Select(option => option.Id)
			.ToImmutableArray();

		return new ModeratorResponse
		{
			InstructionId = InstructionId,
			Type = ExpectedInputType.OptionSelection,
			SelectedOptionIds = orderedSelectedIds
		};
	}
}
