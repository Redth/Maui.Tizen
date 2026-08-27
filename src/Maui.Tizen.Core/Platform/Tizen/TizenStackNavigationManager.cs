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

			// Setting the SAME toolbar again must be a no-op, not a disposal.
			//
			// Ownership transfers to this container: it removes and DISPOSES whatever toolbar it
			// was holding. Without this guard, calling SetToolbar twice with the same instance -
			// which a handler doing "ensure the toolbar is attached" work does naturally - disposed
			// the toolbar and then re-added the disposed instance. The result is a native view that
			// is still in the tree and no longer usable, which does not throw here and instead
			// fails later somewhere unrelated.
			if (ReferenceEquals(_toolbar, toolbar))
			{
				(toolbar.Layout as NLayoutGroup)?.ChangeLayoutSiblingOrder(0);
				return;
			}

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

			await ReconcileStack(previousStack, newPageStack, e.Animated).ConfigureAwait(true);

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

			// Order matters. Detach the content BEFORE disposing the wrapper, then dispose the
			// handler.
			//
			// The wrapper's Content is the handler's platform view, which the handler owns and
			// disposes. Disposing the wrapper while it still holds that content would dispose the
			// child native view too, and the handler would then dispose it a second time. Clearing
			// Content first makes the two disposals disjoint.
			//
			// The wrapper itself was previously only dropped from the map and never disposed, so
			// every page that left the stack leaked its TizenNaviPage.
			if (_pageMap.TryGetValue(page, out var naviPage))
			{
				naviPage.Content = null;
				naviPage.Dispose();
				_pageMap.Remove(page);
			}

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

		/// <summary>
		/// Reconciles the pages beneath an unchanged top, without animating.
		/// </summary>
		/// <remarks>
		/// Insertions and removals are computed together rather than in mutually exclusive
		/// branches. The old code chose one or the other by comparing stack LENGTHS, so a change
		/// that both added and removed pages while keeping the count similar silently did only half
		/// the work. Its insert path also indexed the old stack with a position taken from the
		/// longer new one, which threw once more than one page was inserted.
		/// </remarks>
		void SyncBackStackToNavigationStack(List<IView> newStack)
		{
			foreach (var (page, before) in TizenNavigationReconciler.PlanInsertions(_navigationStack, newStack))
				PlatformNavigation.Insert(GetNavigationItem(before), GetNavigationItem(page));

			foreach (var page in TizenNavigationReconciler.PlanRemovals(_navigationStack, newStack))
			{
				PlatformNavigation.Pop(GetNavigationItem(page));
				ReleasePage(page);
			}
		}

		/// <summary>
		/// Moves the platform stack to <paramref name="target"/> by popping down to the longest
		/// common prefix and pushing the remainder.
		/// </summary>
		/// <remarks>
		/// This replaces separate push-only and pop-only paths that both assumed the new stack was
		/// the old one with pages added or removed at the top. Replacing pages - [A, B] becoming
		/// [A, C, D] - pushed C and D on top of B and left B in the platform stack, permanently
		/// desynchronising it from the managed stack. Reconciling against the common prefix handles
		/// replacement, insertion and truncation without special cases.
		/// </remarks>
		async Task ReconcileStack(List<IView> previous, List<IView> target, bool animated)
		{
			var plan = TizenNavigationReconciler.Reconcile(previous, target);

			// Only the last visible transition animates. If pages are being pushed afterwards, the
			// pops are bookkeeping and animating them would show pages the user never asked for.
			var animatePop = animated && plan.Pushes.Count == 0;

			for (var i = 0; i < plan.Pops.Count; i++)
			{
				var page = plan.Pops[i];

				// Pops are ordered top-most first, so only the first one is the visible transition.
				if (i == 0 && animatePop)
					await PlatformNavigation.Pop(true).ConfigureAwait(true);
				else
					PlatformNavigation.Pop(GetNavigationItem(page));

				ReleasePage(page);
			}

			for (var i = 0; i < plan.Pushes.Count; i++)
			{
				var isTop = i + 1 == plan.Pushes.Count;
				await PlatformNavigation.Push(GetNavigationItem(plan.Pushes[i]), isTop && animated).ConfigureAwait(true);
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
