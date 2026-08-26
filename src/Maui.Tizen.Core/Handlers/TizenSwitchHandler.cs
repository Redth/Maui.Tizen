// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="ISwitch"/>.
	/// </summary>
	public class TizenSwitchHandler : TizenViewHandler<ISwitch, TizenSwitchView>
	{
		/// <summary>The complete property mapper for <see cref="ISwitch"/>.</summary>
		public static readonly IPropertyMapper<ISwitch, TizenSwitchHandler> Mapper =
			new PropertyMapper<ISwitch, TizenSwitchHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(ISwitch.IsOn)] = MapIsOn,
				[nameof(ISwitch.TrackColor)] = MapTrackColor,
				[nameof(ISwitch.ThumbColor)] = MapThumbColor,
			};

		/// <summary>The complete command mapper for <see cref="ISwitch"/>.</summary>
		public static readonly CommandMapper<ISwitch, TizenSwitchHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper);

		public TizenSwitchHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenSwitchHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenSwitchView CreatePlatformView()
		{
#if TIZEN
			return new() { Focusable = true };
#else
			return new();
#endif
		}

		protected override void ConnectHandler(TizenSwitchView platformView)
		{
			base.ConnectHandler(platformView);
#if TIZEN
			platformView.Toggled += OnToggled;
#endif
		}

		protected override void DisconnectHandler(TizenSwitchView platformView)
		{
#if TIZEN
			if (platformView.HasBody())
				platformView.Toggled -= OnToggled;
#endif
			base.DisconnectHandler(platformView);
		}

		public static void MapIsOn(TizenSwitchHandler handler, ISwitch view)
		{
#if TIZEN
			handler.PlatformView?.UpdateIsOn(view);
#endif
		}

		public static void MapTrackColor(TizenSwitchHandler handler, ISwitch view)
		{
#if TIZEN
			handler.PlatformView?.UpdateTrackColor(view);
#endif
		}

		public static void MapThumbColor(TizenSwitchHandler handler, ISwitch view)
		{
#if TIZEN
			handler.PlatformView?.UpdateThumbColor(view);
#endif
		}

#if TIZEN
		void OnToggled(object? sender, EventArgs e)
		{
			if (VirtualView is null || PlatformView is null)
				return;

			if (VirtualView.IsOn != PlatformView.IsToggled)
				VirtualView.IsOn = PlatformView.IsToggled;
		}
#endif
	}
}
