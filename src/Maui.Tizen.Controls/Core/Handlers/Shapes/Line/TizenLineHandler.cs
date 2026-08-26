// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Controls.Handlers.LineHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone handler.
// It is deliberately NOT named LineHandler, which still exists in Microsoft.Maui.Controls.

using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Handlers;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>Tizen handler for <see cref="Line"/>.</summary>
	public class TizenLineHandler : TizenShapeViewHandler
	{
		public static IPropertyMapper<Line, TizenLineHandler> Mapper =
			new PropertyMapper<Line, TizenLineHandler>(TizenShapeViewHandler.Mapper)
			{
				[nameof(Line.X1)] = MapX1,
				[nameof(Line.Y1)] = MapY1,
				[nameof(Line.X2)] = MapX2,
				[nameof(Line.Y2)] = MapY2,
			};

		public static CommandMapper<Line, TizenLineHandler> CommandMapper =
			new(TizenShapeViewHandler.CommandMapper)
			{
			};

		public TizenLineHandler()
			: base(Mapper, CommandMapper)
		{{
		}}

		public TizenLineHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{{
		}}

		public TizenLineHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{{
		}}

		public static void MapX1(TizenLineHandler handler, Line line) =>
			handler.PlatformView?.InvalidateShape(line);

		public static void MapY1(TizenLineHandler handler, Line line) =>
			handler.PlatformView?.InvalidateShape(line);

		public static void MapX2(TizenLineHandler handler, Line line) =>
			handler.PlatformView?.InvalidateShape(line);

		public static void MapY2(TizenLineHandler handler, Line line) =>
			handler.PlatformView?.InvalidateShape(line);
	}
}
