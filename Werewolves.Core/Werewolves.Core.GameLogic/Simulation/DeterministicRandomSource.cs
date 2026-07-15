using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

internal sealed class DeterministicRandomSource
{
	private const ulong Increment = 0x9E3779B97F4A7C15UL;
	private ulong _state;

	internal RunSeedMaterial Material { get; }

	internal DeterministicRandomSource(RunSeedMaterial material)
	{
		Material = material ?? throw new ArgumentNullException(nameof(material));
		_state = DeriveNumericSeed(Material);
	}

	internal static ulong DeriveNumericSeed(RunSeedMaterial material)
	{
		ArgumentNullException.ThrowIfNull(material);
		var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString()));
		return BinaryPrimitives.ReadUInt64BigEndian(digest);
	}

	internal ulong NextUInt64(ulong exclusiveUpperBound)
	{
		ArgumentOutOfRangeException.ThrowIfZero(exclusiveUpperBound);
		var threshold = unchecked(0UL - exclusiveUpperBound) % exclusiveUpperBound;
		while (true)
		{
			var value = NextUInt64();
			if (value >= threshold)
			{
				return value % exclusiveUpperBound;
			}
		}
	}

	internal void Shuffle<T>(IList<T> values)
	{
		ArgumentNullException.ThrowIfNull(values);
		for (var index = values.Count - 1; index > 0; index--)
		{
			var swapIndex = (int)NextUInt64((ulong)index + 1);
			(values[index], values[swapIndex]) = (values[swapIndex], values[index]);
		}
	}

	private ulong NextUInt64()
	{
		_state = unchecked(_state + Increment);
		var value = _state;
		value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
		value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
		return value ^ (value >> 31);
	}
}
