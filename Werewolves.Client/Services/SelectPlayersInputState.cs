using Werewolves.Core.StateModels.Models;

namespace Werewolves.Client.Services;

/// <summary>
/// Manages the selection state for the SelectPlayersView component.
/// Filters the roster to selectable players, orders by seat number,
/// tracks selected player IDs, and enforces the count constraint.
/// </summary>
public sealed class SelectPlayersInputState
{
	private readonly HashSet<Guid> _selectedPlayerIds = [];
	private readonly NumberRangeConstraint _constraint;

	public SelectPlayersInputState(
		IReadOnlyList<DashboardRosterEntry> roster,
		IReadOnlySet<Guid> selectablePlayerIds,
		NumberRangeConstraint constraint)
	{
		_constraint = constraint;

		SelectablePlayers = roster
			.Where(entry => selectablePlayerIds.Contains(entry.PlayerId))
			.OrderBy(entry => entry.SeatNumber)
			.ToList();
	}

	/// <summary>
	/// The filtered and ordered list of selectable players.
	/// </summary>
	public IReadOnlyList<DashboardRosterEntry> SelectablePlayers { get; }

	/// <summary>
	/// The currently selected player IDs.
	/// </summary>
	public IReadOnlySet<Guid> SelectedPlayerIds => _selectedPlayerIds;

	/// <summary>
	/// Whether the current selection satisfies the count constraint and can be submitted.
	/// </summary>
	public bool CanSubmit => _constraint.IsValid(_selectedPlayerIds.ToList());

	/// <summary>
	/// Whether the constraint allows exactly one selection (single-select mode).
	/// In single-select mode, tapping a new player replaces the previous selection.
	/// </summary>
	public bool IsSingleSelect => _constraint.Maximum == 1;

	/// <summary>
	/// Toggles selection of a player. In single-select mode, selecting a new player
	/// replaces the previous selection. In multi-select mode, selecting when already
	/// at the maximum count is rejected (no-op).
	/// </summary>
	/// <param name="playerId">The player ID to toggle.</param>
	/// <returns>True if the selection changed, false otherwise.</returns>
	public bool ToggleSelection(Guid playerId)
	{
		if (_selectedPlayerIds.Contains(playerId))
		{
			_selectedPlayerIds.Remove(playerId);
			return true;
		}

		if (IsSingleSelect)
		{
			_selectedPlayerIds.Clear();
			_selectedPlayerIds.Add(playerId);
			return true;
		}

		if (_selectedPlayerIds.Count >= _constraint.Maximum)
		{
			return false;
		}

		_selectedPlayerIds.Add(playerId);
		return true;
	}

	/// <summary>
	/// Returns the current selection as a HashSet for creating the response.
	/// </summary>
	public HashSet<Guid> GetSelection() => new(_selectedPlayerIds);

	/// <summary>
	/// Whether a specific player is currently selected.
	/// </summary>
	public bool IsSelected(Guid playerId) => _selectedPlayerIds.Contains(playerId);
}
