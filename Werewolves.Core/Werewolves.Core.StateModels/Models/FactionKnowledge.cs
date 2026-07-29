using System.Text.Json.Serialization;
using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

public sealed record FactionBeneficiaryKnowledge
{
	[JsonConstructor]
	public FactionBeneficiaryKnowledge(bool isKnown, Faction? faction)
	{
		if (isKnown != faction.HasValue
			|| faction.HasValue && !Enum.IsDefined(faction.Value))
		{
			throw new ArgumentException(
				"Faction Beneficiary knowledge is structurally invalid.",
				nameof(faction));
		}

		IsKnown = isKnown;
		Faction = faction;
	}

	public static FactionBeneficiaryKnowledge Unknown { get; } =
		new(isKnown: false, faction: null);

	public bool IsKnown { get; }

	public Faction? Faction { get; }

	public static FactionBeneficiaryKnowledge Known(Faction faction)
	{
		if (!Enum.IsDefined(faction))
		{
			throw new ArgumentOutOfRangeException(nameof(faction));
		}

		return new FactionBeneficiaryKnowledge(isKnown: true, faction);
	}
}

public enum FactionAgentKnowledge
{
	Unknown = 0,
	KnownNonAgent = 1,
	KnownAgent = 2
}
