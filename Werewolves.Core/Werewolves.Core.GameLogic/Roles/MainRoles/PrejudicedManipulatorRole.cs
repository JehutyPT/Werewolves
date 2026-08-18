using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal sealed class PrejudicedManipulatorRole
	: DeclaredRoleIdentificationOnlyHookListener
{
	internal override string PublicName =>
		GameStrings.PrejudicedManipulatorRoleName;

	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.PrejudicedManipulator);

	protected override void OnIdentificationAccepted(
		GameSession session,
		ModeratorResponse input)
	{
		var holderId = input.SelectedPlayerIds?.SingleOrDefault()
			?? throw new InvalidOperationException(
				"Prejudiced Manipulator identification requires exactly one Player.");
		if (holderId == Guid.Empty)
		{
			throw new InvalidOperationException(
				"Prejudiced Manipulator identification requires exactly one Player.");
		}

		if (session.GetPlayerState(holderId).PhysicalCharacterCardId is null)
		{
			var card = session.GetModeratorPhysicalCharacterCards()
				.FirstOrDefault(state =>
					state.Zone == PhysicalCharacterCardZone.DealPool &&
					state.OwnerPlayerId is null &&
					state.Card.PrintedRole ==
						MainRoleType.PrejudicedManipulator)
				?.Card ?? throw new InvalidOperationException(
					"No unowned Prejudiced Manipulator Physical Character Card is available.");
			if (!session.TryRecordPhysicalCharacterCardOwnership(
				session.RoleLockIn.Version,
				holderId,
				card.Id))
			{
				throw new InvalidOperationException(
					"The identified Prejudiced Manipulator Physical Character Card could not be bound.");
			}
		}

		base.OnIdentificationAccepted(session, input);
		InitialBeneficiaryClosureRules.TryCommitCurrentSession(session);
	}
}
