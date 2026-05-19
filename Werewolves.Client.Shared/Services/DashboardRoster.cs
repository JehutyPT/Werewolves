using Werewolves.Client.Resources;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Client.Services;

public sealed record DashboardRosterEntry(
	Guid PlayerId,
	int SeatNumber,
	string Name,
	string RoleLabel,
	bool IsRoleKnown,
	string HealthLabel,
	bool IsDead,
	IReadOnlyList<string> StatusEffects,
	string StatusEffectsLabel);

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

				return new DashboardRosterEntry(
					player.Id,
					index + 1,
					player.Name,
					RoleLabel(player.State.MainRole),
					player.State.MainRole is not null,
					HealthLabel(player.State.Health),
					player.State.Health == PlayerHealth.Dead,
					statusEffects,
					statusEffects.Length == 0
						? NoStatusEffectsLabel
						: string.Join(ClientStrings.Common_ListSeparator, statusEffects));
			})
			.ToArray();
	}

	public static string RoleLabel(MainRoleType? role) =>
		role?.GetPublicName() ?? UnknownRoleLabel;

	public static string HealthLabel(PlayerHealth health) => health switch
	{
		PlayerHealth.Alive => ClientStrings.Dashboard_HealthAlive,
		PlayerHealth.Dead => ClientStrings.Dashboard_HealthDead,
		_ => ClientStrings.Dashboard_HealthUnknown
	};

	public static string StatusEffectLabel(StatusEffectTypes effect) => effect switch
	{
		StatusEffectTypes.ElderProtectionLost => ClientStrings.StatusEffect_ElderProtectionLost,
		StatusEffectTypes.LycanthropyInfection => ClientStrings.StatusEffect_LycanthropyInfection,
		StatusEffectTypes.WildChildChanged => ClientStrings.StatusEffect_WildChildChanged,
		StatusEffectTypes.LynchingImmunityUsed => ClientStrings.StatusEffect_LynchingImmunityUsed,
		StatusEffectTypes.Sheriff => ClientStrings.StatusEffect_Sheriff,
		StatusEffectTypes.Lovers => ClientStrings.StatusEffect_Lovers,
		StatusEffectTypes.Charmed => ClientStrings.StatusEffect_Charmed,
		StatusEffectTypes.TownCrier => ClientStrings.StatusEffect_TownCrier,
		StatusEffectTypes.Executioner => ClientStrings.StatusEffect_Executioner,
		_ => ClientStrings.StatusEffect_Fallback
	};
}
