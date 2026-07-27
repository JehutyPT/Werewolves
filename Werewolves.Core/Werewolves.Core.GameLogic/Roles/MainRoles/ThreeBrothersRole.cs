using Werewolves.Core.GameLogic.Models.GameHookListeners;
using Werewolves.Core.GameLogic.RolePowers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Core.GameLogic.Roles.MainRoles;

internal sealed class ThreeBrothersRole : CardinalityRoleHolderNightHookListener
{
	private static readonly RolePowerDefinition Recognition = new(
		new RolePowerIdentifier("three-brothers-recognition"),
		RolePowerCategory.Recognition);

	private static readonly RolePowerDefinition Communication = new(
		new RolePowerIdentifier("three-brothers-communication"),
		RolePowerCategory.Communication);

	internal ThreeBrothersRole(RolePowerAvailabilityGateway availabilityGateway)
		: base(availabilityGateway) { }

	internal override string PublicName => GameStrings.ThreeBrothersRoleName;
	public override ListenerIdentifier Id =>
		ListenerIdentifier.Listener(MainRoleType.ThreeBrothers);
	protected override int InitialRoleHolderCardinality => 3;
	protected override int MinimumCommunicationParticipants => 2;
	protected override RolePowerDefinition RecognitionPower => Recognition;
	protected override RolePowerDefinition CommunicationPower => Communication;
	protected override bool HasCommunicationInterval(int turnNumber) =>
		turnNumber >= 3 && turnNumber % 2 == 1;
}
