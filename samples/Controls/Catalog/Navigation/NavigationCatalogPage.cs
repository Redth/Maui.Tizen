using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Maui.Tizen.Samples.Catalog.Navigation
{
	/// <summary>
	/// Exercises the stack-navigation surface owned by <c>TizenNavigationViewHandler</c> and
	/// <c>TizenToolbarHandler</c>.
	/// </summary>
	/// <remarks>
	/// Catalog pages are written in C# rather than XAML on purpose: XAML would need the MAUI build
	/// tasks and a full application project, neither of which can run until the Samsung .NET 11
	/// workload ships. As plain C# these pages are compiled by the Wave C validation lane, so the
	/// API they exercise is verified even though the app cannot yet be deployed.
	/// </remarks>
	public sealed class NavigationCatalogPage : ContentPage
	{
		int _depth;

		public NavigationCatalogPage()
			: this(depth: 0)
		{
		}

		NavigationCatalogPage(int depth)
		{
			_depth = depth;

			Title = depth == 0 ? "Navigation" : $"Pushed page {depth}";

			// Primary items render inline in the toolbar; secondary items collapse behind the
			// overflow button, which is routed through IToolbarSecondaryActionPresenter.
			ToolbarItems.Add(new ToolbarItem
			{
				Text = "Push",
				Order = ToolbarItemOrder.Primary,
				Priority = 0,
				Command = new Command(async () => await PushAsync()),
			});

			ToolbarItems.Add(new ToolbarItem
			{
				Text = "Pop to root",
				Order = ToolbarItemOrder.Secondary,
				Priority = 0,
				Command = new Command(async () => await Navigation.PopToRootAsync()),
			});

			ToolbarItems.Add(new ToolbarItem
			{
				Text = "Toggle title view",
				Order = ToolbarItemOrder.Secondary,
				Priority = 1,
				Command = new Command(ToggleTitleView),
			});

			Content = new VerticalStackLayout
			{
				Padding = 24,
				Spacing = 12,
				Children =
				{
					new Label { Text = $"Depth: {depth}", FontSize = 20 },
					new Button { Text = "Push another page", Command = new Command(async () => await PushAsync()) },
					new Button
					{
						Text = "Pop",
						Command = new Command(async () => await Navigation.PopAsync(), () => depth > 0),
					},
					new Button { Text = "Insert page before current", Command = new Command(InsertBefore) },
				},
			};
		}

		/// <summary>Builds the navigation page used as the catalog entry point.</summary>
		public static NavigationPage CreateHost() => new(new NavigationCatalogPage())
		{
			BarBackgroundColor = Color.FromArgb("#2196F3"),
			BarTextColor = Colors.White,
		};

		System.Threading.Tasks.Task PushAsync() => Navigation.PushAsync(new NavigationCatalogPage(_depth + 1));

		void ToggleTitleView()
		{
			// Round-trips the title view slot, which is the path that used to leak a platform
			// handler on every remap.
			NavigationPage.SetTitleView(
				this,
				NavigationPage.GetTitleView(this) is null
					? new Label { Text = "Custom title", TextColor = Colors.White }
					: null);
		}

		void InsertBefore()
		{
			// Stack mutation that does not go through Push/Pop, which is where a naive
			// navigation manager loses sync with the virtual stack.
			IReadOnlyList<Page> stack = Navigation.NavigationStack;

			if (stack.Count > 0)
			{
				Navigation.InsertPageBefore(new NavigationCatalogPage(-1), stack[^1]);
			}
		}
	}

	/// <summary>
	/// Exercises <c>TizenTabbedPageHandler</c>: tab selection, bar colours and per-tab toolbars.
	/// </summary>
	public sealed class TabbedCatalogPage : TabbedPage
	{
		public TabbedCatalogPage()
		{
			Title = "Tabs";
			BarBackgroundColor = Color.FromArgb("#2196F3");
			BarTextColor = Colors.White;
			SelectedTabColor = Colors.White;
			UnselectedTabColor = Color.FromArgb("#90CAF9");

			for (var i = 1; i <= 3; i++)
			{
				Children.Add(new ContentPage
				{
					Title = $"Tab {i}",
					Content = new VerticalStackLayout
					{
						Padding = 24,
						Children = { new Label { Text = $"Content of tab {i}" } },
					},
				});
			}
		}
	}

	/// <summary>
	/// Exercises <c>TizenFlyoutViewHandler</c>: flyout behaviour, gesture toggling and the toolbar
	/// drawer toggle that Wave C has to track itself because
	/// <c>Toolbar.DrawerToggleVisible</c> is internal.
	/// </summary>
	public sealed class FlyoutCatalogPage : FlyoutPage
	{
		public FlyoutCatalogPage()
		{
			Flyout = new ContentPage
			{
				Title = "Menu",
				Content = new VerticalStackLayout
				{
					Padding = 16,
					Children =
					{
						new Label { Text = "Flyout content", FontSize = 18 },
					},
				},
			};

			Detail = NavigationCatalogPage.CreateHost();
			FlyoutLayoutBehavior = FlyoutLayoutBehavior.Popover;
		}
	}
}
