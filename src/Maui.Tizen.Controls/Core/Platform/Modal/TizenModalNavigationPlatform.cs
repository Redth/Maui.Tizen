using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Realizes modal pages using only public .NET MAUI handler APIs.
	/// </summary>
	/// <remarks>
	/// This mirrors what <c>Microsoft.Maui.Platform.ElementExtensions.ToPlatform</c> does, but that
	/// extension is compiled per platform and has no Tizen build now that Tizen left the MAUI
	/// repository. The steps here - resolve a handler from the context's handler factory, give it
	/// the context and the virtual view, then take its container or platform view - use only
	/// public, platform-neutral API, which is also what makes modal navigation testable on the host.
	/// </remarks>
	public sealed class TizenModalPageRealizer : ITizenModalPageRealizer
	{
		readonly ConditionalWeakTable<object, IElementHandler> _owners = new();

		/// <inheritdoc/>
		public object Realize(Page page, IMauiContext mauiContext)
		{
			ArgumentNullException.ThrowIfNull(page);
			ArgumentNullException.ThrowIfNull(mauiContext);

			// Element.Handler is IElementHandler; VisualElement narrows it to IViewHandler, so the
			// element-level member is used here to stay compatible with any handler shape.
			var element = (Element)page;
			var handler = element.Handler;
			var ownsHandler = false;

			// A page can be reused across windows - popped from one and pushed modally on another.
			// Its existing handler is bound to the ORIGINATING window's IMauiContext, and reusing
			// it would realize the page into the wrong window's view tree. Discard it and build a
			// fresh one against the target context.
			if (handler is not null && !ReferenceEquals(handler.MauiContext, mauiContext))
			{
				var cleanupFailure = ReleaseHandlerOwnership(element, handler);
				if (cleanupFailure is not null)
				{
					ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
				}

				handler = null;
			}

			try
			{
				if (handler is null)
				{
					handler = mauiContext.Handlers.GetHandler(page.GetType())
						?? throw new InvalidOperationException(
							$"No handler is registered for '{page.GetType()}', so it cannot be presented modally.");

					ownsHandler = true;
					handler.SetMauiContext(mauiContext);
					element.Handler = handler;
				}
				else
				{
					// Same context, but re-apply it so a handler that was created without one - or
					// whose context was cleared on disconnect - is always realized against the target.
					handler.SetMauiContext(mauiContext);
				}

				if (!ReferenceEquals(handler.VirtualView, page))
				{
					handler.SetVirtualView(page);
				}

				var platformView = (handler as IViewHandler)?.ContainerView
					?? handler.PlatformView;

				if (platformView is null)
				{
					throw new InvalidOperationException(
						$"The handler for '{page.GetType()}' produced no platform view, so it cannot be presented modally.");
				}

				_owners.Remove(platformView);
				_owners.Add(platformView, handler);
				return platformView;
			}
			catch (Exception realizationFailure) when (handler is not null && ownsHandler)
			{
				var cleanupFailure = ReleaseHandlerOwnership(element, handler);
				if (cleanupFailure is not null)
				{
					throw new AggregateException(
						"Modal page realization and owned-handler cleanup both failed.",
						realizationFailure,
						cleanupFailure);
				}

				ExceptionDispatchInfo.Capture(realizationFailure).Throw();
				throw new InvalidOperationException("Unreachable.");
			}
		}

		/// <inheritdoc/>
		public void Release(Page page, object platformView, bool platformViewDisposed)
		{
			ArgumentNullException.ThrowIfNull(page);
			ArgumentNullException.ThrowIfNull(platformView);

			var handler = _owners.TryGetValue(platformView, out var owner)
				? owner
				: page.Handler;
			_owners.Remove(platformView);

			if (handler is not null && ReferenceEquals(page.Handler, handler))
			{
				((Element)page).Handler = null;
			}

			if (platformViewDisposed)
			{
				handler?.DisconnectHandler();
			}
			else if (handler is IDisposable disposableHandler)
			{
				disposableHandler.Dispose();
			}
			else
			{
				handler?.DisconnectHandler();

				if (!platformViewDisposed)
				{
					(platformView as IDisposable)?.Dispose();
				}
			}
		}

		static Exception? ReleaseHandlerOwnership(Element element, IElementHandler handler)
		{
			List<Exception>? failures = null;
			object? platformView = null;

			try
			{
				platformView = handler.PlatformView;
			}
			catch (Exception ex)
			{
				(failures ??= new()).Add(ex);
			}

			if (ReferenceEquals(element.Handler, handler))
			{
				element.Handler = null;
			}

			if (handler is IDisposable disposableHandler)
			{
				try
				{
					disposableHandler.Dispose();
				}
				catch (Exception ex)
				{
					(failures ??= new()).Add(ex);
				}
			}
			else
			{
				try
				{
					handler.DisconnectHandler();
				}
				catch (Exception ex)
				{
					(failures ??= new()).Add(ex);
				}

				try
				{
					(platformView as IDisposable)?.Dispose();
				}
				catch (Exception ex)
				{
					(failures ??= new()).Add(ex);
				}
			}

			return failures switch
			{
				null => null,
				{ Count: 1 } => failures[0],
				_ => new AggregateException("One or more modal handler cleanup operations failed.", failures),
			};
		}
	}

	/// <summary>
	/// Presents and dismisses modal pages on the Tizen navigation stack.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the port of <c>ModalNavigationManager.Tizen.cs</c> from dotnet/maui, reshaped onto
	/// the extensibility seam proposed by dotnet/maui#37853. The upstream file was an internal
	/// partial-class completion compiled into <c>Microsoft.Maui.Controls</c> and could not be
	/// supplied from outside the framework at all.
	/// </para>
	/// <para>
	/// The division of labour follows the seam: the framework owns the cross-platform modal stack,
	/// the <c>Appearing</c>/<c>Disappearing</c> notifications, the window modal events and the
	/// reconciliation loop. This type only performs the visual push and pop, which is why the
	/// upstream code's <c>SendDisappearing</c>/<c>SendAppearing</c> calls and its manual
	/// <c>_platformModalPages</c> bookkeeping are deliberately absent - keeping them would raise
	/// those events twice.
	/// </para>
	/// </remarks>
	public sealed class TizenModalNavigationPlatform : IModalNavigationPlatform
	{
		sealed class TrackedModal
		{
			public required Page Page { get; init; }
			public required object PlatformView { get; init; }
			public long Generation { get; set; }
			public bool OperationOwned { get; set; }
			public bool Released { get; set; }
		}

		readonly record struct RemovalResult(
			bool IsAbsent,
			bool PlatformViewDisposed,
			Exception? Failure);

		readonly IModalNavigationHost _host;
		readonly ITizenNavigationStack _stack;
		readonly ITizenModalPageRealizer _realizer;
		readonly ILogger<TizenModalNavigationPlatform>? _logger;
		readonly Dictionary<Page, TrackedModal> _platformViews = new();
		readonly List<Page> _presentationOrder = new();

		long _nextGeneration;
		bool _disposed;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenModalNavigationPlatform"/> class.
		/// </summary>
		/// <param name="host">The per-window host exposing the framework's modal navigation state.</param>
		/// <param name="stack">The window's navigation stack.</param>
		/// <param name="realizer">Turns modal pages into native views.</param>
		/// <param name="backButton">Installs the back-button handler. May be <see langword="null"/>.</param>
		/// <param name="logger">Optional logger.</param>
		public TizenModalNavigationPlatform(
			IModalNavigationHost host,
			ITizenNavigationStack stack,
			ITizenModalPageRealizer realizer,
			ITizenWindowBackButton? backButton = null,
			ILogger<TizenModalNavigationPlatform>? logger = null)
		{
			_host = host ?? throw new ArgumentNullException(nameof(host));
			_stack = stack ?? throw new ArgumentNullException(nameof(stack));
			_realizer = realizer ?? throw new ArgumentNullException(nameof(realizer));
			_ = backButton;
			_logger = logger;
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Tizen has no deferred-readiness requirement: the NUI navigation stack accepts a push as
		/// soon as the window exists. This matches <c>IsModalPlatformReady =&gt; true</c> in the
		/// upstream Tizen partial, and means <see cref="IModalNavigationHost.RequestSync"/> is
		/// never needed here.
		/// </remarks>
		public bool IsReady => !_disposed;

		/// <inheritdoc/>
		public async Task PushModalAsync(Page modal, bool animated)
		{
			ArgumentNullException.ThrowIfNull(modal);
			ObjectDisposedException.ThrowIf(_disposed, this);

			if (_platformViews.ContainsKey(modal))
			{
				throw new InvalidOperationException("The modal page is already being presented.");
			}

			var platformView = _realizer.Realize(modal, _host.MauiContext);
			var generation = ++_nextGeneration;
			var tracked = new TrackedModal
			{
				Page = modal,
				PlatformView = platformView,
				Generation = generation,
				OperationOwned = true,
			};

			if (!_platformViews.TryAdd(modal, tracked))
			{
				try
				{
					_realizer.Release(modal, platformView, platformViewDisposed: false);
				}
				catch (Exception releaseFailure)
				{
					throw new AggregateException(
						"The modal was already tracked and releasing the duplicate realization failed.",
						new InvalidOperationException("The modal page is already being presented."),
						releaseFailure);
				}

				throw new InvalidOperationException("The modal page is already being presented.");
			}

			_presentationOrder.Add(modal);

			try
			{
				await _stack.PushAsync(platformView, animated && !_host.IsBatchPushing).ConfigureAwait(true);
			}
			catch (Exception pushFailure)
			{
				var cleanupFailure = await CleanupTrackedModalAsync(tracked).ConfigureAwait(true);
				ThrowCombined(
					"The modal push and its rollback both failed.",
					pushFailure,
					cleanupFailure);
			}

			if (_disposed || tracked.Generation != generation)
			{
				var invalidationFailure = _disposed
					? new ObjectDisposedException(nameof(TizenModalNavigationPlatform))
					: new InvalidOperationException("The modal push was superseded by a newer transition.");
				var cleanupFailure = await CleanupTrackedModalAsync(tracked).ConfigureAwait(true);
				ThrowCombined(
					"The modal push was invalidated and its rollback failed.",
					invalidationFailure,
					cleanupFailure);
			}

			tracked.OperationOwned = false;
		}

		/// <inheritdoc/>
		public async Task PopModalAsync(Page modal, bool animated)
		{
			ArgumentNullException.ThrowIfNull(modal);

			if (_disposed)
			{
				// The window went away underneath us. The native stack is gone with it, so there is
				// nothing to dismiss and faulting here would surface as a PopModalAsync failure.
				return;
			}

			// A batch pop dismisses several modals at once, for example a Shell pop-to-root.
			// Animating the intermediate ones makes them flash on screen.
			if (!_platformViews.TryGetValue(modal, out var tracked))
			{
				return;
			}

			if (tracked.OperationOwned)
			{
				throw new InvalidOperationException("The modal page already has an active native transition.");
			}

			var generation = ++_nextGeneration;
			tracked.Generation = generation;
			tracked.OperationOwned = true;

			Exception? popFailure = null;
			RemovalResult removal;

			try
			{
				if (_stack.Contains(tracked.PlatformView))
				{
					if (ReferenceEquals(_stack.Top, tracked.PlatformView))
					{
						await _stack.PopAsync(animated && !_host.IsBatchPopping).ConfigureAwait(true);
					}
					else
					{
						_stack.Remove(tracked.PlatformView);
					}
				}

				removal = ConfirmPlatformViewAbsent(tracked.PlatformView);
			}
			catch (Exception ex)
			{
				popFailure = ex;
				removal = await RemovePlatformViewAsync(tracked.PlatformView).ConfigureAwait(true);
			}

			Exception? invalidationFailure = null;
			if (_disposed || tracked.Generation != generation)
			{
				invalidationFailure = _disposed
					? new ObjectDisposedException(nameof(TizenModalNavigationPlatform))
					: new InvalidOperationException("The modal pop was superseded by a newer transition.");
			}

			Exception? releaseFailure = null;
			if (removal.IsAbsent)
			{
				releaseFailure = ReleaseTrackedModal(tracked, removal.PlatformViewDisposed);
			}
			else
			{
				// The native entry is still live. Restore operation ownership to the platform so a
				// later reconciliation pop or framework Dispose can retry without touching a live
				// view that the stack still owns.
				tracked.OperationOwned = false;
			}

			var failure = CombineFailures(
				"The modal pop and cleanup failed.",
				popFailure,
				removal.Failure,
				invalidationFailure,
				releaseFailure);
			if (failure is not null)
			{
				ExceptionDispatchInfo.Capture(failure).Throw();
			}
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Back routing belongs to Core's window fallback. Registering another page handler here
		/// would dispatch an unhandled press to the same top modal twice.
		/// </remarks>
		public void PageAttached()
		{
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			_disposed = true;
			var tracked = _presentationOrder
				.AsEnumerable()
				.Reverse()
				.Select(page => _platformViews[page])
				.ToArray();

			foreach (var modal in tracked)
			{
				modal.Generation = ++_nextGeneration;

				// The async operation that currently owns this entry must observe the generation
				// invalidation after its await and perform the rollback. Releasing here would race
				// a native transition that still has the view.
				if (modal.OperationOwned || modal.Released)
				{
					continue;
				}

				RemovalResult removal;
				try
				{
					if (_stack.Contains(modal.PlatformView))
					{
						_stack.Remove(modal.PlatformView);
					}

					removal = ConfirmPlatformViewAbsent(modal.PlatformView);
				}
				catch (Exception ex)
				{
					removal = new RemovalResult(false, false, ex);
				}

				if (!removal.IsAbsent)
				{
					ReportTeardownFailure(
						removal.Failure ?? new InvalidOperationException(
							"The native modal entry remained in the stack during teardown."),
						modal.Page);
					continue;
				}

				if (removal.Failure is not null)
				{
					ReportTeardownFailure(removal.Failure, modal.Page);
				}

				var releaseFailure = ReleaseTrackedModal(modal, removal.PlatformViewDisposed);
				if (releaseFailure is not null)
				{
					ReportTeardownFailure(releaseFailure, modal.Page);
				}
			}
		}

		async Task<Exception?> CleanupTrackedModalAsync(TrackedModal tracked)
		{
			var removal = await RemovePlatformViewAsync(tracked.PlatformView).ConfigureAwait(true);

			if (!removal.IsAbsent)
			{
				tracked.OperationOwned = false;
				return removal.Failure ?? new InvalidOperationException(
					"The native modal entry could not be confirmed absent.");
			}

			return CombineFailures(
				"The modal rollback and release failed.",
				removal.Failure,
				ReleaseTrackedModal(tracked, removal.PlatformViewDisposed));
		}

		async Task<RemovalResult> RemovePlatformViewAsync(object platformView)
		{
			Exception? failure = null;

			try
			{
				if (!_stack.Contains(platformView))
				{
					return ConfirmPlatformViewAbsent(platformView);
				}

				if (ReferenceEquals(_stack.Top, platformView))
				{
					try
					{
						await _stack.PopAsync(false).ConfigureAwait(true);
					}
					catch (Exception popFailure)
					{
						failure = popFailure;

						try
						{
							if (_stack.Contains(platformView))
							{
								_stack.Remove(platformView);
							}
						}
						catch (Exception removeFailure)
						{
							failure = CombineFailures(
								"The nonanimated modal rollback and identity removal both failed.",
								popFailure,
								removeFailure);
						}
					}
				}
				else
				{
					_stack.Remove(platformView);
				}
			}
			catch (Exception ex)
			{
				failure = CombineFailures(
					"The modal identity removal failed.",
					failure,
					ex);
			}

			var confirmed = ConfirmPlatformViewAbsent(platformView);
			return confirmed with
			{
				Failure = CombineFailures(
					"The modal removal and absence verification failed.",
					failure,
					confirmed.Failure),
			};
		}

		RemovalResult ConfirmPlatformViewAbsent(object platformView)
		{
			try
			{
				if (_stack.Contains(platformView))
				{
					return new RemovalResult(false, false, null);
				}

				return new RemovalResult(true, _stack.IsDisposed(platformView), null);
			}
			catch (Exception ex)
			{
				return new RemovalResult(false, false, ex);
			}
		}

		Exception? ReleaseTrackedModal(TrackedModal tracked, bool platformViewDisposed)
		{
			if (tracked.Released)
			{
				return null;
			}

			tracked.Released = true;
			tracked.OperationOwned = false;
			_platformViews.Remove(tracked.Page);
			_presentationOrder.Remove(tracked.Page);

			try
			{
				_realizer.Release(tracked.Page, tracked.PlatformView, platformViewDisposed);
				return null;
			}
			catch (Exception ex)
			{
				return ex;
			}
		}

		void ReportTeardownFailure(Exception failure, Page page)
		{
			try
			{
				if (_logger is not null)
				{
					_logger.LogError(
						failure,
						"Failed to clean up Tizen modal page {ModalPage} during framework teardown.",
						page);
				}
				else
				{
					Trace.TraceError(
						"Failed to clean up Tizen modal page {0} during framework teardown: {1}",
						page,
						failure);
				}
			}
			catch
			{
				// Framework teardown must not fail because a logger or trace listener failed.
			}
		}

		static void ThrowCombined(string message, Exception primary, Exception? secondary)
		{
			if (secondary is null)
			{
				ExceptionDispatchInfo.Capture(primary).Throw();
			}

			throw new AggregateException(message, primary, secondary);
		}

		static Exception? CombineFailures(string message, params Exception?[] failures)
		{
			var present = new List<Exception>();
			foreach (var failure in failures)
			{
				if (failure is not null
					&& !present.Any(existing => ReferenceEquals(existing, failure)))
				{
					present.Add(failure);
				}
			}

			return present.Count switch
			{
				0 => null,
				1 => present[0],
				_ => new AggregateException(message, present),
			};
		}
	}

	/// <summary>
	/// Creates <see cref="TizenModalNavigationPlatform"/> instances for .NET MAUI.
	/// </summary>
	/// <remarks>
	/// Resolves the window's navigation stack from the window's service scope, so each window gets
	/// its own platform bound to its own stack. Returns <see langword="null"/> when no stack is
	/// registered, which the seam defines as "keep the built-in platform" - a partially configured
	/// host degrades instead of throwing.
	/// </remarks>
	public sealed class TizenModalNavigationPlatformFactory : IModalNavigationPlatformFactory
	{
		readonly ITizenModalPageRealizer _realizer;
		readonly ILoggerFactory? _loggerFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="TizenModalNavigationPlatformFactory"/> class.
		/// </summary>
		/// <param name="realizer">Turns modal pages into native views.</param>
		/// <param name="loggerFactory">Optional logger factory.</param>
		public TizenModalNavigationPlatformFactory(
			ITizenModalPageRealizer realizer,
			ILoggerFactory? loggerFactory = null)
		{
			_realizer = realizer ?? throw new ArgumentNullException(nameof(realizer));
			_loggerFactory = loggerFactory;
		}

		/// <inheritdoc/>
		public IModalNavigationPlatform? CreateModalNavigationPlatform(IModalNavigationHost host)
		{
			ArgumentNullException.ThrowIfNull(host);

			var services = host.MauiContext.Services;
			var stack = services?.GetService(typeof(ITizenNavigationStack)) as ITizenNavigationStack;

			if (stack is null)
			{
				return null;
			}

			return new TizenModalNavigationPlatform(
				host,
				stack,
				_realizer,
				backButton: null,
				_loggerFactory?.CreateLogger<TizenModalNavigationPlatform>());
		}
	}
}
