using Microsoft.Maui.Controls;

namespace MauiTizenApp;

public class App : Application
{
	protected override Window CreateWindow(IActivationState? activationState)
		=> new Window(new MainPage());
}
