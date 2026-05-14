using System.Globalization;
using Microsoft.Extensions.Logging;
using Werewolves.Client.Resources;
using Werewolves.Core.GameLogic.Models;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Client.Services;

#if WINDOWS
using Microsoft.Maui.LifecycleEvents;
using Microsoft.UI.Windowing;
using Windows.Graphics;
#endif

namespace Werewolves.Client
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var portugueseCulture = CultureInfo.GetCultureInfo("pt-PT");
            CultureInfo.DefaultThreadCurrentCulture = portugueseCulture;
            CultureInfo.DefaultThreadCurrentUICulture = portugueseCulture;
            ClientStrings.Culture = portugueseCulture;

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddSingleton<GameService>();
            builder.Services.AddSingleton<LobbySetupMetadata>(sp =>
                sp.GetRequiredService<GameService>().GetLobbySetupMetadata());
            builder.Services.AddSingleton<LobbySetupState>(sp =>
                new LobbySetupState(sp.GetRequiredService<LobbySetupMetadata>()));
            builder.Services.AddSingleton<GameClientManager>();

#if DEBUG
            builder.Services.AddSingleton<BenchmarkClientManager>();
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            // Add this block
#if WINDOWS
        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(wndLifeCycleBuilder =>
            {
                wndLifeCycleBuilder.OnWindowCreated(window =>
                {
                    // Get the native WinUI window handle
                    var nativeWindow = window as Microsoft.UI.Xaml.Window;
                    if (nativeWindow is null) return;

                    // 3. SET THE SIZE (Width, Height)
                    // Note: These are physical pixels. 
                    // If your monitor is at 150% scale, 450px might look small.
                    int newWidth = 400;
                    int newHeight = 800;
                    nativeWindow.AppWindow.Resize(new SizeInt32(newWidth, newHeight));

					// Lock the window size by manipulating the presenter
					if (nativeWindow.AppWindow.Presenter is OverlappedPresenter presenter)
                    {
                        presenter.IsResizable = false;
                        presenter.IsMaximizable = false;
                    }
                });
            });
        });
#endif

			return builder.Build();
        }
    }
}
