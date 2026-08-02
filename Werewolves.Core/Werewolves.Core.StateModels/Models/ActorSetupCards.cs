using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models;

public sealed class ActorSetupCards : IEquatable<ActorSetupCards>
{
	public const int RequiredCount = 3;

	private readonly PhysicalCharacterCard[] _cards;
	private readonly MainRoleType[] _printedRoles;

	public static ActorSetupCards None { get; } = new(
		version: 0,
		Array.Empty<PhysicalCharacterCard>());

	public long Version { get; }

	public IReadOnlyList<PhysicalCharacterCard> Cards { get; }

	public IReadOnlyList<MainRoleType> PrintedRoles { get; }

	public ActorSetupCards(
		long version,
		IEnumerable<PhysicalCharacterCard> cards)
	{
		ArgumentNullException.ThrowIfNull(cards);
		_cards = cards.ToArray();
		if (_cards.Any(card => card is null))
		{
			throw new ArgumentException(
				"Actor Setup Cards cannot contain a null Physical Character Card.",
				nameof(cards));
		}
		if (_cards.Length == 0)
		{
			if (version != 0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(version),
					"An empty Actor Setup artifact must use version zero.");
			}
		}
		else if (version <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(version),
				"A present Actor Setup artifact requires a positive version.");
		}
		if (_cards.Select(card => card.Id).Distinct().Count() != _cards.Length)
		{
			throw new ArgumentException(
				"Actor Setup Card instance identities must be unique.",
				nameof(cards));
		}

		Version = version;
		_printedRoles = _cards.Select(card => card.PrintedRole).ToArray();
		Cards = Array.AsReadOnly(_cards);
		PrintedRoles = Array.AsReadOnly(_printedRoles);
	}

	public ActorSetupCards(IEnumerable<MainRoleType> printedRoles)
		: this(CreateInitialArtifact(printedRoles))
	{
	}

	private ActorSetupCards(
		(long Version, PhysicalCharacterCard[] Cards) initialArtifact)
		: this(initialArtifact.Version, initialArtifact.Cards)
	{
	}

	public static ActorSetupCards CreateFromPrintedRoles(
		long version,
		IEnumerable<MainRoleType> printedRoles)
	{
		ArgumentNullException.ThrowIfNull(printedRoles);
		return new ActorSetupCards(
			version,
			printedRoles.Select(role =>
				new PhysicalCharacterCard(Guid.NewGuid(), role)));
	}

	public bool Equals(ActorSetupCards? other) =>
		other is not null
		&& Version == other.Version
		&& _cards.Length == other._cards.Length
		&& OrderByIdentity(_cards).SequenceEqual(OrderByIdentity(other._cards));

	public override bool Equals(object? obj) =>
		obj is ActorSetupCards other && Equals(other);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add(Version);
		foreach (var card in OrderByIdentity(_cards))
		{
			hash.Add(card);
		}

		return hash.ToHashCode();
	}

	private static (long Version, PhysicalCharacterCard[] Cards) CreateInitialArtifact(
		IEnumerable<MainRoleType> printedRoles)
	{
		ArgumentNullException.ThrowIfNull(printedRoles);
		var roles = printedRoles.ToArray();
		return (
			roles.Length == 0 ? 0 : 1,
			roles.Select(role =>
				new PhysicalCharacterCard(Guid.NewGuid(), role)).ToArray());
	}

	private static IEnumerable<PhysicalCharacterCard> OrderByIdentity(
		IEnumerable<PhysicalCharacterCard> cards) =>
		cards.OrderBy(card => card.Id);
}
