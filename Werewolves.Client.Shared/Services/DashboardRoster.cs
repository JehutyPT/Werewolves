using Werewolves.Client.Resources;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Client.Services;

public enum DashboardRoleVisibility
{
	Unknown,
	ModeratorPrivate,
	Public
}

public sealed record DashboardRosterEntry(
	Guid PlayerId,
	int SeatNumber,
	string Name,
	string RoleLabel,
	bool IsRoleKnown,
	string HealthLabel,
	bool IsDead,
	IReadOnlyList<string> StatusEffects,
	string StatusEffectsLabel)
{
	public DashboardRoleVisibility RoleVisibility { get; init; } = DashboardRoleVisibility.Unknown;
	public string RoleVisibilityLabel { get; init; } =
		DashboardRoster.RoleVisibilityLabel(DashboardRoleVisibility.Unknown);
	public string? VotingGuidanceLabel { get; init; }
}

public static class DashboardRoster
{
	public static string UnknownRoleLabel => GameStrings.DefaultLogValue;
	public static string NoStatusEffectsLabel => ClientStrings.Dashboard_NoStatusEffects;

	public static IReadOnlyList<DashboardRosterEntry> FromSession(IGameSession? session)
	{
		if (session is null)
		{
			return [];
		}

		return session.GetPlayers()
			.Select((player, index) =>
			{
				var statusEffects = player.State
					.GetActiveStatusEffects()
					.Select(StatusEffectLabel)
					.ToArray();
				var roleVisibility = ResolveRoleVisibility(player.State);
				var visibleToModeratorRole =
					player.State.ModeratorKnownRole ??
					player.State.PubliclyRevealedRole;

				return new DashboardRosterEntry(
					player.Id,
					index + 1,
					player.Name,
					RoleLabel(visibleToModeratorRole),
					visibleToModeratorRole is not null,
					HealthLabel(player.State.Health),
					player.State.Health == PlayerHealth.Dead,
					statusEffects,
					statusEffects.Length == 0
						? NoStatusEffectsLabel
						: string.Join(ClientStrings.Common_ListSeparator, statusEffects))
					{
						RoleVisibility = roleVisibility,
						RoleVisibilityLabel = RoleVisibilityLabel(roleVisibility),
						VotingGuidanceLabel = VotingGuidanceLabel(player.State)
					};
			})
			.ToArray();
	}

	public static string RoleLabel(MainRoleType? role) =>
		role?.GetPublicName() ?? UnknownRoleLabel;

	public static string RoleVisibilityLabel(DashboardRoleVisibility visibility) => visibility switch
	{
		DashboardRoleVisibility.Unknown => ClientStrings.Dashboard_RoleKnowledgeUnknown,
		DashboardRoleVisibility.ModeratorPrivate => ClientStrings.Dashboard_RoleKnowledgePrivate,
		DashboardRoleVisibility.Public => ClientStrings.Dashboard_RoleKnowledgePublic,
		_ => ClientStrings.Dashboard_RoleKnowledgeUnknown
	};

	public static string HealthLabel(PlayerHealth health) => health switch
	{
		PlayerHealth.Alive => ClientStrings.Dashboard_HealthAlive,
		PlayerHealth.Dead => ClientStrings.Dashboard_HealthDead,
		_ => ClientStrings.Dashboard_HealthUnknown
	};

	public static string? VotingGuidanceLabel(IPlayerState state)
	{
		ArgumentNullException.ThrowIfNull(state);

		if (state.Health == PlayerHealth.Dead)
		{
			return null;
		}

		if (state.DurableVotingPower == 0)
		{
			return ClientStrings.Dashboard_VotingPowerLostPermanently;
		}

		return state.HasVotingRight
			? null
			: ClientStrings.Dashboard_VotingRightTemporarilyRestricted;
	}

	public static string StatusEffectLabel(StatusEffectTypes effect) => effect switch
	{
		StatusEffectTypes.ElderProtectionLost => ClientStrings.StatusEffect_ElderProtectionLost,
		StatusEffectTypes.LycanthropyInfection => ClientStrings.StatusEffect_LycanthropyInfection,
		StatusEffectTypes.WildChildChanged => ClientStrings.StatusEffect_WildChildChanged,
		StatusEffectTypes.Sheriff => ClientStrings.StatusEffect_Sheriff,
		StatusEffectTypes.Lovers => ClientStrings.StatusEffect_Lovers,
		StatusEffectTypes.Charmed => ClientStrings.StatusEffect_Charmed,
		StatusEffectTypes.TownCrier => ClientStrings.StatusEffect_TownCrier,
		StatusEffectTypes.Executioner => ClientStrings.StatusEffect_Executioner,
		_ => ClientStrings.StatusEffect_Fallback
	};

	private static DashboardRoleVisibility ResolveRoleVisibility(IPlayerState state)
	{
		if (state.ModeratorKnownRole is { } moderatorKnownRole)
		{
			return state.PubliclyRevealedRole == moderatorKnownRole
				? DashboardRoleVisibility.Public
				: DashboardRoleVisibility.ModeratorPrivate;
		}

		return state.PubliclyRevealedRole is not null
			? DashboardRoleVisibility.Public
			: DashboardRoleVisibility.Unknown;
	}
}
