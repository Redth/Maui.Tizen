using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Handler contract for <see cref="ILabel"/> on Tizen.</summary>
	/// <remarks>
	/// MAUI's <c>Microsoft.Maui.Handlers.ILabelHandler</c> redeclares
	/// <c>new PlatformView PlatformView { get; }</c> where <c>PlatformView</c> is a per-TFM alias
	/// (<c>System.Object</c> off-platform, <c>Tizen.UIExtensions.NUI.Label</c> on <c>-tizen</c>).
	/// An explicit interface implementation must match that type exactly, so a single multi-targeted
	/// out-of-repo backend cannot implement it (CS9333). See docs/net11-status.md
	/// ("Required public MAUI API gaps").
	/// </remarks>
	public interface ITizenLabelHandler : IViewHandler
	{
		/// <summary>Gets the cross-platform view.</summary>
		new ILabel VirtualView { get; }

		/// <summary>Gets the platform view.</summary>
		new TizenLabelView PlatformView { get; }
	}

	/// <summary>Handler contract for <see cref="IWindow"/> on Tizen.</summary>
	public interface ITizenWindowHandler : IElementHandler
	{
		/// <summary>Gets the cross-platform window.</summary>
		new IWindow VirtualView { get; }

		/// <summary>Gets the platform window.</summary>
		new TizenNativeWindow PlatformView { get; }
	}

	/// <summary>Handler contract for <see cref="IApplication"/> on Tizen.</summary>
	public interface ITizenApplicationHandler : IElementHandler
	{
		/// <summary>Gets the cross-platform application.</summary>
		new IApplication VirtualView { get; }

		/// <summary>Gets the platform application.</summary>
		new TizenNativeApplication PlatformView { get; }
	}

	/// <summary>Handler contract for <see cref="IContentView"/> on Tizen.</summary>
	/// <remarks>
	/// MAUI's own <c>Microsoft.Maui.Handlers.IContentViewHandler</c> binds <c>PlatformView</c> to
	/// <c>Microsoft.Maui.Platform.ContentViewGroup</c> on a <c>-tizen</c> target framework, so an
	/// out-of-repo backend that owns its platform views cannot implement it. See
	/// docs/net11-status.md ("Required public MAUI API gaps").
	/// </remarks>
	public interface ITizenContentViewHandler : IViewHandler
	{
		/// <summary>Gets the cross-platform view.</summary>
		new IContentView VirtualView { get; }

		/// <summary>Gets the platform view.</summary>
		new TizenContentViewGroup PlatformView { get; }
	}

	/// <summary>Handler contract for a page on Tizen.</summary>
	public interface ITizenPageHandler : ITizenContentViewHandler
	{
	}

	/// <summary>Handler contract for <see cref="ILayout"/> on Tizen.</summary>
	/// <remarks>
	/// Member names deliberately match <c>Microsoft.Maui.ILayoutHandler</c>: MAUI Controls raises
	/// child operations through <c>Handler.Invoke(nameof(ILayoutHandler.Add), ...)</c>, i.e. by
	/// command-mapper key string, so keeping the names identical preserves interop without
	/// implementing MAUI's TFM-bound interface.
	/// </remarks>
	public interface ITizenLayoutHandler : IViewHandler
	{
		/// <summary>Gets the cross-platform view.</summary>
		new ILayout VirtualView { get; }

		/// <summary>Gets the platform view.</summary>
		new TizenLayoutViewGroup PlatformView { get; }

		/// <summary>Adds a child view.</summary>
		/// <param name="view">The child to add.</param>
		void Add(IView view);

		/// <summary>Removes a child view.</summary>
		/// <param name="view">The child to remove.</param>
		void Remove(IView view);

		/// <summary>Removes every child view.</summary>
		void Clear();

		/// <summary>Inserts a child view at the given index.</summary>
		/// <param name="index">The target index.</param>
		/// <param name="view">The child to insert.</param>
		void Insert(int index, IView view);

		/// <summary>Replaces the child view at the given index.</summary>
		/// <param name="index">The target index.</param>
		/// <param name="view">The replacement child.</param>
		void Update(int index, IView view);

		/// <summary>Re-orders a child view according to its z-index.</summary>
		/// <param name="view">The child to re-order.</param>
		void UpdateZIndex(IView view);
	}
}
