namespace Werewolves.Core.StateModels.Models;

/// <summary>
/// Defines the constraints for number range constraints,
/// such as player selection counts in moderator instructions.
/// This flexible structure can express all required scenarios.
/// </summary>
public readonly record struct NumberRangeConstraint(int Minimum, int Maximum, bool IsOptional)
{
    /// <summary>
    /// Creates a number interval constraint.
    /// </summary>
    public static NumberRangeConstraint Range(int minimum, int maximum) => new(minimum, maximum, false);

	/// <summary>
	/// Creates a number constraint for an exact count.
	/// </summary>
	public static NumberRangeConstraint Exact(int count) => Range(count, count);

	public static NumberRangeConstraint AtLeast(int count) => Range(count, int.MaxValue);

	/// <summary>
	/// Creates a constraint for an exact count of 1.
	/// </summary>
	public static NumberRangeConstraint Single => Exact(1);

	/// <summary>
	/// Made to support optional ranges. For example, "select either x to y players, or none at all".
	/// </summary>
	/// <param name="minimum"></param>
	/// <param name="maximum"></param>
	/// <returns></returns>
	public static NumberRangeConstraint RangeOptional(int minimum, int maximum) => new(minimum, maximum, true);

    /// <summary>
    /// Creates a constraint that matches exactly the specified number of elements, or allows the sequence to be empty.
    /// </summary>
    /// <remarks>Use this method when you want to accept either an empty sequence or a sequence with an exact
    /// number of elements, but not any other count.</remarks>
    /// <param name="count">The exact number of elements to match if the sequence is not empty. Must be zero or greater.</param>
    public static NumberRangeConstraint ExactOptional(int count) => RangeOptional(count, count);

	/// <summary>
	/// Creates a number constraint for either 0 or 1.
	/// Commonly used for vote outcomes with ties or optional actions.
	/// </summary>
	public static NumberRangeConstraint SingleOptional => ExactOptional(1);

    public static NumberRangeConstraint Any => Range(0, int.MaxValue);

	/// <summary>
	/// Only use this when constraints are expected to fail outside of core game logic, like in user input validation scenarios.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="value"></param>
	/// <param name="countConstraint"></param>
	/// <returns></returns>
	public bool IsValid<T>(ICollection<T> value)
    {
	    try
	    {
			Enforce(value);
			return true;
	    }
	    catch (Exception)
	    {
		    return false;
	    }
	}

	public static void EnforceConstraint<T>(ICollection<T> value, NumberRangeConstraint countConstraint)
    {
        if(value.Count == 0 && countConstraint.IsOptional)
        {
            return; // Optional constraint satisfied
        }

        if (value.Count < countConstraint.Minimum)
        {
            throw new InvalidOperationException($"Selection constraint violation: Minimum of {countConstraint.Minimum} required, but only {value.Count} provided.");
        }

        if (value.Count > countConstraint.Maximum)
        {
            throw new InvalidOperationException($"Selection constraint violation: Maximum of {countConstraint.Maximum} allowed, but {value.Count} provided.");
        }
    }

    public void Enforce<T>(ICollection<T> value)
    {
        EnforceConstraint(value, this);
    }
}
