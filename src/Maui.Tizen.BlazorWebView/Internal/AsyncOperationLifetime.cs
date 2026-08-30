using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal
{
	internal sealed class AsyncOperationLifetime
	{
		private static readonly CancellationToken RetiredToken = new(canceled: true);
		private readonly object _gate = new();

		private bool _retired;
		private int _activeOperations;
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

		public async Task RunAsync(Func<Task> operation)
		{
			ArgumentNullException.ThrowIfNull(operation);

			if (!TryEnter())
			{
				await Task.FromCanceled(RetiredToken).ConfigureAwait(false);
				return;
			}

			try
			{
				await operation().ConfigureAwait(false);
			}
			finally
			{
				Exit();
			}
		}

		public async Task<TResult> RunAsync<TResult>(Func<Task<TResult>> operation)
		{
			ArgumentNullException.ThrowIfNull(operation);

			if (!TryEnter())
				return await Task.FromCanceled<TResult>(RetiredToken).ConfigureAwait(false);

			try
			{
				return await operation().ConfigureAwait(false);
			}
			finally
			{
				Exit();
			}
		}

		public Task RetireAsync()
		{
			lock (_gate)
			{
				_retired = true;
				if (_activeOperations == 0)
					return Task.CompletedTask;

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

				_activeOperations++;
				return true;
			}
		}

		private void Exit()
		{
			TaskCompletionSource<object?>? drained = null;
			lock (_gate)
			{
				_activeOperations--;
				if (_retired && _activeOperations == 0)
				{
					drained = _drained;
					_drained = null;
				}
			}

			drained?.TrySetResult(null);
		}
	}
}
