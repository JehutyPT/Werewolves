using System.Collections.Immutable;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;

#pragma warning disable BL0006

namespace Werewolves.Client.Tests.Components;

public class AssignRolesViewTests
{
	[Fact]
	public async Task ClickSelectedRole_DeselectsRole()
	{
		using var fixture = new AssignRolesInteractionFixture();
		await fixture.RenderAsync();

		var roleLabel = MainRoleType.SimpleWerewolf.GetPublicName();

		await fixture.ClickAsync(fixture.FindButtonByText(roleLabel)!);

		fixture.FindButtonByText(roleLabel)!.ClassName.Should().Contain("ww-role-btn--selected");
		fixture.FindButtonByText(ClientStrings.Dashboard_ContinueButton)!.IsDisabled.Should().BeFalse();

		await fixture.ClickAsync(fixture.FindButtonByText(roleLabel)!);

		fixture.FindButtonByText(roleLabel)!.ClassName.Should().NotContain("ww-role-btn--selected");
		fixture.FindButtonByText(ClientStrings.Dashboard_ContinueButton)!.IsDisabled.Should().BeTrue();
	}

	[Fact]
	public async Task SinglePlayerNavigationArrows_AreVisibleButDisabled()
	{
		using var fixture = new AssignRolesInteractionFixture();
		await fixture.RenderAsync();

		fixture.FindButtonByAriaLabel(ClientStrings.AssignRoles_PreviousPlayerAria)
			.Should().NotBeNull()
			.And.Match<ButtonSnapshot>(button => button.IsDisabled);
		fixture.FindButtonByAriaLabel(ClientStrings.AssignRoles_NextPlayerAria)
			.Should().NotBeNull()
			.And.Match<ButtonSnapshot>(button => button.IsDisabled);
	}

	[Fact]
	public async Task PlayerNavigation_IsBoundedAndUsesRosterOrder()
	{
		using var fixture = new AssignRolesInteractionFixture(
			["Alice", "Bob"],
			[MainRoleType.SimpleWerewolf, MainRoleType.SimpleVillager]);
		await fixture.RenderAsync();

		fixture.VisibleText.Should().Contain("Alice").And.NotContain("Bob");
		fixture.FindButtonByAriaLabel(ClientStrings.AssignRoles_PreviousPlayerAria)!.IsDisabled.Should().BeTrue();
		fixture.FindButtonByAriaLabel(ClientStrings.AssignRoles_NextPlayerAria)!.IsDisabled.Should().BeFalse();

		await fixture.ClickAsync(fixture.FindButtonByAriaLabel(ClientStrings.AssignRoles_NextPlayerAria)!);

		fixture.VisibleText.Should().Contain("Bob").And.NotContain("Alice");
		fixture.FindButtonByAriaLabel(ClientStrings.AssignRoles_PreviousPlayerAria)!.IsDisabled.Should().BeFalse();
		fixture.FindButtonByAriaLabel(ClientStrings.AssignRoles_NextPlayerAria)!.IsDisabled.Should().BeTrue();
	}

	[Fact]
	public async Task Roles_AreGroupedByRoleTypeAndSortedByDisplayName()
	{
		using var fixture = new AssignRolesInteractionFixture(
			["Alice"],
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.WildChild,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager
			]);
		await fixture.RenderAsync();

		var text = fixture.VisibleTextItems;
		text.IndexOf(RoleGroup.Villagers.GetDisplayName()).Should().BeLessThan(text.IndexOf(RoleGroup.Werewolves.GetDisplayName()));
		text.IndexOf(RoleGroup.Werewolves.GetDisplayName()).Should().BeLessThan(text.IndexOf(RoleGroup.Ambiguous.GetDisplayName()));
		text.IndexOf(MainRoleType.SimpleVillager.GetPublicName()).Should().BeLessThan(text.IndexOf(MainRoleType.Seer.GetPublicName()));
	}

	[Fact]
	public void Markup_RendersRolesFromInstruction()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("Instruction.RolesForAssignment");
	}

	[Fact]
	public void Markup_RendersPlayersForAssignment()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("Instruction.PlayersForAssignment");
	}

	[Fact]
	public void Markup_AcceptsInstructionAndOnResponseParameters()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("[Parameter");
		markup.Should().Contain("AssignRolesInstruction Instruction");
		markup.Should().Contain("EventCallback<ModeratorResponse> OnResponse");
	}

	[Fact]
	public void Markup_AcceptsRosterParameterForPlayerNameResolution()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("IReadOnlyList<DashboardRosterEntry> Roster");
	}

	[Fact]
	public void Markup_CallsCreateResponseOnSubmit()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("Instruction.CreateResponse");
		markup.Should().Contain("OnResponse");
	}

	[Fact]
	public void Markup_SubmitUsesPressAndHoldPattern()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("<HoldButton");
		markup.Should().Contain("Label=\"@ClientStrings.Dashboard_ContinueButton\"");
		markup.Should().Contain("OnHoldComplete=\"HandleSubmit\"");
	}

	[Fact]
	public void Markup_PinsSubmitButtonInDashboardActionZone()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().MatchRegex(@"(?s)<footer class=""ww-dashboard-action-zone"">\s*<HoldButton");
	}

	[Fact]
	public void Markup_SubmitButtonIsDisabledWhenAssignmentsIncomplete()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("Disabled=\"@(!AllPlayersAssigned)\"");
	}

	[Fact]
	public void Markup_UsesGetPublicNameForRoleDisplay()
	{
		var markup = File.ReadAllText(GetViewPath());

		// Roles should use the existing GetPublicName() extension for localization
		markup.Should().Contain("GetPublicName");
	}

	[Fact]
	public void Markup_UsesClientStringsResourceKeys()
	{
		var markup = File.ReadAllText(GetViewPath());

		markup.Should().Contain("ClientStrings.AssignRoles_Title");
		markup.Should().Contain("ClientStrings.AssignRoles_SelectRolePrompt");
		markup.Should().Contain("ClientStrings.Dashboard_ContinueButton");
	}

	private static string GetViewPath()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory is not null)
		{
			var candidate = Path.Combine(
				directory.FullName,
				"Werewolves.Client",
				"Components",
				"Game",
				"Views",
				"AssignRolesView.razor");

			if (File.Exists(candidate))
			{
				return candidate;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException("AssignRolesView.razor could not be found from the test output directory.");
	}

	private static AssignRolesInstruction CreateInstruction(IReadOnlyList<Guid> playerIds, IReadOnlyList<MainRoleType> roles) =>
		(AssignRolesInstruction)AssignRolesConstructor.Invoke(
			[
				playerIds.ToImmutableHashSet(),
				roles,
				null,
				"Assign a role",
				null
			]);

	private static readonly ConstructorInfo AssignRolesConstructor =
		typeof(AssignRolesInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 5);

	private sealed class AssignRolesInteractionFixture : IDisposable
	{
		private readonly ComponentTestRenderer _renderer;
		private readonly int _rootComponentId;
		private readonly ServiceProvider _serviceProvider;

		public AssignRolesInteractionFixture()
			: this(["Alice"], [MainRoleType.SimpleWerewolf, MainRoleType.SimpleVillager])
		{
		}

		public AssignRolesInteractionFixture(IReadOnlyList<string> playerNames, IReadOnlyList<MainRoleType> roles)
		{
			var playerIds = playerNames.Select(_ => Guid.NewGuid()).ToArray();
			var host = new AssignRolesHost
			{
				Instruction = CreateInstruction(playerIds, roles),
				Roster = playerNames
					.Select((name, index) => new DashboardRosterEntry(
						playerIds[index],
						index + 1,
						name,
						DashboardRoster.UnknownRoleLabel,
						false,
						"Alive",
						false,
						[],
						DashboardRoster.NoStatusEffectsLabel))
					.ToArray()
			};

			var services = new ServiceCollection();
			services.AddSingleton<IHapticFeedbackService, NoOpHapticFeedbackService>();
			_serviceProvider = services.BuildServiceProvider();

			_renderer = new ComponentTestRenderer(_serviceProvider);
			_rootComponentId = _renderer.AttachRootComponent(host);
		}

		public Task RenderAsync() =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.RenderRootAsync(_rootComponentId));

		public Task ClickAsync(ButtonSnapshot button) =>
			_renderer.Dispatcher.InvokeAsync(() => _renderer.DispatchClickAsync(button.ClickEventHandlerId));

		public ButtonSnapshot? FindButtonByText(string text) =>
			FindAllButtons().FirstOrDefault(button =>
				button.TextContent.Equals(text, StringComparison.OrdinalIgnoreCase));

		public ButtonSnapshot? FindButtonByAriaLabel(string label) =>
			FindAllButtons().FirstOrDefault(button =>
				button.Attributes.TryGetValue("aria-label", out var value) &&
				value is string text &&
				text.Equals(label, StringComparison.OrdinalIgnoreCase));

		public string VisibleText => string.Join(" ", VisibleTextItems);

		public List<string> VisibleTextItems =>
			EnumerateComponentIds(_rootComponentId)
				.SelectMany(componentId =>
				{
					var frames = _renderer.GetFrames(componentId);
					return Enumerable.Range(0, frames.Count)
						.Select(index => frames.Array[index])
						.Where(frame => frame.FrameType == RenderTreeFrameType.Text)
						.Select(frame => frame.TextContent)
						.Where(text => !string.IsNullOrWhiteSpace(text));
				})
				.ToList();

		private List<ButtonSnapshot> FindAllButtons()
		{
			var buttons = new List<ButtonSnapshot>();
			foreach (var componentId in EnumerateComponentIds(_rootComponentId))
			{
				var frames = _renderer.GetFrames(componentId);
				for (var index = 0; index < frames.Count; index++)
				{
					var frame = frames.Array[index];
					if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != "button")
					{
						continue;
					}

					buttons.Add(CreateButtonSnapshot(frames, index));
				}
			}

			return buttons;
		}

		private IEnumerable<int> EnumerateComponentIds(int componentId)
		{
			yield return componentId;
			var frames = _renderer.GetFrames(componentId);
			for (var index = 0; index < frames.Count; index++)
			{
				var frame = frames.Array[index];
				if (frame.FrameType != RenderTreeFrameType.Component)
				{
					continue;
				}

				foreach (var child in EnumerateComponentIds(frame.ComponentId))
				{
					yield return child;
				}
			}
		}

		private static ButtonSnapshot CreateButtonSnapshot(ArrayRange<RenderTreeFrame> frames, int elementIndex)
		{
			var element = frames.Array[elementIndex];
			var attributes = new Dictionary<string, object?>();
			var text = new List<string>();
			var clickHandlerId = 0UL;
			var isDisabled = false;
			var endIndex = elementIndex + element.ElementSubtreeLength;

			for (var index = elementIndex + 1; index < endIndex; index++)
			{
				var frame = frames.Array[index];
				switch (frame.FrameType)
				{
					case RenderTreeFrameType.Attribute:
						attributes[frame.AttributeName] = frame.AttributeValue;
						if (frame.AttributeName == "onclick")
						{
							clickHandlerId = frame.AttributeEventHandlerId;
						}

						if (frame.AttributeName == "disabled" && frame.AttributeValue is true)
						{
							isDisabled = true;
						}
						break;
					case RenderTreeFrameType.Text:
						text.Add(frame.TextContent);
						break;
				}
			}

			var className = attributes.TryGetValue("class", out var cls) && cls is string s ? s : "";
			return new ButtonSnapshot(
				className,
				string.Concat(text),
				clickHandlerId,
				isDisabled,
				attributes);
		}

		public void Dispose()
		{
			_renderer.Dispose();
			_serviceProvider.Dispose();
		}
	}

	private sealed class AssignRolesHost : ComponentBase
	{
		public AssignRolesInstruction Instruction { get; init; } = default!;
		public IReadOnlyList<DashboardRosterEntry> Roster { get; init; } = [];

		protected override void BuildRenderTree(RenderTreeBuilder builder)
		{
			builder.OpenComponent<AssignRolesView>(0);
			builder.AddAttribute(1, nameof(AssignRolesView.Instruction), Instruction);
			builder.AddAttribute(2, nameof(AssignRolesView.Roster), Roster);
			builder.AddAttribute(3, nameof(AssignRolesView.OnResponse),
				EventCallback.Factory.Create<ModeratorResponse>(this, _ => { }));
			builder.CloseComponent();
		}
	}

	private sealed record ButtonSnapshot(
		string ClassName,
		string TextContent,
		ulong ClickEventHandlerId,
		bool IsDisabled,
		IReadOnlyDictionary<string, object?> Attributes);

	private sealed class ComponentTestRenderer(IServiceProvider serviceProvider)
		: Renderer(serviceProvider, NullLoggerFactory.Instance)
	{
		public override Microsoft.AspNetCore.Components.Dispatcher Dispatcher { get; } =
			Microsoft.AspNetCore.Components.Dispatcher.CreateDefault();

		public int AttachRootComponent(IComponent component) => AssignRootComponentId(component);

		public Task RenderRootAsync(int componentId) => RenderRootComponentAsync(componentId);

		public ArrayRange<RenderTreeFrame> GetFrames(int componentId) =>
			GetCurrentRenderTreeFrames(componentId);

		public Task DispatchClickAsync(ulong eventHandlerId) =>
			DispatchEventAsync(eventHandlerId, default, new MouseEventArgs());

		protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

		protected override void HandleException(Exception exception)
		{
			throw new InvalidOperationException(
				"Unhandled exception during AssignRolesView rendering or event dispatch.", exception);
		}
	}

	private sealed class NoOpHapticFeedbackService : IHapticFeedbackService
	{
		public void Click() { }
		public void LongPress() { }
	}
}
