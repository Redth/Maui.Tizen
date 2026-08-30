using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// The Tizen navigation stack that modal pages and modal dialogs are presented on.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is a Tizen-owned contract over <c>Tizen.UIExtensions.NUI.NavigationStack</c>. Declaring
	/// it here keeps modal coordination - which entry is on top, when a placeholder is pushed and
	/// popped, how a push is unwound when it faults - independent of NUI and therefore testable
	/// off device.
	/// </para>
	/// <para>
	/// One stack exists per window. Register it in the window scope.
	/// </para>
	/// </remarks>
	public interface ITizenNavigationStack
	{
		/// <summary>Gets the number of entries on the stack.</summary>
		int Count { get; }

		/// <summary>Gets the entry currently on top, or <see langword="null"/> when empty.</summary>
		object? Top { get; }

		/// <summary>Returns whether <paramref name="platformView"/> is currently in the stack.</summary>
		/// <param name="platformView">The native view to find.</param>
		bool Contains(object platformView);

		/// <summary>Returns whether <paramref name="platformView"/> has been disposed.</summary>
		/// <param name="platformView">The native view to inspect.</param>
		bool IsDisposed(object platformView);

		/// <summary>
		/// Gets or sets a value indicating whether the entry below the top one stays visible.
		/// </summary>
		/// <remarks>
		/// Used when pushing a placeholder for a dialog: the dialog floats above the page, so the
		/// page underneath must keep rendering.
		/// </remarks>
		bool ShownBehindPage { get; set; }

		/// <summary>
		/// Creates an empty native view suitable for use as a placeholder entry.
		/// </summary>
		/// <remarks>
		/// Dialogs are native popups rather than stack entries, but the stack still has to know
		/// something modal is on screen so that back-button handling and page ordering stay
		/// correct. This lets that placeholder be created without a dependency on NUI.
		/// </remarks>
		object CreatePlaceholder();

		/// <summary>Pushes <paramref name="platformView"/> onto the stack.</summary>
		/// <param name="platformView">The native view to present.</param>
		/// <param name="animated"><see langword="true"/> to animate the transition.</param>
		Task PushAsync(object platformView, bool animated);

		/// <summary>Pops the top entry off the stack.</summary>
		/// <param name="animated"><see langword="true"/> to animate the transition.</param>
		Task PopAsync(bool animated);

		/// <summary>
		/// Removes <paramref name="platformView"/> from anywhere in the stack.
		/// </summary>
		/// <param name="platformView">The native view to remove.</param>
		/// <returns>
		/// <see langword="true"/> when the native stack disposed the view while removing it;
		/// otherwise <see langword="false"/>.
		/// </returns>
		/// <remarks>
		/// Used to unwind a placeholder that is no longer on top because something else was pushed
		/// while a dialog was open.
		/// </remarks>
		bool Remove(object platformView);
	}

	/// <summary>
	/// Releases a disposable modal-page handler after the native navigation stack has already
	/// disposed the captured platform or container view.
	/// </summary>
	/// <remarks>
	/// A handler that exposes a distinct disposable <see cref="IViewHandler.ContainerView"/> must
	/// implement this contract before that container can be presented modally. The implementation
	/// must release all remaining handler resources without disposing
	/// the captured platform view again.
	/// </remarks>
	public interface ITizenModalHandlerLifetime
	{
		/// <summary>
		/// Disposes the handler while preserving the already-disposed captured view.
		/// </summary>
		/// <param name="platformView">
		/// The platform or container view that the native navigation stack already disposed.
		/// </param>
		void DisposeAfterPlatformViewDisposed(object platformView);
	}

	/// <summary>
	/// Turns a modal <see cref="Page"/> into the native view that represents it, and releases it
	/// again once the modal is dismissed.
	/// </summary>
	public interface ITizenModalPageRealizer
	{
		/// <summary>
		/// Creates or returns the native view for <paramref name="page"/>.
		/// </summary>
		/// <param name="page">The page to realize.</param>
		/// <param name="mauiContext">The window-scoped context to realize it in.</param>
		object Realize(Page page, IMauiContext mauiContext);

		/// <summary>
		/// Releases the platform view created for <paramref name="page"/>.
		/// </summary>
		/// <param name="page">The page whose handler should be released.</param>
		/// <param name="platformView">The platform view returned by <see cref="Realize"/>.</param>
		/// <param name="platformViewDisposed">
		/// Whether the native stack already disposed <paramref name="platformView"/>.
		/// </param>
		void Release(Page page, object platformView, bool platformViewDisposed);
	}

	/// <summary>
	/// Installs the handler invoked when the hardware or software back button is pressed.
	/// </summary>
	public interface ITizenWindowBackButton
	{
		/// <summary>
		/// Registers a back-button handler ahead of the window's existing fallback handler.
		/// </summary>
		/// <param name="handler">
		/// Returns <see langword="true"/> when the press was handled and should not fall through to
		/// the platform's default behaviour.
		/// </param>
		/// <returns>A registration that restores the previous routing when disposed.</returns>
		IDisposable RegisterBackButtonPressedHandler(Func<bool> handler);
	}
}
