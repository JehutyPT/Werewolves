namespace Werewolves.Client.Services;

public sealed class MauiHapticFeedbackService : IHapticFeedbackService
{
    public void Click()
    {
        MainThread.BeginInvokeOnMainThread(PerformClick);
    }

    private static void PerformClick()
    {
        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch (Exception)
        {
            // Haptics are optional; platform failures must not abort game actions.
        }
    }
}
