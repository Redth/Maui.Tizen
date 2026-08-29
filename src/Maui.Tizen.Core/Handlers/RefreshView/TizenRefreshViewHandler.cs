// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.RefreshViewHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named RefreshViewHandler, which still
// exists in Microsoft.Maui.Core.

using System;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="IRefreshView"/>.</summary>
	public class TizenRefreshViewHandler : TizenViewHandler<IRefreshView, TizenRefreshLayout>
	{
		readonly TizenDisconnectingState _disconnecting = new();

		public static IPropertyMapper<IRefreshView, TizenRefreshViewHandler> Mapper =
			new PropertyMapper<IRefreshView, TizenRefreshViewHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(IRefreshView.IsRefreshing)] = MapIsRefreshing,
				[nameof(IRefreshView.Content)] = MapContent,
				[nameof(IRefreshView.RefreshColor)] = MapRefreshColor,
				[nameof(IRefreshView.IsRefreshEnabled)] = MapIsRefreshEnabled,
				[nameof(IView.Background)] = MapBackground,
			};

		public static CommandMapper<IRefreshView, TizenRefreshViewHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper)
			{
			};

		public TizenRefreshViewHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenRefreshViewHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenRefreshViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenRefreshLayout CreatePlatformView() => new TizenRefreshLayout();

		protected override void ConnectHandler(TizenRefreshLayout platformView)
		{
			_disconnecting.Connected();
			var dispatcher = TizenDispatchExtensions.CaptureDispatcher(this);
			var replacement = new TizenRefreshCoordinator(
				platformView.RefreshState,
				token => platformView.WaitForNativeIdleAsync(
					dispatcher,
					frameToken => Task.Delay(TizenTicker.FrameIntervalMilliseconds, frameToken),
					token),
				dispatcher,
				platformView.ApplyRefreshState,
				() =>
					ReferenceEquals(((IElementHandler)this).PlatformView, platformView) &&
					ReferenceEquals(VirtualView?.Handler, this));

			TizenCleanup.Run(
				() => _refreshCoordinator?.Dispose(),
				() => _refreshCoordinator = replacement,
				() => base.ConnectHandler(platformView),
				() => platformView.Refreshing += OnRefreshing,
				() => platformView.NativePullTerminated += OnNativePullTerminated);
		}

		TizenRefreshCoordinator? _refreshCoordinator;

		protected override void DisconnectHandler(TizenRefreshLayout platformView)
		{
			var coordinator = _refreshCoordinator;
			_refreshCoordinator = null;

			TizenCleanup.Run(
				_disconnecting.BeginDisconnect,
				platformView.MarkDisconnected,
				() => platformView.Refreshing -= OnRefreshing,
				() => platformView.NativePullTerminated -= OnNativePullTerminated,
				() => coordinator?.Dispose(),
				platformView.DisposeContentHandler,
				() => base.DisconnectHandler(platformView));
		}

		protected override void Dispose(bool disposing)
		{
			if (!disposing)
			{
				base.Dispose(disposing);
				return;
			}

			var platformView = ((IElementHandler)this).PlatformView as TizenRefreshLayout;
			var coordinator = _refreshCoordinator;

			if (platformView is not null
				&& coordinator is not null
				&& (coordinator.IsCompleting || platformView.HasPendingNativeActivity))
			{
				platformView.BeginTeardownObservation();
				var releasePlatform = coordinator.PreparePlatformDisposal(
					platformView.DisposeNativeResources,
					platformView.HasPendingNativeActivity);
				TizenCleanup.Run(
					() => ((IElementHandler)this).DisconnectHandler(),
					() => releasePlatform().FireAndForget(this));
				return;
			}

			if (platformView is not null)
			{
				TizenCleanup.Run(
					() => ((IElementHandler)this).DisconnectHandler(),
					platformView.DisposeNativeResources);
				return;
			}

			base.Dispose(disposing);
		}

		void RequestRefresh(bool desired, bool enabled)
		{
			if (_disconnecting.IsDisconnecting
				|| ((IElementHandler)this).PlatformView is not TizenRefreshLayout)
				return;

			var replay = _refreshCoordinator?.Request(desired, enabled);
			replay?.FireAndForget(this);
		}

		void OnRefreshing(object? sender, EventArgs e)
		{
			Platform(this)?.ObserveNativeRefreshStarted();
			_refreshCoordinator?.ObserveNativeStart();

			// Tizen's RefreshLayout has no API to disable the pull gesture, so the gesture still
			// fires when IsRefreshEnabled is false. Refusing it here is what actually makes the
			// property mean something: without this the control refreshes anyway and immediately
			// snaps back, which reads as a glitch rather than as "disabled".
			if (!VirtualView.IsRefreshEnabled)
			{
				RequestRefresh(desired: false, enabled: false);
				return;
			}

			VirtualView.IsRefreshing = true;
		}

		void OnNativePullTerminated(object? sender, EventArgs e)
		{
			if (sender is not TizenRefreshLayout platformView
				|| !ReferenceEquals(Platform(this), platformView)
				|| VirtualView.IsRefreshEnabled)
				return;

			_refreshCoordinator?.ObserveNativeStart();
			RequestRefresh(desired: false, enabled: false);
		}

		public static void MapIsRefreshing(TizenRefreshViewHandler handler, IRefreshView refreshView)
		{
			var platformView = Platform(handler);
			if (platformView is null)
				return;

			if (DeferDisableWhilePulling(platformView, refreshView))
				return;

			// A refresh that was started before the view was disabled must be cancelled, not left
			// spinning forever with no way to complete it.
			if (refreshView.IsRefreshing && !refreshView.IsRefreshEnabled)
			{
				refreshView.IsRefreshing = false;
			}

			handler.RequestRefresh(refreshView.IsRefreshing, refreshView.IsRefreshEnabled);
		}

		public static void MapContent(TizenRefreshViewHandler handler, IRefreshView refreshView)
		{
			if (handler._disconnecting.IsDisconnecting
				|| ((IElementHandler)handler).PlatformView is not TizenRefreshLayout platformView)
				return;

			var content = refreshView.Content;
			platformView.UpdateContent(
				content,
				handler.MauiContext,
				() =>
					ReferenceEquals(handler.VirtualView, refreshView) &&
					ReferenceEquals(handler.VirtualView.Content, content));
		}

		public static void MapRefreshColor(TizenRefreshViewHandler handler, IRefreshView refreshView) =>
			Platform(handler)?.UpdateRefreshColor(refreshView);

		public static void MapBackground(TizenRefreshViewHandler handler, IRefreshView view)
		{
			var platformView = Platform(handler);
			if (platformView is null)
				return;

			TizenCleanup.Run(
				() => TizenViewMappers.MapBackground(handler, view),
				() => platformView.UpdateBackground(view));
		}

		/// <summary>
		/// Applies <see cref="IRefreshView.IsRefreshEnabled"/>.
		/// </summary>
		/// <remarks>
		/// Tizen's <c>RefreshLayout</c> has no property to disable the pull gesture, so this cannot
		/// be pushed to the native view directly. It is enforced instead at the two points that
		/// matter: an incoming refresh is refused, and a refresh already running when the view is
		/// disabled is stopped. UIExtensions ignores <c>IsRefreshing = false</c> while its private
		/// state is Pulling, so a below-threshold pull defers that stop until the real terminal touch
		/// event arrives. Previously this mapper was an empty body and the property had no effect.
		/// </remarks>
		public static void MapIsRefreshEnabled(TizenRefreshViewHandler handler, IRefreshView refreshView)
		{
			var platformView = Platform(handler);
			if (platformView is null)
				return;

			if (!refreshView.IsRefreshEnabled)
			{
				if (DeferDisableWhilePulling(platformView, refreshView))
					return;

				if (refreshView.IsRefreshing)
					refreshView.IsRefreshing = false;

				handler.RequestRefresh(desired: false, enabled: false);
				return;
			}

			platformView.CancelDeferredNativeDisable();
			handler.RequestRefresh(refreshView.IsRefreshing, enabled: true);
		}

		static bool DeferDisableWhilePulling(
			TizenRefreshLayout platformView,
			IRefreshView refreshView)
		{
			if (refreshView.IsRefreshEnabled
				|| !platformView.DeferDisableUntilNativePullTerminates())
				return false;

			if (refreshView.IsRefreshing)
				refreshView.IsRefreshing = false;

			return true;
		}

		static TizenRefreshLayout? Platform(TizenRefreshViewHandler handler) =>
			!handler._disconnecting.IsDisconnecting &&
			TizenHandlerLifecycle.TryGetLivePlatformView(handler, out TizenRefreshLayout? platformView)
				? platformView
				: null;
	}
}
