using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Maui.Tizen.Samples.Catalog.Navigation
{
	/// <summary>A grouped collection used by the catalog pages.</summary>
	public sealed class CatalogGroup : ObservableCollection<string>
	{
		public CatalogGroup(string name, IEnumerable<string> items)
			: base(items)
		{
			Name = name;
		}

		public string Name { get; }
	}

	/// <summary>
	/// Exercises the items stack: virtualization, selection, grouping, header/footer, empty view
	/// and reordering.
	/// </summary>
	/// <remarks>
	/// The list is deliberately large. Item recycling and measurement are the parts of the Tizen
	/// items implementation with the most room for regressions, and they only misbehave visibly
	/// once the source is longer than a screen.
	/// </remarks>
	public sealed class CollectionViewCatalogPage : ContentPage
	{
		readonly ObservableCollection<string> _flatItems;
		readonly ObservableCollection<CatalogGroup> _groupedItems;
		readonly CollectionView _collectionView;
		readonly Label _status;

		public CollectionViewCatalogPage()
		{
			Title = "CollectionView";

			_flatItems = new ObservableCollection<string>(
				Enumerable.Range(1, 500).Select(i => $"Item {i}"));

			_groupedItems = new ObservableCollection<CatalogGroup>(
				Enumerable.Range(1, 10).Select(g => new CatalogGroup(
					$"Group {g}",
					Enumerable.Range(1, 20).Select(i => $"Group {g} · item {i}"))));

			_status = new Label { Text = "No selection" };

			_collectionView = new CollectionView
			{
				ItemsSource = _flatItems,
				SelectionMode = SelectionMode.Single,
				ItemsLayout = LinearItemsLayout.Vertical,
				ItemTemplate = new DataTemplate(static () =>
				{
					var label = new Label { Padding = new Thickness(16, 12), FontSize = 16 };
					label.SetBinding(Label.TextProperty, static (string s) => s);
					return label;
				}),
				Header = new Label
				{
					Text = "Header",
					Padding = new Thickness(16),
					BackgroundColor = Color.FromArgb("#E3F2FD"),
				},
				Footer = new Label
				{
					Text = "Footer",
					Padding = new Thickness(16),
					BackgroundColor = Color.FromArgb("#E3F2FD"),
				},
				EmptyView = new Label
				{
					Text = "Nothing to show",
					HorizontalTextAlignment = TextAlignment.Center,
				},
				GroupHeaderTemplate = new DataTemplate(static () =>
				{
					var label = new Label
					{
						Padding = new Thickness(16, 8),
						FontSize = 14,
						BackgroundColor = Color.FromArgb("#BBDEFB"),
					};
					label.SetBinding(Label.TextProperty, static (CatalogGroup g) => g.Name);
					return label;
				}),
			};

			_collectionView.SelectionChanged += OnSelectionChanged;

			Content = new Grid
			{
				RowDefinitions =
				{
					new RowDefinition { Height = GridLength.Auto },
					new RowDefinition { Height = GridLength.Auto },
					new RowDefinition { Height = GridLength.Star },
				},
				Children =
				{
					BuildControls(),
					_status,
					_collectionView,
				},
			};

			Grid.SetRow((View)Content.GetVisualTreeDescendants()[0], 0);
		}

		View BuildControls() => new HorizontalStackLayout
		{
			Padding = 8,
			Spacing = 8,
			Children =
			{
				new Button { Text = "Flat", Command = new Command(ShowFlat) },
				new Button { Text = "Grouped", Command = new Command(ShowGrouped) },
				new Button { Text = "Empty", Command = new Command(ShowEmpty) },
				new Button { Text = "Multi-select", Command = new Command(ToggleMultiSelect) },
				new Button { Text = "Grid", Command = new Command(ToggleGridLayout) },
				new Button { Text = "Add", Command = new Command(AddItem) },
				new Button { Text = "Remove", Command = new Command(RemoveItem) },
			},
		};

		void ShowFlat()
		{
			_collectionView.IsGrouped = false;
			_collectionView.ItemsSource = _flatItems;
		}

		void ShowGrouped()
		{
			_collectionView.IsGrouped = true;
			_collectionView.ItemsSource = _groupedItems;
		}

		void ShowEmpty()
		{
			_collectionView.IsGrouped = false;
			_collectionView.ItemsSource = Array.Empty<string>();
		}

		void ToggleMultiSelect()
			=> _collectionView.SelectionMode =
				_collectionView.SelectionMode == SelectionMode.Multiple ? SelectionMode.Single : SelectionMode.Multiple;

		void ToggleGridLayout()
			=> _collectionView.ItemsLayout =
				_collectionView.ItemsLayout is GridItemsLayout
					? LinearItemsLayout.Vertical
					: new GridItemsLayout(2, ItemsLayoutOrientation.Vertical);

		// Incremental mutation while the view is realized is what shakes out adaptor/recycling
		// desync, which is why it is a first-class catalog affordance rather than a test-only path.
		void AddItem() => _flatItems.Insert(0, $"Inserted {DateTime.Now:HH:mm:ss.fff}");

		void RemoveItem()
		{
			if (_flatItems.Count > 0)
			{
				_flatItems.RemoveAt(0);
			}
		}

		void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
			=> _status.Text = e.CurrentSelection.Count == 0
				? "No selection"
				: $"Selected: {string.Join(", ", e.CurrentSelection.Select(static o => o?.ToString()))}";
	}

	/// <summary>
	/// Exercises <c>TizenCarouselViewHandler</c>: looping, position tracking and item sizing.
	/// </summary>
	public sealed class CarouselViewCatalogPage : ContentPage
	{
		public CarouselViewCatalogPage()
		{
			Title = "CarouselView";

			var carousel = new CarouselView
			{
				Loop = true,
				ItemsSource = Enumerable.Range(1, 8).Select(i => $"Card {i}").ToList(),
				ItemTemplate = new DataTemplate(static () =>
				{
					var label = new Label
					{
						FontSize = 28,
						HorizontalTextAlignment = TextAlignment.Center,
						VerticalTextAlignment = TextAlignment.Center,
					};
					label.SetBinding(Label.TextProperty, static (string s) => s);

					return new Border
					{
						Margin = 20,
						BackgroundColor = Color.FromArgb("#E3F2FD"),
						Content = label,
					};
				}),
			};

			var position = new Label { HorizontalTextAlignment = TextAlignment.Center };
			carousel.PositionChanged += (_, e) => position.Text = $"Position {e.CurrentPosition}";

			Content = new Grid
			{
				RowDefinitions =
				{
					new RowDefinition { Height = GridLength.Star },
					new RowDefinition { Height = GridLength.Auto },
				},
				Children = { carousel, position },
			};
		}
	}
}
