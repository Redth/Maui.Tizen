using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Microsoft.Maui.Platforms.Tizen.Platform;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	public class WaveCShellMapperExecutionTests
	{
		[Fact]
		public void ShellItemCurrentItemMapperDrivesTheProductionHandler()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<TestApplication>()
				.Build();
			var shellItem = new FlyoutItem();
			var section = new ShellSection();
			shellItem.Items.Add(section);
			shellItem.CurrentItem = section;
			var handler = Assert.IsType<TizenShellItemHandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(typeof(ShellItem)));
			handler.SetMauiContext(new MauiContext(app.Services));
			handler.SetVirtualView(shellItem);

			handler.UpdateValue(nameof(ShellItem.CurrentItem));

			Assert.Same(section, handler.PlatformView.CurrentItem);
			Assert.True(handler.PlatformView.CurrentItemUpdates > 0);
		}

		[Fact]
		public void ShellSectionCurrentItemMapperMountsRootBeforeStackSync()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<TestApplication>()
				.Build();
			var section = new ShellSection();
			var content = new ShellContent { Content = new ContentPage() };
			section.Items.Add(content);
			section.CurrentItem = content;
			var handler = Assert.IsType<TizenShellSectionHandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(typeof(ShellSection)));
			handler.SetMauiContext(new MauiContext(app.Services));
			handler.SetVirtualView(section);
			Assert.Equal(1, handler.PlatformView.NavigationRequests);
			handler.PlatformView.Calls.Clear();

			TizenShellSectionHandler.MapCurrentItem(handler, section);

			Assert.Same(content, handler.PlatformView.CurrentItem);
			Assert.True(handler.PlatformView.CurrentItemUpdates > 0);
			Assert.Equal(2, handler.PlatformView.NavigationRequests);
			Assert.Equal(["current", "navigation", "finish"], handler.PlatformView.Calls);
		}

		[Fact]
		public void ShellSectionNavigationUsesTheVirtualRequestCompletionHandshakeOnce()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<TestApplication>()
				.Build();
			var dispatched = 0;
			var commands = new CommandMapper<ShellSection, TizenShellSectionHandler>(
				TizenShellSectionHandler.CommandMapper)
			{
				[nameof(IStackNavigation.RequestNavigation)] = (handler, view, args) =>
				{
					dispatched++;
					TizenShellSectionHandler.RequestNavigation(handler, view, args);
				},
			};
			var section = new ShellSection();
			section.Items.Add(new ShellContent { Content = new ContentPage() });
			var handler = new TizenShellSectionHandler(TizenShellSectionHandler.Mapper, commands);
			handler.SetMauiContext(new MauiContext(app.Services));

			handler.SetVirtualView(section);

			Assert.Equal(1, dispatched);
			Assert.Equal(1, handler.PlatformView.NavigationRequests);
			Assert.Equal(["current", "navigation", "finish"], handler.PlatformView.Calls);
		}

		[Fact]
		public void ShellItemHandlerRebindsItsExistingPlatformView()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<TestApplication>()
				.Build();
			var handler = Assert.IsType<TizenShellItemHandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(typeof(ShellItem)));
			handler.SetMauiContext(new MauiContext(app.Services));
			handler.SetVirtualView(new FlyoutItem());
			var platform = handler.PlatformView;

			handler.SetVirtualView(new FlyoutItem());

			Assert.Same(platform, handler.PlatformView);
			Assert.Equal(1, platform.RebindCount);
		}

		[Fact]
		public void ShellSectionHandlerReconnectsItsExistingPlatformManager()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<TestApplication>()
				.Build();
			var handler = Assert.IsType<TizenShellSectionHandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(typeof(ShellSection)));
			handler.SetMauiContext(new MauiContext(app.Services));
			handler.SetVirtualView(new ShellSection());
			var platform = handler.PlatformView;
			var before = platform.ConnectCount;

			handler.SetVirtualView(new ShellSection());

			Assert.Same(platform, handler.PlatformView);
			Assert.True(platform.ConnectCount > before);
		}

		[Fact]
		public void ShellFlyoutItemsLiteralMapperRunsThroughTheProductionHandler()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<TestApplication>()
				.Build();
			var shell = new Shell();
			var handler = Assert.IsType<TizenShellHandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(typeof(Shell)));
			handler.SetMauiContext(new MauiContext(app.Services));
			handler.SetVirtualView(shell);
			var before = handler.PlatformView.FlyoutItemsUpdates;

			handler.UpdateValue("FlyoutItems");

			Assert.Equal(before + 1, handler.PlatformView.FlyoutItemsUpdates);
		}

		[Fact]
		public void ShellFlyoutBackgroundMapperUsesTheBrushProperty()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<TestApplication>()
				.Build();
			var brush = new SolidColorBrush(Colors.CornflowerBlue);
			var shell = new Shell { FlyoutBackground = brush };
			var handler = Assert.IsType<TizenShellHandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(typeof(Shell)));
			handler.SetMauiContext(new MauiContext(app.Services));
			handler.SetVirtualView(shell);

			TizenShellHandler.MapFlyoutBackground(handler, shell);

			Assert.Same(brush, handler.PlatformView.FlyoutBackground);
		}

		[Fact]
		public void NavigationHandlerRebindsManagerAndSynchronizesTheNewStack()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<TestApplication>()
				.Build();
			var firstRoot = new ContentPage();
			var secondRoot = new ContentPage();
			var first = new NavigationPage(firstRoot);
			var second = new NavigationPage(secondRoot);
			var handler = Assert.IsType<TizenNavigationViewHandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(typeof(NavigationPage)));
			handler.SetMauiContext(new MauiContext(app.Services));
			handler.SetVirtualView(first);
			var platform = handler.PlatformView;
			var disconnects = platform.DisconnectCount;

			handler.SetVirtualView(second);

			Assert.Same(platform, handler.PlatformView);
			Assert.Same(second, platform.ConnectedView);
			Assert.True(platform.DisconnectCount > disconnects);
			Assert.Same(secondRoot, Assert.Single(platform.LastStack!));

			((IElementHandler)handler).DisconnectHandler();
		}

		sealed class TestApplication : Application
		{
		}
	}
}
