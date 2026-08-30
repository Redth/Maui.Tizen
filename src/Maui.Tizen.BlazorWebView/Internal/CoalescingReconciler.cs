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
		private readonly object _gate = new();
		private readonly Func<Task> _reconcile;
		private readonly Func<Func<Task>, Task> _dispatch;

		/// <summary>Non-zero when a pass is owed. Written from any thread.</summary>
		private int _requested;

		private bool _running;
		private bool _retired;
		private TaskCompletionSource<object?>? _idle;

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
			lock (_gate)
			{
				if (_retired)
					return;

				// Mark the work outstanding before scheduling. A change arriving while a pass is running
				// must still cause another pass, because the running pass may already have read its desired state.
				Interlocked.Exchange(ref _requested, 1);
			}

			// Do not dispatch under the lock. A dispatcher can run inline when the caller already has
			// access, and RunAsync takes the same lock.
			_ = _dispatch(RunAsync);
		}

		/// <summary>
		/// Rejects future requests and completes after an active reconciliation pass has stopped.
		/// </summary>
		public Task RetireAsync()
		{
			lock (_gate)
			{
				_retired = true;
				Interlocked.Exchange(ref _requested, 0);
				return _running ? _idle!.Task : Task.CompletedTask;
			}
		}

		private async Task RunAsync()
		{
			TaskCompletionSource<object?> idle;
			lock (_gate)
			{
				if (_retired || _running)
				{
					// A pass is already draining. It will observe the flag this call just set.
					return;
				}

				_running = true;
				idle = _idle = new TaskCompletionSource<object?>(
					TaskCreationOptions.RunContinuationsAsynchronously);
			}

			var reschedule = false;
			try
			{
				while (true)
				{
					lock (_gate)
					{
						if (_retired)
						{
							Interlocked.Exchange(ref _requested, 0);
							break;
						}
					}

					if (Interlocked.Exchange(ref _requested, 0) == 0)
						break;

					PassCount++;

					// No ConfigureAwait(false): the continuation must stay on the dispatcher, because the
					// state being reconciled is single-threaded.
					await _reconcile();
				}
			}
			finally
			{
				lock (_gate)
				{
					_running = false;
					_idle = null;
					reschedule = !_retired && Volatile.Read(ref _requested) != 0;
				}

				idle.TrySetResult(null);

				// A request can arrive after the final flag read but before _running is cleared. Its
				// scheduled drain observes _running and returns, so explicitly schedule the owed pass.
				if (reschedule)
					_ = _dispatch(RunAsync);
			}
		}
	}
}
