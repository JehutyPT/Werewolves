using Bunit;
using FluentAssertions;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Xunit;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

namespace Werewolves.Client.Tests.Components;

public sealed class DashboardRosterViewBunitTests
{
	[Fact]
	public void Roster_DistinguishesLivingZeroVotingPowerFromDeathAndTemporaryRestriction()
	{
		using var context = new ModeratorComponentTestContext();
		var roster = new[]
		{
			CreateEntry(
				PlayerNames.Ana,
				DashboardRoster.HealthLabel(PlayerHealth.Alive),
				isDead: false,
				ClientStrings.Dashboard_VotingPowerLostPermanently),
			CreateEntry(
				PlayerNames.Bruno,
				DashboardRoster.HealthLabel(PlayerHealth.Dead),
				isDead: true,
				votingGuidanceLabel: null),
			CreateEntry(
				PlayerNames.Carla,
				DashboardRoster.HealthLabel(PlayerHealth.Alive),
				isDead: false,
				ClientStrings.Dashboard_VotingRightTemporarilyRestricted)
		};

		var cut = context.RenderModeratorComponent<DashboardRosterView>(
			parameters => parameters.Add(component => component.Roster, roster));

		var livingZeroPower = FindPlayer(cut, PlayerNames.Ana);
		livingZeroPower.TextContent.Should()
			.Contain(ClientStrings.Dashboard_VotingPowerLostPermanently)
			.And.NotContain(ClientStrings.Dashboard_VotingRightTemporarilyRestricted)
			.And.NotContain(DashboardRoster.HealthLabel(PlayerHealth.Dead));

		var dead = FindPlayer(cut, PlayerNames.Bruno);
		dead.TextContent.Should()
			.Contain(DashboardRoster.HealthLabel(PlayerHealth.Dead))
			.And.NotContain(ClientStrings.Dashboard_VotingPowerLostPermanently)
			.And.NotContain(ClientStrings.Dashboard_VotingRightTemporarilyRestricted);

		var temporarilyRestricted = FindPlayer(cut, PlayerNames.Carla);
		temporarilyRestricted.TextContent.Should()
			.Contain(ClientStrings.Dashboard_VotingRightTemporarilyRestricted)
			.And.NotContain(ClientStrings.Dashboard_VotingPowerLostPermanently)
			.And.NotContain(DashboardRoster.HealthLabel(PlayerHealth.Dead));
	}

	private static DashboardRosterEntry CreateEntry(
		string name,
		string healthLabel,
		bool isDead,
		string? votingGuidanceLabel) =>
		new(
			Guid.NewGuid(),
			SeatNumber: 1,
			name,
			DashboardRoster.UnknownRoleLabel,
			IsRoleKnown: false,
			healthLabel,
			isDead,
			StatusEffects: [],
			DashboardRoster.NoStatusEffectsLabel)
		{
			VotingGuidanceLabel = votingGuidanceLabel
		};

	private static AngleSharp.Dom.IElement FindPlayer(
		IRenderedComponent<DashboardRosterView> cut,
		string name) =>
		cut.FindAll("li").Single(player =>
			player.TextContent.Contains(name, StringComparison.CurrentCulture));
}
