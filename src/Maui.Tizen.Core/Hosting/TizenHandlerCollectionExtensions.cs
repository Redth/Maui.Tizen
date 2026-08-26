using System;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;

namespace Microsoft.Maui.Platforms.Tizen.Hosting
{
	/// <summary>
	/// Registers the Tizen handlers implemented by this backend.
	/// </summary>
	/// <remarks>
	/// Registration is explicit rather than convention- or reflection-based: nothing in this
	/// backend uses private reflection or <c>DispatchProxy</c>.
	/// </remarks>
	public static class TizenHandlerCollectionExtensions
	{
		/// <summary>
		/// Adds the Tizen handlers for the core vertical slice: application, window, content view,
		/// layout and label.
		/// </summary>
		/// <param name="handlers">The handler collection.</param>
		/// <returns>The handler collection, for chaining.</returns>
		public static IMauiHandlersCollection AddTizenHandlers(this IMauiHandlersCollection handlers)
		{
			ArgumentNullException.ThrowIfNull(handlers);

			handlers.AddHandler<IApplication, TizenApplicationHandler>();
			handlers.AddHandler<IWindow, TizenWindowHandler>();
			handlers.AddHandler<IContentView, TizenContentViewHandler>();
			handlers.AddHandler<ILayout, TizenLayoutHandler>();
			handlers.AddHandler<ILabel, TizenLabelHandler>();

			return handlers;
		}

		/// <summary>
		/// Registers <see cref="TizenPageHandler"/> for the supplied page view type.
		/// </summary>
		/// <remarks>
		/// MAUI Core has no <c>IPage</c> abstraction - a page is an <see cref="IContentView"/>, and
		/// MAUI Controls maps <c>Microsoft.Maui.Controls.Page</c> to its own <c>PageHandler</c>
		/// explicitly. Hosts using MAUI Controls should call
		/// <c>AddTizenPageHandler&lt;Microsoft.Maui.Controls.ContentPage&gt;()</c> so content pages
		/// get the page background and arrange behaviour instead of the plain content-view one.
		/// </remarks>
		/// <typeparam name="TPage">The page view type.</typeparam>
		/// <param name="handlers">The handler collection.</param>
		/// <returns>The handler collection, for chaining.</returns>
		public static IMauiHandlersCollection AddTizenPageHandler<TPage>(this IMauiHandlersCollection handlers)
			where TPage : IContentView
		{
			ArgumentNullException.ThrowIfNull(handlers);

			handlers.AddHandler(typeof(TPage), typeof(TizenPageHandler));
			return handlers;
		}
	}
}
