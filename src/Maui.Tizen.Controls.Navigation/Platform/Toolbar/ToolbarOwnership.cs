using System;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Tracks which toolbar instance a container currently owns, so that event subscriptions can
	/// never outlive the instance they were made against.
	/// </summary>
	/// <typeparam name="TToolbar">The platform toolbar type.</typeparam>
	/// <remarks>
	/// <para>
	/// Core's <c>ITizenToolbarContainer.SetToolbar</c> is an <b>ownership transfer</b>: it replaces
	/// the container's toolbar <em>and disposes the previous one</em>. That makes the obvious
	/// implementation wrong in a way that is invisible until runtime. Holding a cached
	/// <c>_toolbar</c> field and unsubscribing from it during teardown - which is what this backend
	/// did when it was ported - can touch an instance that <c>SetToolbar</c> already disposed.
	/// </para>
	/// <para>
	/// The three rules that make it safe are easy to state and easy to get wrong individually, so
	/// they are implemented once here rather than open-coded at each call site:
	/// </para>
	/// <list type="number">
	/// <item><description>unsubscribe from the outgoing toolbar <em>before</em> the transfer;</description></item>
	/// <item><description>subscribe exactly once to the incoming one, even if it is set repeatedly;</description></item>
	/// <item><description>release idempotently, so a second teardown is a no-op rather than a
	/// double-unsubscribe against a disposed instance.</description></item>
	/// </list>
	/// <para>
	/// This type is deliberately generic and free of any Tizen.NUI dependency, so the ownership
	/// rules are unit-testable on a plain host. The runtime disposal and visual behaviour they
	/// protect remain device-gated.
	/// </para>
	/// </remarks>
	public sealed class ToolbarOwnership<TToolbar>
		where TToolbar : class
	{
		readonly Action<TToolbar> _subscribe;
		readonly Action<TToolbar> _unsubscribe;

		/// <summary>Creates a tracker with the given subscribe and unsubscribe actions.</summary>
		public ToolbarOwnership(Action<TToolbar> subscribe, Action<TToolbar> unsubscribe)
		{
			ArgumentNullException.ThrowIfNull(subscribe);
			ArgumentNullException.ThrowIfNull(unsubscribe);

			_subscribe = subscribe;
			_unsubscribe = unsubscribe;
		}

		/// <summary>The toolbar currently owned, or <see langword="null"/>.</summary>
		public TToolbar? Current { get; private set; }

		/// <summary>Number of live subscriptions; only ever 0 or 1.</summary>
		public int SubscriptionCount { get; private set; }

		/// <summary>
		/// Takes ownership of <paramref name="toolbar"/>, releasing whatever was owned before.
		/// </summary>
		/// <remarks>
		/// Setting the same instance twice is a no-op rather than a second subscription. That is not
		/// a micro-optimisation: a duplicate subscription would fire the icon handler twice per
		/// press, which on a flyout toggle cancels itself out and looks like the toolbar button
		/// doing nothing at all.
		/// </remarks>
		public void Transfer(TToolbar? toolbar)
		{
			if (ReferenceEquals(Current, toolbar))
			{
				return;
			}

			// Always detach from the outgoing instance FIRST. After the caller hands it to
			// SetToolbar it may be disposed, and touching it then is undefined.
			Release();

			if (toolbar is null)
			{
				return;
			}

			Current = toolbar;
			_subscribe(toolbar);
			SubscriptionCount = 1;
		}

		/// <summary>
		/// Releases the currently owned toolbar. Safe to call repeatedly.
		/// </summary>
		public void Release()
		{
			if (Current is null)
			{
				return;
			}

			TToolbar outgoing = Current;

			// Cleared before unsubscribing so that a re-entrant call - a disposal path that runs
			// teardown twice, for instance - sees no owner and does nothing.
			Current = null;
			SubscriptionCount = 0;

			_unsubscribe(outgoing);
		}
	}
}
