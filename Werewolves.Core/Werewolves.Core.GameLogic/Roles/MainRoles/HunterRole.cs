using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.EliminationCascades;
using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal sealed class HunterRole :
	RoleHookListener,
	IEliminationCascadeReaction
{
	private static readonly RolePowerDefinition FinalShotPower = new(
		new RolePowerIdentifier(EliminationCascadeReactionIds.HunterFinalShot),
		RolePowerCategory.Reactive);

	private readonly RolePowerAvailabilityGateway _availabilityGateway;

	internal HunterRole(RolePowerAvailabilityGateway availabilityGateway)
	{
		ArgumentNullException.ThrowIfNull(availabilityGateway);
		_availabilityGateway = availabilityGateway;
	}

	internal override string PublicName => GameStrings.HunterRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.Hunter);

	public string ReactionId => EliminationCascadeReactionIds.HunterFinalShot;

	protected override HookListenerActionResult ExecuteCore(
		GameSession session,
		ModeratorResponse input) =>
		HookListenerActionResult.Skip();

	public EliminationCascadeReactionResult Advance(
		GameSession session,
		IReadOnlyCollection<Guid> eliminatedPlayerIds,
		ModeratorResponse input)
	{
		var pendingInstruction = session.PendingModeratorInstruction;
		var hunter = eliminatedPlayerIds
			.Select(session.GetPlayer)
			.SingleOrDefault(player =>
				player.State.CurrentRole == MainRoleType.Hunter &&
				player.State.Health == PlayerHealth.Dead);
		if (hunter == null)
		{
			if (pendingInstruction?.Semantic ==
				ModeratorInstructionSemantic.SelectHunterFinalShotTarget)
			{
				throw new InvalidOperationException(
					"The pending Hunter final shot has no matching committed Hunter elimination.");
			}

			return EliminationCascadeReactionResult.Complete();
		}

		var legalTargetIds = session.GetPlayers()
			.Where(player =>
				player.Id != hunter.Id &&
				player.State.Health == PlayerHealth.Alive)
			.Select(player => player.Id)
			.ToHashSet();
		if (pendingInstruction?.Semantic ==
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget)
		{
			if (pendingInstruction is not SelectPlayersInstruction pendingSelection ||
				pendingSelection.CountConstraint != NumberRangeConstraint.Single ||
				pendingSelection.AffectedPlayerIds?
					.ToHashSet()
					.SetEquals([hunter.Id]) != true ||
				!pendingSelection.SelectablePlayerIds.SetEquals(legalTargetIds))
			{
				throw new InvalidOperationException(
					"The pending Hunter final shot no longer matches its committed elimination context.");
			}

			if (input.InstructionId != pendingSelection.InstructionId ||
				input.SelectedPlayerIds is not { Count: 1 })
			{
				throw new InvalidOperationException(
					"Hunter final shot received an uncorrelated response.");
			}

			var targetId = input.SelectedPlayerIds.Single();
			if (!legalTargetIds.Contains(targetId))
			{
				throw new InvalidOperationException(
					"Hunter final shot must target one other living Player.");
			}

			return EliminationCascadeReactionResult.Complete(
				[
					new EliminationRequest(
						targetId,
						EliminationReason.HunterShot)
				]);
		}

		var availability = _availabilityGateway.Evaluate(
			new RolePowerAttempt(
				hunter,
				MainRoleType.Hunter,
				FinalShotPower,
				RolePowerInstance.CreateNative(
					hunter,
					MainRoleType.Hunter,
					FinalShotPower)));
		if (!availability.AvailabilityResult.IsAvailable ||
			legalTargetIds.Count == 0)
		{
			return EliminationCascadeReactionResult.Complete();
		}

		return EliminationCascadeReactionResult.NeedInput(
			new SelectPlayersInstruction(
				ModeratorInstructionSemantic.SelectHunterFinalShotTarget,
				legalTargetIds,
				NumberRangeConstraint.Single,
				publicAnnouncement:
					GameStrings.HunterFinalShotSelectionInstruction,
				affectedPlayerIds: [hunter.Id]));
	}
}
