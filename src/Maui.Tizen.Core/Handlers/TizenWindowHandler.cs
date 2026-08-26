using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for <see cref="IWindow"/>.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.Handlers.WindowHandler</c> (Tizen) in dotnet/maui. The platform
	/// view is the single NUI <c>Window</c> instance owned by the process.
	/// </remarks>
	public class TizenWindowHandler : ElementHandler<IWindow, TizenNativeWindow>, IWindowHandler
	{
		/// <summary>Property mapper for <see cref="IWindow"/> on Tizen.</summary>
		public static readonly IPropertyMapper<IWindow, IWindowHandler> Mapper =
			new PropertyMapper<IWindow, IWindowHandler>(ElementMapper, WindowHandler.Mapper)
			{
				[nameof(IWindow.Title)] = MapTitle,
				[nameof(IWindow.Content)] = MapContent,
				[nameof(IWindow.X)] = MapX,
				[nameof(IWindow.Y)] = MapY,
				[nameof(IWindow.Width)] = MapWidth,
				[nameof(IWindow.Height)] = MapHeight,
			};

		/// <summary>Command mapper for <see cref="IWindow"/> on Tizen.</summary>
		public static readonly CommandMapper<IWindow, IWindowHandler> CommandMapper =
			new(ElementCommandMapper)
			{
				[nameof(IWindow.RequestDisplayDensity)] = MapRequestDisplayDensity,
			};

		/// <summary>Initializes a new instance of the <see cref="TizenWindowHandler"/> class.</summary>
		public TizenWindowHandler()
			: base(Mapper, CommandMapper)
		{
		}

		/// <summary>Initializes a new instance of the <see cref="TizenWindowHandler"/> class.</summary>
		/// <param name="mapper">An optional property mapper override.</param>
		/// <param name="commandMapper">An optional command mapper override.</param>
		public TizenWindowHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IWindow IWindowHandler.VirtualView => VirtualView;

		object IWindowHandler.PlatformView => PlatformView;

		/// <inheritdoc />
		protected override TizenNativeWindow CreatePlatformElement() =>
			MauiContext?.GetPlatformWindow()
			?? throw new InvalidOperationException(
				$"{nameof(MauiContext)} did not contain a platform window.");

		/// <summary>
		/// Maps <see cref="IWindow.Title"/>. Not implemented on Tizen, matching dotnet/maui.
		/// </summary>
		/// <param name="handler">The handler.</param>
		/// <param name="window">The window.</param>
		public static void MapTitle(IWindowHandler handler, IWindow window)
		{
		}

		/// <summary>Maps <see cref="IWindow.Content"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="window">The window.</param>
		public static void MapContent(IWindowHandler handler, IWindow window)
		{
			var mauiContext = handler.MauiContext ?? throw new InvalidOperationException(
				$"{nameof(handler.MauiContext)} should have been set by base class.");

#if TIZEN
			var platformContent = window.Content!.ToPlatformView(mauiContext);
			((TizenNativeWindow)handler.PlatformView).SetMainContent(platformContent, window.Content);
#else
			_ = window.Content;
#endif

			window.VisualDiagnosticsOverlay?.Initialize();
		}

		/// <summary>Maps <see cref="IWindow.X"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="window">The window.</param>
		public static void MapX(IWindowHandler handler, IWindow window)
		{
#if TIZEN
			((TizenNativeWindow?)handler.PlatformView)?.UpdateX(window);
#endif
		}

		/// <summary>Maps <see cref="IWindow.Y"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="window">The window.</param>
		public static void MapY(IWindowHandler handler, IWindow window)
		{
#if TIZEN
			((TizenNativeWindow?)handler.PlatformView)?.UpdateY(window);
#endif
		}

		/// <summary>Maps <see cref="IWindow.Width"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="window">The window.</param>
		public static void MapWidth(IWindowHandler handler, IWindow window)
		{
#if TIZEN
			((TizenNativeWindow?)handler.PlatformView)?.UpdateWidth(window);
#endif
		}

		/// <summary>Maps <see cref="IWindow.Height"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="window">The window.</param>
		public static void MapHeight(IWindowHandler handler, IWindow window)
		{
#if TIZEN
			((TizenNativeWindow?)handler.PlatformView)?.UpdateHeight(window);
#endif
		}

		/// <summary>Maps <see cref="IWindow.RequestDisplayDensity"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="window">The window.</param>
		/// <param name="args">The <see cref="DisplayDensityRequest"/>.</param>
		public static void MapRequestDisplayDensity(IWindowHandler handler, IWindow window, object? args)
		{
			if (args is DisplayDensityRequest request)
				request.SetResult((float)TizenDisplayDensity.Current);
		}
	}
}
