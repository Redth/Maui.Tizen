// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.ShapeViewHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named ShapeViewHandler, which still
// exists in Microsoft.Maui.Core.

using Microsoft.Maui;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="IShapeView"/>, drawn through Skia.</summary>
	/// <remarks>
	/// Every stroke/fill property invalidates the whole Skia drawing surface: the Tizen shape view
	/// re-renders from the <see cref="IShapeView"/> on each pass and has no incremental native API.
	/// </remarks>
	public class TizenShapeViewHandler : TizenViewHandler<IShapeView, TizenShapeView>
	{
		public static IPropertyMapper<IShapeView, TizenShapeViewHandler> Mapper =
			new PropertyMapper<IShapeView, TizenShapeViewHandler>(ViewMapper)
			{
				[nameof(IShapeView.Shape)] = MapShape,
				[nameof(IShapeView.Aspect)] = MapAspect,
				[nameof(IShapeView.Fill)] = MapFill,
				[nameof(IShapeView.Stroke)] = MapStroke,
				[nameof(IShapeView.StrokeThickness)] = MapStrokeThickness,
				[nameof(IShapeView.StrokeDashPattern)] = MapStrokeDashPattern,
				[nameof(IShapeView.StrokeDashOffset)] = MapStrokeDashOffset,
				[nameof(IShapeView.StrokeLineCap)] = MapStrokeLineCap,
				[nameof(IShapeView.StrokeLineJoin)] = MapStrokeLineJoin,
				[nameof(IShapeView.StrokeMiterLimit)] = MapStrokeMiterLimit,

				// Controls-contributed key. Microsoft.Maui.Controls.Shapes.Shape.RemapForControls()
				// adds StrokeDashArray to the NEUTRAL ShapeViewHandler.Mapper, which this handler does
				// not chain, so without re-declaring it here the property would silently do nothing.
				// The name is a literal because StrokeDashArray is a Controls concept and this
				// assembly deliberately does not reference Microsoft.Maui.Controls; there is no
				// StrokeDashArray on IShapeView to take nameof from.
				[StrokeDashArrayKey] = MapStrokeDashArray,
			};

		public static CommandMapper<IShapeView, TizenShapeViewHandler> CommandMapper =
			new(ViewCommandMapper)
			{
			};

		public TizenShapeViewHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenShapeViewHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenShapeViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenShapeView CreatePlatformView() => new TizenShapeView();


		public static void MapShape(TizenShapeViewHandler handler, IShapeView shapeView)
		{
			handler.PlatformView?.UpdateShape(shapeView);
		}

		public static void MapAspect(TizenShapeViewHandler handler, IShapeView shapeView) => handler.PlatformView?.InvalidateShape(shapeView);

		public static void MapFill(TizenShapeViewHandler handler, IShapeView shapeView) => handler.PlatformView?.InvalidateShape(shapeView);

		public static void MapStroke(TizenShapeViewHandler handler, IShapeView shapeView) => handler.PlatformView?.InvalidateShape(shapeView);

		public static void MapStrokeThickness(TizenShapeViewHandler handler, IShapeView shapeView) => handler.PlatformView?.InvalidateShape(shapeView);

		public static void MapStrokeDashPattern(TizenShapeViewHandler handler, IShapeView shapeView) => handler.PlatformView?.InvalidateShape(shapeView);

		public static void MapStrokeDashOffset(TizenShapeViewHandler handler, IShapeView shapeView) => handler.PlatformView?.InvalidateShape(shapeView);

		public static void MapStrokeLineCap(TizenShapeViewHandler handler, IShapeView shapeView) => handler.PlatformView?.InvalidateShape(shapeView);

		public static void MapStrokeLineJoin(TizenShapeViewHandler handler, IShapeView shapeView) => handler.PlatformView?.InvalidateShape(shapeView);

		public static void MapStrokeMiterLimit(TizenShapeViewHandler handler, IShapeView shapeView) => handler.PlatformView?.InvalidateShape(shapeView);

		/// <summary>
		/// The <c>StrokeDashArray</c> mapper key, contributed by Microsoft.Maui.Controls rather than
		/// declared on <see cref="IShapeView"/>.
		/// </summary>
		public const string StrokeDashArrayKey = "StrokeDashArray";

		/// <summary>
		/// Redraws the shape when the Controls-level <c>StrokeDashArray</c> changes.
		/// </summary>
		/// <remarks>
		/// Mirrors upstream <c>Shape.Tizen.cs</c>, which invalidates the shape. The dash array feeds
		/// <see cref="IShapeView.StrokeDashPattern"/>, so a redraw is all that is required.
		/// </remarks>
		public static void MapStrokeDashArray(TizenShapeViewHandler handler, IShapeView shapeView) => handler.PlatformView?.InvalidateShape(shapeView);
	}
}
