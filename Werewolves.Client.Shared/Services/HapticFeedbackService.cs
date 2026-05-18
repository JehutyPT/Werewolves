namespace Werewolves.Client.Services;

public interface IHapticFeedbackService
{
    void Click();
    void LongPress();
}

public static class HapticFeedbackServiceExtensions
{
    public static void TryClick(this IHapticFeedbackService hapticFeedback)
    {
        try
        {
            hapticFeedback.Click();
        }
        catch (Exception)
        {
            // Haptics are optional; platform failures must not abort game actions.
        }
    }

    public static void TryLongPress(this IHapticFeedbackService hapticFeedback)
    {
        try
        {
            hapticFeedback.LongPress();
        }
        catch (Exception)
        {
            // Haptics are optional; platform failures must not abort game actions.
        }
    }
}
