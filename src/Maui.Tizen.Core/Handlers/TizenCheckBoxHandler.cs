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
	public class TizenCheckBoxHandler : TizenViewHandler<ICheckBox, TizenCheckBoxView>
	{
		/// <summary>The complete property mapper for <see cref="ICheckBox"/>.</summary>
		public static readonly IPropertyMapper<ICheckBox, TizenCheckBoxHandler> Mapper =
			new PropertyMapper<ICheckBox, TizenCheckBoxHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(ICheckBox.IsChecked)] = MapIsChecked,
				[nameof(ICheckBox.Foreground)] = MapForeground,
			};

		/// <summary>The complete command mapper for <see cref="ICheckBox"/>.</summary>
		public static readonly CommandMapper<ICheckBox, TizenCheckBoxHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper);

		public TizenCheckBoxHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenCheckBoxHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

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

		public static void MapIsChecked(TizenCheckBoxHandler handler, ICheckBox check)
		{
#if TIZEN
			handler.PlatformView?.UpdateIsChecked(check);
#endif
		}

		public static void MapForeground(TizenCheckBoxHandler handler, ICheckBox check)
		{
#if TIZEN
			handler.PlatformView?.UpdateForeground(check);
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
