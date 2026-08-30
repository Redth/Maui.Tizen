using System;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal
{
	internal sealed class InboundMessageLifetime
	{
		private readonly object _gate = new();

		private bool _draining;
		private bool _retired;
		private int _activeMessages;
		private TaskCompletionSource<object?>? _drained;

		public async Task<bool> TryRunAsync(Func<Task<bool>> operation)
		{
			ArgumentNullException.ThrowIfNull(operation);

			if (!TryEnter())
				return false;

			try
			{
				return await operation().ConfigureAwait(false);
			}
			finally
			{
				Exit();
			}
		}

		public Task DrainAsync()
		{
			lock (_gate)
			{
				_draining = true;
				if (_activeMessages == 0)
				{
					_retired = true;
					return Task.CompletedTask;
				}

				_drained ??= new TaskCompletionSource<object?>(
					TaskCreationOptions.RunContinuationsAsynchronously);
				return _drained.Task;
			}
		}

		private bool TryEnter()
		{
			lock (_gate)
			{
				if (_retired)
					return false;

				// While an accepted message is still active, admit generation-matched completion
				// messages needed to let it finish. The final active exit closes admission.
				if (_draining && _activeMessages == 0)
				{
					_retired = true;
					return false;
				}

				_activeMessages++;
				return true;
			}
		}

		private void Exit()
		{
			TaskCompletionSource<object?>? drained = null;
			lock (_gate)
			{
				_activeMessages--;
				if (_draining && _activeMessages == 0)
				{
					_retired = true;
					drained = _drained;
					_drained = null;
				}
			}

			drained?.TrySetResult(null);
		}
	}
}
