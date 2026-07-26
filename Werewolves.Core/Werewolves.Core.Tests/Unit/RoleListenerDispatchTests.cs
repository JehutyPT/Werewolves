using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Roles;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public class RoleListenerDispatchTests
{
	[Fact]
	public void ActiveAdmissionWithoutAFactory_FailsAndNamesTheListener()
	{
		var listenerId = ListenerIdentifier.Listener(MainRoleType.SimpleWerewolf);
		var admissions = new StubRoleAdmissionSource(
			RoleAdmissionKind.Active,
			ListenerFactory: null);

		var act = () => RoleListenerDispatch.Dispatch(
			listenerId,
			admissions,
			(_, factory) => factory(),
			null!,
			null!);

		act.Should()
			.Throw<InvalidOperationException>()
			.WithMessage($"*{listenerId}*factory*");
	}

	[Fact]
	public void PassiveAdmission_DoesNotCreateOrExecuteRoleBehavior()
	{
		var listenerId = ListenerIdentifier.Listener(MainRoleType.SimpleVillager);
		var listener = new CountingListener(listenerId);
		var admissions = new StubRoleAdmissionSource(
			RoleAdmissionKind.Passive,
			ListenerFactory: () => listener);
		var listenerWasCreated = false;

		var result = RoleListenerDispatch.Dispatch(
			listenerId,
			admissions,
			(_, factory) =>
			{
				listenerWasCreated = true;
				return factory();
			},
			null!,
			null!);

		result.Should().BeNull();
		listenerWasCreated.Should().BeFalse();
		listener.ExecuteCount.Should().Be(0);
	}

	[Fact]
	public void UnadmittedPlannedListener_DoesNotCreateOrExecuteRoleBehavior()
	{
		var listenerId = ListenerIdentifier.Listener(MainRoleType.Thief);
		var listener = new CountingListener(listenerId);
		var admissions = new StubRoleAdmissionSource(
			admission: null,
			ListenerFactory: () => listener);
		var listenerWasCreated = false;

		var result = RoleListenerDispatch.Dispatch(
			listenerId,
			admissions,
			(_, factory) =>
			{
				listenerWasCreated = true;
				return factory();
			},
			null!,
			null!);

		result.Should().BeNull();
		listenerWasCreated.Should().BeFalse();
		listener.ExecuteCount.Should().Be(0);
	}

	[Fact]
	public void ActiveAdmissionWithAFactory_ExecutesRoleBehavior()
	{
		var listenerId = ListenerIdentifier.Listener(MainRoleType.SimpleWerewolf);
		var listener = new CountingListener(listenerId);
		var admissions = new StubRoleAdmissionSource(
			RoleAdmissionKind.Active,
			ListenerFactory: () => listener);

		var result = RoleListenerDispatch.Dispatch(
			listenerId,
			admissions,
			(_, factory) => factory(),
			null!,
			null!);

		result.Should().NotBeNull();
		result!.Outcome.Should().Be(HookListenerOutcome.Skip);
		listener.ExecuteCount.Should().Be(1);
	}

	private sealed class StubRoleAdmissionSource(
		RoleAdmissionKind? admission,
		Func<IGameHookListener>? ListenerFactory) : IRoleAdmissionSource
	{
		public RoleAdmissionKind? GetAdmission(ListenerIdentifier listenerId)
			=> admission;

		public bool TryGetListenerFactory(
			ListenerIdentifier listenerId,
			[NotNullWhen(true)] out Func<IGameHookListener>? listenerFactory)
		{
			listenerFactory = ListenerFactory;
			return listenerFactory != null;
		}
	}

	private sealed class CountingListener(ListenerIdentifier id) : IGameHookListener
	{
		public int ExecuteCount { get; private set; }

		public HookListenerActionResult Execute(GameSession session, ModeratorResponse input)
		{
			ExecuteCount++;
			return HookListenerActionResult.Skip();
		}

		public ListenerIdentifier Id { get; } = id;
	}
}
