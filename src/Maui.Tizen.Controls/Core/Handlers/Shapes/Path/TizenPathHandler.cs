// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Controls.Handlers.PathHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone handler.
// It is deliberately NOT named PathHandler, which still exists in Microsoft.Maui.Controls.

using Microsoft.Maui.Controls.Shapes;
using System.Numerics;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Handlers;

using Microsoft.Maui.Platforms.Tizen;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="Path"/>.</summary>
	public class TizenPathHandler : TizenShapeViewHandler
	{
		public static new IPropertyMapper<Path, TizenPathHandler> Mapper =
			new PropertyMapper<Path, TizenPathHandler>(TizenShapeViewHandler.Mapper)
			{
				// Shape is remapped here as well as inherited: the base mapper replaces the whole
				// ShapeDrawable, which discards the render transform applied to the old one.
				[nameof(IShapeView.Shape)] = MapShape,
				[nameof(Path.Data)] = MapData,
				[nameof(Path.RenderTransform)] = MapRenderTransform,
			};

		public static new CommandMapper<Path, TizenPathHandler> CommandMapper =
			new(TizenShapeViewHandler.CommandMapper)
			{
			};

		public TizenPathHandler()
			: base(Mapper, CommandMapper)
		{
			{
			}
		}

		public TizenPathHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
			{
			}
		}

		public TizenPathHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
			{
			}
		}

		/// <summary>Rebuilds the drawable for a new shape, preserving the render transform.</summary>
		/// <remarks>
		/// <c>UpdateShape</c> assigns a brand new <c>ShapeDrawable</c>, so any transform previously
		/// pushed into the old one is gone. Reapplying it here is what stops a transformed path from
		/// silently snapping back to untransformed the next time its data changes.
		/// </remarks>
		public static void MapShape(TizenPathHandler handler, Path path)
		{
			handler.LivePlatformView?.UpdateShape(path);
			ApplyRenderTransform(handler, path);
		}

		public static void MapData(TizenPathHandler handler, Path path)
		{
			handler.LivePlatformView?.UpdateShape(path);
			ApplyRenderTransform(handler, path);
		}

		public static void MapRenderTransform(TizenPathHandler handler, Path path) =>
			ApplyRenderTransform(handler, path);

		/// <summary>Pushes the path's render transform onto the current drawable.</summary>
		/// <remarks>
		/// A null transform resets to identity rather than being ignored. The imported code only
		/// applied non-null matrices, so clearing RenderTransform left the previous transform in
		/// place on the native drawable and the path stayed transformed.
		/// </remarks>
		static void ApplyRenderTransform(TizenPathHandler handler, Path path)
		{
			if (handler.LivePlatformView?.Drawable is not ShapeDrawable shapeDrawable)
				return;

			var matrix = path.RenderTransform?.Value;

			shapeDrawable.UpdateRenderTransform(
				matrix is null ? Matrix3x2.Identity : matrix.Value.ToMatrix3X2());

			handler.LivePlatformView?.InvalidateShape(path);
		}
	}
}
