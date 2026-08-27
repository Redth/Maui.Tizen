// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;

namespace Microsoft.Maui.Platforms.Tizen.Hosting
{
	/// <summary>
	/// Registers the Tizen handlers for containers, images, graphics and swipe interaction.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Kept separate from <see cref="TizenHandlerCollectionExtensions"/> and
	/// <see cref="TizenControlHandlerCollectionExtensions"/> so a host can adopt these without the
	/// application/window slice or the simple controls, matching how the migration is staged.
	/// </para>
	/// <para>
	/// Registration is explicit. Nothing here uses reflection over private members.
	/// </para>
	/// </remarks>
	public static class TizenContentHandlerCollectionExtensions
	{
		/// <summary>
		/// Adds the Tizen handlers for scroll view, border, image, image button, graphics view,
		/// shape view, refresh view, swipe view and its items, and indicator view.
		/// </summary>
		/// <remarks>
		/// The Controls-level shape handlers (BoxView, Line, Path, Polygon, Polyline, Rectangle,
		/// RoundRectangle) are registered separately from Maui.Tizen.Controls, because they map
		/// <c>Microsoft.Maui.Controls</c> types that this assembly does not reference.
		/// </remarks>
		/// <param name="handlers">The handler collection.</param>
		/// <returns>The handler collection, for chaining.</returns>
		public static IMauiHandlersCollection AddTizenContentHandlers(this IMauiHandlersCollection handlers)
		{
			ArgumentNullException.ThrowIfNull(handlers);

			handlers.AddHandler<IScrollView, TizenScrollViewHandler>();
			handlers.AddHandler<IBorderView, TizenBorderHandler>();
			handlers.AddHandler<IImage, TizenImageHandler>();
			handlers.AddHandler<IImageButton, TizenImageButtonHandler>();
			handlers.AddHandler<IGraphicsView, TizenGraphicsViewHandler>();
			handlers.AddHandler<IShapeView, TizenShapeViewHandler>();
			handlers.AddHandler<IRefreshView, TizenRefreshViewHandler>();
			handlers.AddHandler<ISwipeView, TizenSwipeViewHandler>();
			handlers.AddHandler<ISwipeItemView, TizenSwipeItemViewHandler>();
			handlers.AddHandler<ISwipeItemMenuItem, TizenSwipeItemMenuItemHandler>();
			handlers.AddHandler<IIndicatorView, TizenIndicatorViewHandler>();

			return handlers;
		}
	}
}
