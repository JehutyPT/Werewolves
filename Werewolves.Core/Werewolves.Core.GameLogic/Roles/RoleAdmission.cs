using System.Diagnostics.CodeAnalysis;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;

namespace Werewolves.Core.GameLogic.Roles;

internal enum RoleAdmissionKind
{
	Active,
	Passive
}

internal interface IRoleAdmissionSource
{
	RoleAdmissionKind? GetAdmission(ListenerIdentifier listenerId);

	bool TryGetListenerFactory(
		ListenerIdentifier listenerId,
		[NotNullWhen(true)] out Func<IGameHookListener>? listenerFactory);
}

internal sealed record RoleAdmission(
	MainRoleType Role,
	RoleAdmissionKind Kind,
	Func<IGameHookListener>? ListenerFactory)
{
	internal static RoleAdmission Active(
		MainRoleType role,
		Func<IGameHookListener> listenerFactory)
		=> new(role, RoleAdmissionKind.Active, listenerFactory);

	internal static RoleAdmission Passive(MainRoleType role)
		=> new(role, RoleAdmissionKind.Passive, ListenerFactory: null);
}

internal sealed class RoleAdmissionCatalog : IRoleAdmissionSource
{
	private readonly IReadOnlyDictionary<ListenerIdentifier, RoleAdmission> _admissions;

	internal RoleAdmissionCatalog(IEnumerable<RoleAdmission> admissions)
	{
		ArgumentNullException.ThrowIfNull(admissions);

		var admissionList = admissions.ToArray();
		if (admissionList.Any(admission => admission is null))
		{
			throw new ArgumentException(
				"Role admissions cannot contain null entries.",
				nameof(admissions));
		}

		var admittedRoles = new HashSet<MainRoleType>();

		foreach (var admission in admissionList)
		{
			if (!Enum.IsDefined(admission.Role))
			{
				throw new InvalidOperationException(
					$"Role admission declares unknown Role '{admission.Role}'.");
			}

			if (!admittedRoles.Add(admission.Role))
			{
				throw new InvalidOperationException(
					$"Role admission '{admission.Role}' was declared more than once.");
			}

			if (!Enum.IsDefined(admission.Kind))
			{
				throw new InvalidOperationException(
					$"Role admission '{admission.Role}' declares unknown admission kind '{admission.Kind}'.");
			}

			if (admission is { Kind: RoleAdmissionKind.Active, ListenerFactory: null })
			{
				throw new InvalidOperationException(
					$"Active Role admission '{admission.Role}' must declare a listener factory.");
			}

			if (admission is { Kind: RoleAdmissionKind.Passive, ListenerFactory: not null })
			{
				throw new InvalidOperationException(
					$"Passive Role admission '{admission.Role}' must not declare a listener factory.");
			}
		}

		Roles = admissionList.Select(admission => admission.Role).ToArray();
		_admissions = admissionList.ToDictionary(
			admission => ListenerIdentifier.Listener(admission.Role));
		ListenerFactories = admissionList
			.Where(admission => admission.Kind == RoleAdmissionKind.Active)
			.ToDictionary(
				admission => ListenerIdentifier.Listener(admission.Role),
				admission => admission.ListenerFactory!);
	}

	internal IReadOnlyList<MainRoleType> Roles { get; }

	internal IReadOnlyDictionary<ListenerIdentifier, Func<IGameHookListener>> ListenerFactories { get; }

	internal RoleAdmissionKind? GetAdmission(ListenerIdentifier listenerId)
		=> _admissions.TryGetValue(listenerId, out var admission)
			? admission.Kind
			: null;

	internal bool TryGetListenerFactory(
		ListenerIdentifier listenerId,
		[NotNullWhen(true)] out Func<IGameHookListener>? listenerFactory)
		=> ListenerFactories.TryGetValue(listenerId, out listenerFactory);

	RoleAdmissionKind? IRoleAdmissionSource.GetAdmission(ListenerIdentifier listenerId)
		=> GetAdmission(listenerId);

	bool IRoleAdmissionSource.TryGetListenerFactory(
		ListenerIdentifier listenerId,
		[NotNullWhen(true)] out Func<IGameHookListener>? listenerFactory)
		=> TryGetListenerFactory(listenerId, out listenerFactory);
}
