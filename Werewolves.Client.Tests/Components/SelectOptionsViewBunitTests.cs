using System.Globalization;
using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Xunit;
using Html = Werewolves.Client.Tests.Helpers.ClientTestReferences.Html;

namespace Werewolves.Client.Tests.Components;

public class SelectOptionsViewBunitTests
{
	private const string FirstOption = "Acordar";
	private const string SecondOption = "Continuar a dormir";
	private const string ThirdOption = "Alertar a aldeia";
	private static string SelectedOptionClass => ClientTestReferences.Css.Classes.OptionButtonSelected;
	private static string HoldButtonSelector =>
		Html.Selectors.ButtonWithClass(ClientTestReferences.Css.Classes.HoldButton);

	[Fact]
	public void SingleSelectionOptions_RenderCoreProvidedControlsAndCanSwitchSelection()
	{
		using var context = new ModeratorComponentTestContext();
		var instruction = CreateInstruction(
			NumberRangeConstraint.Single,
			FirstOption,
			SecondOption,
			ThirdOption);

		var cut = context.RenderModeratorComponent<SelectOptionsView>(parameters => parameters
			.Add(component => component.Instruction, instruction));

		var optionGroup = FindOptionGroup(cut);
		var optionButtons = FindOptionButtons(optionGroup);
		optionButtons.Select(button => button.TextContent.Trim())
			.Should()
			.BeEquivalentTo([FirstOption, SecondOption, ThirdOption]);
		cut.Markup.Should().Contain(SelectionCountLabel(0, 1));
		optionButtons.Should().OnlyContain(button =>
			button.GetAttribute(Html.Attributes.Type) == Html.AttributeValues.ButtonType);
		optionButtons.Should().OnlyContain(button =>
			button.GetAttribute(Html.Attributes.AriaPressed) == Html.AriaValues.False);

		FindOptionButton(cut, FirstOption).Click();

		FindOptionButton(cut, FirstOption).GetAttribute(Html.Attributes.AriaPressed)
			.Should()
			.Be(Html.AriaValues.True);
		FindOptionButton(cut, FirstOption).ClassList.Should().Contain(SelectedOptionClass);
		cut.Markup.Should().Contain(SelectionCountLabel(1, 1));
		FindOptionButton(cut, SecondOption).GetAttribute(Html.Attributes.AriaPressed)
			.Should()
			.Be(Html.AriaValues.False);

		FindOptionButton(cut, SecondOption).Click();

		FindOptionButton(cut, FirstOption).GetAttribute(Html.Attributes.AriaPressed)
			.Should()
			.Be(Html.AriaValues.False);
		FindOptionButton(cut, FirstOption).ClassList.Should().NotContain(SelectedOptionClass);
		FindOptionButton(cut, SecondOption).GetAttribute(Html.Attributes.AriaPressed)
			.Should()
			.Be(Html.AriaValues.True);
		FindOptionButton(cut, SecondOption).ClassList.Should().Contain(SelectedOptionClass);
	}

	[Fact]
	public async Task SubmitRemainsUnavailableUntilSelectionRangeIsSatisfiedAndHoldEmitsOneResponse()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var responses = new List<ModeratorResponse>();
		var instruction = CreateInstruction(
			NumberRangeConstraint.Exact(2),
			FirstOption,
			SecondOption,
			ThirdOption);

		var cut = context.RenderModeratorComponent<SelectOptionsView>(parameters => parameters
			.Add(component => component.Instruction, instruction)
			.Add(component => component.OnResponse,
				EventCallback.Factory.Create<ModeratorResponse>(this, responses.Add)));

		FindActionZone(cut).QuerySelector(HoldButtonSelector).Should().NotBeNull();
		FindHoldButton(cut).GetAttribute(Html.Attributes.AriaLabel)
			.Should()
			.Be(ClientStrings.Common_HoldToConfirm);
		FindHoldButton(cut).TextContent.Should().Contain(ClientStrings.Dashboard_ContinueButton);
		cut.Markup.Should().Contain(SelectionCountLabel(0, 2));
		FindHoldButton(cut).HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
		await AttemptDisabledHoldAsync(cut, FindHoldButton(cut), timing);
		responses.Should().BeEmpty();

		FindOptionButton(cut, FirstOption).Click();

		FindHoldButton(cut).HasAttribute(Html.Attributes.Disabled).Should().BeTrue();
		cut.Markup.Should().Contain(SelectionCountLabel(1, 2));
		await AttemptDisabledHoldAsync(cut, FindHoldButton(cut), timing);
		responses.Should().BeEmpty();

		FindOptionButton(cut, SecondOption).Click();
		FindOptionButton(cut, ThirdOption).Click();

		FindOptionButton(cut, FirstOption).GetAttribute(Html.Attributes.AriaPressed)
			.Should()
			.Be(Html.AriaValues.True);
		FindOptionButton(cut, SecondOption).GetAttribute(Html.Attributes.AriaPressed)
			.Should()
			.Be(Html.AriaValues.True);
		FindOptionButton(cut, ThirdOption).GetAttribute(Html.Attributes.AriaPressed)
			.Should()
			.Be(Html.AriaValues.False);
		cut.Markup.Should().Contain(SelectionCountLabel(2, 2));
		FindHoldButton(cut).HasAttribute(Html.Attributes.Disabled).Should().BeFalse();

		var holdButton = FindHoldButton(cut);
		var holdTask = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		timing.AdvanceBy(RenderedHoldButtonDriver.HoldDuration);
		var releaseTask = RenderedHoldButtonDriver.ReleaseHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		timing.AdvanceBy(RenderedHoldButtonDriver.SuccessFlashDuration);
		await Task.WhenAll(holdTask, releaseTask);
		await RenderedHoldButtonDriver.ReleaseHoldAsync(holdButton);

		responses.Should().ContainSingle();
		var response = responses.Single();
		response.Type.Should().Be(ExpectedInputType.OptionSelection);
		response.SelectedOption.Should().BeEquivalentTo([FirstOption, SecondOption]);
	}

	private static IElement FindOptionGroup(IRenderedComponent<SelectOptionsView> cut) =>
		cut.FindAll("*")
			.Single(element => element.GetAttribute(Html.Attributes.AriaLabel) == ClientStrings.SelectOptions_Title);

	private static IReadOnlyList<IElement> FindOptionButtons(IElement optionGroup) =>
		optionGroup.QuerySelectorAll(Html.Selectors.Button).ToArray();

	private static IElement FindOptionButton(IRenderedComponent<SelectOptionsView> cut, string option) =>
		FindOptionButtons(FindOptionGroup(cut))
			.Single(button => button.TextContent.Trim() == option);

	private static IElement FindHoldButton(IRenderedComponent<SelectOptionsView> cut) =>
		cut.Find(HoldButtonSelector);

	private static IElement FindActionZone(IRenderedComponent<SelectOptionsView> cut) =>
		cut.Find($"footer.{ClientTestReferences.Css.Classes.DashboardActionZone}");

	private static async Task AttemptDisabledHoldAsync(
		IRenderedComponent<SelectOptionsView> cut,
		IElement holdButton,
		ControlledHoldButtonTiming timing)
	{
		holdButton.HasAttribute(Html.Attributes.Disabled).Should().BeTrue();

		var holdTask = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		timing.AdvanceBy(RenderedHoldButtonDriver.HoldDuration + RenderedHoldButtonDriver.SuccessFlashDuration);
		await holdTask;
		await RenderedHoldButtonDriver.FlushAsync(cut);
	}

	private static string SelectionCountLabel(int selectedCount, int maximumCount) =>
		string.Format(
			CultureInfo.CurrentCulture,
			ClientStrings.SelectOptions_SelectionCountFormat,
			selectedCount,
			maximumCount);

	private static SelectOptionsInstruction CreateInstruction(
		NumberRangeConstraint selectionRange,
		params string[] options) =>
		(SelectOptionsInstruction)SelectOptionsConstructor.Invoke(
			[
				new HashSet<string>(options, StringComparer.CurrentCulture),
				selectionRange,
				null,
				GameStrings.ConfirmNightStarted,
				null
			]);

	private static readonly ConstructorInfo SelectOptionsConstructor =
		typeof(SelectOptionsInstruction)
			.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
			.Single(ctor => ctor.GetParameters().Length == 5);
}
