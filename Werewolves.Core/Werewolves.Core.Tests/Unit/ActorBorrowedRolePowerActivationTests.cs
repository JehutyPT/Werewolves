using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public sealed class ActorBorrowedRolePowerActivationTests
{
	[Fact]
	public void ValidActivation_PreservesActorSpecificBorrowedLineage()
	{
		var activationId = Guid.NewGuid();
		var actingPlayerId = Guid.NewGuid();
		var selectedCardId = Guid.NewGuid();

		var activation = new ActorBorrowedRolePowerActivation(
			activationId,
			actingPlayerId,
			MainRoleType.Actor,
			selectedCardId,
			MainRoleType.Seer);

		activation.ActivationId.Should().Be(activationId);
		activation.ActingPlayerId.Should().Be(actingPlayerId);
		activation.ActingRole.Should().Be(MainRoleType.Actor);
		activation.SelectedCardId.Should().Be(selectedCardId);
		activation.SourceRole.Should().Be(MainRoleType.Seer);
		activation.Origin.Should().Be(RolePowerInstanceOrigin.Borrowed);
	}
}
