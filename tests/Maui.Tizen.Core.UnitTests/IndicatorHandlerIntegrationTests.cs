// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	[Collection(StaticMapperCollection.Name)]
	public class IndicatorHandlerIntegrationTests
	{
		[Fact]
		public void RebindUsesCurrentVirtualViewAndVisibilityReappliesHideSingle()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<ControlsApp>()
				.Build();
			var handler = Assert.IsType<TizenIndicatorViewHandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(typeof(IndicatorView)));
			var elementHandler = (IElementHandler)handler;
			elementHandler.SetMauiContext(new MauiContext(app.Services));

			var first = new IndicatorView { Count = 20, MaximumVisible = 5, Position = 10 };
			elementHandler.SetVirtualView(first);
			var platform = Assert.IsType<TizenPageControl>(elementHandler.PlatformView);
			Assert.Same(first, platform.BoundView);

			var second = new IndicatorView
			{
				Count = 1,
				HideSingle = true,
				IsVisible = false,
			};
			elementHandler.SetVirtualView(second);

			Assert.Same(second, platform.BoundView);
			Assert.False(platform.IsShown);

			second.IsVisible = true;
			elementHandler.UpdateValue(nameof(IView.Visibility));
			Assert.False(platform.IsShown);

			second.Count = 2;
			elementHandler.UpdateValue(nameof(IIndicatorView.Count));
			Assert.True(platform.IsShown);

			second.IndicatorTemplate = new DataTemplate(() => new Label());
			var resetCount = platform.ResetCount;
			elementHandler.UpdateValue("IndicatorTemplate");
			Assert.Equal(resetCount + 1, platform.ResetCount);

			elementHandler.DisconnectHandler();
		}

		sealed class ControlsApp : Application
		{
		}
	}
}
