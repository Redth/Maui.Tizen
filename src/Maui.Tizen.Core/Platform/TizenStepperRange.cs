// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// The stepper's bounds-and-value state machine.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Split out of <c>TizenStepperView</c> because none of this arithmetic touches NUI, and
	/// keeping it here is what lets it be executed by the host-side unit tests. The view owns the
	/// buttons and the rendering; this owns "what is the value, and did it change".
	/// </para>
	/// <para>
	/// The reason it exists as a unit at all is that applying bounds and value separately is
	/// incorrect, not merely inefficient - see <see cref="Apply"/>.
	/// </para>
	/// </remarks>
	public sealed class TizenStepperRange
	{
		/// <summary>
		/// The upper bound before any is supplied.
		/// </summary>
		/// <remarks>
		/// Matches the native default the Tizen control shipped with. It is called out as a named
		/// constant because it is precisely what made the old per-property path throw: a minimum
		/// above this value produced an inverted clamp range.
		/// </remarks>
		public const double DefaultMaximum = 10;

		double _minimum;
		double _maximum = DefaultMaximum;
		double _value;

		/// <summary>The lower bound.</summary>
		public double Minimum => _minimum;

		/// <summary>The upper bound.</summary>
		public double Maximum => _maximum;

		/// <summary>The current value, always within <see cref="Minimum"/>..<see cref="Maximum"/>.</summary>
		public double Value => _value;

		/// <summary>The amount a single press changes <see cref="Value"/> by.</summary>
		public double Increment { get; set; } = 1;

		/// <summary>Whether the increment button should be enabled.</summary>
		public bool CanIncrease => _value < _maximum;

		/// <summary>Whether the decrement button should be enabled.</summary>
		public bool CanDecrease => _value > _minimum;

		/// <summary>
		/// Applies bounds and value together, as one atomic change.
		/// </summary>
		/// <remarks>
		/// <para>
		/// MAUI drives mapper keys in declaration order, so <c>Minimum</c> lands before
		/// <c>Maximum</c> and <c>Value</c>. Applied one at a time, a stepper with min 5, max 30,
		/// value 25 would first clamp the value into <c>[5, 10]</c> - the native default maximum -
		/// and report a change with an intermediate number, which the handler writes straight back
		/// onto the virtual view. The application's bound value is corrupted before the real one is
		/// ever applied.
		/// </para>
		/// <para>
		/// A minimum above <see cref="DefaultMaximum"/> was worse still: it made
		/// <see cref="Math.Clamp(double, double, double)"/> throw <see cref="ArgumentException"/>
		/// from inside a property mapper, because the lower bound exceeded the upper bound.
		/// </para>
		/// <para>
		/// Applying all three at once means no inconsistent range is ever observed, and at most one
		/// change is reported.
		/// </para>
		/// </remarks>
		/// <param name="minimum">The lower bound.</param>
		/// <param name="maximum">The upper bound.</param>
		/// <param name="value">The requested value, clamped into the resulting range.</param>
		/// <returns><see langword="true"/> if <see cref="Value"/> changed.</returns>
		public bool Apply(double minimum, double maximum, double value)
		{
			// An inverted range is legal to *receive* - MAUI lets the properties be set in any
			// order, so a transient inversion is expected - but it cannot be clamped with.
			// Collapse it to a degenerate point rather than throwing out of a property mapper.
			if (maximum < minimum)
				maximum = minimum;

			var clamped = Math.Clamp(value, minimum, maximum);
			var changed = !clamped.Equals(_value);

			_minimum = minimum;
			_maximum = maximum;
			_value = clamped;

			return changed;
		}

		/// <summary>
		/// Applies every stepper property from the cross-platform view in one step.
		/// </summary>
		/// <param name="stepper">The cross-platform stepper.</param>
		/// <returns><see langword="true"/> if <see cref="Value"/> changed.</returns>
		public bool Apply(IStepper stepper)
		{
			ArgumentNullException.ThrowIfNull(stepper);

			Increment = stepper.Interval;
			return Apply(stepper.Minimum, stepper.Maximum, stepper.Value);
		}

		/// <summary>
		/// Moves the value by one <see cref="Increment"/>.
		/// </summary>
		/// <param name="direction">Positive to increase, negative to decrease.</param>
		/// <returns><see langword="true"/> if <see cref="Value"/> changed.</returns>
		public bool Step(int direction) =>
			Apply(_minimum, _maximum, _value + (Increment * Math.Sign(direction)));
	}
}
