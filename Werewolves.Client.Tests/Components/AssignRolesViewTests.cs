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
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;
using Css = Werewolves.Client.Tests.Helpers.ClientTestReferences.Css;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;
using PlayerNames = Werewolves.Client.Tests.Helpers.ClientTestReferences.PlayerNames;

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

		fixture.FindButtonByText(roleLabel)!.ClassName.Should().Contain(Css.Classes.RoleButtonSelected);
		fixture.FindButtonByText(ClientStrings.Dashboard_ContinueButton)!.IsDisabled.Should().BeFalse();

		await fixture.ClickAsync(fixture.FindButtonByText(roleLabel)!);

		fixture.FindButtonByText(roleLabel)!.ClassName.Should().NotContain(Css.Classes.RoleButtonSelected);
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
			PlayerNames.AssignRolesPair,
			[MainRoleType.SimpleWerewolf, MainRoleType.SimpleVillager]);
		await fixture.RenderAsync();

		fixture.VisibleText.Should().Contain(PlayerNames.Alice).And.NotContain(PlayerNames.Bob);
		fixture.FindButtonByAriaLabel(ClientStrings.AssignRoles_PreviousPlayerAria)!.IsDisabled.Should().BeTrue();
		fixture.FindButtonByAriaLabel(ClientStrings.AssignRoles_NextPlayerAria)!.IsDisabled.Should().BeFalse();

		await fixture.ClickAsync(fixture.FindButtonByAriaLabel(ClientStrings.AssignRoles_NextPlayerAria)!);

		fixture.VisibleText.Should().Contain(PlayerNames.Bob).And.NotContain(PlayerNames.Alice);
		fixture.FindButtonByAriaLabel(ClientStrings.AssignRoles_PreviousPlayerAria)!.IsDisabled.Should().BeFalse();
		fixture.FindButtonByAriaLabel(ClientStrings.AssignRoles_NextPlayerAria)!.IsDisabled.Should().BeTrue();
	}

	[Fact]
	public async Task Roles_AreGroupedByRoleTypeAndSortedByDisplayName()
	{
		using var fixture = new AssignRolesInteractionFixture(
			PlayerNames.AssignRolesSingle,
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

	private static AssignRolesInstruction CreateInstruction(IReadOnlyList<Guid> playerIds, IReadOnlyList<MainRoleType> roles) =>
		(AssignRolesInstruction)AssignRolesConstructor.Invoke(
			[
				playerIds.ToImmutableHashSet(),
				roles,
				null,
				GameStrings.RevealRolePromptSpecify,
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
			: this(PlayerNames.AssignRolesSingle, [MainRoleType.SimpleWerewolf, MainRoleType.SimpleVillager])
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
						DashboardRoster.HealthLabel(PlayerHealth.Alive),
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
				button.Attributes.TryGetValue(Html.Attributes.AriaLabel, out var value) &&
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
					if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != Html.Elements.Button)
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
						if (frame.AttributeName == Html.Events.Click)
						{
							clickHandlerId = frame.AttributeEventHandlerId;
						}

						if (frame.AttributeName == Html.Attributes.Disabled && frame.AttributeValue is true)
						{
							isDisabled = true;
						}
						break;
					case RenderTreeFrameType.Text:
						text.Add(frame.TextContent);
						break;
				}
			}

			var className = attributes.TryGetValue(Html.Attributes.Class, out var cls) && cls is string s ? s : "";
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
				ClientTestReferences.ExceptionMessages.ComponentRenderOrDispatchFailure("AssignRolesView"), exception);
		}
	}

	private sealed class NoOpHapticFeedbackService : IHapticFeedbackService
	{
		public void Click() { }
		public void LongPress() { }
	}
}
