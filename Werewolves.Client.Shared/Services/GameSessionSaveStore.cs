namespace Werewolves.Client.Services;

public interface IGameSessionSaveStore
{
	string? Load();
	void Save(string serializedSession);
	void Clear();
}

public sealed class DisabledGameSessionSaveStore : IGameSessionSaveStore
{
	public static DisabledGameSessionSaveStore Instance { get; } = new();

	private DisabledGameSessionSaveStore()
	{
	}

	public string? Load() => null;

	public void Save(string serializedSession)
	{
	}

	public void Clear()
	{
	}
}
