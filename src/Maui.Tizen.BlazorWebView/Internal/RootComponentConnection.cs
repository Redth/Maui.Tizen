using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal
{
	internal sealed class RootComponentConnection
	{
		private readonly object _desiredGate = new();
		private readonly Func<RootComponent, Task> _add;
		private readonly Func<RootComponent, Task> _remove;
		private readonly List<RootComponent> _mounted = new();
		private readonly CoalescingReconciler _reconciler;
		private readonly TaskCompletionSource<object?> _retirement = new(
			TaskCreationOptions.RunContinuationsAsynchronously);

		private IReadOnlyList<RootComponent> _desired = Array.Empty<RootComponent>();
		private int _retired;

		public RootComponentConnection(
			Func<RootComponent, Task> add,
			Func<RootComponent, Task> remove,
			Func<Func<Task>, Task> dispatch,
			Action<Exception>? reportFailure = null)
		{
			_add = add ?? throw new ArgumentNullException(nameof(add));
			_remove = remove ?? throw new ArgumentNullException(nameof(remove));
			_reconciler = new CoalescingReconciler(ReconcileAsync, dispatch, reportFailure);
		}

		public void UpdateDesired(IEnumerable<RootComponent>? desired)
		{
			if (Volatile.Read(ref _retired) != 0)
				return;

			lock (_desiredGate)
			{
				if (_retired != 0)
					return;

				_desired = desired?.ToArray() ?? Array.Empty<RootComponent>();
			}

			_reconciler.Request();
		}

		public Task RetireAsync()
		{
			Interlocked.Exchange(ref _retired, 1);
			_retirement.TrySetResult(null);
			lock (_desiredGate)
			{
				_desired = Array.Empty<RootComponent>();
			}

			return _reconciler.RetireAsync();
		}

		internal IReadOnlyList<RootComponent> Mounted => _mounted;

		private async Task ReconcileAsync()
		{
			if (Volatile.Read(ref _retired) != 0)
				return;

			IReadOnlyList<RootComponent> desired;
			lock (_desiredGate)
			{
				desired = _desired;
			}

			foreach (var item in _mounted.Except(desired).ToList())
			{
				if (Volatile.Read(ref _retired) != 0)
					return;

				if (!await CompleteBeforeRetirementAsync(_remove(item)))
					return;

				if (Volatile.Read(ref _retired) != 0)
					return;

				_mounted.Remove(item);
			}

			foreach (var item in desired.Except(_mounted).ToList())
			{
				if (Volatile.Read(ref _retired) != 0)
					return;

				if (!await CompleteBeforeRetirementAsync(_add(item)))
					return;

				if (Volatile.Read(ref _retired) != 0)
					return;

				_mounted.Add(item);
			}
		}

		private async Task<bool> CompleteBeforeRetirementAsync(Task operation)
		{
			if (operation.IsCompleted)
			{
				await operation;
				return true;
			}

			var completed = await Task.WhenAny(operation, _retirement.Task);
			if (ReferenceEquals(completed, operation))
			{
				await operation;
				return true;
			}

			// Manager disposal owns cancellation after retirement. Observe any eventual failure so the
			// abandoned render acknowledgement cannot become an unobserved task exception.
			_ = operation.ContinueWith(
				static task => _ = task.Exception,
				CancellationToken.None,
				TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
			return false;
		}
	}
}
