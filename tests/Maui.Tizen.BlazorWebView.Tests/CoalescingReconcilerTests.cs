using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	/// <summary>
	/// Covers the serialization/coalescing primitive behind root component reconciliation.
	/// </summary>
	/// <remarks>
	/// The real reconciliation pass needs a <c>TizenWebViewManager</c>, which only exists on device. The
	/// correctness risk is not in the mount/unmount calls themselves though - it is in the scheduling:
	/// independent passes carrying stale snapshots could leave components mounted that the application
	/// had already removed. That scheduling is what these tests drive, against a model of the same shape
	/// (a desired collection, a mounted set, and an awaiting reconcile pass).
	/// </remarks>
	public class CoalescingReconcilerTests
	{
		/// <summary>
		/// Stands in for the Blazor dispatcher: a real single-threaded message loop.
		/// </summary>
		/// <remarks>
		/// Modelling this faithfully matters more than it looks. An earlier version of this fake chained
		/// each work item onto the previous one's completion, which serialized passes all by itself - and
		/// therefore made these tests pass even with the coalescing guard deleted. A real dispatcher does
		/// not do that: when a work item awaits, it releases the loop and the next queued item runs, which
		/// is precisely how independent reconciliation passes came to interleave. So this pumps a queue,
		/// and continuations post back to that queue via a <see cref="SynchronizationContext"/>.
		/// </remarks>
		private sealed class LoopDispatcher
		{
			private readonly Queue<Action> _queue = new();

			/// <summary>
			/// When set, work runs immediately on the calling thread instead of being queued - what a
			/// real dispatcher does when it is already on its own thread. Continuations still post to
			/// the queue, so a caller must drain to let an awaiting item finish.
			/// </summary>
			private readonly bool _inline;

			public LoopDispatcher(bool inline = false) => _inline = inline;

			private sealed class QueueContext : SynchronizationContext
			{
				private readonly LoopDispatcher _owner;

				public QueueContext(LoopDispatcher owner) => _owner = owner;

				public override void Post(SendOrPostCallback d, object? state) =>
					_owner._queue.Enqueue(() => d(state));

				public override void Send(SendOrPostCallback d, object? state) => d(state);
			}

			public Task InvokeAsync(Func<Task> work)
			{
				var completion = new TaskCompletionSource();

				if (_inline)
				{
					_ = RunItemAsync(work, completion);
				}
				else
				{
					_queue.Enqueue(() => _ = RunItemAsync(work, completion));
				}

				return completion.Task;
			}

			/// <summary>
			/// Runs <paramref name="action"/> with this dispatcher's context installed, then drains.
			/// </summary>
			/// <remarks>
			/// The context must be installed while the action runs, otherwise an <c>await</c> inside the
			/// work item captures the ambient context (the thread pool) and completes on another thread -
			/// which makes any assertion afterwards a race rather than a deterministic check.
			/// </remarks>
			public void RunWithContext(Action action)
			{
				var previous = SynchronizationContext.Current;
				SynchronizationContext.SetSynchronizationContext(new QueueContext(this));
				try
				{
					action();
				}
				finally
				{
					SynchronizationContext.SetSynchronizationContext(previous);
				}

				RunToCompletion();
			}

			private static async Task RunItemAsync(Func<Task> work, TaskCompletionSource completion)
			{
				try
				{
					await work();
					completion.TrySetResult();
				}
				catch (Exception ex)
				{
					completion.TrySetException(ex);
				}
			}

			/// <summary>Runs at most <paramref name="steps"/> queued items, leaving the rest pending.</summary>
			public void Pump(int steps) => Pump(steps, drain: false);

			/// <summary>Runs until the queue is empty.</summary>
			public void RunToCompletion() => Pump(int.MaxValue, drain: true);

			private void Pump(int steps, bool drain)
			{
				var previous = SynchronizationContext.Current;
				SynchronizationContext.SetSynchronizationContext(new QueueContext(this));
				try
				{
					var executed = 0;
					while (_queue.Count > 0 && (drain || executed < steps))
					{
						_queue.Dequeue()();
						executed++;
					}
				}
				finally
				{
					SynchronizationContext.SetSynchronizationContext(previous);
				}
			}
		}

		/// <summary>Mirrors the handler: a desired collection reconciled into a mounted set.</summary>
		private sealed class ComponentModel
		{
			public List<string> Desired { get; } = new();

			public List<string> Mounted { get; } = new();

			public int MaxConcurrentPasses { get; private set; }

			private int _activePasses;

			public async Task ReconcileAsync()
			{
				_activePasses++;
				MaxConcurrentPasses = Math.Max(MaxConcurrentPasses, _activePasses);

				// Re-read desired state on every pass. Snapshotting here is the bug being guarded against.
				var desired = Desired.ToList();

				foreach (var stale in Mounted.Except(desired).ToList())
				{
					// Await mid-pass: this is the suspension point that let independent passes interleave.
					await Task.Yield();
					Mounted.Remove(stale);
				}

				foreach (var added in desired.Except(Mounted).ToList())
				{
					await Task.Yield();
					Mounted.Add(added);
				}

				_activePasses--;
			}
		}

		private static (CoalescingReconciler Reconciler, ComponentModel Model, LoopDispatcher Dispatcher) Create()
		{
			var model = new ComponentModel();
			var dispatcher = new LoopDispatcher();
			var reconciler = new CoalescingReconciler(model.ReconcileAsync, dispatcher.InvokeAsync);
			return (reconciler, model, dispatcher);
		}

		[Fact]
		public void AddFollowedImmediatelyByClearLeavesNothingMounted()
		{
			// The exact failure the review called out: the Clear pass used to compute its removals before
			// the Add pass had recorded the component, leaving it mounted in an emptied collection.
			var (reconciler, model, dispatcher) = Create();

			model.Desired.Add("Counter");
			reconciler.Request();

			// Let the first pass start and suspend at its first await, exactly as it would on device.
			dispatcher.Pump(1);

			model.Desired.Clear();
			reconciler.Request();

			dispatcher.RunToCompletion();

			Assert.Empty(model.Mounted);
		}

		[Fact]
		public void RapidReplaceLeavesOnlyTheNewestComponentMounted()
		{
			var (reconciler, model, dispatcher) = Create();

			foreach (var name in new[] { "First", "Second", "Third" })
			{
				model.Desired.Clear();
				model.Desired.Add(name);
				reconciler.Request();

				// Each replace lands while the previous pass is still in flight.
				dispatcher.Pump(1);
			}

			dispatcher.RunToCompletion();

			Assert.Equal(new[] { "Third" }, model.Mounted);
		}

		[Fact]
		public void PassesNeverRunConcurrently()
		{
			var (reconciler, model, dispatcher) = Create();

			for (var i = 0; i < 25; i++)
			{
				model.Desired.Add($"Component{i}");
				reconciler.Request();
				dispatcher.Pump(1);
			}

			dispatcher.RunToCompletion();

			// Overlapping passes would corrupt the mounted set, which is single-threaded state.
			Assert.Equal(1, model.MaxConcurrentPasses);
		}

		[Fact]
		public void BurstOfChangesIsCoalescedIntoFewerPassesThanRequests()
		{
			var (reconciler, model, dispatcher) = Create();

			for (var i = 0; i < 50; i++)
			{
				model.Desired.Add($"Component{i}");
				reconciler.Request();
			}

			dispatcher.RunToCompletion();

			Assert.True(
				reconciler.PassCount < 50,
				$"Expected requests to coalesce, but every one of the 50 ran its own pass ({reconciler.PassCount}).");
		}

		[Fact]
		public void FinalStateIsReachedNoMatterHowManyPassesCoalesced()
		{
			// Coalescing must never lose the last change: the flag is set before scheduling, so a request
			// arriving mid-pass always earns another pass.
			var (reconciler, model, dispatcher) = Create();

			for (var i = 0; i < 40; i++)
			{
				model.Desired.Clear();
				model.Desired.Add($"Component{i}");
				reconciler.Request();
				dispatcher.Pump(1);
			}

			dispatcher.RunToCompletion();

			Assert.Equal(new[] { "Component39" }, model.Mounted);
		}

		[Fact]
		public void RequestAfterQuiesceStartsAFreshPass()
		{
			var (reconciler, model, dispatcher) = Create();

			model.Desired.Add("First");
			reconciler.Request();
			dispatcher.RunToCompletion();

			model.Desired.Add("Second");
			reconciler.Request();
			dispatcher.RunToCompletion();

			Assert.Equal(new[] { "First", "Second" }, model.Mounted);
		}

		[Fact]
		public void RequestIsNotLostWhenTheDispatcherRunsInline()
		{
			// Pins the ordering inside Request: the "work is owed" flag must be set BEFORE the pass is
			// scheduled. A queueing dispatcher can never expose this, because queueing guarantees the flag
			// is set first. Running inline - which is what a dispatcher does when it is already on its own
			// thread - closes that gap: if the flag were set after scheduling, the pass would run, observe
			// no work, exit, and the change would be stranded with nothing left to consume it.
			var model = new ComponentModel();
			var dispatcher = new LoopDispatcher(inline: true);
			var reconciler = new CoalescingReconciler(model.ReconcileAsync, dispatcher.InvokeAsync);

			model.Desired.Add("Counter");
			dispatcher.RunWithContext(() => reconciler.Request());

			Assert.Equal(new[] { "Counter" }, model.Mounted);
			Assert.Equal(1, reconciler.PassCount);
		}

		[Fact]
		public void ConstructorRejectsMissingCollaborators()
		{
			Assert.Throws<ArgumentNullException>(() => new CoalescingReconciler(null!, _ => Task.CompletedTask));
			Assert.Throws<ArgumentNullException>(() => new CoalescingReconciler(() => Task.CompletedTask, null!));
		}

		[Fact]
		public void ReconciliationFailuresAreReportedAndDoNotBlockALaterPass()
		{
			var attempts = 0;
			var failures = new List<Exception>();
			var expected = new InvalidOperationException("reconcile failed");
			var reconciler = new CoalescingReconciler(
				() =>
				{
					attempts++;
					return attempts == 1
						? Task.FromException(expected)
						: Task.CompletedTask;
				},
				work => work(),
				failures.Add);

			reconciler.Request();
			reconciler.Request();

			Assert.Equal(2, attempts);
			Assert.Equal(new[] { expected }, failures);
		}
	}
}
