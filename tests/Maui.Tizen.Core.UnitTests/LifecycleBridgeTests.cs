using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Covers the Tizen application-to-window lifecycle bridge and window-scope initialization.
	/// </summary>
	public class LifecycleBridgeTests
	{
		sealed class RecordingWindow : IWindow
		{
			public List<string> Events { get; } = new();

			public IView Content => null!;

			public string Title => "recording";

			public IElementHandler? Handler { get; set; }

			public IElement? Parent => null;

			public double X => double.NaN;

			public double Y => double.NaN;

			public double Width => double.NaN;

			public double Height => double.NaN;

			public double MinimumWidth => -1;

			public double MinimumHeight => -1;

			public double MaximumWidth => -1;

			public double MaximumHeight => -1;

			public IPersistedState PersistedState { get; } = new State();

			public IVisualDiagnosticsOverlay VisualDiagnosticsOverlay => null!;

			public FlowDirection FlowDirection => FlowDirection.LeftToRight;

			public IReadOnlyCollection<IWindowOverlay> Overlays { get; } = Array.Empty<IWindowOverlay>();

			public void Created() => Events.Add(nameof(Created));

			public void Activated() => Events.Add(nameof(Activated));

			public void Deactivated() => Events.Add(nameof(Deactivated));

			public void Stopped() => Events.Add(nameof(Stopped));

			public void Resumed() => Events.Add(nameof(Resumed));

			public void Destroying() => Events.Add(nameof(Destroying));

			public bool BackButtonClicked() => false;

			public void DisplayDensityChanged(float displayDensity)
			{
			}

			public float RequestDisplayDensity() => 1f;

			public bool AddOverlay(IWindowOverlay overlay) => false;

			public bool RemoveOverlay(IWindowOverlay overlay) => false;

			public void Backgrounding(IPersistedState state)
			{
			}

			public void FrameChanged(Rect frame)
			{
			}

			sealed class State : Dictionary<string, string?>, IPersistedState
			{
			}
		}

		sealed class RecordingApplication : IApplication
		{
			readonly List<IWindow> _windows = new();

			public RecordingApplication(IWindow window) => _windows.Add(window);

			public IReadOnlyList<IWindow> Windows => _windows;

			public IElementHandler? Handler { get; set; }

			public IElement? Parent => null;

			public AppTheme UserAppTheme { get; set; } = AppTheme.Unspecified;

			public IWindow CreateWindow(IActivationState? activationState) => _windows[0];

			public void ThemeChanged()
			{
			}

			public void OpenWindow(IWindow window)
			{
			}

			public void CloseWindow(IWindow window)
			{
			}

			public void ActivateWindow(IWindow window)
			{
			}
		}

		sealed class PlatformApplicationShim : IPlatformApplication
		{
			public PlatformApplicationShim(IApplication application, IServiceProvider services)
			{
				Application = application;
				Services = services;
			}

			public IServiceProvider Services { get; }

			public IApplication Application { get; }
		}

		static (TizenWindowLifecycleBridge Bridge, RecordingWindow Window) CreateBridge()
		{
			var window = new RecordingWindow();
			var application = new RecordingApplication(window);

			IPlatformApplication.Current = new PlatformApplicationShim(
				application,
				new ServiceCollection().BuildServiceProvider());

			return (new TizenWindowLifecycleBridge(), window);
		}

		[Fact]
		public void CreateRaisesOnlyCreated()
		{
			// Activated must NOT come from OnCreate. Tizen always follows OnCreate with OnResume,
			// and MAUI's contract everywhere else is Created -> Resumed -> Activated.
			var (bridge, window) = CreateBridge();

			bridge.OnCreate();

			Assert.Equal(new[] { "Created" }, window.Events);
		}

		[Fact]
		public void ColdStartupIsCreatedThenActivatedWithNoResumed()
		{
			// A cold start must NOT raise Resumed. Resumed means "came back from being stopped",
			// which is how MAUI defines it and what Android does - it raises Resumed from
			// OnRestart, not from the first OnStart.
			//
			// Tizen delivers OnResume on both a cold start and a return to the foreground, and
			// says nothing about which it is, so the bridge has to remember. It previously raised
			// Resumed unconditionally, making every cold start look like a return from the
			// background - so an app restoring state in Resumed did it on first launch too.
			var (bridge, window) = CreateBridge();

			bridge.OnCreate();
			bridge.OnResume();

			Assert.Equal(new[] { "Created", "Activated" }, window.Events);
		}

		[Fact]
		public void ResumedIsRaisedOnlyAfterARealStopped()
		{
			var (bridge, window) = CreateBridge();

			bridge.OnCreate();
			bridge.OnResume();
			Assert.DoesNotContain("Resumed", window.Events);

			bridge.OnPause();
			window.Events.Clear();

			bridge.OnResume();

			Assert.Equal(new[] { "Resumed", "Activated" }, window.Events);
		}

		[Fact]
		public void DuplicateStoppedIsSuppressed()
		{
			// Tizen can deliver OnPause more than once with no OnResume in between. A second
			// Stopped is observable to anything pairing Stopped with Resumed.
			var (bridge, window) = CreateBridge();

			bridge.OnCreate();
			bridge.OnResume();
			bridge.OnPause();
			window.Events.Clear();

			bridge.OnPause();

			Assert.Empty(window.Events);
		}

		[Fact]
		public void DuplicateResumedIsSuppressed()
		{
			var (bridge, window) = CreateBridge();

			bridge.OnCreate();
			bridge.OnResume();
			bridge.OnPause();
			bridge.OnResume();
			window.Events.Clear();

			bridge.OnResume();

			Assert.Empty(window.Events);
		}

		[Fact]
		public void PauseRaisesDeactivatedThenStopped()
		{
			var (bridge, window) = CreateBridge();
			bridge.OnCreate();
			bridge.OnResume();
			window.Events.Clear();

			bridge.OnPause();

			Assert.Equal(new[] { "Deactivated", "Stopped" }, window.Events);
		}

		[Fact]
		public void ResumeRaisesResumedThenActivated()
		{
			var (bridge, window) = CreateBridge();
			bridge.OnCreate();
			bridge.OnResume();
			bridge.OnPause();
			window.Events.Clear();

			bridge.OnResume();

			Assert.Equal(new[] { "Resumed", "Activated" }, window.Events);
		}

		[Fact]
		public void TerminateRaisesDeactivatedThenDestroying()
		{
			var (bridge, window) = CreateBridge();
			bridge.OnCreate();
			bridge.OnResume();
			window.Events.Clear();

			bridge.OnTerminate();

			Assert.Equal(new[] { "Deactivated", "Destroying" }, window.Events);
		}

		[Fact]
		public void ActivatedAndDeactivatedStayBalanced()
		{
			// Tizen can deliver OnResume without a preceding OnPause. Raising Activated twice
			// would be observable to a host that pairs the two.
			var (bridge, window) = CreateBridge();

			bridge.OnCreate();
			bridge.OnResume();
			bridge.OnResume();

			var activated = window.Events.FindAll(e => e == "Activated").Count;
			var deactivated = window.Events.FindAll(e => e == "Deactivated").Count;

			Assert.Equal(1, activated);
			Assert.Equal(0, deactivated);
		}

		[Fact]
		public void CreatedIsRaisedOnlyOnce()
		{
			var (bridge, window) = CreateBridge();

			bridge.OnCreate();
			bridge.OnCreate();

			Assert.Single(window.Events.FindAll(e => e == "Created"));
		}

		[Fact]
		public void FullLifecycleSequenceIsOrdered()
		{
			var (bridge, window) = CreateBridge();

			bridge.OnCreate();
			bridge.OnResume();
			bridge.OnPause();
			bridge.OnResume();
			bridge.OnTerminate();

			Assert.Equal(
				new[]
				{
					"Created", "Activated",
					"Deactivated", "Stopped",
					"Resumed", "Activated",
					"Deactivated", "Destroying",
				},
				window.Events);
		}

		[Fact]
		public void BridgeToleratesNoWindow()
		{
			IPlatformApplication.Current = null;
			var bridge = new TizenWindowLifecycleBridge();

			// Lifecycle callbacks can arrive before the window exists; they must not throw.
			bridge.OnCreate();
			bridge.OnResume();
			bridge.OnPause();
			bridge.OnTerminate();
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void BridgeIsRegisteredAsASingleton(bool useDefaults)
		{
			var builder = MauiApp.CreateBuilder(useDefaults);
			builder.ConfigureTizen();
			using var app = builder.Build();

			var first = app.Services.GetService<TizenWindowLifecycleBridge>();
			var second = app.Services.GetService<TizenWindowLifecycleBridge>();

			Assert.NotNull(first);
			Assert.Same(first, second);
		}
	}

	/// <summary>Covers window-scope creation.</summary>
	public class WindowScopeTests
	{
		sealed class RecordingScopedInitializer : IMauiInitializeScopedService
		{
			public static int Count;

			public void Initialize(IServiceProvider services) => Interlocked.Increment(ref Count);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void MakeWindowScopeRunsScopedInitializers(bool useDefaults)
		{
			// MAUI runs IMauiInitializeScopedService when it creates a window scope; its own
			// dispatcher relies on that, and a host's initializers would otherwise never run.
			RecordingScopedInitializer.Count = 0;

			var builder = MauiApp.CreateBuilder(useDefaults);
			builder.ConfigureTizen();
			builder.Services.AddScoped<IMauiInitializeScopedService, RecordingScopedInitializer>();

			using var app = builder.Build();

			var root = new TizenMauiContext(app.Services);
			var windowContext = root.MakeWindowScope(new TizenPlatformWindow(), out var scope);

			using (scope)
			{
				Assert.NotNull(windowContext);
				Assert.True(RecordingScopedInitializer.Count >= 1);
			}
		}

		[Fact]
		public void WindowScopePublishesThePlatformWindow()
		{
			var builder = MauiApp.CreateBuilder(useDefaults: true);
			builder.ConfigureTizen();
			using var app = builder.Build();

			var platformWindow = new TizenPlatformWindow();
			var context = new TizenMauiContext(app.Services).MakeWindowScope(platformWindow, out var scope);

			using (scope)
			{
				Assert.Same(platformWindow, context.GetPlatformWindow());
			}
		}
	}
}
