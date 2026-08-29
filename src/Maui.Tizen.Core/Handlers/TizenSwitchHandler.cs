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
	public class TizenSwitchHandler : TizenViewHandler<ISwitch, TizenSwitchView>, ISwitchHandler
	{
		/// <summary>The complete property mapper for <see cref="ISwitch"/>.</summary>
		public static readonly IPropertyMapper<ISwitch, ISwitchHandler> Mapper =
			new PropertyMapper<ISwitch, ISwitchHandler>(TizenHandlerMappers.Chain(SwitchHandler.Mapper))
			{
				[nameof(ISwitch.IsOn)] = MapIsOn,
				[nameof(ISwitch.TrackColor)] = MapTrackColor,
				[nameof(ISwitch.ThumbColor)] = MapThumbColor,
			};

		/// <summary>The complete command mapper for <see cref="ISwitch"/>.</summary>
		public static readonly CommandMapper<ISwitch, ISwitchHandler> CommandMapper =
			new CommandMapper<ISwitch, ISwitchHandler>(TizenHandlerMappers.ChainCommands(SwitchHandler.CommandMapper));

		public TizenSwitchHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenSwitchHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		ISwitch ISwitchHandler.VirtualView => VirtualView;

		/// <remarks>
		/// <see cref="ISwitchHandler"/> types this as <see cref="object"/>. MAUI ships no Tizen asset,
		/// so this backend resolves the neutral <c>net11.0</c> assembly on every target framework
		/// and the interface is implementable without the per-platform alias mismatch that would
		/// otherwise occur.
		/// </remarks>
		object ISwitchHandler.PlatformView => PlatformView;

		/// <summary>
		/// The typed platform view for a mapping.
		/// </summary>
		/// <remarks>
		/// <see cref="ISwitchHandler"/> types <c>PlatformView</c> as <see cref="object"/>, because MAUI's
		/// neutral assembly has no Tizen alias. Mappings therefore narrow it here rather than at
		/// every call site.
		/// </remarks>
		/// <param name="handler">The handler.</param>
		/// <returns>The platform view, or <see langword="null"/> if it is not yet created.</returns>
		static TizenSwitchView? Platform(ISwitchHandler handler) => handler.PlatformView as TizenSwitchView;

		/// <summary>The concrete handler, for mappings that need its own state.</summary>
		/// <param name="handler">The handler.</param>
		/// <returns>The concrete handler.</returns>
		static TizenSwitchHandler AsHandler(ISwitchHandler handler) => (TizenSwitchHandler)handler;

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

		public static void MapIsOn(ISwitchHandler handler, ISwitch view)
		{
#if TIZEN
			Platform(handler)?.UpdateIsOn(view);
#endif
		}

		public static void MapTrackColor(ISwitchHandler handler, ISwitch view)
		{
#if TIZEN
			Platform(handler)?.UpdateTrackColor(view);
#endif
		}

		public static void MapThumbColor(ISwitchHandler handler, ISwitch view)
		{
#if TIZEN
			Platform(handler)?.UpdateThumbColor(view);
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
