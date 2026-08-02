using FluentAssertions;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class RoleAdmissionTests
{
	[Fact]
	public void Catalog_RejectsNullAdmissionCollectionDeliberately()
	{
		var act = () => new RoleAdmissionCatalog(null!);

		act.Should()
			.Throw<ArgumentNullException>()
			.WithParameterName("admissions");
	}

	[Fact]
	public void Catalog_RejectsNullAdmissionEntryDeliberately()
	{
		var act = () => new RoleAdmissionCatalog([null!]);

		act.Should()
			.Throw<ArgumentException>()
			.WithMessage("*null entries*")
			.WithParameterName("admissions");
	}

	[Fact]
	public void Catalog_RejectsUndefinedRoleAdmission()
	{
		var malformedAdmission = new RoleAdmission(
			(MainRoleType)999,
			RoleAdmissionKind.Passive,
			ListenerFactory: null);

		var act = () => new RoleAdmissionCatalog([malformedAdmission]);

		act.Should()
			.Throw<InvalidOperationException>()
			.WithMessage("*unknown Role*999*");
	}

	[Fact]
	public void Catalog_RejectsActiveAdmissionWithoutAListenerFactory()
	{
		var malformedAdmission = new RoleAdmission(
			MainRoleType.SimpleWerewolf,
			RoleAdmissionKind.Active,
			ListenerFactory: null);

		var act = () => new RoleAdmissionCatalog([malformedAdmission]);

		act.Should()
			.Throw<InvalidOperationException>()
			.WithMessage("*SimpleWerewolf*factory*");
	}

	[Fact]
	public void Catalog_RejectsPassiveAdmissionWithAListenerFactory()
	{
		var malformedAdmission = new RoleAdmission(
			MainRoleType.SimpleVillager,
			RoleAdmissionKind.Passive,
			ListenerFactory: () => null!);

		var act = () => new RoleAdmissionCatalog([malformedAdmission]);

		act.Should()
			.Throw<InvalidOperationException>()
			.WithMessage("*SimpleVillager*must not declare*factory*");
	}

	[Fact]
	public void Catalog_RejectsAdmissionThatIsNeitherActiveNorPassive()
	{
		var malformedAdmission = new RoleAdmission(
			MainRoleType.SimpleVillager,
			(RoleAdmissionKind)999,
			ListenerFactory: null);

		var act = () => new RoleAdmissionCatalog([malformedAdmission]);

		act.Should()
			.Throw<InvalidOperationException>()
			.WithMessage("*SimpleVillager*admission kind*");
	}

	[Fact]
	public void Catalog_RejectsDuplicateAdmissionForTheSameRole()
	{
		var act = () => new RoleAdmissionCatalog(
		[
			new RoleAdmission(
				MainRoleType.SimpleVillager,
				RoleAdmissionKind.Passive,
				ListenerFactory: null),
			new RoleAdmission(
				MainRoleType.SimpleVillager,
				RoleAdmissionKind.Passive,
				ListenerFactory: null)
		]);

		act.Should()
			.Throw<InvalidOperationException>()
			.WithMessage("*SimpleVillager*more than once*");
	}

	[Fact]
	public void Catalog_DistinguishesActivePassiveAndUnadmittedRoles()
	{
		var catalog = new RoleAdmissionCatalog(
		[
			new RoleAdmission(
				MainRoleType.SimpleWerewolf,
				RoleAdmissionKind.Active,
				ListenerFactory: () => null!),
			new RoleAdmission(
				MainRoleType.SimpleVillager,
				RoleAdmissionKind.Passive,
				ListenerFactory: null)
		]);
		var activeListener = ListenerIdentifier.Listener(MainRoleType.SimpleWerewolf);
		var passiveListener = ListenerIdentifier.Listener(MainRoleType.SimpleVillager);
		var unadmittedListener = ListenerIdentifier.Listener(MainRoleType.Thief);

		catalog.Roles.Should().Equal(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager);
		catalog.GetAdmission(activeListener).Should().Be(RoleAdmissionKind.Active);
		catalog.GetAdmission(passiveListener).Should().Be(RoleAdmissionKind.Passive);
		catalog.GetAdmission(unadmittedListener).Should().BeNull();
		catalog.TryGetListenerFactory(activeListener, out _).Should().BeTrue();
		catalog.TryGetListenerFactory(passiveListener, out _).Should().BeFalse();
	}

	[Fact]
	public void SupportedRoleCatalog_AdmitsTwentyFiveActiveRolesAndThreePassiveRoles()
	{
		var catalog = SupportedRoleCatalog.Admissions;

		catalog.Roles.Should().BeEquivalentTo(
		[
				MainRoleType.SimpleWerewolf,
				MainRoleType.LittleGirl,
				MainRoleType.Seer,
				MainRoleType.BigBadWolf,
				MainRoleType.Defender,
				MainRoleType.WildChild,
			MainRoleType.WolfHound,
				MainRoleType.AccursedWolfFather,
				MainRoleType.Thief,
				MainRoleType.DevotedServant,
				MainRoleType.WhiteWerewolf,
				MainRoleType.Piper,
				MainRoleType.PrejudicedManipulator,
				MainRoleType.Cupid,
				MainRoleType.TwoSisters,
			MainRoleType.ThreeBrothers,
			MainRoleType.Witch,
			MainRoleType.Hunter,
			MainRoleType.StutteringJudge,
			MainRoleType.Scapegoat,
			MainRoleType.VillageIdiot,
			MainRoleType.BearTamer,
			MainRoleType.KnightWithRustySword,
			MainRoleType.Fox,
			MainRoleType.Elder,
			MainRoleType.SimpleVillager,
			MainRoleType.VillagerVillager,
			MainRoleType.Angel
		]);

		foreach (var activeRole in new[]
		         {
				         MainRoleType.SimpleWerewolf,
				         MainRoleType.LittleGirl,
				         MainRoleType.Seer,
				         MainRoleType.BigBadWolf,
				         MainRoleType.Defender,
				         MainRoleType.WildChild,
			         MainRoleType.WolfHound,
			         MainRoleType.AccursedWolfFather,
			         MainRoleType.Thief,
			         MainRoleType.DevotedServant,
			         MainRoleType.WhiteWerewolf,
			         MainRoleType.Piper,
			         MainRoleType.PrejudicedManipulator,
			         MainRoleType.Cupid,
			         MainRoleType.TwoSisters,
			         MainRoleType.ThreeBrothers,
			         MainRoleType.Witch,
			         MainRoleType.Hunter,
			         MainRoleType.StutteringJudge,
			         MainRoleType.Scapegoat,
			         MainRoleType.VillageIdiot,
			         MainRoleType.BearTamer,
			         MainRoleType.KnightWithRustySword,
			         MainRoleType.Fox,
			         MainRoleType.Elder
		         })
		{
			var listenerId = ListenerIdentifier.Listener(activeRole);
			catalog.GetAdmission(listenerId).Should().Be(RoleAdmissionKind.Active);
			catalog.TryGetListenerFactory(listenerId, out _).Should().BeTrue();
		}

		foreach (var passiveRole in new[]
			         {
				         MainRoleType.SimpleVillager,
				         MainRoleType.VillagerVillager,
				         MainRoleType.Angel
			         })
		{
			var listenerId = ListenerIdentifier.Listener(passiveRole);
			catalog.GetAdmission(listenerId).Should().Be(RoleAdmissionKind.Passive);
			catalog.TryGetListenerFactory(listenerId, out _).Should().BeFalse();
		}
	}
}
