// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
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
	public class RefreshHandlerIntegrationTests
	{
		[Fact]
		public void DisabledNativePullIsObservedThenForcedThroughCompletion()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<ControlsApp>()
				.Build();
			var view = new RefreshView { IsRefreshEnabled = false };
			var handler = Assert.IsType<TizenRefreshViewHandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(typeof(RefreshView)));
			var elementHandler = (IElementHandler)handler;
			elementHandler.SetMauiContext(new MauiContext(app.Services));
			elementHandler.SetVirtualView(view);
			var platform = Assert.IsType<TizenRefreshLayout>(elementHandler.PlatformView);
			platform.DelayNativeCompletion = true;

			platform.RaiseRefreshing();

			Assert.False(platform.IsRefreshing);
			Assert.False(view.IsRefreshing);
			Assert.True(platform.RefreshState.IsCompleting);

			platform.NotifyNativeIdle();
			Assert.True(SpinWait.SpinUntil(() => !platform.RefreshState.IsCompleting, TimeSpan.FromSeconds(1)));

			elementHandler.DisconnectHandler();
		}

		[Fact]
		public void DisposeDuringObservedCompletionRetainsPlatformUntilNativeIdle()
		{
			using var app = MauiApp.CreateBuilder()
				.UseMauiAppTizenControls<ControlsApp>()
				.Build();
			var view = new RefreshView { IsRefreshEnabled = true };
			var handler = Assert.IsType<TizenRefreshViewHandler>(
				app.Services.GetRequiredService<IMauiHandlersFactory>().GetHandler(typeof(RefreshView)));
			var elementHandler = (IElementHandler)handler;
			elementHandler.SetMauiContext(new MauiContext(app.Services));
			elementHandler.SetVirtualView(view);
			var platform = Assert.IsType<TizenRefreshLayout>(elementHandler.PlatformView);
			platform.DelayNativeCompletion = true;

			view.IsRefreshing = true;
			elementHandler.UpdateValue(nameof(IRefreshView.IsRefreshing));
			view.IsRefreshing = false;
			elementHandler.UpdateValue(nameof(IRefreshView.IsRefreshing));

			Assert.True(platform.RefreshState.IsCompleting);

			handler.Dispose();

			Assert.False(platform.IsDisposed);

			platform.NotifyNativeIdle();
			Assert.True(SpinWait.SpinUntil(() => platform.IsDisposed, TimeSpan.FromSeconds(1)));
		}

		sealed class ControlsApp : Application
		{
		}
	}
}
