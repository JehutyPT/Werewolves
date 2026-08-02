namespace Werewolves.Core.StateModels.Models;

public sealed class GameSessionPlayerConfig : IEquatable<GameSessionPlayerConfig>
{
	public Guid Id { get; }
	public string Name { get; }

	public GameSessionPlayerConfig(Guid id, string name)
	{
		if (id == Guid.Empty)
		{
			throw new ArgumentException(
				"A Game Session Player configuration requires a stable Player identity.",
				nameof(id));
		}
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException(
				"A Game Session Player configuration requires a display name.",
				nameof(name));
		}

		Id = id;
		Name = name;
	}

	public bool Equals(GameSessionPlayerConfig? other) =>
		other is not null && Id == other.Id;

	public override bool Equals(object? obj) =>
		obj is GameSessionPlayerConfig other && Equals(other);

	public override int GetHashCode() => Id.GetHashCode();
}
