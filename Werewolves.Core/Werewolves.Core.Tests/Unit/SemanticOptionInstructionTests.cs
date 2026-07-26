using FluentAssertions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class SemanticOptionInstructionTests
{
	[Fact]
	public void CreateResponse_DuplicateLabels_PreservesSemanticInstructionOrder()
	{
		var instruction = new SelectOptionsInstruction(
			[
				new ModeratorOption("first", "Mesmo rótulo"),
				new ModeratorOption("second", "Mesmo rótulo")
			],
			NumberRangeConstraint.Exact(2),
			privateInstruction: nameof(CreateResponse_DuplicateLabels_PreservesSemanticInstructionOrder));

		var response = instruction.CreateResponse("second", "first");

		instruction.Options.Select(option => option.Id)
			.Should().ContainInOrder("first", "second");
		response.SelectedOptionIds.Should().ContainInOrder("first", "second");
		response.InstructionId.Should().Be(instruction.InstructionId);
	}

	[Fact]
	public void Constructor_DuplicateSemanticIds_IsRejectedEvenWhenLabelsDiffer()
	{
		var act = () => new SelectOptionsInstruction(
			[
				new ModeratorOption("same-id", "Primeiro"),
				new ModeratorOption("same-id", "Segundo")
			],
			NumberRangeConstraint.Single,
			privateInstruction: nameof(Constructor_DuplicateSemanticIds_IsRejectedEvenWhenLabelsDiffer));

		act.Should().Throw<ArgumentException>()
			.WithMessage("*duplicate ID*");
	}

	[Fact]
	public void InstructionAndResponse_CopyCallerOwnedOptionCollections()
	{
		var options = new List<ModeratorOption>
		{
			new("first", "Primeiro"),
			new("second", "Segundo")
		};
		var instruction = new SelectOptionsInstruction(
			options,
			NumberRangeConstraint.Single,
			privateInstruction: nameof(InstructionAndResponse_CopyCallerOwnedOptionCollections));
		options.Clear();
		options.Add(new ModeratorOption("replacement", "Substituto"));

		var selectedIds = new List<string> { "second" };
		var response = instruction.CreateResponse(selectedIds);
		selectedIds.Clear();
		selectedIds.Add("first");

		instruction.Options.Select(option => option.Id)
			.Should().Equal("first", "second");
		response.SelectedOptionIds.Should().Equal("second");
	}
}
