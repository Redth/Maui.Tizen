using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Platforms.Tizen.BlazorWebView.Internal;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView
{
	/// <summary>
	/// Adapts the MAUI <see cref="IDispatcher"/> exposed by the Tizen backend to the Blazor
	/// <see cref="Microsoft.AspNetCore.Components.Dispatcher"/> used by <c>WebViewManager</c>.
	/// </summary>
	/// <remarks>
	/// The equivalent upstream adapter (<c>MauiDispatcher</c>) is internal to
	/// <c>Microsoft.AspNetCore.Components.WebView.Maui</c>, so the standalone Tizen package supplies its own.
	/// It deliberately depends only on the public <see cref="IDispatcher"/> abstraction, which the Tizen core
	/// backend registers, rather than on any Tizen specific dispatcher type.
	/// </remarks>
	internal sealed class TizenBlazorDispatcher : Microsoft.AspNetCore.Components.Dispatcher
	{
		internal sealed class OperationCapture : IDisposable
		{
			internal sealed class Reservation
			{
				private readonly OperationCapture _owner;
				private int _completed;

				public Reservation(OperationCapture owner) => _owner = owner;

				public void Attach(Task operation)
				{
					if (Interlocked.Exchange(ref _completed, 1) != 0)
						return;

					if (operation.IsCompleted)
					{
						_owner.Complete(operation);
						return;
					}

					_ = operation.ContinueWith(
						static (completed, state) =>
							((OperationCapture)state!).Complete(completed),
						_owner,
						CancellationToken.None,
						TaskContinuationOptions.ExecuteSynchronously,
						TaskScheduler.Default);
				}

				public void Fail(Exception exception)
				{
					if (Interlocked.Exchange(ref _completed, 1) == 0)
						_owner.Complete(exception);
				}
			}

			private readonly object _gate = new();
			private readonly TizenBlazorDispatcher _owner;
			private readonly OperationCapture? _previous;

			private List<Exception>? _failures;
			private TaskCompletionSource<object?>? _drained;
			private Task? _drainTask;
			private int _activeOperations;
			private bool _draining;
			private bool _sealed;

			public OperationCapture(
				TizenBlazorDispatcher owner,
				OperationCapture? previous)
			{
				_owner = owner;
				_previous = previous;
			}

			public Task DrainAsync()
			{
				lock (_gate)
				{
					if (_drainTask is not null)
						return _drainTask;

					_draining = true;
					if (_activeOperations == 0)
					{
						_sealed = true;
						return _drainTask = CreateTerminalTask();
					}

					_drained = new TaskCompletionSource<object?>(
						TaskCreationOptions.RunContinuationsAsynchronously);
					return _drainTask = _drained.Task;
				}
			}

			public void Dispose()
			{
				if (ReferenceEquals(_owner._operationCapture.Value, this))
					_owner._operationCapture.Value = _previous;
			}

			internal Reservation? Reserve()
			{
				lock (_gate)
				{
					if (_sealed)
						return null;

					_activeOperations++;
					return new Reservation(this);
				}
			}

			private void Complete(Task operation)
			{
				lock (_gate)
				{
					if (operation.IsFaulted)
					{
						(_failures ??= new()).AddRange(
							operation.Exception!.Flatten().InnerExceptions);
					}
					else if (operation.IsCanceled)
					{
						(_failures ??= new()).Add(new TaskCanceledException(operation));
					}

					CompleteReservation();
				}
			}

			private void Complete(Exception exception)
			{
				lock (_gate)
				{
					(_failures ??= new()).Add(exception);
					CompleteReservation();
				}
			}

			private void CompleteReservation()
			{
				_activeOperations--;
				if (!_draining || _activeOperations != 0)
					return;

				_sealed = true;
				if (_failures is { Count: 1 })
					_drained!.TrySetException(_failures[0]);
				else if (_failures is not null)
					_drained!.TrySetException(
						new AggregateException(
							"One or more captured dispatcher operations failed.",
							_failures));
				else
					_drained!.TrySetResult(null);
			}

			private Task CreateTerminalTask()
			{
				if (_failures is { Count: 1 })
					return Task.FromException(_failures[0]);

				if (_failures is not null)
				{
					return Task.FromException(
						new AggregateException(
							"One or more captured dispatcher operations failed.",
							_failures));
				}

				return Task.CompletedTask;
			}
		}

		private readonly IDispatcher _dispatcher;
		private readonly AsyncOperationLifetime _lateOperations = new();
		private readonly AsyncLocal<OperationCapture?> _operationCapture = new();

		public TizenBlazorDispatcher(IDispatcher dispatcher)
		{
			_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		}

		public override bool CheckAccess() => !_dispatcher.IsDispatchRequired;

		public override Task InvokeAsync(Action workItem) =>
			Track(() => _dispatcher.DispatchAsync(workItem));

		public override Task InvokeAsync(Func<Task> workItem) =>
			Track(() => _dispatcher.DispatchAsync(workItem));

		public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem) =>
			Track(() => _dispatcher.DispatchAsync(workItem));

		public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem) =>
			Track(() => _dispatcher.DispatchAsync(workItem));

		internal OperationCapture BeginOperationCapture()
		{
			var capture = new OperationCapture(this, _operationCapture.Value);
			_operationCapture.Value = capture;
			return capture;
		}

		internal Task RetireAsync() => _lateOperations.RetireAsync();

		private Task Track(Func<Task> dispatch)
		{
			var capture = _operationCapture.Value;
			var reservation = capture?.Reserve();
			if (capture is not null && reservation is null)
				return _lateOperations.RunAsync(dispatch);

			try
			{
				var operation = dispatch();
				reservation?.Attach(operation);
				return operation;
			}
			catch (Exception ex)
			{
				reservation?.Fail(ex);
				throw;
			}
		}

		private Task<TResult> Track<TResult>(Func<Task<TResult>> dispatch)
		{
			var capture = _operationCapture.Value;
			var reservation = capture?.Reserve();
			if (capture is not null && reservation is null)
				return _lateOperations.RunAsync(dispatch);

			try
			{
				var operation = dispatch();
				reservation?.Attach(operation);
				return operation;
			}
			catch (Exception ex)
			{
				reservation?.Fail(ex);
				throw;
			}
		}
	}
}
