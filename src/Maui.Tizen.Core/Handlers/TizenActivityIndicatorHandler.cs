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
	public class TizenActivityIndicatorHandler : TizenViewHandler<IActivityIndicator, TizenActivityIndicatorView>
	{
		/// <summary>The complete property mapper for <see cref="IActivityIndicator"/>.</summary>
		public static readonly IPropertyMapper<IActivityIndicator, TizenActivityIndicatorHandler> Mapper =
			new PropertyMapper<IActivityIndicator, TizenActivityIndicatorHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(IActivityIndicator.IsRunning)] = MapIsRunning,
				[nameof(IActivityIndicator.Color)] = MapColor,
			};

		/// <summary>The complete command mapper for <see cref="IActivityIndicator"/>.</summary>
		public static readonly CommandMapper<IActivityIndicator, TizenActivityIndicatorHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper);

		public TizenActivityIndicatorHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenActivityIndicatorHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenActivityIndicatorView CreatePlatformView() => new();

		public static void MapIsRunning(TizenActivityIndicatorHandler handler, IActivityIndicator activityIndicator)
		{
#if TIZEN
			handler.PlatformView?.UpdateIsRunning(activityIndicator);
#endif
		}

		public static void MapColor(TizenActivityIndicatorHandler handler, IActivityIndicator activityIndicator)
		{
#if TIZEN
			handler.PlatformView?.UpdateColor(activityIndicator);
#endif
		}
	}
}
