namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Resolves the native Tizen window that a <see cref="IMauiContext"/> belongs to.
	/// </summary>
	/// <remarks>
	/// Alert requests are routed per window. The alert subscription compares the window that owns
	/// the requesting page against the window it was created for, so a dialog raised on one window
	/// is never presented on another. This contract exists so that window affinity can be
	/// exercised without a native NUI window.
	/// </remarks>
	public interface ITizenPlatformWindowProvider
	{
		/// <summary>
		/// Gets an object identifying the native window backing <paramref name="context"/>,
		/// or <see langword="null"/> when the context is not attached to a window.
		/// </summary>
		/// <param name="context">The context to resolve. May be <see langword="null"/>.</param>
		object? GetPlatformWindow(IMauiContext? context);
	}
}
