using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Tizen.NUI.BaseComponents;
using Tizen.UIExtensions.NUI;
using NLayoutGroup = Tizen.NUI.LayoutGroup;
using NLinearLayout = Tizen.NUI.LinearLayout;
using NNavigationStack = Tizen.UIExtensions.NUI.NavigationStack;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Drives NUI stack navigation for a cross-platform <see cref="IStackNavigation"/> view.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Ported from <c>Microsoft.Maui.Platform.StackNavigationManager</c> in dotnet/maui. Navigation
	/// behaviour - initial stack, push-to-sync, pop-to-sync and back-stack reconciliation - is
	/// preserved exactly; only type ownership changed.
	/// </para>
	/// <para>
	/// Named <c>TizenStackNavigationManager</c> so it cannot collide (CS0433) with the
	/// <c>net*-tizen</c> build of <c>Microsoft.Maui.dll</c>, which still exports its own
	/// <c>Microsoft.Maui.Platform.StackNavigationManager</c>.
	/// </para>
	/// <para>
	/// This type is Core-owned because it is a platform primitive, not a handler. The Wave C
	/// navigation handlers construct and drive it; no navigation handler lives in this package.
	/// </para>
	/// </remarks>
	public class TizenStackNavigationManager : View, ITizenToolbarContainer
	{
		readonly Dictionary<IView, TizenNaviPage> _pageMap = new();
		readonly Dictionary<IView, IViewHandler?> _handlerMap = new();

		TizenToolbarView? _toolbar;

		/// <summary>Initializes a new instance of the <see cref="TizenStackNavigationManager"/> class.</summary>
		public TizenStackNavigationManager()
		{
			HeightSpecification = LayoutParamPolicies.MatchParent;
			WidthSpecification = LayoutParamPolicies.MatchParent;

			Layout = new NLinearLayout
			{
				LinearOrientation = NLinearLayout.Orientation.Vertical,
			};

			PlatformNavigation = new NNavigationStack();
			Add(PlatformNavigation);
		}

		/// <summary>Gets the current cross-platform navigation stack, bottom-first.</summary>
		public IReadOnlyList<IView> NavigationStack => _navigationStack;

		List<IView> _navigationStack = new();

		/// <summary>Gets the MAUI context, available once <see cref="Connect"/> has run.</summary>
		protected IMauiContext? MauiContext { get; set; }

		/// <summary>Gets the cross-platform navigation view being driven.</summary>
		protected IStackNavigation? NavigationView { get; set; }

		/// <summary>Gets the underlying NUI navigation stack.</summary>
		protected NNavigationStack PlatformNavigation { get; }

		/// <inheritdoc />
		public void SetToolbar(TizenToolbarView toolbar)
		{
			ArgumentNullException.ThrowIfNull(toolbar);

			if (_toolbar is not null)
			{
				Remove(_toolbar);
				_toolbar.Dispose();
				_toolbar = null;
			}

			_toolbar = toolbar;
			Add(toolbar);

			// The toolbar must sit above the navigation stack in the vertical layout.
			(toolbar.Layout as NLayoutGroup)?.ChangeLayoutSiblingOrder(0);
		}

		/// <summary>Connects this manager to a cross-platform navigation view.</summary>
		/// <param name="navigationView">The navigation view.</param>
		public virtual void Connect(IView navigationView)
		{
			ArgumentNullException.ThrowIfNull(navigationView);

			NavigationView = (IStackNavigation)navigationView;
			MauiContext = navigationView.Handler?.MauiContext;
		}

		/// <summary>Disconnects this manager from its navigation view.</summary>
		public virtual void Disconnect()
		{
			NavigationView = null;
			MauiContext = null;
		}

		/// <summary>Applies a navigation request to the platform stack.</summary>
		/// <param name="e">The request.</param>
		public virtual async void RequestNavigation(NavigationRequest e)
		{
			ArgumentNullException.ThrowIfNull(e);

			var newPageStack = new List<IView>(e.NavigationStack);
			var previousStack = _navigationStack;
			var previousCount = previousStack.Count;

			if (previousCount == 0)
			{
				await InitializeStack(newPageStack, e.Animated).ConfigureAwait(true);
				Finish(newPageStack);
				return;
			}

			// Same top of stack: only the pages underneath changed, so reconcile without animating.
			if (newPageStack.Count > 0 &&
				newPageStack[^1] == previousStack[previousCount - 1])
			{
				SyncBackStackToNavigationStack(newPageStack);
				Finish(newPageStack);
				return;
			}

			if (newPageStack.Count > previousCount)
				await PushToSync(newPageStack, e.Animated).ConfigureAwait(true);
			else
				await PopToSync(newPageStack, e.Animated).ConfigureAwait(true);

			Finish(newPageStack);
		}

		/// <summary>Pushes an entire stack, animating only the top page.</summary>
		/// <param name="newStack">The stack to realise.</param>
		/// <param name="animated">Whether the top transition animates.</param>
		/// <returns>A task that completes when the stack is realised.</returns>
		protected virtual async Task InitializeStack(IReadOnlyList<IView> newStack, bool animated)
		{
			ArgumentNullException.ThrowIfNull(newStack);

			if (newStack.Count == 0)
				return;

			var top = newStack[^1];
			foreach (var page in newStack)
				await PlatformNavigation.Push(GetNavigationItem(page), page == top && animated).ConfigureAwait(true);
		}

		/// <summary>
		/// Called after each navigation completes, with the stack that is now current.
		/// </summary>
		/// <remarks>
		/// Overridable so a handler can observe completion - to sync a toolbar's back button, for
		/// instance - without re-implementing <see cref="RequestNavigation"/>. Always call the base
		/// implementation, which is what notifies the cross-platform navigation view.
		/// </remarks>
		/// <param name="stack">The navigation stack that is now current.</param>
		protected virtual void OnNavigationFinished(IReadOnlyList<IView> stack) =>
			NavigationView?.NavigationFinished(stack);

		/// <summary>
		/// Creates the platform page wrapper for a cross-platform page.
		/// </summary>
		/// <remarks>
		/// Overridable so a handler can decorate the page - attaching a per-page title view, for
		/// example. The base implementation realises the page's platform view, sizes it to fill the
		/// parent, and caches both the wrapper and the page's handler so
		/// <see cref="OnPageRemoved"/> can dispose it later.
		/// </remarks>
		/// <param name="page">The cross-platform page.</param>
		/// <returns>The platform page wrapper.</returns>
		protected virtual TizenNaviPage CreateNavigationItem(IView page)
		{
			ArgumentNullException.ThrowIfNull(page);

			var mauiContext = MauiContext
				?? throw new InvalidOperationException(
					$"{nameof(MauiContext)} is not set. Call {nameof(Connect)} before navigating.");

			var content = page.ToPlatformView(mauiContext);
			content.WidthSpecification = LayoutParamPolicies.MatchParent;
			content.HeightSpecification = LayoutParamPolicies.MatchParent;

			return new TizenNaviPage
			{
				Content = content,
				WidthSpecification = LayoutParamPolicies.MatchParent,
				HeightSpecification = LayoutParamPolicies.MatchParent,
			};
		}

		/// <summary>
		/// Called when a page leaves the stack, after it has been popped from the platform stack.
		/// </summary>
		/// <remarks>
		/// The base implementation drops the cached wrapper and disposes the page's handler. That
		/// disposal is load-bearing - popping only unparents the native view, so without it the
		/// page keeps its whole child handler graph alive. An override that does not call base
		/// will leak.
		/// </remarks>
		/// <param name="page">The page leaving the stack.</param>
		protected virtual void OnPageRemoved(IView page)
		{
			ArgumentNullException.ThrowIfNull(page);

			_pageMap.Remove(page);

			if (_handlerMap.TryGetValue(page, out var handler))
			{
				(handler as ITizenPlatformViewHandler)?.Dispose();
				_handlerMap.Remove(page);
			}
		}

		/// <summary>Gets the toolbar currently attached, if any.</summary>
		protected TizenToolbarView? Toolbar => _toolbar;

		void Finish(List<IView> stack)
		{
			_navigationStack = stack;
			OnNavigationFinished(stack);
		}

		void SyncBackStackToNavigationStack(List<IView> newStack)
		{
			if (newStack.Count > _navigationStack.Count)
			{
				for (var i = 0; i < newStack.Count; i++)
				{
					if (_navigationStack.IndexOf(newStack[i]) == -1)
						PlatformNavigation.Insert(GetNavigationItem(_navigationStack[i]), GetNavigationItem(newStack[i]));
				}

				return;
			}

			foreach (var page in _navigationStack)
			{
				if (newStack.IndexOf(page) == -1)
				{
					PlatformNavigation.Pop(GetNavigationItem(page));
					ReleasePage(page);
				}
			}
		}

		async Task PushToSync(List<IView> newStack, bool animated)
		{
			for (var i = _navigationStack.Count; i < newStack.Count; i++)
			{
				var isTop = i + 1 == newStack.Count;
				await PlatformNavigation.Push(GetNavigationItem(newStack[i]), isTop && animated).ConfigureAwait(true);
			}
		}

		async Task PopToSync(List<IView> newStack, bool animated)
		{
			for (var i = newStack.Count; i < _navigationStack.Count; i++)
			{
				var isLast = i + 1 == _navigationStack.Count;
				var page = _navigationStack[i];

				if (isLast)
					await PlatformNavigation.Pop(animated).ConfigureAwait(true);
				else
					PlatformNavigation.Pop(GetNavigationItem(page));

				ReleasePage(page);
			}
		}

		void ReleasePage(IView page) => OnPageRemoved(page);

		/// <summary>
		/// Gets, creating and caching if needed, the platform wrapper for a page.
		/// </summary>
		/// <param name="page">The cross-platform page.</param>
		/// <returns>The platform page wrapper.</returns>
		protected TizenNaviPage GetNavigationItem(IView page)
		{
			ArgumentNullException.ThrowIfNull(page);

			if (_pageMap.TryGetValue(page, out var existing))
				return existing;

			var naviPage = CreateNavigationItem(page);

			_pageMap[page] = naviPage;
			_handlerMap[page] = page.Handler;

			return naviPage;
		}
	}
}
