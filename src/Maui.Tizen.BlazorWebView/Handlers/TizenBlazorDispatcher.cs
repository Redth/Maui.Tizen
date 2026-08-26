using System;
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
		private readonly IDispatcher _dispatcher;

		public TizenBlazorDispatcher(IDispatcher dispatcher)
		{
			_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		}

		public override bool CheckAccess() => !_dispatcher.IsDispatchRequired;

		public override Task InvokeAsync(Action workItem) => _dispatcher.DispatchAsync(workItem);

		public override Task InvokeAsync(Func<Task> workItem) => _dispatcher.DispatchAsync(workItem);

		public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem) => _dispatcher.DispatchAsync(workItem);

		public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem) => _dispatcher.DispatchAsync(workItem);
	}
}
