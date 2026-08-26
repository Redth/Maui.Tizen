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

		/// <summary>Initializes a new instance of the <see cref="TizenDispatcher"/> class.</summary>
		/// <param name="context">The synchronization context of the Tizen main loop.</param>
		public TizenDispatcher(SynchronizationContext context) =>
			_context = context ?? throw new ArgumentNullException(nameof(context));

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
		public bool DispatchDelayed(TimeSpan delay, Action action)
		{
			ArgumentNullException.ThrowIfNull(action);

			Timer? timer = null;
			timer = new Timer(
				_ =>
				{
					_context.Post(_ => action(), null);
					timer?.Dispose();
				},
				null,
				Timeout.Infinite,
				Timeout.Infinite);

			timer.Change(delay, delay);
			return true;
		}

		/// <inheritdoc />
		public IDispatcherTimer CreateTimer() => new TizenDispatcherTimer(_context);
	}

	/// <summary>
	/// <see cref="IDispatcherTimer"/> implementation that raises ticks on the Tizen main loop.
	/// </summary>
	/// <remarks>Ported from <c>Microsoft.Maui.Dispatching.DispatcherTimer</c> (Tizen).</remarks>
	public class TizenDispatcherTimer : IDispatcherTimer, IDisposable
	{
		readonly SynchronizationContext _context;
		readonly Timer _timer;

		/// <summary>Initializes a new instance of the <see cref="TizenDispatcherTimer"/> class.</summary>
		/// <param name="context">The synchronization context of the Tizen main loop.</param>
		public TizenDispatcherTimer(SynchronizationContext context)
		{
			_context = context ?? throw new ArgumentNullException(nameof(context));
			_timer = new Timer(_ => _context.Post(OnTimerTick, null), null, Timeout.Infinite, Timeout.Infinite);
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
			if (IsRunning)
				return;

			IsRunning = true;

			// The interval is applied separately so the callback cannot run before the field is set.
			_timer.Change(Interval, Interval);
		}

		/// <inheritdoc />
		public void Stop()
		{
			if (!IsRunning)
				return;

			IsRunning = false;
			_timer.Change(Timeout.Infinite, Timeout.Infinite);
		}

		void OnTimerTick(object? state)
		{
			if (!IsRunning)
				return;

			Tick?.Invoke(this, EventArgs.Empty);

			if (!IsRepeating)
			{
				IsRunning = false;
				_timer.Change(Timeout.Infinite, Timeout.Infinite);
			}
		}

		/// <inheritdoc />
		public void Dispose()
		{
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
