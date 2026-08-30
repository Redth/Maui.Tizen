using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	public class WaveCProductionRegistrationTests
	{
		public static TheoryData<Type, string> Registrations => new()
		{
			{ typeof(Toolbar), "TizenToolbarHandler" },
			{ typeof(MenuBar), "TizenMenuBarHandler" },
			{ typeof(MenuBarItem), "TizenMenuBarItemHandler" },
			{ typeof(MenuFlyout), "TizenMenuFlyoutHandler" },
			{ typeof(MenuFlyoutItem), "TizenMenuFlyoutItemHandler" },
			{ typeof(MenuFlyoutSeparator), "TizenMenuFlyoutSeparatorHandler" },
			{ typeof(MenuFlyoutSubItem), "TizenMenuFlyoutSubItemHandler" },
			{ typeof(NavigationPage), "TizenNavigationViewHandler" },
			{ typeof(FlyoutPage), "TizenFlyoutViewHandler" },
			{ typeof(TabbedPage), "TizenTabbedPageHandler" },
			{ typeof(Shell), "TizenShellHandler" },
			{ typeof(ShellItem), "TizenShellItemHandler" },
			{ typeof(ShellSection), "TizenShellSectionHandler" },
			{ typeof(CollectionView), "TizenCollectionViewHandler" },
			{ typeof(CarouselView), "TizenCarouselViewHandler" },
		};

		[Theory]
		[MemberData(nameof(Registrations))]
		public void RealControlsStartupResolvesEveryWaveCConcreteType(Type virtualView, string handlerName)
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<TestApplication>()
				.Build();

			var handler = app.Services
				.GetRequiredService<IMauiHandlersFactory>()
				.GetHandler(virtualView);

			Assert.NotNull(handler);
			Assert.Equal(handlerName, handler.GetType().Name);
		}

		sealed class TestApplication : Application
		{
		}
	}
}
