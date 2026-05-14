using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Xunit;

namespace Werewolves.Client.Tests.Helpers;

public class LobbySetupMetadataFixtureTests
{
	[Fact]
	public void ForRoles_CreatesRoleMetadataUsingProductionRules()
	{
		var metadata = LobbySetupMetadataFixture.ForRoles(
			MainRoleType.Seer,
			MainRoleType.TwoSisters);

		var seer = metadata.AvailableRoles.Single(role => role.Role == MainRoleType.Seer);
		seer.DisplayName.Should().Be(MainRoleType.Seer.GetPublicName());
		seer.Group.Should().Be(MainRoleType.Seer.GetRoleGroup());
		seer.GroupDisplayName.Should().Be(MainRoleType.Seer.GetRoleGroup().GetDisplayName());
		seer.CountConstraint.Should().Be(GameSessionConfig.RoleCountConstraints[MainRoleType.Seer]);

		var twoSisters = metadata.AvailableRoles.Single(role => role.Role == MainRoleType.TwoSisters);
		twoSisters.CountConstraint.Should().Be(GameSessionConfig.RoleCountConstraints[MainRoleType.TwoSisters]);
	}
}
