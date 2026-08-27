// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.RefreshViewHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named RefreshViewHandler, which still
// exists in Microsoft.Maui.Core.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="IRefreshView"/>.</summary>
	public class TizenRefreshViewHandler : TizenViewHandler<IRefreshView, TizenRefreshLayout>
	{
		public static IPropertyMapper<IRefreshView, TizenRefreshViewHandler> Mapper =
			new PropertyMapper<IRefreshView, TizenRefreshViewHandler>(ViewMapper)
			{
				[nameof(IRefreshView.IsRefreshing)] = MapIsRefreshing,
				[nameof(IRefreshView.Content)] = MapContent,
				[nameof(IRefreshView.RefreshColor)] = MapRefreshColor,
				[nameof(IRefreshView.IsRefreshEnabled)] = MapIsRefreshEnabled,
				[nameof(IView.Background)] = MapBackground,
			};

		public static CommandMapper<IRefreshView, TizenRefreshViewHandler> CommandMapper =
			new(ViewCommandMapper)
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
			base.ConnectHandler(platformView);
			platformView.Refreshing += OnRefreshing;
		}

		protected override void DisconnectHandler(TizenRefreshLayout platformView)
		{
			platformView.Refreshing -= OnRefreshing;

			// Stop any refresh in flight before the view goes away.
			platformView.IsRefreshing = false;

			// The content handler is owned here, so tearing this handler down must tear it down too.
			platformView.DisposeContentHandler();
			platformView.Content = null;

			base.DisconnectHandler(platformView);
		}

		void OnRefreshing(object? sender, EventArgs e)
		{
			// Tizen's RefreshLayout has no API to disable the pull gesture, so the gesture still
			// fires when IsRefreshEnabled is false. Refusing it here is what actually makes the
			// property mean something: without this the control refreshes anyway and immediately
			// snaps back, which reads as a glitch rather than as "disabled".
			if (!VirtualView.IsRefreshEnabled)
			{
				PlatformView.IsRefreshing = false;
				return;
			}

			VirtualView.IsRefreshing = true;
		}

		public static void MapIsRefreshing(TizenRefreshViewHandler handler, IRefreshView refreshView)
		{
			// A refresh that was started before the view was disabled must be cancelled, not left
			// spinning forever with no way to complete it.
			if (refreshView.IsRefreshing && !refreshView.IsRefreshEnabled)
			{
				handler.PlatformView.IsRefreshing = false;
				refreshView.IsRefreshing = false;
				return;
			}

			handler.PlatformView.UpdateIsRefreshing(refreshView);
		}

		public static void MapContent(TizenRefreshViewHandler handler, IRefreshView refreshView) =>
			handler.PlatformView.UpdateContent(handler.VirtualView.Content, handler.MauiContext);

		public static void MapRefreshColor(TizenRefreshViewHandler handler, IRefreshView refreshView) =>
			handler.PlatformView.UpdateRefreshColor(refreshView);

		public static void MapBackground(TizenRefreshViewHandler handler, IRefreshView view) =>
			handler.PlatformView.UpdateBackground(view);

		/// <summary>
		/// Applies <see cref="IRefreshView.IsRefreshEnabled"/>.
		/// </summary>
		/// <remarks>
		/// Tizen's <c>RefreshLayout</c> has no property to disable the pull gesture, so this cannot
		/// be pushed to the native view directly. It is enforced instead at the two points that
		/// matter: an incoming gesture is refused, and a refresh already running when the view is
		/// disabled is cancelled. Previously this mapper was an empty body and the property had no
		/// effect at all.
		/// </remarks>
		public static void MapIsRefreshEnabled(TizenRefreshViewHandler handler, IRefreshView refreshView)
		{
			if (!refreshView.IsRefreshEnabled && handler.PlatformView.IsRefreshing)
			{
				handler.PlatformView.IsRefreshing = false;
				refreshView.IsRefreshing = false;
			}
		}
	}
}
