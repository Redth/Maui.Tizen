using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using GColor = Microsoft.Maui.Graphics.Color;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Manages the navigation stack for a ShellSection, derived from the public TizenStackNavigationManager.
	/// </summary>
	public class TizenShellSectionStackManager : TizenStackNavigationManager
	{
		protected ShellSection? ShellSection { get; private set; }

		readonly ShellRootMountCoordinator<ShellContent, TizenShellSectionView> _rootMount = new();

		/// <summary>
		/// Connects this manager to a ShellSection navigation view.
		/// </summary>
		public void Connect(IElement navigationView)
		{
			NavigationView = (IStackNavigation)navigationView;
			MauiContext = navigationView.Handler?.MauiContext;
			ShellSection = (ShellSection)navigationView;
			_rootMount.SetCurrent(ShellSection.CurrentItem, static (root, content) => root.UpdateCurrentItem(content));
		}

		public override void Disconnect()
		{
			base.Disconnect();
			_rootMount.Clear(root =>
			{
				PlatformNavigation.Pop(root);
				root.Dispose();
			});
			ShellSection = null;
		}

		public void UpdateCurrentItem(ShellContent? content) =>
			_rootMount.SetCurrent(content, static (root, current) => root.UpdateCurrentItem(current));

		/// <summary>
		/// Updates the top tab bar colors for the root view.
		/// </summary>
		public void UpdateTopTabBarColors(GColor foregroundColor, GColor backgroundColor, GColor titleColor, GColor unselectedColor)
		{
			_rootMount.Root?.UpdateTopTabBarColors(foregroundColor, backgroundColor, titleColor, unselectedColor);
		}

		protected override async Task InitializeStack(IReadOnlyList<IView> newStack, bool animated)
		{
			if (newStack.Count == 0)
				return;

			List<IView> navigationStack = new List<IView>(newStack);

			var root = _rootMount.GetOrCreate(
				() => new TizenShellSectionView(ShellSection!, MauiContext!),
				static (view, content) => view.UpdateCurrentItem(content));

			await PlatformNavigation.Push(root, false);

			navigationStack.RemoveAt(0);
			await base.InitializeStack(navigationStack, animated);
		}
	}
}
