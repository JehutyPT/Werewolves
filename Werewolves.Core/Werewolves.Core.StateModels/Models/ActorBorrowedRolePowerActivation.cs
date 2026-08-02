using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;

namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Actor-specific lineage for one borrowed Role Power activation.
/// It deliberately carries no concrete source-power or resource identity;
/// those identities belong to the concrete borrowed power when one is used.
/// </summary>
public sealed record ActorBorrowedRolePowerActivation
{
	public Guid ActivationId { get; }
	public Guid ActingPlayerId { get; }
	public MainRoleType ActingRole { get; }
	public Guid SelectedCardId { get; }
	public MainRoleType SourceRole { get; }
	public RolePowerInstanceOrigin Origin => RolePowerInstanceOrigin.Borrowed;

	public ActorBorrowedRolePowerActivation(
		Guid activationId,
		Guid actingPlayerId,
		MainRoleType actingRole,
		Guid selectedCardId,
		MainRoleType sourceRole)
	{
		if (activationId == Guid.Empty)
		{
			throw new ArgumentException(
				"An Actor borrowed Role Power activation requires a stable identity.",
				nameof(activationId));
		}
		if (actingPlayerId == Guid.Empty)
		{
			throw new ArgumentException(
				"An Actor borrowed Role Power activation requires an acting Player.",
				nameof(actingPlayerId));
		}
		if (actingRole != MainRoleType.Actor)
		{
			throw new ArgumentException(
				"An Actor borrowed Role Power activation must retain the Actor Role.",
				nameof(actingRole));
		}
		if (selectedCardId == Guid.Empty)
		{
			throw new ArgumentException(
				"An Actor borrowed Role Power activation requires a selected setup card.",
				nameof(selectedCardId));
		}
		if (!sourceRole.IsEligibleActorSetupCard())
		{
			throw new ArgumentException(
				"An Actor borrowed Role Power activation requires an eligible source Role.",
				nameof(sourceRole));
		}

		ActivationId = activationId;
		ActingPlayerId = actingPlayerId;
		ActingRole = actingRole;
		SelectedCardId = selectedCardId;
		SourceRole = sourceRole;
	}
}
