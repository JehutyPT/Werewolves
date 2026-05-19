using Microsoft.AspNetCore.DataProtection;
using Werewolves.Client.BrowserQaHost;
using Werewolves.Client.BrowserQaHost.Components;

BrowserQaHostCulture.UsePortuguese();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDataProtection()
	.UseEphemeralDataProtectionProvider();
builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();
builder.Services.AddBrowserQaHostModeratorServices();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
