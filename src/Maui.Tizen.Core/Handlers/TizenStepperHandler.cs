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
			new PropertyMapper<IStepper, TizenStepperHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(IStepper.Minimum)] = MapMinimum,
				[nameof(IStepper.Maximum)] = MapMaximum,
				[nameof(IStepper.Interval)] = MapInterval,
				[nameof(IStepper.Value)] = MapValue,
			};

		/// <summary>The complete command mapper for <see cref="IStepper"/>.</summary>
		/// <remarks>
		/// Focus is overridden because a stepper is a composite: the group itself accepts no
		/// input, so focusing it would appear to do nothing. The request is forwarded to whichever
		/// button can take it.
		/// </remarks>
		public static readonly CommandMapper<IStepper, TizenStepperHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper)
			{
				[nameof(IView.Focus)] = MapFocus,
				[nameof(IView.Unfocus)] = MapUnfocus,
			};

		/// <summary>Maps <see cref="IView.Focus"/> onto the stepper's buttons.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="stepper">The stepper.</param>
		/// <param name="args">The <see cref="FocusRequest"/>.</param>
		public static void MapFocus(TizenStepperHandler handler, IStepper stepper, object? args)
		{
			if (args is not FocusRequest request)
				return;
#if TIZEN
			request.TrySetResult(handler.PlatformView?.FocusButton() ?? false);
#else
			request.TrySetResult(false);
#endif
		}

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
			platformView.ButtonFocused += OnButtonFocused;
			platformView.ButtonUnfocused += OnButtonUnfocused;
#endif
		}

		protected override void DisconnectHandler(TizenStepperView platformView)
		{
#if TIZEN
			if (platformView.HasBody())
			{
				platformView.ValueChanged -= OnValueChanged;
				platformView.ButtonFocused -= OnButtonFocused;
				platformView.ButtonUnfocused -= OnButtonUnfocused;
				platformView.DisconnectEvents();
			}
#endif
			base.DisconnectHandler(platformView);
		}

		/// <summary>Maps <see cref="IRange.Minimum"/>.</summary>
		/// <remarks>
		/// All four stepper mappings apply the whole range atomically. Applying them one at a
		/// time lets an intermediate value escape to the virtual view, and could throw outright
		/// when the new minimum exceeded the old maximum. See
		/// <see cref="TizenStepperView.Apply"/>.
		/// </remarks>
		public static void MapMinimum(TizenStepperHandler handler, IStepper stepper)
		{
#if TIZEN
			handler.PlatformView?.UpdateRange(stepper);
#endif
		}

		public static void MapMaximum(TizenStepperHandler handler, IStepper stepper)
		{
#if TIZEN
			handler.PlatformView?.UpdateRange(stepper);
#endif
		}

		public static void MapInterval(TizenStepperHandler handler, IStepper stepper)
		{
#if TIZEN
			handler.PlatformView?.UpdateRange(stepper);
#endif
		}

		public static void MapValue(TizenStepperHandler handler, IStepper stepper)
		{
#if TIZEN
			handler.PlatformView?.UpdateRange(stepper);
#endif
		}

		/// <summary>Maps <see cref="IView.Unfocus"/> onto the stepper's buttons.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="stepper">The stepper.</param>
		/// <param name="args">Unused.</param>
		public static void MapUnfocus(TizenStepperHandler handler, IStepper stepper, object? args)
		{
#if TIZEN
			handler.PlatformView?.UnfocusButton();
#endif
		}

#if TIZEN
		/// <remarks>
		/// Focus lands on a button, not on the group, so it has to be reflected back onto the
		/// virtual view by hand - the base handler only observes focus on the platform view it
		/// owns, which for a composite never receives it.
		/// </remarks>
		void OnButtonFocused(object? sender, EventArgs e)
		{
			if (VirtualView is not null)
				VirtualView.IsFocused = true;
		}

		void OnButtonUnfocused(object? sender, EventArgs e)
		{
			if (VirtualView is not null)
				VirtualView.IsFocused = false;
		}
#endif

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
