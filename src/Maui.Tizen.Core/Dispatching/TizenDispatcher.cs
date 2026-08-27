using System;
using System.Threading;
using Microsoft.Maui.Dispatching;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// <see cref="IDispatcher"/> implementation backed by the Tizen main-loop
	/// <see cref="SynchronizationContext"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ported from <c>Microsoft.Maui.Dispatching.Dispatcher</c> (Tizen) in dotnet/maui. MAUI's own
	/// type is a partial class stitched together per-platform inside <c>Microsoft.Maui.dll</c>, so
	/// an out-of-repo backend cannot contribute to it. This is a standalone implementation of the
	/// same public <see cref="IDispatcher"/> contract with identical behaviour.
	/// </para>
	/// <para>
	/// Nothing here is Tizen specific at the type level - it only needs the platform's
	/// <see cref="SynchronizationContext"/> - which is why the whole dispatcher stack is unit
	/// testable on the host.
	/// </para>
	/// </remarks>
	public class TizenDispatcher : IDispatcher
	{
		readonly SynchronizationContext _context;
		readonly TimeProvider _timeProvider;

		/// <summary>Initializes a new instance of the <see cref="TizenDispatcher"/> class.</summary>
		/// <param name="context">The synchronization context of the Tizen main loop.</param>
		public TizenDispatcher(SynchronizationContext context)
			: this(context, TimeProvider.System)
		{
		}

		/// <summary>
		/// Initializes a new instance with an explicit <see cref="TimeProvider"/>, so delayed
		/// dispatch can be driven deterministically.
		/// </summary>
		/// <remarks>
		/// Internal rather than public: it exists for testability, and the unit-test lane compiles
		/// these sources directly, so it does not need to widen the package's public surface.
		///
		/// Without it the delayed-dispatch tests could only wait on the wall clock and hope, which
		/// is exactly how ConcurrentDelayedDispatchesEachFireExactlyOnce came to fail in CI with
		/// none of its callbacks fired - the dispatcher was fine, the runner was just busy.
		/// </remarks>
		internal TizenDispatcher(SynchronizationContext context, TimeProvider timeProvider)
		{
			_context = context ?? throw new ArgumentNullException(nameof(context));
			_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
		}

		/// <inheritdoc />
		public bool IsDispatchRequired => _context != SynchronizationContext.Current;

		/// <inheritdoc />
		public bool Dispatch(Action action)
		{
			ArgumentNullException.ThrowIfNull(action);

			_context.Post(_ => action(), null);
			return true;
		}

		/// <inheritdoc />
		/// <remarks>
		/// The timer is one-shot: the period is <see cref="Timeout.InfiniteTimeSpan"/>, so it fires
		/// exactly once and is then disposed.
		/// <para>
		/// dotnet/maui's Tizen dispatcher passes <c>delay</c> as both due time and period, making
		/// the timer repeating, and relies on disposing it from inside its own callback to stop it.
		/// That races: the callback runs on a thread-pool thread while <c>Dispose</c> is in flight,
		/// so a second tick can be queued before the first completes, and the caller's action runs
		/// more than once. The action here is posted to the main loop, so a duplicate is a real
		/// user-visible double execution rather than a harmless extra tick.
		/// </para>
		/// </remarks>
		public bool DispatchDelayed(TimeSpan delay, Action action)
		{
			ArgumentNullException.ThrowIfNull(action);

			var state = new DelayedDispatch(_context, action, _timeProvider);
			state.Start(delay);

			return true;
		}

		/// <summary>
		/// Owns the one-shot timer for a single <see cref="DispatchDelayed"/> call, so the timer
		/// field is assigned before the callback can possibly observe it.
		/// </summary>
		sealed class DelayedDispatch
		{
			readonly SynchronizationContext _context;
			readonly Action _action;
			readonly TimeProvider _timeProvider;
			ITimer? _timer;
			int _fired;

			public DelayedDispatch(SynchronizationContext context, Action action, TimeProvider timeProvider)
			{
				_context = context;
				_action = action;
				_timeProvider = timeProvider;
			}

			public void Start(TimeSpan delay)
			{
				// Created stopped, then armed, so _timer is non-null by the time OnTick can run.
				_timer = _timeProvider.CreateTimer(
					static s => ((DelayedDispatch)s!).OnTick(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

				_timer.Change(delay, Timeout.InfiniteTimeSpan);
			}

			void OnTick()
			{
				// Interlocked rather than the timer period alone. The period being infinite is the
				// first line of defence and disposing before posting is the second, but neither is
				// airtight on its own if the timer implementation ever queues a tick concurrently.
				// This makes "exactly once" a property of the dispatch itself.
				if (Interlocked.Exchange(ref _fired, 1) != 0)
					return;

				// Dispose before posting: the timer is already one-shot, so there is nothing left
				// to cancel, and this cannot suppress the dispatch.
				_timer?.Dispose();
				_timer = null;

				_context.Post(static s => ((Action)s!)(), _action);
			}
		}

		/// <inheritdoc />
		public IDispatcherTimer CreateTimer() => new TizenDispatcherTimer(_context, _timeProvider);
	}

	/// <summary>
	/// <see cref="IDispatcherTimer"/> implementation that raises ticks on the Tizen main loop.
	/// </summary>
	/// <remarks>Ported from <c>Microsoft.Maui.Dispatching.DispatcherTimer</c> (Tizen).</remarks>
	public class TizenDispatcherTimer : IDispatcherTimer, IDisposable
	{
		readonly SynchronizationContext _context;
		readonly ITimer _timer;
		volatile bool _disposed;

		/// <summary>
		/// Incremented by every <see cref="Start"/> and <see cref="Stop"/>, and captured in each
		/// posted tick.
		/// </summary>
		/// <remarks>
		/// Ticks are POSTED to the main loop, so one can still be sitting in the queue when the
		/// timer is stopped and started again. Checking IsRunning alone is not enough: by the time
		/// the stale tick is delivered the timer is running again, so it passes the check, raises a
		/// Tick that belongs to the previous run, and - for a one-shot - then disarms the NEW run.
		/// A callback is only honoured when the arming it was queued under is still current.
		/// </remarks>
		int _generation;

		/// <summary>Initializes a new instance of the <see cref="TizenDispatcherTimer"/> class.</summary>
		/// <param name="context">The synchronization context of the Tizen main loop.</param>
		public TizenDispatcherTimer(SynchronizationContext context)
			: this(context, TimeProvider.System)
		{
		}

		/// <summary>
		/// Initializes a new instance with an explicit <see cref="TimeProvider"/>, so ticks can be
		/// driven deterministically. Internal; see <see cref="TizenDispatcher"/>.
		/// </summary>
		internal TizenDispatcherTimer(SynchronizationContext context, TimeProvider timeProvider)
		{
			_context = context ?? throw new ArgumentNullException(nameof(context));
			ArgumentNullException.ThrowIfNull(timeProvider);

			// The arming generation is captured when the tick is POSTED, not when it is delivered,
			// so a callback carries the identity of the run that scheduled it.
			_timer = timeProvider.CreateTimer(
				_ => _context.Post(OnTimerTick, Volatile.Read(ref _generation)),
				null,
				Timeout.InfiniteTimeSpan,
				Timeout.InfiniteTimeSpan);
		}

		/// <inheritdoc />
		public TimeSpan Interval { get; set; }

		/// <inheritdoc />
		public bool IsRepeating { get; set; }

		/// <inheritdoc />
		public bool IsRunning { get; private set; }

		/// <inheritdoc />
		public event EventHandler? Tick;

		/// <inheritdoc />
		public void Start()
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			if (IsRunning)
				return;

			IsRunning = true;

			// New arming: anything queued under the previous one is now stale.
			Interlocked.Increment(ref _generation);

			// A non-repeating timer is armed with an infinite PERIOD rather than being armed to
			// repeat and then disarmed after the first tick. The latter leaves a real window in
			// which the underlying timer can queue a second tick before the disarm lands.
			_timer.Change(Interval, IsRepeating ? Interval : Timeout.InfiniteTimeSpan);
		}

		/// <inheritdoc />
		public void Stop()
		{
			if (!IsRunning)
				return;

			IsRunning = false;

			// Invalidate anything already queued, so a tick delivered after this Stop cannot be
			// honoured even if the timer is restarted before the loop pumps.
			Interlocked.Increment(ref _generation);

			if (!_disposed)
				_timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
		}

		void OnTimerTick(object? state)
		{
			// A tick is posted to the main loop, so it is queued behind whatever else is on the
			// loop and can be delivered AFTER Stop or Dispose has already run. Both are checked
			// here, and again after the handler, because the handler itself is a very common place
			// to call Stop() or Dispose().
			//
			// The _disposed half of this entry check is redundant with IsRunning on a single
			// thread, since Dispose clears both; it is kept for the concurrent case and is
			// deliberately NOT claimed as test-covered - no deterministic test can distinguish it.
			if (_disposed || !IsRunning)
				return;

			// Queued under a previous arming - Stop, or Stop followed by Start - so it belongs to
			// a run that is over. Honouring it would raise a spurious Tick and, for a one-shot,
			// disarm the run currently in progress.
			if (state is int queuedGeneration && queuedGeneration != Volatile.Read(ref _generation))
				return;

			// The shot has been fired, so a one-shot is no longer running BEFORE the handler sees
			// it. Start() is a no-op while IsRunning is true, so leaving it set meant a handler
			// that called Start() to re-arm was silently ignored and the timer stopped dead - the
			// standard shape for a self-scheduling loop with a variable delay, which MAUI's own
			// animation code uses.
			if (!IsRepeating)
				IsRunning = false;

			Tick?.Invoke(this, EventArgs.Empty);

			if (_disposed)
				return;

			// IsRunning now reflects whatever the handler did: left alone for a one-shot it is
			// false and the timer is disarmed; if the handler called Start() it is true and the
			// timer is already re-armed, so this correctly leaves that arming alone.
			//
			// A generation counter was tried here to distinguish "handler re-armed" from "one-shot
			// finished". With IsRunning cleared before the handler it turned out to be redundant -
			// no mutation of it could be made to fail a test - so it was removed rather than kept
			// as unjustifiable complexity.
			if (!IsRunning)
				_timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
		}

		/// <inheritdoc />
		public void Dispose()
		{
			if (_disposed)
				return;

			// Mark disposed BEFORE tearing the timer down, so a tick already queued on the loop
			// sees the flag and returns instead of raising Tick on a disposed timer.
			_disposed = true;
			IsRunning = false;

			_timer.Dispose();
			GC.SuppressFinalize(this);
		}
	}

	/// <summary>
	/// <see cref="IDispatcherProvider"/> that hands out a <see cref="TizenDispatcher"/> for the
	/// current thread's <see cref="SynchronizationContext"/>.
	/// </summary>
	/// <remarks>
	/// Registering this provider is what lets <c>Microsoft.Maui.ApplicationModel.MainThread</c>
	/// work on Tizen through the .NET 11 dispatcher bridge - there is deliberately no port of
	/// MAUI's <c>MainThread.tizen.cs</c> in this backend.
	/// </remarks>
	public class TizenDispatcherProvider : IDispatcherProvider
	{
		[ThreadStatic]
		static IDispatcher? _threadDispatcher;

		/// <inheritdoc />
		public IDispatcher? GetForCurrentThread()
		{
			if (_threadDispatcher is not null)
				return _threadDispatcher;

			var context = SynchronizationContext.Current;
			if (context is null)
				return null;

			_threadDispatcher = new TizenDispatcher(context);
			return _threadDispatcher;
		}
	}
}
