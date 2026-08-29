// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.IndicatorViewHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named IndicatorViewHandler, which still
// exists in Microsoft.Maui.Core.

using Microsoft.Maui;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="IIndicatorView"/>.</summary>
	/// <remarks>
	/// Appearance properties (size, colours, shape) have no incremental native API on the Tizen page
	/// control, so they rebuild the indicator set via <c>ResetIndicators</c>.
	/// </remarks>
	public class TizenIndicatorViewHandler : TizenViewHandler<IIndicatorView, TizenPageControl>
	{
		const string IndicatorTemplateKey = "IndicatorTemplate";
		readonly TizenDisconnectingState _disconnecting = new();

		public static IPropertyMapper<IIndicatorView, TizenIndicatorViewHandler> Mapper =
			new PropertyMapper<IIndicatorView, TizenIndicatorViewHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(IIndicatorView.Count)] = MapCount,
				[nameof(IIndicatorView.Position)] = MapPosition,
				[nameof(IIndicatorView.HideSingle)] = MapHideSingle,
				[nameof(IIndicatorView.MaximumVisible)] = MapMaximumVisible,
				[nameof(IIndicatorView.IndicatorSize)] = MapIndicatorSize,
				[nameof(IIndicatorView.IndicatorColor)] = MapIndicatorColor,
				[nameof(IIndicatorView.SelectedIndicatorColor)] = MapSelectedIndicatorColor,
				[nameof(IIndicatorView.IndicatorsShape)] = MapIndicatorShape,
				[nameof(IView.Visibility)] = MapVisibility,
				[IndicatorTemplateKey] = MapIndicatorTemplate,
			};

		public static CommandMapper<IIndicatorView, TizenIndicatorViewHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper)
			{
			};

		public TizenIndicatorViewHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenIndicatorViewHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenIndicatorViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenPageControl CreatePlatformView() => new TizenPageControl(VirtualView);

		protected override void ConnectHandler(TizenPageControl platformView)
		{
			_disconnecting.Connected();
			base.ConnectHandler(platformView);
		}

		public override void SetVirtualView(IView view)
		{
			(((IElementHandler)this).PlatformView as TizenPageControl)?.Rebind((IIndicatorView)view);
			base.SetVirtualView(view);
			PlatformView.Rebind(VirtualView);
		}

		/// <inheritdoc />
		/// <remarks>
		/// A templated indicator creates a handler for its template, which this handler owns.
		/// </remarks>
		protected override void DisconnectHandler(TizenPageControl platformView)
		{
			TizenCleanup.Run(
				_disconnecting.BeginDisconnect,
				platformView.DisposeTemplatedViewHandler,
				() => base.DisconnectHandler(platformView));
		}

		public static void MapCount(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.UpdateCount();

		public static void MapPosition(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.UpdatePosition();

		public static void MapHideSingle(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.UpdateCount();

		public static void MapMaximumVisible(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.UpdateCount();

		public static void MapIndicatorSize(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.ResetIndicators();

		public static void MapIndicatorColor(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.ResetIndicators();

		public static void MapSelectedIndicatorColor(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.ResetIndicators();

		public static void MapIndicatorShape(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.ResetIndicators();

		public static void MapVisibility(TizenIndicatorViewHandler handler, IIndicatorView indicator)
		{
			TizenViewMappers.MapVisibility(handler, indicator);
			handler.PlatformView.UpdateCount();
		}

		public static void MapIndicatorTemplate(TizenIndicatorViewHandler handler, IIndicatorView indicator)
		{
			if (!handler._disconnecting.IsDisconnecting
				&& ((IElementHandler)handler).PlatformView is TizenPageControl platformView)
				platformView.ResetIndicators();
		}
	}
}
