using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
using Font = Microsoft.Maui.Font;

namespace Maui.Tizen.Sample
{
	/// <summary>The sample's cross-platform application.</summary>
	public class SampleApplication : IApplication
	{
		readonly List<IWindow> _windows = new();

		/// <inheritdoc />
		public IReadOnlyList<IWindow> Windows => _windows;

		/// <inheritdoc />
		public IElementHandler? Handler { get; set; }

		/// <inheritdoc />
		public IElement? Parent { get; set; }

		/// <inheritdoc />
		public AppTheme UserAppTheme { get; set; } = AppTheme.Unspecified;

		/// <inheritdoc />
		public IWindow CreateWindow(IActivationState? activationState)
		{
			var window = new SampleWindow();
			_windows.Add(window);
			return window;
		}

		/// <inheritdoc />
		public void ThemeChanged()
		{
		}

		/// <inheritdoc />
		public void OpenWindow(IWindow window)
		{
		}

		/// <inheritdoc />
		public void CloseWindow(IWindow window) => _windows.Remove(window);

		/// <inheritdoc />
		public void ActivateWindow(IWindow window)
		{
		}
	}

	/// <summary>The sample's single window.</summary>
	public class SampleWindow : IWindow
	{
		readonly SamplePage _page;

		/// <summary>Initializes a new instance of the <see cref="SampleWindow"/> class.</summary>
		public SampleWindow()
		{
			var layout = new SampleStackLayout();

			layout.Add(new SampleLabel
			{
				Text = "Maui.Tizen",
				TextColor = Colors.Black,
				Font = Font.SystemFontOfSize(28, FontWeight.Bold),
			});

			layout.Add(new SampleLabel
			{
				Text = "Standalone Tizen (NUI) backend for .NET MAUI.",
				TextColor = Colors.DimGray,
				Font = Font.SystemFontOfSize(16),
			});

			layout.Add(new SampleLabel
			{
				Text = $"Rendered by {nameof(Microsoft.Maui.Platforms.Tizen.Handlers.TizenLabelHandler)}.",
				TextColor = Colors.SlateGray,
				Font = Font.SystemFontOfSize(13),
			});

			_page = new SamplePage(layout);
			_page.Parent = this;
		}

		/// <inheritdoc />
		public IView Content => _page;

		/// <inheritdoc />
		public string Title => "Maui.Tizen Sample";

		/// <inheritdoc />
		public IElementHandler? Handler { get; set; }

		/// <inheritdoc />
		public IElement? Parent => null;

		/// <inheritdoc />
		public double X => double.NaN;

		/// <inheritdoc />
		public double Y => double.NaN;

		/// <inheritdoc />
		public double Width => double.NaN;

		/// <inheritdoc />
		public double Height => double.NaN;

		/// <inheritdoc />
		public double MinimumWidth => -1;

		/// <inheritdoc />
		public double MinimumHeight => -1;

		/// <inheritdoc />
		public double MaximumWidth => -1;

		/// <inheritdoc />
		public double MaximumHeight => -1;

		/// <inheritdoc />
		public IPersistedState PersistedState { get; } = new SamplePersistedState();

		/// <inheritdoc />
		public IVisualDiagnosticsOverlay? VisualDiagnosticsOverlay => null;

		/// <inheritdoc />
		public FlowDirection FlowDirection => FlowDirection.LeftToRight;

		/// <inheritdoc />
		public void Created()
		{
		}

		/// <inheritdoc />
		public void Activated()
		{
		}

		/// <inheritdoc />
		public void Deactivated()
		{
		}

		/// <inheritdoc />
		public void Stopped()
		{
		}

		/// <inheritdoc />
		public void Resumed()
		{
		}

		/// <inheritdoc />
		public void Destroying()
		{
		}

		/// <inheritdoc />
		public bool BackButtonClicked() => false;

		/// <inheritdoc />
		public void DisplayDensityChanged(float displayDensity)
		{
		}

		/// <inheritdoc />
		public IReadOnlyCollection<IWindowOverlay> Overlays { get; } = Array.Empty<IWindowOverlay>();

		/// <inheritdoc />
		public bool AddOverlay(IWindowOverlay overlay) => false;

		/// <inheritdoc />
		public bool RemoveOverlay(IWindowOverlay overlay) => false;

		/// <inheritdoc />
		public void Backgrounding(IPersistedState state)
		{
		}

		/// <inheritdoc />
		public void FrameChanged(Rect frame)
		{
		}

		/// <inheritdoc />
		public float RequestDisplayDensity()
		{
			var request = new DisplayDensityRequest();
			Handler?.Invoke(nameof(IWindow.RequestDisplayDensity), request);
			return request.Result;
		}

		sealed class SamplePersistedState : Dictionary<string, string>, IPersistedState
		{
		}
	}
}
