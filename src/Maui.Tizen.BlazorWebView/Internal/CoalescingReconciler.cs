using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal
{
	/// <summary>
	/// Runs an asynchronous reconciliation pass on a dispatcher, never concurrently with itself, and
	/// re-runs it whenever a request arrives while a pass is in flight.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This exists because of how <c>RootComponents</c> change notifications behave. Each notification
	/// previously started its own independent asynchronous pass carrying a delta captured when the event
	/// was raised. Because those passes await, they interleave, and a pass can apply a decision computed
	/// from a collection state that no longer exists:
	/// </para>
	/// <list type="bullet">
	/// <item><description>
	/// <c>Add</c> immediately followed by <c>Clear</c>: the Clear pass computes what to unmount before the
	/// Add pass has recorded the new component, so the component is mounted and never removed. It stays
	/// rendered in a collection the application has emptied.
	/// </description></item>
	/// <item><description>
	/// Rapid <c>Replace</c>: the outgoing component can be left mounted, or the incoming one registered
	/// against a selector the outgoing one still occupies.
	/// </description></item>
	/// </list>
	/// <para>
	/// The fix is to treat a change as "state is dirty" rather than "apply this delta". Passes are
	/// serialized, and each pass re-reads the desired state, so the last pass to run always observes the
	/// final collection. The request flag is set <b>before</b> the pass is scheduled and cleared only
	/// immediately before a pass reads state, so no change can be lost in the window between the two.
	/// </para>
	/// </remarks>
	internal sealed class CoalescingReconciler
	{
		private readonly Func<Task> _reconcile;
		private readonly Func<Func<Task>, Task> _dispatch;

		/// <summary>Non-zero when a pass is owed. Written from any thread.</summary>
		private int _requested;

		/// <summary>Only ever touched on the dispatcher, so it needs no synchronization.</summary>
		private bool _running;

		/// <param name="reconcile">
		/// Brings actual state in line with desired state. Must re-read desired state on every call
		/// rather than closing over a snapshot.
		/// </param>
		/// <param name="dispatch">Queues work onto the dispatcher that owns the reconciled state.</param>
		public CoalescingReconciler(Func<Task> reconcile, Func<Func<Task>, Task> dispatch)
		{
			_reconcile = reconcile ?? throw new ArgumentNullException(nameof(reconcile));
			_dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
		}

		/// <summary>Number of passes that have run. For tests asserting coalescing.</summary>
		internal int PassCount { get; private set; }

		/// <summary>Requests a pass, coalescing with any already pending or running.</summary>
		public void Request()
		{
			// Mark the work outstanding before scheduling. A change arriving while a pass is running must
			// still cause another pass, because the running pass may already have read its desired state.
			Interlocked.Exchange(ref _requested, 1);

			_ = _dispatch(RunAsync);
		}

		private async Task RunAsync()
		{
			if (_running)
			{
				// A pass is already draining. It will observe the flag this call just set.
				return;
			}

			_running = true;
			try
			{
				while (Interlocked.Exchange(ref _requested, 0) == 1)
				{
					PassCount++;

					// No ConfigureAwait(false): the continuation must stay on the dispatcher, because the
					// state being reconciled is single-threaded.
					await _reconcile();
				}
			}
			finally
			{
				_running = false;
			}
		}
	}
}
