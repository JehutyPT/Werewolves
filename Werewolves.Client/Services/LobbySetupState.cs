using Werewolves.Core.StateModels.Models;

namespace Werewolves.Client.Services;

public enum AddPlayerResult { Success, EmptyName, DuplicateName }

public class LobbySetupState
{
	private readonly List<string> _playerNames = new();

	public IReadOnlyList<string> PlayerNames => _playerNames;

	public bool HasPlayerConfigIssues(out List<GameConfigValidationError> issues)
	{
		return GameSessionConfig.TryGetPlayerConfigIssues(_playerNames, out issues);
	}

	public bool CanMovePlayerUp(int index) => index > 0 && index < _playerNames.Count;

	public bool CanMovePlayerDown(int index) => index >= 0 && index < _playerNames.Count - 1;

	public AddPlayerResult AddPlayer(string playerName)
	{
		var normalizedName = playerName.Trim();
		if (normalizedName.Length == 0)
		{
			return AddPlayerResult.EmptyName;
		}

		if (_playerNames.Any(n => string.Equals(n, normalizedName, StringComparison.OrdinalIgnoreCase)))
		{
			return AddPlayerResult.DuplicateName;
		}

		_playerNames.Add(normalizedName);
		return AddPlayerResult.Success;
	}

	public bool RemovePlayerAt(int index)
	{
		if (index < 0 || index >= _playerNames.Count)
		{
			return false;
		}

		_playerNames.RemoveAt(index);
		return true;
	}

	public bool MovePlayerUp(int index)
	{
		if (!CanMovePlayerUp(index))
		{
			return false;
		}

		(_playerNames[index - 1], _playerNames[index]) = (_playerNames[index], _playerNames[index - 1]);
		return true;
	}

	public bool MovePlayerDown(int index)
	{
		if (!CanMovePlayerDown(index))
		{
			return false;
		}

		(_playerNames[index], _playerNames[index + 1]) = (_playerNames[index + 1], _playerNames[index]);
		return true;
	}
}
