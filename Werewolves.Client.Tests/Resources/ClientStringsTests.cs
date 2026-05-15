using System.Globalization;
using FluentAssertions;
using Werewolves.Client.Resources;
using Xunit;

namespace Werewolves.Client.Tests.Resources;

public class ClientStringsTests
{
	[Fact]
	public void ClientStrings_ExposesPortugueseUiCopyThroughGeneratedAccessor()
	{
		var previousCulture = ClientStrings.Culture;
		try
		{
			ClientStrings.Culture = CultureInfo.GetCultureInfo("pt-PT");

			ClientStrings.LobbyRoster_Title.Should().Be("Jogadores");
			ClientStrings.Validation_EmptyPlayerName.Should().Be("Escreve um nome antes de adicionar.");
			ClientStrings.RoleSelection_Title.Should().Be("Papéis");
			ClientStrings.RoleSelection_StartGameButton.Should().Be("Iniciar jogo");
			ClientStrings.Dashboard_NoSession.Should().Be("Sem sessão");
			ClientStrings.Dashboard_HealthDead.Should().Be("Eliminado");
			ClientStrings.Benchmark_RunButton.Should().Be("Executar 1.000 jogos");
			ClientStrings.SelectPlayers_SubmitButton.Should().Be("Confirmar");
			ClientStrings.SelectPlayers_ListAria.Should().Be("Jogadores selecionáveis");

			// SelectOptionsView and AssignRolesView strings
			ClientStrings.SelectOptions_Title.Should().Be("Escolher opção");
			ClientStrings.SelectOptions_SelectionCountFormat.Should().Be("{0} de {1} selecionadas");
			ClientStrings.AssignRoles_Title.Should().Be("Atribuir papel");
			ClientStrings.AssignRoles_SelectRolePrompt.Should().Be("Escolher papel");
			ClientStrings.Common_HoldToConfirm.Should().Be("Manter premido para confirmar");
			ClientStrings.Dashboard_DebateTimerLabel.Should().Be("Debate");
			ClientStrings.Victory_Title.Should().Be("Fim de Jogo");
			ClientStrings.Victory_StepLabel.Should().Be("Resultado");
			ClientStrings.Victory_ReturnToLobbyButton.Should().Be("Novo jogo");
		}
		finally
		{
			ClientStrings.Culture = previousCulture;
		}
	}
}
