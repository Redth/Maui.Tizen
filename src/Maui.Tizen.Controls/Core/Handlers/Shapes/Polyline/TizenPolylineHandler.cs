// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Controls.Handlers.PolylineHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone handler.
// It is deliberately NOT named PolylineHandler, which still exists in Microsoft.Maui.Controls.

using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using System.Collections.Specialized;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Handlers;

using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="Polyline"/>.</summary>
	public class TizenPolylineHandler : TizenShapeViewHandler
	{
		public static new IPropertyMapper<Polyline, TizenPolylineHandler> Mapper =
			new PropertyMapper<Polyline, TizenPolylineHandler>(TizenShapeViewHandler.Mapper)
			{
				// Shape is remapped here as well as inherited: the base mapper replaces the whole
				// ShapeDrawable, which discards the winding mode applied to the old one.
				[nameof(IShapeView.Shape)] = MapShape,
				[nameof(Polyline.Points)] = MapPoints,
				[nameof(Polyline.FillRule)] = MapFillRule,
			};

		public static new CommandMapper<Polyline, TizenPolylineHandler> CommandMapper =
			new(TizenShapeViewHandler.CommandMapper)
			{
			};

		public TizenPolylineHandler()
			: base(Mapper, CommandMapper)
		{
			{
			}
		}

		public TizenPolylineHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
			{
			}
		}

		public TizenPolylineHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
			{
			}
		}

		Microsoft.Maui.Controls.PointCollection? _points;

		protected override void ConnectHandler(TizenShapeView platformView)
		{
			if (VirtualView is Polyline polyline)
			{
				UpdatePointsSubscription(polyline.Points);
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

		public static void MapPoints(TizenPolylineHandler handler, Polyline polyline)
		{
			handler.UpdatePointsSubscription(polyline.Points);
			ApplyFillRule(handler, polyline);
		}

		/// <summary>Rebuilds the drawable for a new shape, preserving the winding mode.</summary>
		/// <remarks>
		/// <c>UpdateShape</c> assigns a brand new <c>ShapeDrawable</c>, so the fill rule pushed into
		/// the old one is lost. Without reapplying it, an EvenOdd polygon silently reverts to
		/// NonZero the next time its shape changes.
		/// </remarks>
		public static void MapShape(TizenPolylineHandler handler, Polyline polyline)
		{
			handler.LivePlatformView?.UpdateShape(polyline);
			ApplyFillRule(handler, polyline);
		}

		public static void MapFillRule(TizenPolylineHandler handler, Polyline polyline) =>
			ApplyFillRule(handler, polyline);

		static void ApplyFillRule(TizenPolylineHandler handler, Polyline polyline)
		{
			if (handler.LivePlatformView?.Drawable is not ShapeDrawable shapeDrawable)
				return;

			shapeDrawable.UpdateWindingMode(
				polyline.FillRule == FillRule.EvenOdd ? WindingMode.EvenOdd : WindingMode.NonZero);

			handler.LivePlatformView?.InvalidateShape(polyline);
		}
	}
}
