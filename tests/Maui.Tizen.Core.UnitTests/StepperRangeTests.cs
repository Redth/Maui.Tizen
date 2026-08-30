// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Platforms.Tizen;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Regressions for the stepper's bounds-and-value initialization.
	/// </summary>
	/// <remarks>
	/// The defect these pin: bounds and value were applied one property at a time, in MAUI's
	/// mapper declaration order (<c>Minimum</c>, then <c>Maximum</c>, then <c>Value</c>). That is
	/// wrong in two distinct ways, and both are covered here.
	/// </remarks>
	public class StepperRangeTests
	{
		/// <summary>
		/// The exact case from the review: min 5, max 30, value 25.
		/// </summary>
		/// <remarks>
		/// Applied one property at a time, setting <c>Minimum = 5</c> first clamps the value into
		/// <c>[5, 10]</c> - 10 being the native default maximum - and reports a change. The handler
		/// writes that intermediate 5 back onto the virtual view, so the application's bound value
		/// is destroyed before the real value is ever applied.
		/// </remarks>
		[Fact]
		public void ApplyingMin5Max30Value25LandsOn25WithNoIntermediateValue()
		{
			var range = new TizenStepperRange();

			var changed = range.Apply(minimum: 5, maximum: 30, value: 25);

			Assert.True(changed);
			Assert.Equal(5, range.Minimum);
			Assert.Equal(30, range.Maximum);
			Assert.Equal(25, range.Value);
		}

		/// <summary>
		/// A minimum above the native default maximum must not throw.
		/// </summary>
		/// <remarks>
		/// <see cref="Math.Clamp(double, double, double)"/> throws <see cref="ArgumentException"/>
		/// when its lower bound exceeds its upper bound. Setting <c>Minimum = 20</c> while the
		/// maximum was still the default 10 did exactly that, from inside a property mapper.
		/// </remarks>
		[Theory]
		[InlineData(11)]
		[InlineData(20)]
		[InlineData(1000)]
		public void MinimumAboveTheDefaultMaximumDoesNotThrow(double minimum)
		{
			var range = new TizenStepperRange();

			var exception = Record.Exception(() => range.Apply(minimum, TizenStepperRange.DefaultMaximum, value: 0));

			Assert.Null(exception);
		}

		/// <summary>
		/// An inverted range collapses to a point rather than throwing.
		/// </summary>
		[Fact]
		public void InvertedRangeCollapsesToTheMinimum()
		{
			var range = new TizenStepperRange();

			range.Apply(minimum: 50, maximum: 10, value: 30);

			Assert.Equal(50, range.Minimum);
			Assert.Equal(50, range.Maximum);
			Assert.Equal(50, range.Value);
		}

		/// <summary>
		/// Re-applying the same state must not report a change.
		/// </summary>
		/// <remarks>
		/// The handler turns a reported change into a write onto the virtual view. Reporting one
		/// spuriously would make every mapper pass push a redundant value back into MAUI.
		/// </remarks>
		[Fact]
		public void ReapplyingTheSameStateReportsNoChange()
		{
			var range = new TizenStepperRange();
			range.Apply(minimum: 0, maximum: 100, value: 42);

			var changed = range.Apply(minimum: 0, maximum: 100, value: 42);

			Assert.False(changed);
		}

		/// <summary>
		/// Applying through the whole stepper interface preserves the requested value.
		/// </summary>
		[Fact]
		public void ApplyingFromTheStepperPreservesTheValue()
		{
			var range = new TizenStepperRange();
			var stepper = new StubStepper { Minimum = 5, Maximum = 30, Value = 25, Interval = 2 };

			var changed = range.Apply(stepper);

			Assert.True(changed);
			Assert.Equal(25, range.Value);
			Assert.Equal(2, range.Increment);
		}

		/// <summary>
		/// Stepping stops at the bounds without reporting redundant changes.
		/// </summary>
		/// <remarks>
		/// Holding the increment button at the maximum would otherwise raise a change per click,
		/// each one writing the same value back through the handler.
		/// </remarks>
		[Fact]
		public void SteppingClampsAtTheMaximumAndStopsReportingChanges()
		{
			var range = new TizenStepperRange();
			range.Apply(minimum: 0, maximum: 3, value: 2);

			Assert.True(range.Step(1));
			Assert.Equal(3, range.Value);

			Assert.False(range.Step(1));
			Assert.Equal(3, range.Value);
			Assert.False(range.CanIncrease);
		}

		/// <summary>
		/// Stepping stops at the minimum symmetrically.
		/// </summary>
		[Fact]
		public void SteppingClampsAtTheMinimumAndStopsReportingChanges()
		{
			var range = new TizenStepperRange();
			range.Apply(minimum: 1, maximum: 10, value: 2);

			Assert.True(range.Step(-1));
			Assert.Equal(1, range.Value);

			Assert.False(range.Step(-1));
			Assert.Equal(1, range.Value);
			Assert.False(range.CanDecrease);
		}

		/// <summary>
		/// The handler must apply the whole range for any one of its four mapper keys.
		/// </summary>
		/// <remarks>
		/// This is what makes the ordering defect unreachable: whichever key MAUI drives first, the
		/// complete range is applied from the virtual view rather than one property in isolation.
		/// </remarks>
		[Theory]
		[InlineData(nameof(IStepper.Minimum))]
		[InlineData(nameof(IStepper.Maximum))]
		[InlineData(nameof(IStepper.Interval))]
		[InlineData(nameof(IStepper.Value))]
		public void EveryStepperMapperKeyAppliesTheWholeRange(string key)
		{
			var handler = new TizenStepperHandler();
			var stepper = new StubStepper { Minimum = 5, Maximum = 30, Value = 25 };

			handler.SetVirtualView(stepper);

			var exception = Record.Exception(() => ((IElementHandler)handler).UpdateValue(key));

			Assert.Null(exception);

			// The virtual view must never have been written back with an intermediate value.
			Assert.Equal(25, stepper.Value);
		}

		/// <summary>
		/// Connecting a handler whose minimum exceeds the native default must not throw.
		/// </summary>
		[Fact]
		public void ConnectingAStepperWithMinimumAboveTheDefaultMaximumDoesNotThrow()
		{
			var handler = new TizenStepperHandler();
			var stepper = new StubStepper { Minimum = 20, Maximum = 40, Value = 30 };

			var exception = Record.Exception(() => handler.SetVirtualView(stepper));

			Assert.Null(exception);
			Assert.Equal(30, stepper.Value);
		}
	}
}
