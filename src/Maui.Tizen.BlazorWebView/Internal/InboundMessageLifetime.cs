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
		private int _originalMessagesRemaining;
		private TaskCompletionSource<object?>? _drained;

		public async Task<bool> TryRunAsync(Func<Task<bool>> operation)
		{
			ArgumentNullException.ThrowIfNull(operation);

			if (!TryEnter(out var original))
				return false;

			try
			{
				return await operation().ConfigureAwait(false);
			}
			finally
			{
				Exit(original);
			}
		}

		public Task DrainAsync()
		{
			lock (_gate)
			{
				if (_draining)
					return _activeMessages == 0 ? Task.CompletedTask : _drained!.Task;

				_draining = true;
				_originalMessagesRemaining = _activeMessages;
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

		private bool TryEnter(out bool original)
		{
			lock (_gate)
			{
				original = false;
				if (_retired)
					return false;

				// While an accepted message is still active, admit generation-matched completion
				// messages needed to let it finish. Once the pre-drain cohort completes, seal
				// admission even while already-admitted follow-up messages are still finishing.
				if (_draining && _originalMessagesRemaining == 0)
				{
					return false;
				}

				original = !_draining;
				_activeMessages++;
				return true;
			}
		}

		private void Exit(bool original)
		{
			TaskCompletionSource<object?>? drained = null;
			lock (_gate)
			{
				_activeMessages--;
				if (_draining && original)
					_originalMessagesRemaining--;

				if (_draining && _originalMessagesRemaining == 0)
					_retired = true;

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
