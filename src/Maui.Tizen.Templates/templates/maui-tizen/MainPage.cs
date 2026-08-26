using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace MauiTizenApp;

public class MainPage : ContentPage
{
	private int _count;

	public MainPage()
	{
		var label = new Label
		{
			Text = "Hello from .NET MAUI on Tizen!",
			HorizontalOptions = LayoutOptions.Center,
		};

		var button = new Button
		{
			Text = "Click me",
			HorizontalOptions = LayoutOptions.Center,
		};

		button.Clicked += (_, _) =>
		{
			_count++;
			button.Text = _count == 1 ? "Clicked 1 time" : $"Clicked {_count} times";
		};

		Content = new VerticalStackLayout
		{
			Spacing = 24,
			Padding = 24,
			VerticalOptions = LayoutOptions.Center,
			Children = { label, button },
		};
	}
}
