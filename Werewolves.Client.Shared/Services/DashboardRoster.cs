using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;

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
	public const string UnknownRoleLabel = "Desconhecido";
	public const string NoStatusEffectsLabel = "Sem efeitos";

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
					statusEffects.Length == 0 ? NoStatusEffectsLabel : string.Join(", ", statusEffects));
			})
			.ToArray();
	}

	public static string RoleLabel(MainRoleType? role) =>
		role?.GetPublicName() ?? UnknownRoleLabel;

	public static string HealthLabel(PlayerHealth health) => health switch
	{
		PlayerHealth.Alive => "Vivo",
		PlayerHealth.Dead => "Morto",
		_ => "Estado desconhecido"
	};

	public static string StatusEffectLabel(StatusEffectTypes effect) => effect switch
	{
		StatusEffectTypes.ElderProtectionLost => "Proteção perdida",
		StatusEffectTypes.LycanthropyInfection => "Infetado",
		StatusEffectTypes.WildChildChanged => "Transformado",
		StatusEffectTypes.LynchingImmunityUsed => "Imunidade usada",
		StatusEffectTypes.Sheriff => "Capitão",
		StatusEffectTypes.Lovers => "Apaixonado",
		StatusEffectTypes.Charmed => "Encantado",
		StatusEffectTypes.TownCrier => "Pregoeiro",
		StatusEffectTypes.Executioner => "Carrasco",
		_ => "Efeito"
	};
}
