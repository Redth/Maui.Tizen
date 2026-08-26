// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.RefreshViewHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named RefreshViewHandler, which still
// exists in Microsoft.Maui.Core.

using System;
using Microsoft.Maui.Platform;

namespace Microsoft.Maui.Handlers
{
	/// <summary>Tizen handler for <see cref="IRefreshView"/>.</summary>
	public class TizenRefreshViewHandler : ViewHandler<IRefreshView, MauiRefreshLayout>
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

		protected override MauiRefreshLayout CreatePlatformView() => new MauiRefreshLayout();

		protected override void ConnectHandler(MauiRefreshLayout platformView)
		{
			base.ConnectHandler(platformView);
			platformView.Refreshing += OnRefreshing;
		}

		protected override void DisconnectHandler(MauiRefreshLayout platformView)
		{
			platformView.Refreshing -= OnRefreshing;
			platformView.Content = null;
			base.DisconnectHandler(platformView);
		}

		void OnRefreshing(object? sender, EventArgs e)
		{
			VirtualView.IsRefreshing = true;
		}

		public static void MapIsRefreshing(TizenRefreshViewHandler handler, IRefreshView refreshView) =>
			handler.PlatformView.UpdateIsRefreshing(refreshView);

		public static void MapContent(TizenRefreshViewHandler handler, IRefreshView refreshView) =>
			handler.PlatformView.UpdateContent(handler.VirtualView.Content, handler.MauiContext);

		public static void MapRefreshColor(TizenRefreshViewHandler handler, IRefreshView refreshView) =>
			handler.PlatformView.UpdateRefreshColor(refreshView);

		public static void MapBackground(TizenRefreshViewHandler handler, IRefreshView view) =>
			handler.PlatformView.UpdateBackground(view);

		/// <summary>
		/// Intentional no-op. Tizen's <c>RefreshLayout</c> exposes no API to disable the pull gesture
		/// while keeping the control enabled, so <see cref="IRefreshView.IsRefreshEnabled"/> cannot be
		/// honoured independently. Disabling the whole view via <c>IsEnabled</c> still works.
		/// </summary>
		public static void MapIsRefreshEnabled(TizenRefreshViewHandler handler, IRefreshView refreshView)
		{
		}
	}
}
