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
			handler.PlatformView.Calls.Clear();

			TizenShellSectionHandler.MapCurrentItem(handler, section);

			Assert.Same(content, handler.PlatformView.CurrentItem);
			Assert.True(handler.PlatformView.CurrentItemUpdates > 0);
			Assert.True(handler.PlatformView.NavigationRequests > 0);
			Assert.Equal(["current", "navigation"], handler.PlatformView.Calls);
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

		sealed class TestApplication : Application
		{
		}
	}
}
