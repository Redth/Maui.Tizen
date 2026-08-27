// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Behavioural coverage for the mappings MAUI Controls adds by <c>RemapForControls</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This suite exists because of a specific review finding: the core slice shipped Label's
	/// <c>FormattedText</c>, <c>LineBreakMode</c>, <c>MaxLines</c> and accessibility keys in a state
	/// where they were <em>reachable</em> - key present, no cast failure - but behaviourally inert,
	/// because the body they resolved to was MAUI's off-platform no-op. Every test that had been
	/// written passed.
	/// </para>
	/// <para>
	/// The lesson generalises: <b>resolution is not implementation</b>. Key presence and absence of
	/// <see cref="InvalidCastException"/> are necessary but nowhere near sufficient, so the
	/// remapped keys are asserted here by observable effect instead.
	/// </para>
	/// <para>
	/// What is observable off-device is limited and the limit is honest: a control-specific body
	/// like <c>TizenEntryHandler.MapBackground</c> is entirely inside <c>#if TIZEN</c>, so on this
	/// lane it genuinely does nothing and no host test can claim otherwise. What <i>can</i> be
	/// verified here is the part that is pure dispatch logic - whether a Controls key forwards to
	/// the backend key that implements it - and that is exactly where the remaps live.
	/// </para>
	/// </remarks>
	public class ControlsRemapBehaviorTests
	{
		/// <summary>
		/// <c>Picker.ItemsSource</c> must actually cause an <c>IPicker.Items</c> update.
		/// </summary>
		/// <remarks>
		/// Controls' own <c>MapItemsSource</c> forwards to <c>IPicker.Items</c>; the Tizen override
		/// has to reproduce that, because a picker whose items changed but which never re-renders
		/// shows stale text. Asserting only that the key resolves would have passed even when this
		/// mapping threw <see cref="InvalidCastException"/>, which it did before it was overridden.
		/// </remarks>
		[Fact]
		public void PickerItemsSourceForwardsToItems()
		{
			var forwarded = Record<IPicker, IPickerHandler>(
				TizenPickerHandler.Mapper,
				"Items",
				mapper => new TizenPickerHandler(mapper),
				new Controls.Picker(),
				"ItemsSource");

			Assert.True(
				forwarded,
				"Dispatching 'ItemsSource' did not trigger an 'Items' update. MAUI Controls adds " +
				"ItemsSource via Picker.RemapForControls and implements it by forwarding to " +
				"IPicker.Items; without that forwarding the platform picker keeps rendering the " +
				"previous items.");
		}

		/// <summary>
		/// <c>Stepper.Increment</c> must actually cause an <c>IStepper.Interval</c> update.
		/// </summary>
		/// <remarks>
		/// <c>Controls.Stepper.Increment</c> is the bindable property behind
		/// <c>IStepper.Interval</c> - two names for one value - so a stepper whose Increment changed
		/// without an Interval update keeps stepping by the old amount.
		/// </remarks>
		[Fact]
		public void StepperIncrementForwardsToInterval()
		{
			var forwarded = Record<IStepper, IStepperHandler>(
				TizenStepperHandler.Mapper,
				nameof(IStepper.Interval),
				mapper => new TizenStepperHandler(mapper),
				new Controls.Stepper(),
				"Increment");

			Assert.True(
				forwarded,
				"Dispatching 'Increment' did not trigger an 'Interval' update. Controls adds " +
				"Increment via Stepper.RemapForControls and implements it by forwarding to " +
				"IStepper.Interval; without that forwarding the stepper keeps its old step size.");
		}

		/// <summary>
		/// The semantic properties Controls remaps must reach the backend's <c>Semantics</c> mapping.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>SemanticProperties.Description</c>, <c>Hint</c> and <c>HeadingLevel</c> are separate
		/// mapper keys that Controls implements by re-raising <c>IView.Semantics</c>. The backend
		/// implements <c>Semantics</c>, so the accessible name only ever reaches NUI if that
		/// forwarding works - measured here rather than assumed.
		/// </para>
		/// </remarks>
		[Theory]
		[InlineData("Description")]
		[InlineData("Hint")]
		[InlineData("HeadingLevel")]
		public void SemanticPropertiesReachTheSemanticsMapping(string key)
		{
			var entry = new Controls.Entry();
			var handler = new TizenEntryHandler();
			handler.SetVirtualView(entry);

			var platform = (TizenPlatformView)handler.PlatformView!;
			platform.Applied.Clear();

			handler.UpdateValue(key);

			Assert.True(
				platform.Applied.Contains(nameof(IView.Semantics)),
				$"Dispatching '{key}' did not reach the backend's Semantics mapping, so the " +
				"accessible name/hint would never be applied to the platform view. Controls " +
				"implements this key by re-raising IView.Semantics.");
		}

		/// <summary>
		/// Records the two accessibility keys that are reachable but demonstrably inert.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>IsInAccessibleTree</c> and <c>ExcludedWithChildren</c> resolve through the chain -
		/// they are in MAUI's <c>ViewMapper</c> and in the Tizen base mapper's key set - but
		/// dispatching them produces no observable effect on this backend. That was measured, not
		/// assumed, and it is precisely the "reachable but inert" state the review flagged.
		/// </para>
		/// <para>
		/// It is pinned rather than fixed because the mapping is core-owned
		/// (<c>TizenViewMappers</c>) and is reported there. The test's value is that the fact is now
		/// recorded: if someone implements these keys, this fails and says so, and the honest
		/// "reachable, inert" note can be removed. A passing run means the gap still exists.
		/// </para>
		/// </remarks>
		[Theory]
		[InlineData("IsInAccessibleTree")]
		[InlineData("ExcludedWithChildren")]
		public void KnownInertAccessibilityKeysAreStillInert(string key)
		{
			var entry = new Controls.Entry();
			var handler = new TizenEntryHandler();
			handler.SetVirtualView(entry);

			var platform = (TizenPlatformView)handler.PlatformView!;

			// Reachable: the key really does resolve, which is what makes the inertness deceptive.
			Assert.Contains(key, TizenControlHandlers.GetMapperKeys(typeof(TizenEntryHandler)));

			platform.Applied.Clear();
			handler.UpdateValue(key);

			Assert.True(
				platform.Applied.Count == 0,
				$"'{key}' now has an observable effect ({string.Join(", ", platform.Applied)}). " +
				"That is an improvement, not a regression: the key used to resolve while doing " +
				"nothing. Remove this test and drop the 'reachable but inert' caveat for it from " +
				"docs/wave-a-handlers.md.");
		}

		/// <summary>
		/// Dispatches <paramref name="trigger"/> and reports whether <paramref name="expectedKey"/>
		/// was raised as a result.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The observation works by handing the handler a mapper that chains the real one but
		/// overrides <paramref name="expectedKey"/> with a recorder. The trigger still resolves
		/// through the chain to the real mapping, and when that mapping calls
		/// <c>handler.UpdateValue(expectedKey)</c> it re-enters this mapper and is recorded.
		/// </para>
		/// <para>
		/// This observes the real shipped mapping rather than a re-implementation of it - the
		/// forwarding under test is the production one.
		/// </para>
		/// </remarks>
		static bool Record<TVirtualView, THandler>(
			IPropertyMapper<TVirtualView, THandler> real,
			string expectedKey,
			Func<IPropertyMapper, IElementHandler> createHandler,
			TVirtualView virtualView,
			string trigger)
			where TVirtualView : IView
			where THandler : IElementHandler
		{
			ControlsRemap.Force();

			var raised = false;

			var spy = new PropertyMapper<TVirtualView, THandler>((IPropertyMapper)real)
			{
				[expectedKey] = (_, _) => raised = true,
			};

			var handler = createHandler(spy);
			handler.SetVirtualView(virtualView);

			// The trigger must genuinely resolve, or a silent no-op would pass as "not forwarded"
			// for the wrong reason.
			Assert.True(
				spy.GetProperty(trigger) is not null,
				$"'{trigger}' does not resolve at all, so this test cannot say anything about " +
				"whether it forwards. Has the Controls remap stopped being applied?");

			raised = false;
			handler.UpdateValue(trigger);

			return raised;
		}
	}
}
