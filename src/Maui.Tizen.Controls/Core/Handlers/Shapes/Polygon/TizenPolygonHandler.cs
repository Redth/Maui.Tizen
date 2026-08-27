// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Controls.Handlers.PolygonHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone handler.
// It is deliberately NOT named PolygonHandler, which still exists in Microsoft.Maui.Controls.

using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using System.Collections.Specialized;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Handlers;

using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="Polygon"/>.</summary>
	public class TizenPolygonHandler : TizenShapeViewHandler
	{
		public static new IPropertyMapper<Polygon, TizenPolygonHandler> Mapper =
			new PropertyMapper<Polygon, TizenPolygonHandler>(TizenShapeViewHandler.Mapper)
			{
				// Shape is remapped here as well as inherited: the base mapper replaces the whole
				// ShapeDrawable, which discards the winding mode applied to the old one.
				[nameof(IShapeView.Shape)] = MapShape,
				[nameof(Polygon.Points)] = MapPoints,
				[nameof(Polygon.FillRule)] = MapFillRule,
			};

		public static new CommandMapper<Polygon, TizenPolygonHandler> CommandMapper =
			new(TizenShapeViewHandler.CommandMapper)
			{
			};

		public TizenPolygonHandler()
			: base(Mapper, CommandMapper)
		{{
		}}

		public TizenPolygonHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{{
		}}

		public TizenPolygonHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{{
		}}

		Microsoft.Maui.Controls.PointCollection? _points;

		protected override void ConnectHandler(TizenShapeView platformView)
		{
			if (VirtualView is Polygon polygon)
			{
				UpdatePointsSubscription(polygon.Points);
			}

			base.ConnectHandler(platformView);
		}

		protected override void DisconnectHandler(TizenShapeView platformView)
		{
			ClearPointsSubscription();

			base.DisconnectHandler(platformView);
		}

		// Upstream this lived in the neutral half of the handler. The points collection is mutable,
		// so the handler has to redraw on collection changes, not just on Points being reassigned.
		void UpdatePointsSubscription(Microsoft.Maui.Controls.PointCollection? points)
		{
			ClearPointsSubscription();

			_points = points;

			if (_points is not null)
			{
				_points.CollectionChanged += OnPointsCollectionChanged;
			}
		}

		void ClearPointsSubscription()
		{
			if (_points is not null)
			{
				_points.CollectionChanged -= OnPointsCollectionChanged;
				_points = null;
			}
		}

		void OnPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			if (VirtualView is IShapeView shapeView)
			{
				PlatformView?.InvalidateShape(shapeView);
			}
		}

		public static void MapPoints(TizenPolygonHandler handler, Polygon polygon)
		{
			handler.UpdatePointsSubscription(polygon.Points);
			ApplyFillRule(handler, polygon);
		}

		/// <summary>Rebuilds the drawable for a new shape, preserving the winding mode.</summary>
		/// <remarks>
		/// <c>UpdateShape</c> assigns a brand new <c>ShapeDrawable</c>, so the fill rule pushed into
		/// the old one is lost. Without reapplying it, an EvenOdd polygon silently reverts to
		/// NonZero the next time its shape changes.
		/// </remarks>
		public static void MapShape(TizenPolygonHandler handler, Polygon polygon)
		{
			handler.PlatformView?.UpdateShape(polygon);
			ApplyFillRule(handler, polygon);
		}

		public static void MapFillRule(TizenPolygonHandler handler, Polygon polygon) =>
			ApplyFillRule(handler, polygon);

		static void ApplyFillRule(TizenPolygonHandler handler, Polygon polygon)
		{
			if (handler.PlatformView?.Drawable is not ShapeDrawable shapeDrawable)
				return;

			shapeDrawable.UpdateWindingMode(
				polygon.FillRule == FillRule.EvenOdd ? WindingMode.EvenOdd : WindingMode.NonZero);

			handler.PlatformView?.InvalidateShape(polygon);
		}
	}
}
