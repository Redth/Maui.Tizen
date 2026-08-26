// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Controls.Handlers.RectangleHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone handler.
// It is deliberately NOT named RectangleHandler, which still exists in Microsoft.Maui.Controls.

using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Microsoft.Maui.Controls.Handlers
{
	/// <summary>Tizen handler for <see cref="Rectangle"/>.</summary>
	public class TizenRectangleHandler : TizenShapeViewHandler
	{
		public static IPropertyMapper<Rectangle, TizenRectangleHandler> Mapper =
			new PropertyMapper<Rectangle, TizenRectangleHandler>(TizenShapeViewHandler.Mapper)
			{
				[nameof(Rectangle.RadiusX)] = MapRadiusX,
				[nameof(Rectangle.RadiusY)] = MapRadiusY,
			};

		public static CommandMapper<Rectangle, TizenRectangleHandler> CommandMapper =
			new(TizenShapeViewHandler.CommandMapper)
			{
			};

		public TizenRectangleHandler()
			: base(Mapper, CommandMapper)
		{{
		}}

		public TizenRectangleHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{{
		}}

		public TizenRectangleHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{{
		}}

		public static void MapRadiusX(TizenRectangleHandler handler, Rectangle rectangle) =>
			handler.PlatformView?.InvalidateShape(rectangle);

		public static void MapRadiusY(TizenRectangleHandler handler, Rectangle rectangle) =>
			handler.PlatformView?.InvalidateShape(rectangle);
	}
}
