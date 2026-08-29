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

			Assert.True(platform.IsDisconnected);
			Assert.True(platform.PollingStartedAfterDisconnect);
			Assert.False(platform.IsDisposed);

			platform.NotifyNativeIdle();
			Assert.True(SpinWait.SpinUntil(() => platform.IsDisposed, TimeSpan.FromSeconds(1)));
		}

		[Fact]
		public void BelowThresholdPullRetainsPlatformThroughNativeResetFrames()
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

			platform.BeginBelowThresholdPull();
			platform.ReleaseBelowThresholdPull();
			handler.Dispose();

			Assert.False(platform.IsDisposed);
			Assert.True(SpinWait.SpinUntil(() => platform.IsDisposed, TimeSpan.FromSeconds(1)));
			Assert.Equal(0, platform.NativeStateReadAfterDisposeCount);
		}

		[Fact]
		public void SuccessfulPullClearsBelowThresholdResetActivity()
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

			platform.BeginBelowThresholdPull();
			platform.ReleaseBelowThresholdPull();
			Assert.True(platform.HasPendingNativeActivity);

			platform.RaiseRefreshing();

			Assert.False(platform.HasPendingPullReset);
			elementHandler.DisconnectHandler();
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void DisableDuringBelowThresholdPullDefersUntilNativeTerminal(bool interrupted)
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

			platform.BeginBelowThresholdPull();
			view.IsRefreshEnabled = false;

			Assert.True(platform.DeferredDisableCount > 0);
			Assert.True(platform.IsNativePulling);
			Assert.True(platform.HasPendingNativeActivity);
			Assert.Equal(0, platform.NativeStopApplyCount);
			Assert.Equal(0, platform.NativeStopIgnoredWhilePullingCount);
			Assert.False(platform.RefreshState.IsCompleting);

			if (interrupted)
				platform.InterruptBelowThresholdPull();
			else
				platform.ReleaseBelowThresholdPull();

			Assert.True(platform.RefreshState.IsCompleting);
			Assert.True(SpinWait.SpinUntil(
				() => !platform.RefreshState.IsCompleting,
				TimeSpan.FromSeconds(1)));

			Assert.Equal(1, platform.NativeStopApplyCount);
			Assert.False(platform.HasPendingNativeActivity);
			elementHandler.DisconnectHandler();
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void DisabledMidPullWaitsForNativeTerminalBeforeDisposal(bool interrupted)
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

			platform.BeginBelowThresholdPull();
			view.IsRefreshEnabled = false;
			handler.Dispose();

			Assert.True(platform.IsDisconnected);
			Assert.True(platform.PollingStartedAfterDisconnect);
			Assert.False(platform.IsDisposed);
			Assert.False(SpinWait.SpinUntil(
				() => platform.IsDisposed,
				TimeSpan.FromMilliseconds(150)));
			Assert.True(platform.HasPendingNativeActivity);

			if (interrupted)
				platform.InterruptBelowThresholdPull();
			else
				platform.ReleaseBelowThresholdPull();

			Assert.True(SpinWait.SpinUntil(
				() => platform.IsDisposed,
				TimeSpan.FromSeconds(1)));
			Assert.Equal(1, platform.DisposeCount);

			platform.ReleaseBelowThresholdPull();
			platform.InterruptBelowThresholdPull();
			Assert.Equal(1, platform.DisposeCount);
			Assert.Equal(0, platform.NativeStateReadAfterDisposeCount);
		}

		[Fact]
		public void HostNativeStopIsIgnoredWhilePulling()
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

			platform.BeginBelowThresholdPull();
			platform.ApplyRefreshState(false);

			Assert.Equal(1, platform.NativeStopIgnoredWhilePullingCount);
			Assert.True(platform.IsNativePulling);
			Assert.True(platform.HasPendingNativeActivity);

			platform.ReleaseBelowThresholdPull();
			elementHandler.DisconnectHandler();
		}

		[Fact]
		public void DisposeDuringPullThenAboveThresholdRefreshForcesOwnedCompletion()
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

			platform.BeginBelowThresholdPull();
			handler.Dispose();

			Assert.True(platform.IsTeardownObserverActive);
			Assert.False(platform.IsDisposed);

			platform.ReleaseAboveThresholdPull();

			Assert.Equal(1, platform.TeardownForcedCompletionCount);
			Assert.True(SpinWait.SpinUntil(
				() => platform.IsDisposed,
				TimeSpan.FromSeconds(1)));
			Assert.False(platform.NativeIsRefreshing);
			Assert.False(platform.IsTeardownObserverActive);
			Assert.Equal(1, platform.DisposeCount);

			platform.NotifyNativeIdle();
			Assert.Equal(1, platform.DisposeCount);
			Assert.Equal(0, platform.NativeStateReadAfterDisposeCount);
		}

		[Fact]
		public void CancelledPullRequiresObservedWrapperResetBeforeDisposal()
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
			platform.DelayPullResetCompletion = true;

			platform.BeginBelowThresholdPull();
			handler.Dispose();
			platform.RaiseUiExtensionsCancelled();

			Assert.Equal(1, platform.UiExtensionsCancelledWithoutResetCount);
			Assert.True(platform.IsNativePulling);
			Assert.False(SpinWait.SpinUntil(
				() => platform.IsDisposed,
				TimeSpan.FromMilliseconds(150)));

			platform.ApplyWrapperCancellationReset();

			Assert.Equal(1, platform.ExplicitCancellationResetCount);
			Assert.True(platform.HasPendingPullReset);
			Assert.False(platform.IsNativePulling);
			Assert.False(SpinWait.SpinUntil(
				() => platform.IsDisposed,
				TimeSpan.FromMilliseconds(150)));

			platform.CompletePullReset();

			Assert.True(SpinWait.SpinUntil(
				() => platform.IsDisposed,
				TimeSpan.FromSeconds(1)));
			Assert.False(platform.HasPendingPullReset);
			Assert.Equal(1, platform.DisposeCount);
			Assert.Equal(0, platform.NativeStateReadAfterDisposeCount);
		}

		[Fact]
		public void TeardownRejectsAStartedGestureInjectedBeforeAtomicDisposal()
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
			platform.DelayPullResetCompletion = true;

			platform.BeginBelowThresholdPull();
			handler.Dispose();
			platform.BeforeDisposeRecheck = platform.BeginBelowThresholdPull;
			platform.ReleaseBelowThresholdPull();
			platform.CompletePullReset();

			Assert.True(SpinWait.SpinUntil(
				() => platform.IsDisposed,
				TimeSpan.FromSeconds(1)));
			Assert.Equal(1, platform.RejectedStartedGestureCount);
			Assert.Equal(0, platform.AtomicDisposalDeferralCount);
			Assert.False(platform.HasPendingNativeActivity);
			Assert.Equal(1, platform.DisposeCount);
			Assert.Equal(0, platform.NativeStateReadAfterDisposeCount);
		}

		[Fact]
		public void StartRequestedDuringResetReplaysExactlyOnceAfterResetCompletes()
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
			platform.DelayPullResetCompletion = true;

			platform.BeginBelowThresholdPull();
			platform.ReleaseBelowThresholdPull();
			view.IsRefreshing = true;

			Assert.True(platform.HasQueuedStart);
			Assert.Equal(0, platform.NativeRefreshStartCount);

			platform.CompletePullReset();

			Assert.True(platform.IsRefreshing);
			Assert.False(platform.HasQueuedStart);
			Assert.Equal(1, platform.NativeRefreshStartCount);
			Thread.Sleep(50);
			Assert.Equal(1, platform.NativeRefreshStartCount);

			view.IsRefreshing = false;
			Assert.True(SpinWait.SpinUntil(
				() => !platform.HasPendingNativeActivity,
				TimeSpan.FromSeconds(1)));
			elementHandler.DisconnectHandler();
		}

		[Theory]
		[InlineData("false")]
		[InlineData("disable")]
		[InlineData("disconnect")]
		public void QueuedResetStartIsCancelledByLaterIntent(string cancellation)
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
			platform.DelayPullResetCompletion = true;

			platform.BeginBelowThresholdPull();
			platform.ReleaseBelowThresholdPull();
			view.IsRefreshing = true;
			Assert.True(platform.HasQueuedStart);

			switch (cancellation)
			{
				case "false":
					view.IsRefreshing = false;
					break;
				case "disable":
					view.IsRefreshEnabled = false;
					break;
				default:
					elementHandler.DisconnectHandler();
					break;
			}

			Assert.False(platform.HasQueuedStart);
			platform.CompletePullReset();
			Assert.Equal(0, platform.NativeRefreshStartCount);

			if (cancellation != "disconnect")
				elementHandler.DisconnectHandler();
			else
				Assert.True(platform.TryDisposeNativeResources());
		}

		[Fact]
		public void DisposeDuringActiveRefreshImmediatelyStartsOwnedCompletion()
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
			Assert.True(platform.IsRefreshing);
			Assert.Equal(1, platform.NativeRefreshStartCount);

			handler.Dispose();

			Assert.Equal(1, platform.TeardownActiveRefreshCompletionCount);
			Assert.False(platform.IsRefreshing);
			Assert.True(platform.HasPendingNativeActivity);
			Assert.False(platform.IsDisposed);

			platform.NotifyNativeIdle();

			Assert.True(SpinWait.SpinUntil(
				() => platform.IsDisposed,
				TimeSpan.FromSeconds(1)));
			Assert.Equal(1, platform.NativeRefreshStartCount);
			Assert.Equal(1, platform.DisposeCount);
			Assert.Equal(0, platform.NativeStateReadAfterDisposeCount);
		}

		sealed class ControlsApp : Application
		{
		}
	}
}
