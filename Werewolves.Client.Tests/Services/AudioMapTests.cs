using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Enums;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class AudioMapTests
{
	[Fact]
	public void DefaultMap_ResolvesEveryCoreSemanticSoundEffect()
	{
		var map = new AudioMap();
		var semanticEffects = Enum
			.GetValues<SoundEffectsEnum>()
			.Where(effect => effect != SoundEffectsEnum.None);

		var unmappedEffects = semanticEffects
			.Where(effect => string.IsNullOrWhiteSpace(map.Resolve(effect)));

		unmappedEffects.Should().BeEmpty();
	}

	[Fact]
	public void ResolveFirst_IgnoresNoneAndReturnsFirstMappedFilename()
	{
		const SoundEffectsEnum mappedEffect = (SoundEffectsEnum)1;
		var map = new AudioMap(new Dictionary<SoundEffectsEnum, string>
		{
			[mappedEffect] = "noite-lobos.mp3"
		});

		var result = map.ResolveFirst([SoundEffectsEnum.None, mappedEffect]);

		result.Should().NotBeNull();
		result!.SoundEffect.Should().Be(mappedEffect);
		result.FileName.Should().Be("noite-lobos.mp3");
	}
}
