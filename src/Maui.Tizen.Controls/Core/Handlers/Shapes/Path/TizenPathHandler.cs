// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Controls.Handlers.PathHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone handler.
// It is deliberately NOT named PathHandler, which still exists in Microsoft.Maui.Controls.

using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Handlers;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>Tizen handler for <see cref="Path"/>.</summary>
	public class TizenPathHandler : TizenShapeViewHandler
	{
		public static IPropertyMapper<Path, TizenPathHandler> Mapper =
			new PropertyMapper<Path, TizenPathHandler>(TizenShapeViewHandler.Mapper)
			{
				[nameof(Path.Data)] = MapData,
				[nameof(Path.RenderTransform)] = MapRenderTransform,
			};

		public static CommandMapper<Path, TizenPathHandler> CommandMapper =
			new(TizenShapeViewHandler.CommandMapper)
			{
			};

		public TizenPathHandler()
			: base(Mapper, CommandMapper)
		{{
		}}

		public TizenPathHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{{
		}}

		public TizenPathHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{{
		}}

		public static void MapData(TizenPathHandler handler, Path path) =>
			handler.PlatformView?.UpdateShape(path);

		public static void MapRenderTransform(TizenPathHandler handler, Path path)
		{
			IDrawable? drawable = handler.PlatformView?.Drawable;

			if (drawable is null)
				return;

			if (drawable is ShapeDrawable shapeDrawable)
			{
				Matrix? matrix = path.RenderTransform?.Value;

				if (matrix is not null)
				{
					shapeDrawable.UpdateRenderTransform(matrix.Value.ToMatrix3X2());
				}
			}

			handler.PlatformView?.InvalidateShape(path);
		}
	}
}
