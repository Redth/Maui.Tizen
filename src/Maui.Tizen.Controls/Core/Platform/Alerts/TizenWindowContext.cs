using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Exposes the native window associated with the current .NET MAUI window scope.
	/// </summary>
	/// <remarks>
	/// .NET MAUI creates a scoped <see cref="IMauiContext"/> per window, so a service registered
	/// with a scoped lifetime is inherently per window. The Controls scoped initializer attaches
	/// Core's native window to that scope, which lets window-affine services such as the alert
	/// infrastructure discover which window they belong to without taking a dependency on NUI.
	/// </remarks>
	public interface ITizenWindowContext
	{
		/// <summary>
		/// Gets the context of the window that owns this scope, or <see langword="null"/> when no
		/// window has been attached yet.
		/// </summary>
		IMauiContext? MauiContext { get; }

		/// <summary>
		/// Gets the native window that owns this scope, or <see langword="null"/> when no window
		/// has been attached yet.
		/// </summary>
		object? PlatformWindow { get; }
	}

	/// <summary>
	/// Default <see cref="ITizenWindowContext"/> implementation. Register it with a scoped
	/// lifetime so that each .NET MAUI window scope gets its own instance.
	/// </summary>
	public sealed class TizenWindowContext : ITizenWindowContext
	{
		/// <inheritdoc/>
		public IMauiContext? MauiContext { get; private set; }

		/// <inheritdoc/>
		public object? PlatformWindow { get; private set; }

		/// <summary>
		/// Associates a native window with this scope.
		/// </summary>
		/// <param name="mauiContext">The window's context.</param>
		/// <param name="platformWindow">The native window.</param>
		public void Attach(IMauiContext mauiContext, object platformWindow)
		{
			ArgumentNullException.ThrowIfNull(mauiContext);
			ArgumentNullException.ThrowIfNull(platformWindow);

			MauiContext = mauiContext;
			PlatformWindow = platformWindow;
		}

		/// <summary>
		/// Clears the association created by <see cref="Attach"/>.
		/// </summary>
		public void Detach()
		{
			MauiContext = null;
			PlatformWindow = null;
		}

		/// <summary>
		/// Attaches <paramref name="platformWindow"/> to the window scope described by
		/// <paramref name="mauiContext"/>.
		/// </summary>
		/// <param name="mauiContext">The window's context.</param>
		/// <param name="platformWindow">The native window.</param>
		/// <remarks>
		/// This is the single association window-affine services need. It is a no-op when the
		/// application did not register the Tizen
		/// services, so a partially configured host degrades instead of throwing.
		/// </remarks>
		public static void AttachTo(IMauiContext mauiContext, object platformWindow)
		{
			ArgumentNullException.ThrowIfNull(mauiContext);
			ArgumentNullException.ThrowIfNull(platformWindow);

			(mauiContext.Services.GetService<ITizenWindowContext>() as TizenWindowContext)
				?.Attach(mauiContext, platformWindow);
		}
	}

	/// <summary>
	/// Default <see cref="ITizenPlatformWindowProvider"/> implementation. It resolves the native
	/// window through the <see cref="ITizenWindowContext"/> registered in the supplied context's
	/// window scope.
	/// </summary>
	public sealed class TizenPlatformWindowProvider : ITizenPlatformWindowProvider
	{
		/// <inheritdoc/>
		public object? GetPlatformWindow(IMauiContext? context) =>
			context?.Services?.GetService<ITizenWindowContext>()?.PlatformWindow;
	}
}
