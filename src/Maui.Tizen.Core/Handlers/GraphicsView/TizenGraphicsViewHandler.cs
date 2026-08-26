// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.GraphicsViewHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so that neutral handler no
// longer has a Tizen half to complete and a partial declaration would not bind. This is a
// standalone handler that owns its own mappers.
//
// It is deliberately NOT named GraphicsViewHandler: that type still exists in Microsoft.Maui.Core
// and re-declaring the name would be ambiguous for consumers referencing both assemblies.

using Microsoft.Maui.Platform;

namespace Microsoft.Maui.Handlers
{
	/// <summary>Tizen handler for <see cref="IGraphicsView"/>, backed by the Skia drawing surface.</summary>
	public class TizenGraphicsViewHandler : ViewHandler<IGraphicsView, PlatformTouchGraphicsView>
	{
		public static IPropertyMapper<IGraphicsView, TizenGraphicsViewHandler> Mapper =
			new PropertyMapper<IGraphicsView, TizenGraphicsViewHandler>(ViewMapper)
			{
				[nameof(IView.Background)] = MapBackground,
				[nameof(IGraphicsView.Drawable)] = MapDrawable,
				[nameof(IView.FlowDirection)] = MapFlowDirection,
			};

		public static CommandMapper<IGraphicsView, TizenGraphicsViewHandler> CommandMapper =
			new(ViewCommandMapper)
			{
				[nameof(IGraphicsView.Invalidate)] = MapInvalidate,
			};

		public TizenGraphicsViewHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenGraphicsViewHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenGraphicsViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override PlatformTouchGraphicsView CreatePlatformView() => new PlatformTouchGraphicsView();

		protected override void ConnectHandler(PlatformTouchGraphicsView platformView)
		{
			platformView.Connect(VirtualView);
			base.ConnectHandler(platformView);
		}

		protected override void DisconnectHandler(PlatformTouchGraphicsView platformView)
		{
			platformView.Disconnect();
			base.DisconnectHandler(platformView);
		}

		public static void MapDrawable(TizenGraphicsViewHandler handler, IGraphicsView graphicsView)
		{
			handler.PlatformView?.UpdateDrawable(graphicsView);
		}

		public static void MapFlowDirection(TizenGraphicsViewHandler handler, IGraphicsView graphicsView)
		{
			handler.PlatformView?.UpdateFlowDirection(graphicsView);
			handler.PlatformView?.Invalidate();
		}

		public static void MapInvalidate(TizenGraphicsViewHandler handler, IGraphicsView graphicsView, object? arg)
		{
			handler.PlatformView?.Invalidate();
		}
	}
}
