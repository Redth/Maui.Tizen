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

		readonly TizenStepperRange _range = new();

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

			_less.FocusGained += OnButtonFocusGained;
			_more.FocusGained += OnButtonFocusGained;
			_less.FocusLost += OnButtonFocusLost;
			_more.FocusLost += OnButtonFocusLost;

			Add(_less);
			Add(_more);

			UpdateButtonState();
		}

		/// <summary>Raised when <see cref="Value"/> changes.</summary>
		public event EventHandler? ValueChanged;

		/// <summary>Raised when either button gains focus.</summary>
		/// <remarks>
		/// A stepper is a composite: focus lands on one of the two buttons, never on the group.
		/// Surfacing it is what lets the handler keep <see cref="IView.IsFocused"/> truthful.
		/// </remarks>
		public event EventHandler? ButtonFocused;

		/// <summary>Raised when focus leaves both buttons.</summary>
		public event EventHandler? ButtonUnfocused;

		/// <summary>
		/// Moves focus to the first button that can accept it.
		/// </summary>
		/// <remarks>
		/// Prefers whichever button is currently enabled. At a bound one of them is disabled, and
		/// focusing a disabled button would silently leave the stepper unfocused.
		/// </remarks>
		/// <returns><see langword="true"/> if a button took focus.</returns>
		public bool FocusButton()
		{
			var target = _more.IsEnabled ? _more : _less.IsEnabled ? _less : null;

			return target is not null
				&& global::Tizen.NUI.FocusManager.Instance.SetCurrentFocusView(target);
		}

		/// <summary>
		/// Removes focus from whichever button holds it.
		/// </summary>
		public void UnfocusButton()
		{
			var focused = global::Tizen.NUI.FocusManager.Instance.GetCurrentFocusView();

			if (focused == _more || focused == _less)
				global::Tizen.NUI.FocusManager.Instance.ClearFocus();
		}

		/// <summary>The current value, always within <see cref="Minimum"/>..<see cref="Maximum"/>.</summary>
		public double Value
		{
			get => _range.Value;
			set => Apply(_range.Minimum, _range.Maximum, value);
		}

		/// <summary>The lower bound.</summary>
		/// <remarks>
		/// Prefer <see cref="UpdateRange"/> when more than one of the bounds is changing, so the
		/// range is never momentarily inconsistent. See <see cref="TizenStepperRange.Apply(double, double, double)"/>.
		/// </remarks>
		public double Minimum
		{
			get => _range.Minimum;
			set => Apply(value, _range.Maximum, _range.Value);
		}

		/// <summary>The upper bound.</summary>
		/// <remarks>See <see cref="Minimum"/>.</remarks>
		public double Maximum
		{
			get => _range.Maximum;
			set => Apply(_range.Minimum, value, _range.Value);
		}

		/// <summary>The amount a single press changes <see cref="Value"/> by.</summary>
		public double Increment
		{
			get => _range.Increment;
			set => _range.Increment = value;
		}

		public TSize Measure(double availableWidth, double availableHeight) =>
			new(Math.Min(PreferredWidth.ToScaledPixel(), availableWidth), PreferredHeight.ToScaledPixel());

		/// <summary>
		/// Applies the bounds and the value together, as one atomic change.
		/// </summary>
		/// <remarks>
		/// The arithmetic lives in <see cref="TizenStepperRange"/>, which is platform-independent
		/// and therefore actually covered by the host-side tests. This adds only the parts that
		/// need the native control: refreshing the buttons and raising the event.
		/// </remarks>
		/// <param name="minimum">The lower bound.</param>
		/// <param name="maximum">The upper bound.</param>
		/// <param name="value">The requested value.</param>
		public void Apply(double minimum, double maximum, double value)
		{
			var changed = _range.Apply(minimum, maximum, value);

			UpdateButtonState();

			if (changed)
				ValueChanged?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Applies the stepper's bounds, interval and value in one atomic step.
		/// </summary>
		/// <param name="stepper">The cross-platform stepper.</param>
		public void UpdateRange(IStepper stepper)
		{
			var changed = _range.Apply(stepper);

			UpdateButtonState();

			if (changed)
				ValueChanged?.Invoke(this, EventArgs.Empty);
		}

		/// <summary>
		/// Detaches the event handlers this control owns.
		/// </summary>
		public void DisconnectEvents()
		{
			if (_less.HasBody())
			{
				_less.Clicked -= OnLessClicked;
				_less.FocusGained -= OnButtonFocusGained;
				_less.FocusLost -= OnButtonFocusLost;
			}

			if (_more.HasBody())
			{
				_more.Clicked -= OnMoreClicked;
				_more.FocusGained -= OnButtonFocusGained;
				_more.FocusLost -= OnButtonFocusLost;
			}
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

			_more.IsEnabled = _range.CanIncrease;
			_less.IsEnabled = _range.CanDecrease;
		}

		void OnButtonFocusGained(object? sender, EventArgs e) => ButtonFocused?.Invoke(this, EventArgs.Empty);

		/// <remarks>
		/// Focus moving between the two buttons is still focus on the stepper, so a loss is only
		/// reported once neither button holds it.
		/// </remarks>
		void OnButtonFocusLost(object? sender, EventArgs e)
		{
			var focused = global::Tizen.NUI.FocusManager.Instance.GetCurrentFocusView();

			if (focused != _more && focused != _less)
				ButtonUnfocused?.Invoke(this, EventArgs.Empty);
		}

		void OnMoreClicked(object? sender, EventArgs e) => Step(1);

		void OnLessClicked(object? sender, EventArgs e) => Step(-1);

		void Step(int direction)
		{
			var changed = _range.Step(direction);

			UpdateButtonState();

			if (changed)
				ValueChanged?.Invoke(this, EventArgs.Empty);
		}

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
