// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Controls.Handlers.RoundRectangleHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone handler.
// It is deliberately NOT named RoundRectangleHandler, which still exists in Microsoft.Maui.Controls.

using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Handlers;

using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="RoundRectangle"/>.</summary>
	public class TizenRoundRectangleHandler : TizenShapeViewHandler
	{
		public static IPropertyMapper<RoundRectangle, TizenRoundRectangleHandler> Mapper =
			new PropertyMapper<RoundRectangle, TizenRoundRectangleHandler>(TizenShapeViewHandler.Mapper)
			{
				[nameof(RoundRectangle.CornerRadius)] = MapCornerRadius,
			};

		public static CommandMapper<RoundRectangle, TizenRoundRectangleHandler> CommandMapper =
			new(TizenShapeViewHandler.CommandMapper)
			{
			};

		public TizenRoundRectangleHandler()
			: base(Mapper, CommandMapper)
		{{
		}}

		public TizenRoundRectangleHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{{
		}}

		public TizenRoundRectangleHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{{
		}}

		public static void MapCornerRadius(TizenRoundRectangleHandler handler, RoundRectangle roundRectangle) =>
			handler.PlatformView?.InvalidateShape(roundRectangle);
	}
}
