using Microsoft.Maui;
using Tizen.Applications;

namespace Microsoft.Maui.Platforms.Tizen.LifecycleEvents
{
	/// <summary>
	/// Tizen lifecycle event delegates.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.LifecycleEvents.TizenLifecycle</c> in dotnet/maui. The type
	/// name is kept but the namespace is this backend's, so it cannot collide (CS0433) with MAUI's
	/// own Tizen build.
	/// </remarks>
	public static class TizenLifecycle
	{
		// Raised from CoreUIApplication overrides.

		/// <summary>Raised when the application is paused.</summary>
		/// <param name="application">The platform application.</param>
		public delegate void OnPause(CoreApplication application);

		/// <summary>Raised before the application is created.</summary>
		/// <param name="application">The platform application.</param>
		public delegate void OnPreCreate(CoreApplication application);

		/// <summary>Raised when the application resumes.</summary>
		/// <param name="application">The platform application.</param>
		public delegate void OnResume(CoreApplication application);

		// Raised from CoreApplication overrides.

		/// <summary>Raised when an app control is received.</summary>
		/// <param name="application">The platform application.</param>
		/// <param name="e">The event arguments.</param>
		public delegate void OnAppControlReceived(CoreApplication application, AppControlReceivedEventArgs e);

		/// <summary>Raised when the application is created.</summary>
		/// <param name="application">The platform application.</param>
		public delegate void OnCreate(CoreApplication application);

		/// <summary>Raised when the device orientation changes.</summary>
		/// <param name="application">The platform application.</param>
		/// <param name="e">The event arguments.</param>
		public delegate void OnDeviceOrientationChanged(CoreApplication application, DeviceOrientationEventArgs e);

		/// <summary>Raised when the locale changes.</summary>
		/// <param name="application">The platform application.</param>
		/// <param name="e">The event arguments.</param>
		public delegate void OnLocaleChanged(CoreApplication application, LocaleChangedEventArgs e);

		/// <summary>Raised on low battery.</summary>
		/// <param name="application">The platform application.</param>
		/// <param name="e">The event arguments.</param>
		public delegate void OnLowBattery(CoreApplication application, LowBatteryEventArgs e);

		/// <summary>Raised on low memory.</summary>
		/// <param name="application">The platform application.</param>
		/// <param name="e">The event arguments.</param>
		public delegate void OnLowMemory(CoreApplication application, LowMemoryEventArgs e);

		/// <summary>Raised when the region format changes.</summary>
		/// <param name="application">The platform application.</param>
		/// <param name="e">The event arguments.</param>
		public delegate void OnRegionFormatChanged(CoreApplication application, RegionFormatChangedEventArgs e);

		/// <summary>Raised when the application terminates.</summary>
		/// <param name="application">The platform application.</param>
		public delegate void OnTerminate(CoreApplication application);

		/// <summary>
		/// Raised once the window-scoped <see cref="IMauiContext"/> has been created.
		/// </summary>
		/// <remarks>
		/// MAUI declares the equivalent delegate <c>internal</c>; it is public here because an
		/// out-of-repo backend has no other way to let a host observe window-scope creation.
		/// </remarks>
		/// <param name="mauiContext">The window-scoped context.</param>
		public delegate void OnMauiContextCreated(IMauiContext mauiContext);
	}
}
