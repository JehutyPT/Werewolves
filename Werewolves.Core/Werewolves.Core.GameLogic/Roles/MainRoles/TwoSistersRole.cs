using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal sealed class TwoSistersRole : CardinalityRoleHolderNightHookListener
{
	private static readonly RolePowerDefinition Recognition = new(
		new RolePowerIdentifier("two-sisters-recognition"),
		RolePowerCategory.Recognition);

	private static readonly RolePowerDefinition Communication = new(
		new RolePowerIdentifier("two-sisters-communication"),
		RolePowerCategory.Communication);

	internal TwoSistersRole(RolePowerAvailabilityGateway availabilityGateway)
		: base(availabilityGateway) { }

	internal override string PublicName => GameStrings.TwoSistersRoleName;
	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.TwoSisters);
	protected override int InitialRoleHolderCardinality => 2;
	protected override int MinimumCommunicationParticipants => 2;
	protected override RolePowerDefinition RecognitionPower => Recognition;
	protected override RolePowerDefinition CommunicationPower => Communication;
	protected override bool HasCommunicationInterval(int turnNumber) =>
		turnNumber >= 3 && turnNumber % 2 == 1;
}
