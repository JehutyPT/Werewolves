using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;

namespace Werewolves.Core.StateModels.Models.Simulation;

public enum ThiefOfferBranch
{
	Offer1 = 1,
	Offer2 = 2,
	Decline = 3
}

public sealed class ThiefOfferBranchPolicy : IEquatable<ThiefOfferBranchPolicy>
{
	private readonly ThiefOfferBranch[] _branches;

	public IReadOnlyList<ThiefOfferBranch> Branches { get; }

	private ThiefOfferBranchPolicy(IEnumerable<ThiefOfferBranch> branches)
	{
		_branches = branches.ToArray();
		if (_branches.Length == 0 ||
		    _branches.Any(branch => !Enum.IsDefined(branch)) ||
		    _branches.Distinct().Count() != _branches.Length)
		{
			throw new ArgumentException(
				"A Thief offer branch policy requires distinct known branches.",
				nameof(branches));
		}

		Branches = Array.AsReadOnly(_branches);
	}

	public static ThiefOfferBranchPolicy Create(
		MainRoleType offer1Role,
		MainRoleType offer2Role)
	{
		if (!Enum.IsDefined(offer1Role) || !Enum.IsDefined(offer2Role))
		{
			throw new ArgumentOutOfRangeException(nameof(offer1Role));
		}

		var branches = new List<ThiefOfferBranch>
		{
			ThiefOfferBranch.Offer1
		};
		if (offer2Role != offer1Role)
		{
			branches.Add(ThiefOfferBranch.Offer2);
		}
		if (!offer1Role.IsHardAlignedWerewolf() ||
		    !offer2Role.IsHardAlignedWerewolf())
		{
			branches.Add(ThiefOfferBranch.Decline);
		}

		return new ThiefOfferBranchPolicy(branches);
	}

	public ThiefOfferBranch GetBranch(long runNumber)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(runNumber);
		return _branches[(int)(runNumber % _branches.Length)];
	}

	internal static bool TryParse(
		string value,
		MainRoleType offer1Role,
		MainRoleType offer2Role,
		out ThiefOfferBranchPolicy policy)
	{
		policy = null!;
		if (!value.StartsWith("thief=[", StringComparison.Ordinal) ||
		    !value.EndsWith(']'))
		{
			return false;
		}

		var expected = Create(offer1Role, offer2Role);
		if (!string.Equals(
				value["thief=[".Length..^1],
				expected.ToString(),
				StringComparison.Ordinal))
		{
			return false;
		}

		policy = expected;
		return true;
	}

	public override string ToString() => string.Join(',', _branches);

	public bool Equals(ThiefOfferBranchPolicy? other) =>
		other is not null && _branches.SequenceEqual(other._branches);

	public override bool Equals(object? obj) =>
		obj is ThiefOfferBranchPolicy other && Equals(other);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		foreach (var branch in _branches)
		{
			hash.Add(branch);
		}

		return hash.ToHashCode();
	}
}
