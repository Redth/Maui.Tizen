using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platforms.Tizen.LifecycleEvents;
using Tizen.NUI;
using TizenLifecycleEvents = Microsoft.Maui.Platforms.Tizen.LifecycleEvents.TizenLifecycle;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Bridges <see cref="Tizen.Applications.CoreApplication"/> lifecycle callbacks onto MAUI's
	/// application, window and handler plumbing.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ported from <c>Microsoft.Maui.MauiApplication</c> (Tizen) in dotnet/maui.
	/// </para>
	/// <para>
	/// <b>Naming:</b> MAUI's type is <c>Microsoft.Maui.MauiApplication</c>. Re-declaring that exact
	/// name here would be a CS0433 hazard for any consumer that also references the
	/// <c>net11.0-tizen*</c> build of <c>Microsoft.Maui.dll</c>, which still ships its own
	/// <c>MauiApplication</c>. This backend therefore keeps the shape and behaviour but owns the
	/// name <c>TizenMauiApplication</c> in the <c>Microsoft.Maui.Platforms.Tizen</c> namespace.
	/// </para>
	/// </remarks>
	public abstract class TizenMauiApplication : NUIApplication, IPlatformApplication
	{
		const string FontCacheFolderName = "fonts";

		IMauiContext? _applicationContext;
		IApplication? _application;
		IServiceProvider? _services;
		IServiceScope? _windowScope;

		/// <summary>Initializes a new instance of the <see cref="TizenMauiApplication"/> class.</summary>
		protected TizenMauiApplication()
		{
			Current = this;
			IPlatformApplication.Current = this;
		}

		/// <summary>Gets the running application instance.</summary>
		public static new TizenMauiApplication Current { get; private set; } = null!;

		/// <summary>Creates the <see cref="MauiApp"/> for this process.</summary>
		/// <returns>The configured <see cref="MauiApp"/>.</returns>
		protected abstract MauiApp CreateMauiApp();

		IServiceProvider IPlatformApplication.Services => _services!;

		IApplication IPlatformApplication.Application => _application!;

		/// <inheritdoc />
		protected override void OnPreCreate()
		{
			base.OnPreCreate();

			FocusManager.Instance.EnableDefaultAlgorithm(true);
			TizenNativeView.SetDefaultGrabTouchAfterLeave(true);

			var fontResourcePath = System.IO.Path.Combine(
				global::Tizen.Applications.Application.Current.DirectoryInfo.Resource,
				FontCacheFolderName);
			FontClient.Instance.AddCustomFontDirectory(fontResourcePath);

			var mauiApp = CreateMauiApp();
			var rootContext = new TizenMauiContext(mauiApp.Services);

			var platformWindow = TizenCoreApplicationExtensions.GetDefaultWindow();
			rootContext.AddSpecific(platformWindow);

			_applicationContext = rootContext.MakeApplicationScope(this);
			_services = _applicationContext.Services
				?? throw new InvalidOperationException($"The {nameof(IServiceProvider)} instance was not found.");

			_services.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnPreCreate>(del => del(this));
		}

		/// <inheritdoc />
		protected override void OnCreate()
		{
			base.OnCreate();

			if (_services is null)
				throw new InvalidOperationException($"The {nameof(IServiceProvider)} instance was not found.");

			if (_applicationContext is null)
				throw new InvalidOperationException($"The {nameof(IMauiContext)} instance was not found.");

			_application = _services.GetRequiredService<IApplication>();

			this.SetApplicationHandler(_application, _applicationContext);
			_windowScope = this.CreatePlatformWindow(_application, _applicationContext);

			_services.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnCreate>(del => del(this));
		}

		/// <inheritdoc />
		protected override void OnAppControlReceived(global::Tizen.Applications.AppControlReceivedEventArgs e)
		{
			base.OnAppControlReceived(e);
			_services?.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnAppControlReceived>(del => del(this, e));
		}

		/// <inheritdoc />
		protected override void OnDeviceOrientationChanged(global::Tizen.Applications.DeviceOrientationEventArgs e)
		{
			base.OnDeviceOrientationChanged(e);
			_services?.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnDeviceOrientationChanged>(del => del(this, e));
		}

		/// <inheritdoc />
		protected override void OnLocaleChanged(global::Tizen.Applications.LocaleChangedEventArgs e)
		{
			base.OnLocaleChanged(e);
			_services?.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnLocaleChanged>(del => del(this, e));
		}

		/// <inheritdoc />
		protected override void OnLowBattery(global::Tizen.Applications.LowBatteryEventArgs e)
		{
			base.OnLowBattery(e);
			_services?.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnLowBattery>(del => del(this, e));
		}

		/// <inheritdoc />
		protected override void OnLowMemory(global::Tizen.Applications.LowMemoryEventArgs e)
		{
			base.OnLowMemory(e);
			_services?.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnLowMemory>(del => del(this, e));
		}

		/// <inheritdoc />
		protected override void OnRegionFormatChanged(global::Tizen.Applications.RegionFormatChangedEventArgs e)
		{
			base.OnRegionFormatChanged(e);
			_services?.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnRegionFormatChanged>(del => del(this, e));
		}

		/// <inheritdoc />
		protected override void OnPause()
		{
			base.OnPause();
			_services?.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnPause>(del => del(this));
		}

		/// <inheritdoc />
		protected override void OnResume()
		{
			base.OnResume();
			_services?.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnResume>(del => del(this));
		}

		/// <inheritdoc />
		protected override void OnTerminate()
		{
			base.OnTerminate();
			_services?.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnTerminate>(del => del(this));

			_windowScope?.Dispose();
			_windowScope = null;
		}
	}
}
