using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Maui.Tizen.Samples.Catalog.Navigation
{
	/// <summary>
	/// Exercises the Shell surface owned by <c>TizenShellHandler</c>,
	/// <c>TizenShellItemHandler</c> and <c>TizenShellSectionHandler</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Deliberately mixes every structural shape the Tizen shell view has to render: a flyout with
	/// a header, footer and menu items; a shell item with several sections (top tab bar); a section
	/// with several contents; and a search handler.
	/// </para>
	/// <para>
	/// The content pages are registered through <see cref="ShellContent.ContentTemplate"/> rather
	/// than <see cref="ShellContent.Content"/> on purpose. That is the path that keeps shell content
	/// creation lazy, and lazy creation is exactly the behaviour most at risk of regressing during
	/// the migration - eager realization looks identical on screen and only shows up as a startup
	/// cost proportional to the number of tabs.
	/// </para>
	/// </remarks>
	public sealed class ShellCatalogPage : Shell
	{
		public ShellCatalogPage()
		{
			FlyoutBehavior = FlyoutBehavior.Flyout;
			FlyoutHeaderBehavior = FlyoutHeaderBehavior.Fixed;

			FlyoutHeader = new Grid
			{
				HeightRequest = 120,
				BackgroundColor = Color.FromArgb("#2196F3"),
				Children =
				{
					new Label
					{
						Text = "Maui.Tizen catalog",
						TextColor = Colors.White,
						FontSize = 20,
						HorizontalTextAlignment = TextAlignment.Center,
						VerticalTextAlignment = TextAlignment.Center,
					},
				},
			};

			FlyoutFooter = new Label
			{
				Text = "Footer",
				Padding = new Thickness(16),
				HorizontalTextAlignment = TextAlignment.Center,
			};

			SetValue(SearchHandlerProperty, new CatalogSearchHandler());

			// A shell item with two sections renders the top tab bar.
			var browse = new TabBar { Title = "Browse", Route = "browse" };

			browse.Items.Add(new ShellSection
			{
				Title = "Lists",
				Route = "lists",
				Items =
				{
					new ShellContent
					{
						Title = "CollectionView",
						Route = "collectionview",
						ContentTemplate = new DataTemplate(static () => new CollectionViewCatalogPage()),
					},
					new ShellContent
					{
						Title = "CarouselView",
						Route = "carouselview",
						ContentTemplate = new DataTemplate(static () => new CarouselViewCatalogPage()),
					},
				},
			});

			browse.Items.Add(new ShellSection
			{
				Title = "Navigation",
				Route = "navigation",
				Items =
				{
					new ShellContent
					{
						Title = "Stack",
						Route = "stack",
						ContentTemplate = new DataTemplate(static () => new NavigationCatalogPage()),
					},
				},
			});

			Items.Add(browse);

			// A bare MenuItem in the flyout: the case whose item template cannot be fully resolved
			// out-of-tree because MenuShellItem is internal. See docs/waves/wave-c.md.
			Items.Add(new MenuItem
			{
				Text = "About",
				Command = new Command(() => _ = DisplayAlertAsync("Maui.Tizen", "Wave C catalog", "OK")),
			});
		}

		sealed class CatalogSearchHandler : SearchHandler
		{
			public CatalogSearchHandler()
			{
				Placeholder = "Search the catalog";
				ShowsResults = true;

				ItemTemplate = new DataTemplate(static () =>
				{
					var label = new Label { Padding = new Thickness(12, 8) };
					label.SetBinding(Label.TextProperty, static (string s) => s);
					return label;
				});
			}

			protected override void OnQueryChanged(string oldValue, string newValue)
			{
				base.OnQueryChanged(oldValue, newValue);

				string[] all = { "CollectionView", "CarouselView", "Navigation stack", "Tabs", "Flyout" };

				ItemsSource = string.IsNullOrWhiteSpace(newValue)
					? null
					: Array.FindAll(all, i => i.Contains(newValue, StringComparison.OrdinalIgnoreCase));
			}
		}
	}
}
