using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platforms.Tizen.LifecycleEvents;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.NUI;
using TizenLifecycleEvents = Microsoft.Maui.Platforms.Tizen.LifecycleEvents.TizenLifecycle;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Application/window wiring helpers.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.Platform.CoreAppExtensions</c> and
	/// <c>Microsoft.Maui.Platform.ElementExtensions</c> (Tizen) in dotnet/maui, both
	/// <c>internal</c> there.
	/// </remarks>
	public static class TizenCoreApplicationExtensions
	{
		// Keyed weakly so a disposed window does not keep its content, or its handlers, alive.
		static readonly ConditionalWeakTable<TizenNativeWindow, TizenNativeView> s_windowContent = new();
		static readonly ConditionalWeakTable<TizenNativeWindow, ITizenPlatformViewHandler> s_windowContentHandler = new();
		static readonly ConditionalWeakTable<TizenNativeWindow, Action> s_windowCloseRequestHandler = new();
		static readonly ConditionalWeakTable<TizenNativeWindow, BackButtonRouter> s_windowBackButtonRouter = new();
		static readonly ConditionalWeakTable<TizenNativeWindow, NavigationStack> s_windowNavigationStack = new();

		/// <summary>Gets the process-wide NUI window.</summary>
		/// <remarks>
		/// dotnet/maui uses <c>Window.Instance</c>, which TizenFX deprecated in API12 in favour of
		/// <c>Window.Default</c>. This backend targets API15, so the modern member is used.
		/// </remarks>
		/// <returns>The default window.</returns>
		public static TizenNativeWindow GetDefaultWindow() => Window.Default;

		/// <summary>Creates and attaches the <see cref="IApplication"/> handler.</summary>
		/// <param name="platformApplication">The platform application.</param>
		/// <param name="application">The cross-platform application.</param>
		/// <param name="context">The application-scoped context.</param>
		public static void SetApplicationHandler(
			this TizenNativeApplication platformApplication,
			IApplication application,
			IMauiContext context)
		{
			ArgumentNullException.ThrowIfNull(platformApplication);
			ArgumentNullException.ThrowIfNull(application);
			ArgumentNullException.ThrowIfNull(context);

			SetHandler(application, platformApplication, context);
		}

		/// <summary>
		/// Creates the cross-platform window, its scoped context and its handler.
		/// </summary>
		/// <param name="platformApplication">The platform application.</param>
		/// <param name="application">The cross-platform application.</param>
		/// <param name="applicationContext">The application-scoped context.</param>
		/// <returns>The window DI scope, which the caller owns.</returns>
		public static IServiceScope CreatePlatformWindow(
			this TizenNativeApplication platformApplication,
			IApplication application,
			IMauiContext applicationContext)
		{
			ArgumentNullException.ThrowIfNull(platformApplication);
			ArgumentNullException.ThrowIfNull(application);
			ArgumentNullException.ThrowIfNull(applicationContext);

			var platformWindow = GetDefaultWindow()
				?? throw new InvalidOperationException("The default NUI window was not found.");

			var navigationStack = GetNavigationStack(platformWindow);
			var mauiContext = applicationContext.MakeWindowScope(
				platformWindow,
				navigationStack,
				out var windowScope);

			platformWindow.SetWindowCloseRequestHandler(platformApplication.Exit);

			applicationContext.Services.InvokeTizenLifecycleEvents<TizenLifecycleEvents.OnMauiContextCreated>(
				del => del(mauiContext));

			var activationState = new ActivationState(mauiContext);
			var window = application.CreateWindow(activationState);

			// Hardware back goes to the cross-platform window first; if it does not handle the
			// press, the close request runs and the application exits.
			platformWindow.SetBackButtonPressedHandler(window.BackButtonClicked);

			SetHandler(window, platformWindow, mauiContext);

			return windowScope;
		}

		/// <summary>Resolves a cross-platform window from its platform window.</summary>
		/// <param name="platformWindow">The platform window.</param>
		/// <returns>The cross-platform window, or <see langword="null"/> when not found.</returns>
		public static IWindow? GetWindow(this TizenNativeWindow? platformWindow)
		{
			if (platformWindow is null)
				return null;

			foreach (var window in IPlatformApplication.Current?.Application?.Windows ?? Array.Empty<IWindow>())
			{
				if (ReferenceEquals(window?.Handler?.PlatformView, platformWindow))
					return window;
			}

			return null;
		}

		/// <summary>Sets the main content view of the platform window, replacing any previous content.</summary>
		/// <remarks>
		/// <see cref="Handlers.TizenWindowHandler.MapContent"/> is a property mapper on
		/// <see cref="IWindow.Content"/>, so it re-runs whenever the content changes. Adding without
		/// removing would leave the previous view parented and visible underneath the new one, and
		/// leak its whole NUI subtree. The per-window navigation stack is also the stack used by
		/// modal pages and dialog placeholders, so replacing the root content must clear and push
		/// through that stack rather than adding a competing child directly to the window.
		/// </remarks>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="content">The content view.</param>
		public static void SetMainContent(this TizenNativeWindow platformWindow, TizenNativeView content, IView? contentView = null)
		{
			ArgumentNullException.ThrowIfNull(platformWindow);
			ArgumentNullException.ThrowIfNull(content);

			var navigationStack = GetNavigationStack(platformWindow);

			if (s_windowContent.TryGetValue(platformWindow, out var previous))
			{
				if (ReferenceEquals(previous, content))
					return;

				var stackDisposedPrevious = navigationStack.Stack.Count <= 1;

				if (stackDisposedPrevious)
				{
					navigationStack.Clear();
					_ = navigationStack.Push(content, true);
				}
				else
				{
					// Root replacement must not clear modal pages above it. Insert the new content
					// below the current bottom entry, then remove the previous root by identity.
					// The visible top remains unchanged when a modal is active.
					navigationStack.Insert(navigationStack.Stack[0], content);

					if (navigationStack.Stack.Contains(previous))
					{
						navigationStack.Pop(previous);
					}
				}

				// Removing the native view only unparents it. The handler still holds the platform
				// view, its event subscriptions and its child handler graph, so without an explicit
				// disconnect the whole previous page leaks on every content swap.
				if (s_windowContentHandler.TryGetValue(platformWindow, out var previousHandler))
				{
					if (stackDisposedPrevious)
					{
						((IElementHandler)previousHandler).DisconnectHandler();
					}
					else
					{
						previousHandler.Dispose();
					}
					s_windowContentHandler.Remove(platformWindow);
				}
				else if (!stackDisposedPrevious)
				{
					previous.Dispose();
				}
			}
			else
			{
				navigationStack.Clear();
				_ = navigationStack.Push(content, true);
			}

			content.WidthSpecification = LayoutParamPolicies.MatchParent;
			content.HeightSpecification = LayoutParamPolicies.MatchParent;
			content.WidthResizePolicy = ResizePolicyType.FillToParent;
			content.HeightResizePolicy = ResizePolicyType.FillToParent;

			s_windowContent.Remove(platformWindow);
			s_windowContent.Add(platformWindow, content);

			s_windowContentHandler.Remove(platformWindow);
			if (contentView?.Handler is ITizenPlatformViewHandler contentHandler)
				s_windowContentHandler.Add(platformWindow, contentHandler);
		}

		/// <summary>
		/// Registers the available orientations and the hardware back-key handler on the platform
		/// window.
		/// </summary>
		/// <remarks>
		/// Ported from <c>WindowExtensions.Initialize</c> in dotnet/maui. Without this call the
		/// device would never rotate, the hardware back key would be inert, and window-scoped
		/// presentation services would have no native navigation stack to attach to.
		/// </remarks>
		/// <param name="platformWindow">The platform window.</param>
		public static void InitializePlatformWindow(this TizenNativeWindow platformWindow)
		{
			ArgumentNullException.ThrowIfNull(platformWindow);

			platformWindow.AddAvailableOrientation(Window.WindowOrientation.Landscape);
			platformWindow.AddAvailableOrientation(Window.WindowOrientation.LandscapeInverse);
			platformWindow.AddAvailableOrientation(Window.WindowOrientation.Portrait);
			platformWindow.AddAvailableOrientation(Window.WindowOrientation.PortraitInverse);

			platformWindow.KeyEvent += (_, e) =>
			{
				if (e.Key.IsDeclineKeyEvent())
					OnBackButtonPressed(platformWindow);
			};

			if (!s_windowNavigationStack.TryGetValue(platformWindow, out _))
			{
				var navigationStack = new NavigationStack
				{
					HeightSpecification = LayoutParamPolicies.MatchParent,
					WidthSpecification = LayoutParamPolicies.MatchParent,
					WidthResizePolicy = ResizePolicyType.FillToParent,
					HeightResizePolicy = ResizePolicyType.FillToParent,
				};

				platformWindow.GetDefaultLayer().Add(navigationStack);
				s_windowNavigationStack.Add(platformWindow, navigationStack);
			}
		}

		/// <summary>Registers the handler invoked when the window asks to close.</summary>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="handler">The handler.</param>
		public static void SetWindowCloseRequestHandler(this TizenNativeWindow platformWindow, Action handler)
		{
			ArgumentNullException.ThrowIfNull(platformWindow);
			ArgumentNullException.ThrowIfNull(handler);

			s_windowCloseRequestHandler.Remove(platformWindow);
			s_windowCloseRequestHandler.Add(platformWindow, handler);
		}

		/// <summary>
		/// Registers the handler invoked when the hardware back key is pressed. Returning
		/// <see langword="true"/> marks the press as handled and suppresses the close request.
		/// </summary>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="handler">The handler.</param>
		public static void SetBackButtonPressedHandler(this TizenNativeWindow platformWindow, Func<bool> handler)
		{
			ArgumentNullException.ThrowIfNull(platformWindow);
			ArgumentNullException.ThrowIfNull(handler);

			s_windowBackButtonRouter.GetOrCreateValue(platformWindow).SetFallback(handler);
		}

		/// <summary>
		/// Registers a handler that runs before the window's existing back-button fallback.
		/// </summary>
		/// <param name="platformWindow">The platform window.</param>
		/// <param name="handler">The handler.</param>
		/// <returns>A registration that removes the handler when disposed.</returns>
		public static IDisposable RegisterBackButtonPressedHandler(this TizenNativeWindow platformWindow, Func<bool> handler)
		{
			ArgumentNullException.ThrowIfNull(platformWindow);
			ArgumentNullException.ThrowIfNull(handler);

			return s_windowBackButtonRouter.GetOrCreateValue(platformWindow).Register(handler);
		}

		static void OnBackButtonPressed(TizenNativeWindow platformWindow)
		{
			if (s_windowBackButtonRouter.TryGetValue(platformWindow, out var router) && router.Invoke())
				return;

			if (s_windowCloseRequestHandler.TryGetValue(platformWindow, out var closeHandler))
				closeHandler();
		}

		static NavigationStack GetNavigationStack(TizenNativeWindow platformWindow) =>
			s_windowNavigationStack.TryGetValue(platformWindow, out var navigationStack)
				? navigationStack
				: throw new InvalidOperationException(
					"The platform window has no navigation stack. Call InitializePlatformWindow before creating its MAUI window scope.");

		sealed class BackButtonRouter
		{
			readonly object _gate = new();
			readonly List<Func<bool>> _handlers = new();
			Func<bool>? _fallback;

			public void SetFallback(Func<bool> fallback)
			{
				lock (_gate)
				{
					_fallback = fallback;
				}
			}

			public IDisposable Register(Func<bool> handler)
			{
				lock (_gate)
				{
					_handlers.Add(handler);
				}

				return new Registration(this, handler);
			}

			public bool Invoke()
			{
				Func<bool>[] handlers;
				Func<bool>? fallback;

				lock (_gate)
				{
					handlers = _handlers.ToArray();
					fallback = _fallback;
				}

				for (var index = handlers.Length - 1; index >= 0; index--)
				{
					if (handlers[index]())
						return true;
				}

				return fallback?.Invoke() == true;
			}

			void Remove(Func<bool> handler)
			{
				lock (_gate)
				{
					_handlers.Remove(handler);
				}
			}

			sealed class Registration : IDisposable
			{
				BackButtonRouter? _owner;
				readonly Func<bool> _handler;

				public Registration(BackButtonRouter owner, Func<bool> handler)
				{
					_owner = owner;
					_handler = handler;
				}

				public void Dispose() =>
					Interlocked.Exchange(ref _owner, null)?.Remove(_handler);
			}
		}

		static void SetHandler(IElement element, object platformElement, IMauiContext context)
		{
			_ = platformElement;

			var handler = element.Handler;
			if (handler is null)
			{
				handler = context.Handlers.GetHandler(element.GetType())
					?? throw new InvalidOperationException(
						$"No handler is registered for {element.GetType().FullName}. "
						+ "Call IMauiHandlersCollection.AddTizenHandlers() from ConfigureMauiHandlers.");

				handler.SetMauiContext(context);
			}

			element.Handler = handler;
			handler.SetVirtualView(element);
		}
	}
}
