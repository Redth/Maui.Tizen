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

		/// <summary>
		/// Whether <see cref="IWindow.Stopped"/> has been raised and not yet answered by a
		/// <see cref="IWindow.Resumed"/>.
		/// </summary>
		/// <remarks>
		/// This is what distinguishes a cold start from a return to the foreground. Tizen delivers
		/// OnResume in both cases and gives no hint which it is.
		/// </remarks>
		bool _stopped;

		/// <summary>Gets the current window, if the application has created one.</summary>
		/// <returns>The window, or <see langword="null"/>.</returns>
		public static IWindow? GetCurrentWindow()
		{
			var windows = IPlatformApplication.Current?.Application?.Windows;
			if (windows is null || windows.Count == 0)
				return null;

			return windows[0];
		}

		/// <summary>Raises <see cref="IWindow.Created"/>.</summary>
		/// <remarks>
		/// <see cref="IWindow.Activated"/> is deliberately NOT raised here; it follows from the
		/// OnResume that Tizen always delivers next. Raising it here would emit it before the
		/// window is actually foregrounded, and would double up with that OnResume.
		/// </remarks>
		public void OnCreate()
		{
			var window = GetCurrentWindow();
			if (window is null)
				return;

			if (_created)
				return;

			window.Created();
			_created = true;
		}

		/// <summary>
		/// Raises <see cref="IWindow.Resumed"/> when returning from the background, then
		/// <see cref="IWindow.Activated"/>.
		/// </summary>
		/// <remarks>
		/// A cold start is <c>Created</c> &#8594; <c>Activated</c>, with NO <c>Resumed</c>.
		/// <c>Resumed</c> means "came back from being stopped", which is how MAUI defines it and
		/// what Android does - it raises Resumed from OnRestart, not from the first OnStart. Tizen
		/// delivers OnResume for both cases and does not say which, so the bridge has to remember.
		///
		/// Raising Resumed unconditionally made every cold start look like a return from the
		/// background, so any app doing restore-state work in Resumed did it on first launch too.
		/// </remarks>
		public void OnResume()
		{
			var window = GetCurrentWindow();
			if (window is null)
				return;

			if (_stopped)
			{
				window.Resumed();
				_stopped = false;
			}

			Activate(window);
		}

		/// <summary>Raises <see cref="IWindow.Deactivated"/> and <see cref="IWindow.Stopped"/>.</summary>
		/// <remarks>
		/// Both are suppressed if already in that state. Tizen can deliver OnPause more than once
		/// without an intervening OnResume, and a duplicate Stopped is observable to anything that
		/// pairs it with Resumed.
		/// </remarks>
		public void OnPause()
		{
			var window = GetCurrentWindow();
			if (window is null)
				return;

			Deactivate(window);

			if (_stopped)
				return;

			window.Stopped();
			_stopped = true;
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
			_stopped = false;
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
