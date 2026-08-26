using System;
using System.Threading;
using Microsoft.Maui.Animations;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Animation <see cref="Ticker"/> driven by a ~60 fps timer that marshals onto the Tizen main
	/// loop.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.Animations.PlatformTicker</c> (Tizen) in dotnet/maui. MAUI's
	/// type is named <c>PlatformTicker</c> in the neutral <c>Microsoft.Maui.Animations</c>
	/// namespace; re-declaring that name here would collide (CS0433) for anyone compiling against
	/// both assemblies, so this backend owns a distinctly named ticker.
	/// </remarks>
	public class TizenTicker : Ticker, IDisposable
	{
		readonly Timer _timer;
		readonly SynchronizationContext? _context;
		bool _isRunning;

		/// <summary>Frame interval in milliseconds (~60 fps), matching dotnet/maui.</summary>
		public const int FrameIntervalMilliseconds = 16;

		/// <summary>Initializes a new instance of the <see cref="TizenTicker"/> class.</summary>
		public TizenTicker()
		{
			EnsureSynchronizationContext();

			_context = SynchronizationContext.Current;
			_timer = new Timer(OnElapsed, this, Timeout.Infinite, Timeout.Infinite);
		}

		/// <inheritdoc />
		public override bool IsRunning => _isRunning;

		/// <inheritdoc />
		public override void Start()
		{
			_timer.Change(FrameIntervalMilliseconds, FrameIntervalMilliseconds);
			_isRunning = true;
		}

		/// <inheritdoc />
		public override void Stop()
		{
			_timer.Change(Timeout.Infinite, Timeout.Infinite);
			_isRunning = false;
		}

		void OnElapsed(object? state)
		{
			if (_context is not null)
				_context.Post(_ => Fire?.Invoke(), null);
			else
				Fire?.Invoke();
		}

		static void EnsureSynchronizationContext()
		{
#if TIZEN
			if (SynchronizationContext.Current is null)
				global::Tizen.Applications.TizenSynchronizationContext.Initialize();
#endif
		}

		/// <inheritdoc />
		public void Dispose()
		{
			_timer.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
