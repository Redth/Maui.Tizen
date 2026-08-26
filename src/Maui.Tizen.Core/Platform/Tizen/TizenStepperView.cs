// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.Common;
using Tizen.UIExtensions.NUI;
using MaterialIconButton = Tizen.UIExtensions.NUI.GraphicsView.MaterialIconButton;
using MaterialIcons = Tizen.UIExtensions.Common.GraphicsView.MaterialIcons;
using NColor = Tizen.NUI.Color;
using NView = Tizen.NUI.BaseComponents.View;
using TColor = Tizen.UIExtensions.Common.Color;
using TSize = Tizen.UIExtensions.Common.Size;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// The platform view for <c>Stepper</c>: a decrement/increment button pair.
	/// </summary>
	/// <remarks>
	/// Tizen has no stepper control, so this composes two Material icon buttons. The value
	/// itself is held here rather than on a native control, which is what lets the buttons be
	/// disabled individually once a bound is reached.
	/// </remarks>
	public class TizenStepperView : NView, IMeasurable
	{
		const double PreferredWidth = 200;
		const double PreferredHeight = 60;

		readonly StepperButton _less;
		readonly StepperButton _more;

		double _value;
		double _minimum;
		double _maximum = 10;

		public TizenStepperView()
		{
			Layout = new LinearLayout
			{
				LinearOrientation = LinearLayout.Orientation.Horizontal
			};

			_less = new StepperButton { Icon = MaterialIcons.Remove };
			_more = new StepperButton { Icon = MaterialIcons.Add };

			_less.Clicked += OnLessClicked;
			_more.Clicked += OnMoreClicked;

			Add(_less);
			Add(_more);

			UpdateButtonState();
		}

		/// <summary>Raised when <see cref="Value"/> changes.</summary>
		public event EventHandler? ValueChanged;

		/// <summary>
		/// The current value, always within <see cref="Minimum"/>..<see cref="Maximum"/>.
		/// </summary>
		/// <remarks>
		/// The change notification is suppressed when the clamped value is unchanged. Without
		/// that, holding the increment button at the maximum would raise a change event per
		/// click and push a redundant write back through the handler on every one.
		/// </remarks>
		public double Value
		{
			get => _value;
			set
			{
				var clamped = Math.Clamp(value, Minimum, Maximum);

				if (clamped.Equals(_value))
					return;

				_value = clamped;
				UpdateButtonState();
				ValueChanged?.Invoke(this, EventArgs.Empty);
			}
		}

		public double Minimum
		{
			get => _minimum;
			set
			{
				_minimum = value;
				Value = Math.Clamp(_value, Minimum, Maximum);
				UpdateButtonState();
			}
		}

		public double Maximum
		{
			get => _maximum;
			set
			{
				_maximum = value;
				Value = Math.Clamp(_value, Minimum, Maximum);
				UpdateButtonState();
			}
		}

		/// <summary>The amount a single press changes <see cref="Value"/> by.</summary>
		public double Increment { get; set; } = 1;

		public TSize Measure(double availableWidth, double availableHeight) =>
			new(Math.Min(PreferredWidth.ToScaledPixel(), availableWidth), PreferredHeight.ToScaledPixel());

		public void UpdateMinimum(IStepper stepper) => Minimum = stepper.Minimum;

		public void UpdateMaximum(IStepper stepper) => Maximum = stepper.Maximum;

		public void UpdateIncrement(IStepper stepper) => Increment = stepper.Interval;

		public void UpdateValue(IStepper stepper)
		{
			if (!Value.Equals(stepper.Value))
				Value = stepper.Value;
		}

		/// <summary>
		/// Detaches the event handlers this control owns.
		/// </summary>
		public void DisconnectEvents()
		{
			if (_less.HasBody())
				_less.Clicked -= OnLessClicked;

			if (_more.HasBody())
				_more.Clicked -= OnMoreClicked;
		}

		protected override void OnEnabled(bool enabled)
		{
			base.OnEnabled(enabled);

			if (enabled)
			{
				UpdateButtonState();
			}
			else
			{
				_more.IsEnabled = false;
				_less.IsEnabled = false;
			}
		}

		/// <remarks>
		/// Disabling the button at a bound is the only affordance the user gets: there is no
		/// value readout on a Tizen stepper, so an increment that silently does nothing would
		/// be indistinguishable from an unresponsive control.
		/// </remarks>
		void UpdateButtonState()
		{
			if (!IsEnabled)
				return;

			_more.IsEnabled = Value < Maximum;
			_less.IsEnabled = Value > Minimum;
		}

		void OnMoreClicked(object? sender, EventArgs e) => Value += Increment;

		void OnLessClicked(object? sender, EventArgs e) => Value -= Increment;

		/// <summary>One of the stepper's two buttons.</summary>
		sealed class StepperButton : MaterialIconButton
		{
			static readonly TColor NormalBackground = TColor.FromHex("#eeeeee");
			static readonly TColor DisabledBackground = TColor.FromHex("#e0e0e0");
			static readonly TColor PressedBackground = TColor.FromHex("#fefefe");

			const double MarginDp = 10;
			const double CornerRadiusDp = 10;

			public StepperButton()
			{
				BackgroundColor = NormalBackground.ToNative();
				HeightSpecification = LayoutParamPolicies.MatchParent;
				WidthSpecification = LayoutParamPolicies.MatchParent;

				var margin = (ushort)MarginDp.ToScaledPixel();
				Margin = new Extents(margin, margin, margin, margin);

				BorderlineWidth = 1d.ToScaledPixel();
				BorderlineColor = NColor.Black;
				CornerRadius = CornerRadiusDp.ToScaledPixel();

				Pressed += OnPressed;
				Released += OnReleased;
				KeyEvent += OnKeyEvent;
			}

			protected override void OnEnabled(bool enabled)
			{
				base.OnEnabled(enabled);
				BackgroundColor = enabled ? NormalBackground.ToNative() : DisabledBackground.ToNative();
				Color = enabled ? TColor.Black : TColor.Gray;
			}

			void OnReleased(object? sender, EventArgs e) => BackgroundColor = NormalBackground.ToNative();

			void OnPressed(object? sender, EventArgs e) => BackgroundColor = PressedBackground.ToNative();

			/// <remarks>
			/// Returns <see langword="false"/> so the key still reaches the button's own click
			/// handling; this only supplies the pressed-state visual, which NUI does not derive
			/// from key input on its own.
			/// </remarks>
			bool OnKeyEvent(object source, NView.KeyEventArgs e)
			{
				if (e.Key.KeyPressedName.IsEnterKey())
					BackgroundColor = e.Key.State == Key.StateType.Down ? PressedBackground.ToNative() : NormalBackground.ToNative();

				return false;
			}
		}
	}
}
