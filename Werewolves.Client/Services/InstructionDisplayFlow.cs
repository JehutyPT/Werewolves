using Werewolves.Core.StateModels.Models;

namespace Werewolves.Client.Services;

public sealed class InstructionDisplayFlow
{
	private ModeratorInstruction? _instruction;
	private bool _isShowingPrivateInstruction;

	public InstructionDisplayFlow(ModeratorInstruction? instruction = null)
	{
		SetInstruction(instruction);
	}

	public ModeratorInstruction? Instruction => _instruction;
	public bool HasInstruction => _instruction is not null;
	public bool HasTwoPartInstruction => HasPublicAnnouncement(_instruction) && HasPrivateInstruction(_instruction);
	public bool IsShowingInput => HasInstruction && (!HasTwoPartInstruction || _isShowingPrivateInstruction);

	public string? CurrentText
	{
		get
		{
			if (_instruction is null)
			{
				return null;
			}

			if (HasTwoPartInstruction && !_isShowingPrivateInstruction)
			{
				return _instruction.PublicAnnouncement;
			}

			return HasPrivateInstruction(_instruction)
				? _instruction.PrivateInstruction
				: _instruction.PublicAnnouncement;
		}
	}

	public void SetInstruction(ModeratorInstruction? instruction)
	{
		if (ReferenceEquals(_instruction, instruction))
		{
			return;
		}

		_instruction = instruction;
		_isShowingPrivateInstruction = false;
	}

	public void Advance()
	{
		if (HasTwoPartInstruction)
		{
			_isShowingPrivateInstruction = true;
		}
	}

	private static bool HasPublicAnnouncement(ModeratorInstruction? instruction) =>
		!string.IsNullOrWhiteSpace(instruction?.PublicAnnouncement);

	private static bool HasPrivateInstruction(ModeratorInstruction? instruction) =>
		!string.IsNullOrWhiteSpace(instruction?.PrivateInstruction);
}
