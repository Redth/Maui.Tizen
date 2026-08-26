// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="IStepper"/>.
	/// </summary>
	public class TizenStepperHandler : TizenViewHandler<IStepper, TizenStepperView>
	{
		/// <summary>The complete property mapper for <see cref="IStepper"/>.</summary>
		public static readonly IPropertyMapper<IStepper, TizenStepperHandler> Mapper =
			new PropertyMapper<IStepper, TizenStepperHandler>(ViewHandler.ViewMapper)
			{
				[nameof(IStepper.Minimum)] = MapMinimum,
				[nameof(IStepper.Maximum)] = MapMaximum,
				[nameof(IStepper.Interval)] = MapInterval,
				[nameof(IStepper.Value)] = MapValue,
			};

		/// <summary>The complete command mapper for <see cref="IStepper"/>.</summary>
		public static readonly CommandMapper<IStepper, TizenStepperHandler> CommandMapper =
			new(ViewHandler.ViewCommandMapper);

		public TizenStepperHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenStepperHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenStepperView CreatePlatformView() => new();

		protected override void ConnectHandler(TizenStepperView platformView)
		{
			base.ConnectHandler(platformView);
#if TIZEN
			platformView.ValueChanged += OnValueChanged;
#endif
		}

		protected override void DisconnectHandler(TizenStepperView platformView)
		{
#if TIZEN
			if (platformView.HasBody())
			{
				platformView.ValueChanged -= OnValueChanged;
				platformView.DisconnectEvents();
			}
#endif
			base.DisconnectHandler(platformView);
		}

		public static void MapMinimum(TizenStepperHandler handler, IStepper stepper)
		{
#if TIZEN
			handler.PlatformView?.UpdateMinimum(stepper);
#endif
		}

		public static void MapMaximum(TizenStepperHandler handler, IStepper stepper)
		{
#if TIZEN
			handler.PlatformView?.UpdateMaximum(stepper);
#endif
		}

		public static void MapInterval(TizenStepperHandler handler, IStepper stepper)
		{
#if TIZEN
			handler.PlatformView?.UpdateIncrement(stepper);
#endif
		}

		public static void MapValue(TizenStepperHandler handler, IStepper stepper)
		{
#if TIZEN
			handler.PlatformView?.UpdateValue(stepper);
#endif
		}

#if TIZEN
		void OnValueChanged(object? sender, EventArgs e)
		{
			if (VirtualView is null || PlatformView is null)
				return;

			if (!VirtualView.Value.Equals(PlatformView.Value))
				VirtualView.Value = PlatformView.Value;
		}
#endif
	}
}
