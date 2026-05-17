using CoreGraphics;
using Microsoft.Maui.Controls;

namespace Werewolves.Client;

internal static class MacCatalystPhoneWindow
{
    public const double Width = 360;
    public const double Height = 800;

    public static CGSize Size { get; } = new(Width, Height);

    public static void Apply(Window window)
    {
        window.Width = Width;
        window.Height = Height;
        window.MinimumWidth = Width;
        window.MaximumWidth = Width;
        window.MinimumHeight = Height;
        window.MaximumHeight = Height;
    }
}
