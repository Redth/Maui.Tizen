// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="ICheckBox"/>.
	/// </summary>
	public class TizenCheckBoxHandler : TizenViewHandler<ICheckBox, TizenCheckBoxView>, ICheckBoxHandler
	{
		/// <summary>The complete property mapper for <see cref="ICheckBox"/>.</summary>
		public static readonly IPropertyMapper<ICheckBox, ICheckBoxHandler> Mapper =
			new PropertyMapper<ICheckBox, ICheckBoxHandler>(TizenHandlerMappers.Chain(CheckBoxHandler.Mapper))
			{
				[nameof(ICheckBox.IsChecked)] = MapIsChecked,
				[nameof(ICheckBox.Foreground)] = MapForeground,

				// Added to CheckBoxHandler.Mapper by Controls' RemapForControls, so it only
				// exists once Microsoft.Maui.Controls is loaded. Chaining MAUI's static mapper
				// makes the key reachable; this supplies the Tizen body, which the inherited
				// one would not.
				["Color"] = MapColor,
			};

		/// <summary>The complete command mapper for <see cref="ICheckBox"/>.</summary>
		public static readonly CommandMapper<ICheckBox, ICheckBoxHandler> CommandMapper =
			new CommandMapper<ICheckBox, ICheckBoxHandler>(TizenHandlerMappers.ChainCommands(CheckBoxHandler.CommandMapper));

		public TizenCheckBoxHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenCheckBoxHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		ICheckBox ICheckBoxHandler.VirtualView => VirtualView;

		/// <remarks>
		/// <see cref="ICheckBoxHandler"/> types this as <see cref="object"/>. MAUI ships no Tizen asset,
		/// so this backend resolves the neutral <c>net11.0</c> assembly on every target framework
		/// and the interface is implementable without the per-platform alias mismatch that would
		/// otherwise occur.
		/// </remarks>
		object ICheckBoxHandler.PlatformView => PlatformView;

		/// <summary>
		/// The typed platform view for a mapping.
		/// </summary>
		/// <remarks>
		/// <see cref="ICheckBoxHandler"/> types <c>PlatformView</c> as <see cref="object"/>, because MAUI's
		/// neutral assembly has no Tizen alias. Mappings therefore narrow it here rather than at
		/// every call site.
		/// </remarks>
		/// <param name="handler">The handler.</param>
		/// <returns>The platform view, or <see langword="null"/> if it is not yet created.</returns>
		static TizenCheckBoxView? Platform(ICheckBoxHandler handler) => handler.PlatformView as TizenCheckBoxView;

		/// <summary>The concrete handler, for mappings that need its own state.</summary>
		/// <param name="handler">The handler.</param>
		/// <returns>The concrete handler.</returns>
		static TizenCheckBoxHandler AsHandler(ICheckBoxHandler handler) => (TizenCheckBoxHandler)handler;

		protected override TizenCheckBoxView CreatePlatformView()
		{
#if TIZEN
			return new() { Focusable = true };
#else
			return new();
#endif
		}

		protected override void ConnectHandler(TizenCheckBoxView platformView)
		{
			base.ConnectHandler(platformView);
#if TIZEN
			platformView.ValueChanged += OnValueChanged;
#endif
		}

		protected override void DisconnectHandler(TizenCheckBoxView platformView)
		{
#if TIZEN
			if (platformView.HasBody())
				platformView.ValueChanged -= OnValueChanged;
#endif
			base.DisconnectHandler(platformView);
		}

		public static void MapIsChecked(ICheckBoxHandler handler, ICheckBox check)
		{
#if TIZEN
			Platform(handler)?.UpdateIsChecked(check);
#endif
		}

		/// <summary>
		/// Maps <c>Microsoft.Maui.Controls.CheckBox.Color</c>.
		/// </summary>
		/// <remarks>
		/// Controls exposes the check colour as <c>Color</c> while Core models it as
		/// <see cref="ICheckBox.Foreground"/>; both drive the same Skia drawable, so this defers
		/// to the foreground mapping rather than duplicating the paint handling. Keyed by string
		/// because the property is declared on Controls, which this assembly does not reference.
		/// </remarks>
		/// <param name="handler">The handler.</param>
		/// <param name="check">The check box.</param>
		public static void MapColor(ICheckBoxHandler handler, ICheckBox check) => MapForeground(handler, check);

		public static void MapForeground(ICheckBoxHandler handler, ICheckBox check)
		{
#if TIZEN
			Platform(handler)?.UpdateForeground(check);
#endif
		}

#if TIZEN
		/// <remarks>
		/// Pushes the native state back to the virtual view. The write is guarded so a value
		/// that originated from a property map does not bounce straight back to the platform.
		/// </remarks>
		void OnValueChanged(object? sender, EventArgs e)
		{
			if (VirtualView is null || PlatformView is null)
				return;

			if (VirtualView.IsChecked != PlatformView.IsChecked)
				VirtualView.IsChecked = PlatformView.IsChecked;
		}
#endif
	}
}
