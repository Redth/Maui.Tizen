// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="IActivityIndicator"/>.
	/// </summary>
	public class TizenActivityIndicatorHandler : TizenViewHandler<IActivityIndicator, TizenActivityIndicatorView>, IActivityIndicatorHandler
	{
		/// <summary>The complete property mapper for <see cref="IActivityIndicator"/>.</summary>
		public static readonly IPropertyMapper<IActivityIndicator, IActivityIndicatorHandler> Mapper =
			new PropertyMapper<IActivityIndicator, IActivityIndicatorHandler>(TizenHandlerMappers.Chain(ActivityIndicatorHandler.Mapper))
			{
				[nameof(IActivityIndicator.IsRunning)] = MapIsRunning,
				[nameof(IActivityIndicator.Color)] = MapColor,
			};

		/// <summary>The complete command mapper for <see cref="IActivityIndicator"/>.</summary>
		public static readonly CommandMapper<IActivityIndicator, IActivityIndicatorHandler> CommandMapper =
			new CommandMapper<IActivityIndicator, IActivityIndicatorHandler>(TizenHandlerMappers.ChainCommands(ActivityIndicatorHandler.CommandMapper));

		public TizenActivityIndicatorHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenActivityIndicatorHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		IActivityIndicator IActivityIndicatorHandler.VirtualView => VirtualView;

		/// <remarks>
		/// <see cref="IActivityIndicatorHandler"/> types this as <see cref="object"/>. MAUI ships no Tizen asset,
		/// so this backend resolves the neutral <c>net11.0</c> assembly on every target framework
		/// and the interface is implementable without the per-platform alias mismatch that would
		/// otherwise occur.
		/// </remarks>
		object IActivityIndicatorHandler.PlatformView => PlatformView;

		/// <summary>
		/// The typed platform view for a mapping.
		/// </summary>
		/// <remarks>
		/// <see cref="IActivityIndicatorHandler"/> types <c>PlatformView</c> as <see cref="object"/>, because MAUI's
		/// neutral assembly has no Tizen alias. Mappings therefore narrow it here rather than at
		/// every call site.
		/// </remarks>
		/// <param name="handler">The handler.</param>
		/// <returns>The platform view, or <see langword="null"/> if it is not yet created.</returns>
		static TizenActivityIndicatorView? Platform(IActivityIndicatorHandler handler) => handler.PlatformView as TizenActivityIndicatorView;

		/// <summary>The concrete handler, for mappings that need its own state.</summary>
		/// <param name="handler">The handler.</param>
		/// <returns>The concrete handler.</returns>
		static TizenActivityIndicatorHandler AsHandler(IActivityIndicatorHandler handler) => (TizenActivityIndicatorHandler)handler;

		protected override TizenActivityIndicatorView CreatePlatformView() => new();

		public static void MapIsRunning(IActivityIndicatorHandler handler, IActivityIndicator activityIndicator)
		{
#if TIZEN
			Platform(handler)?.UpdateIsRunning(activityIndicator);
#endif
		}

		public static void MapColor(IActivityIndicatorHandler handler, IActivityIndicator activityIndicator)
		{
#if TIZEN
			Platform(handler)?.UpdateColor(activityIndicator);
#endif
		}
	}
}
