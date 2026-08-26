using System;
using Microsoft.Maui;
using Microsoft.Maui.Platforms.Tizen.LifecycleEvents;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Bridges Tizen application lifecycle callbacks onto the cross-platform
	/// <see cref="IWindow"/> lifecycle.
	/// </summary>
	/// <remarks>
	/// <para>
	/// MAUI raises <see cref="IWindow.Created"/>, <see cref="IWindow.Activated"/>,
	/// <see cref="IWindow.Deactivated"/>, <see cref="IWindow.Stopped"/>,
	/// <see cref="IWindow.Resumed"/> and <see cref="IWindow.Destroying"/> from each platform's
	/// native lifecycle. Tizen's <c>CoreApplication</c> exposes <c>OnCreate</c>, <c>OnResume</c>,
	/// <c>OnPause</c> and <c>OnTerminate</c>, so without an explicit bridge none of those window
	/// events ever fire and any app relying on them - to save state on backgrounding, for
	/// instance - silently does nothing.
	/// </para>
	/// <para>
	/// Registered by <c>ConfigureTizen</c> as a lifecycle event handler, so it participates in the
	/// same pipeline a host would use and can be observed or supplemented rather than replaced.
	/// </para>
	/// </remarks>
	public sealed class TizenWindowLifecycleBridge
	{
		bool _created;
		bool _active;

		/// <summary>Gets the current window, if the application has created one.</summary>
		/// <returns>The window, or <see langword="null"/>.</returns>
		public static IWindow? GetCurrentWindow()
		{
			var windows = IPlatformApplication.Current?.Application?.Windows;
			if (windows is null || windows.Count == 0)
				return null;

			return windows[0];
		}

		/// <summary>Raises <see cref="IWindow.Created"/> and <see cref="IWindow.Activated"/>.</summary>
		public void OnCreate()
		{
			var window = GetCurrentWindow();
			if (window is null)
				return;

			if (!_created)
			{
				window.Created();
				_created = true;
			}

			Activate(window);
		}

		/// <summary>Raises <see cref="IWindow.Resumed"/> and <see cref="IWindow.Activated"/>.</summary>
		public void OnResume()
		{
			var window = GetCurrentWindow();
			if (window is null)
				return;

			window.Resumed();
			Activate(window);
		}

		/// <summary>Raises <see cref="IWindow.Deactivated"/> and <see cref="IWindow.Stopped"/>.</summary>
		public void OnPause()
		{
			var window = GetCurrentWindow();
			if (window is null)
				return;

			Deactivate(window);
			window.Stopped();
		}

		/// <summary>Raises <see cref="IWindow.Destroying"/>.</summary>
		public void OnTerminate()
		{
			var window = GetCurrentWindow();
			if (window is null)
				return;

			Deactivate(window);
			window.Destroying();
			_created = false;
		}

		void Activate(IWindow window)
		{
			// Activated/Deactivated must be balanced: Tizen can deliver OnResume without a
			// preceding OnPause, and raising Activated twice would be observable to a host that
			// counts them.
			if (_active)
				return;

			window.Activated();
			_active = true;
		}

		void Deactivate(IWindow window)
		{
			if (!_active)
				return;

			window.Deactivated();
			_active = false;
		}
	}
}
