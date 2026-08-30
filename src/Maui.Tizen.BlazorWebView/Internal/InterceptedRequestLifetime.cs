using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal
{
	internal sealed class InterceptedRequestLifetime
	{
		private readonly object _gate = new();
		private readonly Action<IInterceptedRequest> _process;
		private readonly ILogger _logger;

		private bool _retired;
		private int _activeRequests;
		private TaskCompletionSource<object?>? _drained;

		public InterceptedRequestLifetime(
			Action<IInterceptedRequest> process,
			ILogger logger)
		{
			_process = process ?? throw new ArgumentNullException(nameof(process));
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public void Process(IInterceptedRequest request)
		{
			ArgumentNullException.ThrowIfNull(request);

			if (!TryEnter())
			{
				Ignore(request);
				return;
			}

			try
			{
				_process(request);
			}
			catch (Exception ex)
			{
				ReportFailure(ex);
				Ignore(request);
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
				if (_activeRequests == 0)
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

				_activeRequests++;
				return true;
			}
		}

		private void Exit()
		{
			TaskCompletionSource<object?>? drained = null;
			lock (_gate)
			{
				_activeRequests--;
				if (_retired && _activeRequests == 0)
				{
					drained = _drained;
					_drained = null;
				}
			}

			drained?.TrySetResult(null);
		}

		private void Ignore(IInterceptedRequest request)
		{
			try
			{
				request.Ignore();
			}
			catch (Exception ex)
			{
				ReportFailure(ex);
			}
		}

		private void ReportFailure(Exception exception)
		{
			try
			{
				_logger.LogError(exception, "Handling an intercepted Tizen BlazorWebView request failed.");
			}
			catch
			{
				// A native callback must not fail because logging failed.
			}
		}
	}
}
