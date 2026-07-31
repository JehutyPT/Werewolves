using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

public sealed record PhysicalCharacterCard
{
	public Guid Id { get; }
	public MainRoleType PrintedRole { get; }

	public PhysicalCharacterCard(Guid id, MainRoleType printedRole)
	{
		if (id == Guid.Empty)
		{
			throw new ArgumentException(
				"A Physical Character Card requires a stable instance identity.",
				nameof(id));
		}
		if (!Enum.IsDefined(printedRole))
		{
			throw new ArgumentOutOfRangeException(nameof(printedRole));
		}

		Id = id;
		PrintedRole = printedRole;
	}
}

public enum PhysicalCharacterCardZone
{
	DealPool = 0,
	Offer1 = 1,
	Offer2 = 2,
	PlayerOwned = 3,
	SetAside = 4
}

public sealed record PhysicalCharacterCardState(
	PhysicalCharacterCard Card,
	PhysicalCharacterCardZone Zone,
	Guid? OwnerPlayerId);

public sealed class RoleLockIn
{
	public long Version { get; }
	public int PlayerCount { get; }
	public IReadOnlyList<PhysicalCharacterCard> RoleComposition { get; }
	public IReadOnlyList<PhysicalCharacterCard> DealPool { get; }
	public PhysicalCharacterCard? Offer1 { get; }
	public PhysicalCharacterCard? Offer2 { get; }

	public RoleLockIn(
		long version,
		int playerCount,
		IEnumerable<PhysicalCharacterCard> roleComposition,
		IEnumerable<Guid> dealPoolCardIds,
		Guid? offer1CardId = null,
		Guid? offer2CardId = null)
	{
		ArgumentNullException.ThrowIfNull(roleComposition);
		ArgumentNullException.ThrowIfNull(dealPoolCardIds);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(playerCount);

		var inventory = roleComposition.ToArray();
		if (inventory.Select(card => card.Id).Distinct().Count() != inventory.Length)
		{
			throw new ArgumentException(
				"Physical Character Card instance identities must be unique.",
				nameof(roleComposition));
		}

		var cardsById = inventory.ToDictionary(card => card.Id);
		PhysicalCharacterCard ResolveCard(Guid cardId, string parameterName)
		{
			if (!cardsById.TryGetValue(cardId, out var card))
			{
				throw new ArgumentException(
					"Every locked zone must reference a card in the Role Composition.",
					parameterName);
			}
			return card;
		}

		var dealPool = dealPoolCardIds
			.Select(cardId => ResolveCard(cardId, nameof(dealPoolCardIds)))
			.ToArray();
		var offer1 = offer1CardId is { } offer1Id
			? ResolveCard(offer1Id, nameof(offer1CardId))
			: null;
		var offer2 = offer2CardId is { } offer2Id
			? ResolveCard(offer2Id, nameof(offer2CardId))
			: null;
		var thiefCount = inventory.Count(card => card.PrintedRole == MainRoleType.Thief);
		if (thiefCount == 0)
		{
			if (offer1 is not null)
			{
				throw new ArgumentException(
					"A non-Thief Role Lock-In cannot contain offer cards.",
					nameof(offer1CardId));
			}

			if (offer2 is not null)
			{
				throw new ArgumentException(
					"A non-Thief Role Lock-In cannot contain offer cards.",
					nameof(offer2CardId));
			}

			if (inventory.Length != playerCount || dealPool.Length != playerCount)
			{
				throw new ArgumentException(
					"A non-Thief Role Lock-In must contain exactly one Deal Pool card per Player.",
					nameof(dealPoolCardIds));
			}
		}
		else
		{
			if (thiefCount != 1 ||
				inventory.Length != playerCount + 2 ||
				dealPool.Length != playerCount ||
				dealPool.Count(card => card.PrintedRole == MainRoleType.Thief) != 1)
			{
				throw new ArgumentException(
					"A Thief Role Lock-In must contain PlayerCount + 2 cards and a PlayerCount Deal Pool with exactly one Thief.",
					nameof(dealPoolCardIds));
			}

			if (offer1 is null)
			{
				throw new ArgumentException(
					"A Thief Role Lock-In requires Offer1.",
					nameof(offer1CardId));
			}

			if (offer2 is null)
			{
				throw new ArgumentException(
					"A Thief Role Lock-In requires Offer2.",
					nameof(offer2CardId));
			}

			if (offer1.PrintedRole == MainRoleType.Thief)
			{
				throw new ArgumentException(
					"Thief cannot occupy an offer slot.",
					nameof(offer1CardId));
			}

			if (offer2.PrintedRole == MainRoleType.Thief)
			{
				throw new ArgumentException(
					"Thief cannot occupy an offer slot.",
					nameof(offer2CardId));
			}
		}

		var zonedCardIds = dealPool
			.Select(card => card.Id)
			.Concat(offer1 is null ? [] : [offer1.Id])
			.Concat(offer2 is null ? [] : [offer2.Id])
			.ToArray();
		if (zonedCardIds.Length != inventory.Length ||
			zonedCardIds.Distinct().Count() != inventory.Length)
		{
			throw new ArgumentException(
				"Every Physical Character Card must occupy exactly one locked zone.",
				nameof(dealPoolCardIds));
		}

		if (offer1 is { PrintedRole: MainRoleType.TwoSisters or MainRoleType.ThreeBrothers })
		{
			throw new ArgumentException(
				"Grouped Roles are Deal-Pool-only and cannot be offered.",
				nameof(offer1CardId));
		}

		if (offer2 is { PrintedRole: MainRoleType.TwoSisters or MainRoleType.ThreeBrothers })
		{
			throw new ArgumentException(
				"Grouped Roles are Deal-Pool-only and cannot be offered.",
				nameof(offer2CardId));
		}

		ValidateReachableRoleCounts(dealPool);
		if (thiefCount == 1)
		{
			var retainedDealCards = dealPool
				.Where(card => card.PrintedRole != MainRoleType.Thief)
				.ToArray();
			ValidateReachableRoleCounts(retainedDealCards.Append(offer1!));
			ValidateReachableRoleCounts(retainedDealCards.Append(offer2!));
		}

		Version = version;
		PlayerCount = playerCount;
		RoleComposition = Array.AsReadOnly(inventory);
		DealPool = Array.AsReadOnly(dealPool);
		Offer1 = offer1;
		Offer2 = offer2;

		void ValidateReachableRoleCounts(
			IEnumerable<PhysicalCharacterCard> reachableCards)
		{
			foreach (var roleGroup in reachableCards.GroupBy(card => card.PrintedRole))
			{
				if (!GameSessionConfig.RoleCountConstraints.TryGetValue(
						roleGroup.Key,
						out var constraint) ||
					!constraint.IsValid(roleGroup
						.Select(card => card.PrintedRole)
						.ToArray()))
				{
					throw new ArgumentException(
						$"A reachable Role count for {roleGroup.Key} is invalid.",
						nameof(roleComposition));
				}
			}
		}
	}
}
