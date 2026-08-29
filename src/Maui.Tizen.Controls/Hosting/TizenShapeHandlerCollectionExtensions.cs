// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;

namespace Microsoft.Maui.Platforms.Tizen.Hosting
{
	/// <summary>
	/// Registers the Tizen handlers for the Controls-level shapes.
	/// </summary>
	/// <remarks>
	/// These live in Maui.Tizen.Controls rather than Maui.Tizen.Core because they map
	/// <c>Microsoft.Maui.Controls</c> types. Core registers <c>IShapeView</c>; this adds the
	/// concrete shapes, which MAUI Controls otherwise maps to its own neutral handlers.
	/// </remarks>
	public static class TizenShapeHandlerCollectionExtensions
	{
		/// <summary>
		/// Adds the Tizen handlers for BoxView, Line, Path, Polygon, Polyline, Rectangle and
		/// RoundRectangle.
		/// </summary>
		/// <param name="handlers">The handler collection.</param>
		/// <returns>The handler collection, for chaining.</returns>
		public static IMauiHandlersCollection AddTizenShapeHandlers(this IMauiHandlersCollection handlers)
		{
			ArgumentNullException.ThrowIfNull(handlers);

			handlers.AddHandler<BoxView, TizenBoxViewHandler>();
			handlers.AddHandler<Ellipse, TizenShapeViewHandler>();
			handlers.AddHandler<Line, TizenLineHandler>();
			handlers.AddHandler<Microsoft.Maui.Controls.Shapes.Path, TizenPathHandler>();
			handlers.AddHandler<Polygon, TizenPolygonHandler>();
			handlers.AddHandler<Polyline, TizenPolylineHandler>();
			handlers.AddHandler<Rectangle, TizenRectangleHandler>();
			handlers.AddHandler<RoundRectangle, TizenRoundRectangleHandler>();

			return handlers;
		}
	}
}
