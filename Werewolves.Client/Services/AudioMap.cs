using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Client.Services;

public interface IAudioMap
{
	string? Resolve(SoundEffectsEnum soundEffect);
	AudioMapResult? ResolveFirst(IEnumerable<SoundEffectsEnum> soundEffects);
}

public sealed record AudioMapResult(SoundEffectsEnum SoundEffect, string FileName);

public sealed class AudioMap : IAudioMap
{
	private static readonly IReadOnlyDictionary<SoundEffectsEnum, string> DefaultMappings =
		new Dictionary<SoundEffectsEnum, string>();

	private readonly IReadOnlyDictionary<SoundEffectsEnum, string> _mappings;

	public AudioMap()
		: this(DefaultMappings)
	{
	}

	public AudioMap(IReadOnlyDictionary<SoundEffectsEnum, string> mappings)
	{
		_mappings = mappings;
	}

	public string? Resolve(SoundEffectsEnum soundEffect)
	{
		if (soundEffect == SoundEffectsEnum.None)
		{
			return null;
		}

		return _mappings.TryGetValue(soundEffect, out var fileName) && !string.IsNullOrWhiteSpace(fileName)
			? fileName
			: null;
	}

	public AudioMapResult? ResolveFirst(IEnumerable<SoundEffectsEnum> soundEffects)
	{
		foreach (var soundEffect in soundEffects.Distinct())
		{
			var fileName = Resolve(soundEffect);
			if (fileName is not null)
			{
				return new AudioMapResult(soundEffect, fileName);
			}
		}

		return null;
	}
}
