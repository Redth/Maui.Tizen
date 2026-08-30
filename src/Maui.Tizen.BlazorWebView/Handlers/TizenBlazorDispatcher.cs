using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Dispatching;

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
			private readonly object _gate = new();
			private readonly TizenBlazorDispatcher _owner;
			private readonly OperationCapture? _previous;
			private readonly List<Task> _operations = new();

			public OperationCapture(
				TizenBlazorDispatcher owner,
				OperationCapture? previous)
			{
				_owner = owner;
				_previous = previous;
			}

			public async Task DrainAsync()
			{
				var drained = 0;
				while (true)
				{
					Task[] operations;
					lock (_gate)
					{
						if (drained == _operations.Count)
							return;

						operations = _operations.GetRange(
							drained,
							_operations.Count - drained).ToArray();
						drained = _operations.Count;
					}

					await Task.WhenAll(operations).ConfigureAwait(false);
				}
			}

			public void Dispose()
			{
				if (ReferenceEquals(_owner._operationCapture.Value, this))
					_owner._operationCapture.Value = _previous;
			}

			internal void Track(Task operation)
			{
				lock (_gate)
				{
					_operations.Add(operation);
				}
			}
		}

		private readonly IDispatcher _dispatcher;
		private readonly AsyncLocal<OperationCapture?> _operationCapture = new();

		public TizenBlazorDispatcher(IDispatcher dispatcher)
		{
			_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		}

		public override bool CheckAccess() => !_dispatcher.IsDispatchRequired;

		public override Task InvokeAsync(Action workItem) => _dispatcher.DispatchAsync(workItem);

		public override Task InvokeAsync(Func<Task> workItem) =>
			Track(_dispatcher.DispatchAsync(workItem));

		public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem) => _dispatcher.DispatchAsync(workItem);

		public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
		{
			var operation = _dispatcher.DispatchAsync(workItem);
			Track(operation);
			return operation;
		}

		internal OperationCapture BeginOperationCapture()
		{
			var capture = new OperationCapture(this, _operationCapture.Value);
			_operationCapture.Value = capture;
			return capture;
		}

		private Task Track(Task operation)
		{
			_operationCapture.Value?.Track(operation);
			return operation;
		}
	}
}
