using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for <see cref="IApplication"/>.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.Handlers.ApplicationHandler</c> (Tizen) in dotnet/maui,
	/// including the <c>"Terminate"</c> command key that MAUI Controls raises by string.
	/// </remarks>
	public class TizenApplicationHandler : ElementHandler<IApplication, TizenNativeApplication>, ITizenApplicationHandler
	{
		/// <summary>
		/// Command key used to terminate the application. Matches
		/// <c>Microsoft.Maui.Handlers.ApplicationHandler.TerminateCommandKey</c>, which is internal
		/// in MAUI but raised by key string from Controls.
		/// </summary>
		public const string TerminateCommandKey = "Terminate";

		/// <summary>Property mapper for <see cref="IApplication"/> on Tizen.</summary>
		public static readonly IPropertyMapper<IApplication, ITizenApplicationHandler> Mapper =
			new PropertyMapper<IApplication, ITizenApplicationHandler>(ElementMapper);

		/// <summary>Command mapper for <see cref="IApplication"/> on Tizen.</summary>
		public static readonly CommandMapper<IApplication, ITizenApplicationHandler> CommandMapper =
			new(ElementCommandMapper)
			{
				[TerminateCommandKey] = MapTerminate,
				[nameof(IApplication.OpenWindow)] = MapOpenWindow,
				[nameof(IApplication.CloseWindow)] = MapCloseWindow,
				[nameof(IApplication.ActivateWindow)] = MapActivateWindow,
			};

		/// <summary>Initializes a new instance of the <see cref="TizenApplicationHandler"/> class.</summary>
		public TizenApplicationHandler()
			: base(Mapper, CommandMapper)
		{
		}

		/// <summary>Initializes a new instance of the <see cref="TizenApplicationHandler"/> class.</summary>
		/// <param name="mapper">An optional property mapper override.</param>
		/// <param name="commandMapper">An optional command mapper override.</param>
		public TizenApplicationHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IApplication ITizenApplicationHandler.VirtualView => VirtualView;

		TizenNativeApplication ITizenApplicationHandler.PlatformView => PlatformView;

		/// <inheritdoc />
		protected override TizenNativeApplication CreatePlatformElement() =>
			MauiContext?.Services.GetService<TizenNativeApplication>()
			?? throw new InvalidOperationException(
				$"{nameof(MauiContext)} did not contain a valid platform application.");

		/// <summary>Maps the <c>Terminate</c> command.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="application">The application.</param>
		/// <param name="args">Unused.</param>
		public static void MapTerminate(ITizenApplicationHandler handler, IApplication application, object? args)
		{
#if TIZEN
			handler.PlatformView?.Exit();
#endif
		}

		/// <summary>Maps <see cref="IApplication.OpenWindow"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="application">The application.</param>
		/// <param name="args">The <see cref="OpenWindowRequest"/>, when supplied.</param>
		public static void MapOpenWindow(ITizenApplicationHandler handler, IApplication application, object? args)
		{
			// Tizen exposes a single NUI window per process, so this is a no-op - the same
			// behaviour as dotnet/maui's Tizen ApplicationHandler.
		}

		/// <summary>Maps <see cref="IApplication.CloseWindow"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="application">The application.</param>
		/// <param name="args">The <see cref="IWindow"/> to close.</param>
		public static void MapCloseWindow(ITizenApplicationHandler handler, IApplication application, object? args)
		{
#if TIZEN
			if (args is IWindow window)
				(window.Handler?.PlatformView as TizenNativeWindow)?.Dispose();
#endif
		}

		/// <summary>Maps <see cref="IApplication.ActivateWindow"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="application">The application.</param>
		/// <param name="args">The <see cref="IWindow"/> to activate.</param>
		public static void MapActivateWindow(ITizenApplicationHandler handler, IApplication application, object? args)
		{
#if TIZEN
			if (args is IWindow window && window.Handler?.PlatformView is global::Tizen.NUI.Window platformWindow)
				platformWindow.Raise();
#endif
		}
	}
}
