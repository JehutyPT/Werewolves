using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Xunit;

namespace Werewolves.Client.BrowserQa.Tests;

public sealed class BrowserQaHostFixture : IAsyncLifetime
{
	private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);
	private readonly List<string> _hostOutput = [];
	private Process? _hostProcess;

	public string BaseAddress { get; private set; } = string.Empty;

	public Uri DashboardScenarioUri => new($"{BaseAddress}/?qa=dashboard");
	public Uri ProbabilityScenarioUri => new($"{BaseAddress}/?qa=probability");
	public Uri DegenerateScenarioUri => new($"{BaseAddress}/?qa=degenerate");

	public async Task InitializeAsync()
	{
		var port = AllocateTcpPort();
		BaseAddress = $"http://127.0.0.1:{port}";

		var hostProjectPath = Path.Combine(
			RepositoryRoot,
			"Werewolves.Client.BrowserQaHost",
			"Werewolves.Client.BrowserQaHost.csproj");

		var startInfo = new ProcessStartInfo("dotnet")
		{
			WorkingDirectory = RepositoryRoot,
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			UseShellExecute = false
		};

		startInfo.ArgumentList.Add("run");
		startInfo.ArgumentList.Add("--no-launch-profile");
		startInfo.ArgumentList.Add("--project");
		startInfo.ArgumentList.Add(hostProjectPath);
		startInfo.ArgumentList.Add("--urls");
		startInfo.ArgumentList.Add(BaseAddress);
		startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
		startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

		_hostProcess = Process.Start(startInfo)
			?? throw new InvalidOperationException("Could not start the Browser QA Host process.");
		_hostProcess.OutputDataReceived += (_, args) => CaptureHostOutput(args.Data);
		_hostProcess.ErrorDataReceived += (_, args) => CaptureHostOutput(args.Data);
		_hostProcess.BeginOutputReadLine();
		_hostProcess.BeginErrorReadLine();

		await WaitForHostAsync();
	}

	public async Task DisposeAsync()
	{
		if (_hostProcess is null)
		{
			return;
		}

		if (!_hostProcess.HasExited)
		{
			_hostProcess.Kill(entireProcessTree: true);
			await _hostProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
		}

		_hostProcess.Dispose();
	}

	private async Task WaitForHostAsync()
	{
		using var client = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(2)
		};
		var deadline = DateTimeOffset.UtcNow + StartupTimeout;
		Exception? lastFailure = null;

		while (DateTimeOffset.UtcNow < deadline)
		{
			if (_hostProcess?.HasExited == true)
			{
				throw new InvalidOperationException(
					$"Browser QA Host exited before it was ready.{Environment.NewLine}{CapturedHostOutput()}");
			}

			try
			{
				using var response = await client.GetAsync(BaseAddress);
				if ((int)response.StatusCode < 500)
				{
					return;
				}
			}
			catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
			{
				lastFailure = ex;
			}

			await Task.Delay(250);
		}

		throw new TimeoutException(
			$"Browser QA Host did not become ready at {BaseAddress}. Last failure: {lastFailure?.Message}{Environment.NewLine}{CapturedHostOutput()}");
	}

	private static int AllocateTcpPort()
	{
		using var listener = new TcpListener(IPAddress.Loopback, port: 0);
		listener.Start();
		return ((IPEndPoint)listener.LocalEndpoint).Port;
	}

	private static string RepositoryRoot
	{
		get
		{
			var directory = new DirectoryInfo(AppContext.BaseDirectory);
			while (directory is not null)
			{
				if (File.Exists(Path.Combine(directory.FullName, "Werewolves.sln")))
				{
					return directory.FullName;
				}

				directory = directory.Parent;
			}

			throw new InvalidOperationException("Could not locate the Werewolves repository root.");
		}
	}

	private void CaptureHostOutput(string? line)
	{
		if (!string.IsNullOrWhiteSpace(line))
		{
			_hostOutput.Add(line);
		}
	}

	private string CapturedHostOutput() => string.Join(Environment.NewLine, _hostOutput);
}
