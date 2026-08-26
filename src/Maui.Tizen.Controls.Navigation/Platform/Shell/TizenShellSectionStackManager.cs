using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;
using GColor = Microsoft.Maui.Graphics.Color;

namespace Microsoft.Maui.Platforms.Tizen.Platform
{
	/// <summary>
	/// Manages the navigation stack for a ShellSection, derived from the public StackNavigationManager.
	/// </summary>
	public class TizenShellSectionStackManager : StackNavigationManager
	{
		protected ShellSection? ShellSection { get; private set; }

		TizenShellSectionView? _rootView;

		/// <summary>
		/// Connects this manager to a ShellSection navigation view.
		/// </summary>
		public void Connect(IElement navigationView)
		{
			NavigationView = (IStackNavigation)navigationView;
			MauiContext = navigationView.Handler?.MauiContext;
			ShellSection = (ShellSection)navigationView;
		}

		public override void Disconnect()
		{
			base.Disconnect();
			ShellSection = null;
		}

		/// <summary>
		/// Updates the top tab bar colors for the root view.
		/// </summary>
		public void UpdateTopTabBarColors(GColor foregroundColor, GColor backgroundColor, GColor titleColor, GColor unselectedColor)
		{
			_rootView?.UpdateTopTabBarColors(foregroundColor, backgroundColor, titleColor, unselectedColor);
		}

		protected override async Task InitializeStack(IReadOnlyList<IView> newStack, bool animated)
		{
			if (newStack.Count == 0)
				return;

			List<IView> navigationStack = new List<IView>(newStack);

			_rootView = new TizenShellSectionView(ShellSection!, MauiContext!);

			await PlatformNavigation.Push(_rootView, false);

			navigationStack.RemoveAt(0);
			await base.InitializeStack(navigationStack, animated);
		}
	}
}
