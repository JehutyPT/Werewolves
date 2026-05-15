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
