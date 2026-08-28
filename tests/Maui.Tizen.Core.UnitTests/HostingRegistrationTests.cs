using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Animations;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Primitives;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	public class HostingRegistrationTests
	{
		static MauiApp BuildApp(Action<MauiAppBuilder>? configure = null, bool useDefaults = false)
		{
			var builder = MauiApp.CreateBuilder(useDefaults);
			builder.UseMauiAppTizen<StubApplication>();
			configure?.Invoke(builder);
			return builder.Build();
		}

		[Fact]
		public void UseMauiAppTizenRegistersTheApplication()
		{
			using var app = BuildApp();

			var application = app.Services.GetService<IApplication>();

			Assert.NotNull(application);
			Assert.IsType<StubApplication>(application);
		}

		[Theory]
		[InlineData(typeof(IApplication), typeof(TizenApplicationHandler))]
		[InlineData(typeof(IWindow), typeof(TizenWindowHandler))]
		[InlineData(typeof(IContentView), typeof(TizenContentViewHandler))]
		[InlineData(typeof(ILayout), typeof(TizenLayoutHandler))]
		[InlineData(typeof(ILabel), typeof(TizenLabelHandler))]
		public void UseMauiAppTizenRegistersHandler(Type virtualViewType, Type expectedHandlerType)
		{
			using var app = BuildApp();

			var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();

			Assert.Equal(expectedHandlerType, handlers.GetHandlerType(virtualViewType));
		}

		[Theory]
		[InlineData(typeof(ILabel))]
		[InlineData(typeof(ILayout))]
		[InlineData(typeof(IContentView))]
		public void RegisteredHandlersCanBeInstantiated(Type virtualViewType)
		{
			using var app = BuildApp();

			var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();
			var handler = handlers.GetHandler(virtualViewType);

			Assert.NotNull(handler);
			Assert.IsAssignableFrom<IViewHandler>(handler);
		}

		[Fact]
		public void PageHandlerIsOptInAndOverridesContentView()
		{
			using var app = BuildApp(builder =>
				builder.ConfigureMauiHandlers(handlers => handlers.AddTizenPageHandler<StubPage>()));

			var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();

			Assert.Equal(typeof(TizenPageHandler), handlers.GetHandlerType(typeof(StubPage)));
			Assert.Equal(typeof(TizenContentViewHandler), handlers.GetHandlerType(typeof(IContentView)));
		}

		[Fact]
		public void DispatcherProviderIsRegistered()
		{
			using var app = BuildApp();

			var provider = app.Services.GetService<IDispatcherProvider>();

			Assert.NotNull(provider);
			Assert.IsType<TizenDispatcherProvider>(provider);
		}

		[Fact]
		public void TickerIsRegisteredAsTheTizenTicker()
		{
			using var app = BuildApp();
			using var scope = app.Services.CreateScope();

			var ticker = scope.ServiceProvider.GetService<ITicker>();

			Assert.NotNull(ticker);
			Assert.IsType<TizenTicker>(ticker);
		}

		[Fact]
		public void AnimationManagerIsRegistered()
		{
			using var app = BuildApp();
			using var scope = app.Services.CreateScope();

			Assert.NotNull(scope.ServiceProvider.GetService<IAnimationManager>());
		}

		[Fact]
		public void TickerIsScopedNotSingletonOrTransient()
		{
			// TizenTicker is IDisposable and captures SynchronizationContext.Current in its
			// constructor. A singleton would pin every animation callback to whichever thread
			// resolved it first; a transient resolved from the root provider would keep its Timer
			// alive for the whole process. dotnet/maui registers it scoped for the same reasons.
			using var app = BuildApp();
			using var first = app.Services.CreateScope();
			using var second = app.Services.CreateScope();

            var a1 = first.ServiceProvider.GetRequiredService<ITicker>();
			var a2 = first.ServiceProvider.GetRequiredService<ITicker>();
			var b = second.ServiceProvider.GetRequiredService<ITicker>();

			Assert.Same(a1, a2);
			Assert.NotSame(a1, b);
		}

		[Fact]
		public void AnimationManagerIsScopedAndUsesTheScopedTicker()
		{
			using var app = BuildApp();
			using var first = app.Services.CreateScope();
			using var second = app.Services.CreateScope();

			var a = first.ServiceProvider.GetRequiredService<IAnimationManager>();
			var b = second.ServiceProvider.GetRequiredService<IAnimationManager>();

			Assert.NotSame(a, b);
			Assert.Same(first.ServiceProvider.GetRequiredService<ITicker>(), a.Ticker);
		}

		[Fact]
		public void ConfigureTizenDoesNotOverrideAnExplicitApplicationRegistration()
		{
			var builder = MauiApp.CreateBuilder(useDefaults: false);
			var expected = new StubApplication();
			builder.Services.AddSingleton<IApplication>(expected);
			builder.UseMauiAppTizen<StubApplication>();

			using var app = builder.Build();

			Assert.Same(expected, app.Services.GetRequiredService<IApplication>());
		}

		[Fact]
		public void UseMauiAppTizenWithFactoryUsesTheFactory()
		{
			var expected = new StubApplication();
			var builder = MauiApp.CreateBuilder(useDefaults: false);
			builder.UseMauiAppTizen(_ => expected);

			using var app = builder.Build();

			Assert.Same(expected, app.Services.GetRequiredService<IApplication>());
		}

		[Fact]
		public void AddTizenHandlersIsIdempotent()
		{
			using var app = BuildApp(builder =>
				builder.ConfigureMauiHandlers(handlers => handlers.AddTizenHandlers()));

			var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();

			Assert.Equal(typeof(TizenLabelHandler), handlers.GetHandlerType(typeof(ILabel)));
		}

		// ------------------------------------------------------------------------------------
		// useDefaults: true
		//
		// This is what a real app gets - MauiApp.CreateBuilder() defaults to true, and the sample
		// uses it. MAUI's own ConfigureDispatching/ConfigureAnimations run FIRST and register
		// neutral implementations, so any TryAdd in ConfigureTizen is a silent no-op and the Tizen
		// services never win. The whole suite above ran with useDefaults:false and could not see
		// that, which is exactly how the bug survived.
		// ------------------------------------------------------------------------------------

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void DispatcherProviderIsTheTizenProviderRegardlessOfDefaults(bool useDefaults)
		{
			using var app = BuildApp(useDefaults: useDefaults);

			var provider = app.Services.GetService<IDispatcherProvider>();

			Assert.IsType<TizenDispatcherProvider>(provider);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void TickerIsTheTizenTickerRegardlessOfDefaults(bool useDefaults)
		{
			using var app = BuildApp(useDefaults: useDefaults);
			using var scope = app.Services.CreateScope();

			Assert.IsType<TizenTicker>(scope.ServiceProvider.GetService<ITicker>());
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void AnimationManagerResolvesAndUsesTheTizenTicker(bool useDefaults)
		{
			using var app = BuildApp(useDefaults: useDefaults);
			using var scope = app.Services.CreateScope();

			var manager = scope.ServiceProvider.GetService<IAnimationManager>();

			Assert.NotNull(manager);
			Assert.IsType<TizenTicker>(manager!.Ticker);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void DispatcherResolvesNonNullOnAThreadWithASynchronizationContext(bool useDefaults)
		{
			using var app = BuildApp(useDefaults: useDefaults);

			IDispatcher? dispatcher = null;
			Exception? failure = null;

			// A SynchronizationContext stands in for the NUI main loop; without one there is
			// legitimately no dispatcher to hand out.
			var thread = new Thread(() =>
			{
				SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
				try
				{
					using var scope = app.Services.CreateScope();
					dispatcher = scope.ServiceProvider.GetService<IDispatcher>();
				}
				catch (Exception ex)
				{
					failure = ex;
				}
			});

			thread.Start();
			thread.Join();

			Assert.Null(failure);
			Assert.NotNull(dispatcher);
			Assert.IsType<TizenDispatcher>(dispatcher);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void StaticDispatcherProviderIsPublishedByBuildAlone(bool useDefaults)
		{
			// MainThread reads the STATIC DispatcherProvider.Current, not DI, and a real app never
			// resolves IDispatcher by hand to prime it. So this asserts on the state immediately
			// after Build() with NO test-only resolve - anything else would prove nothing about
			// what a real app sees.
			//
			// Measured before the fix: useDefaults:true happened to work, because MAUI's own
			// ApplicationDispatcherInitializer resolves IDispatcher at Build time as a side
			// effect. useDefaults:false left the neutral Microsoft.Maui.Dispatching.
			// DispatcherProvider in place, silently. TizenDispatcherProviderInitializer now
			// publishes it explicitly on both paths.
			DispatcherProvider.SetCurrent(null);

			using var app = BuildApp(useDefaults: useDefaults);

			Assert.IsType<TizenDispatcherProvider>(DispatcherProvider.Current);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void MainThreadBridgeSeesTheTizenProviderWithoutAnyManualResolve(bool useDefaults)
		{
			// The end-to-end consequence: with the Tizen provider published, a thread carrying a
			// SynchronizationContext (which the NUI main loop does) yields a Tizen dispatcher
			// through the same static path MainThread uses.
			DispatcherProvider.SetCurrent(null);

			using var app = BuildApp(useDefaults: useDefaults);

			IDispatcher? dispatcher = null;
			var thread = new Thread(() =>
			{
				SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
				dispatcher = DispatcherProvider.Current.GetForCurrentThread();
			});

			thread.Start();
			thread.Join();

			Assert.IsType<TizenDispatcher>(dispatcher);
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void HandlersAreRegisteredRegardlessOfDefaults(bool useDefaults)
		{
			using var app = BuildApp(useDefaults: useDefaults);

			var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();

			Assert.Equal(typeof(TizenLabelHandler), handlers.GetHandlerType(typeof(ILabel)));
			Assert.Equal(typeof(TizenLayoutHandler), handlers.GetHandlerType(typeof(ILayout)));
		}

		[Fact]
		public void UserRegisteredApplicationStillWinsUnderDefaults()
		{
			// IApplication stays on TryAdd on purpose: a host that registers its own instance
			// before calling UseMauiAppTizen should keep it. Platform services are the opposite.
			var expected = new StubApplication();
			var builder = MauiApp.CreateBuilder(useDefaults: true);
			builder.Services.AddSingleton<IApplication>(expected);
			builder.UseMauiAppTizen<StubApplication>();

			using var app = builder.Build();

			Assert.Same(expected, app.Services.GetRequiredService<IApplication>());
		}

		internal sealed class StubApplication : IApplication
		{
			public IReadOnlyList<IWindow> Windows { get; } = Array.Empty<IWindow>();

			public IElementHandler? Handler { get; set; }

			public IElement? Parent => null;

			public IWindow CreateWindow(IActivationState? activationState) =>
				throw new NotSupportedException("The stub application never creates a window.");

			public AppTheme UserAppTheme { get; set; } = AppTheme.Unspecified;

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

		sealed class StubPage : IContentView
		{
			public object? Content => null;

			public IView? PresentedContent => null;

			public Thickness Padding => Thickness.Zero;

			public Size CrossPlatformMeasure(double widthConstraint, double heightConstraint) => Size.Zero;

			public Size CrossPlatformArrange(Rect bounds) => bounds.Size;

			public IViewHandler? Handler { get; set; }

			IElementHandler? IElement.Handler
			{
				get => Handler;
				set => Handler = value as IViewHandler;
			}

			public IElement? Parent => null;

			public bool IsFocused { get; set; }

			public Visibility Visibility => Visibility.Visible;

			public double Opacity => 1;

			public Paint? Background => null;

			public IShape? Clip => null;

			public IShadow? Shadow => null;

			public bool InputTransparent => false;

			public bool IsEnabled => true;

			public double Width => 0;

			public double Height => 0;

			public double MinimumWidth => 0;

			public double MinimumHeight => 0;

			public double MaximumWidth => double.PositiveInfinity;

			public double MaximumHeight => double.PositiveInfinity;

			public Thickness Margin => Thickness.Zero;

			public Rect Frame { get; set; }

			public FlowDirection FlowDirection => FlowDirection.LeftToRight;

			public LayoutAlignment HorizontalLayoutAlignment => LayoutAlignment.Fill;

			public LayoutAlignment VerticalLayoutAlignment => LayoutAlignment.Fill;

			public Semantics? Semantics => null;

			public string AutomationId => string.Empty;

			public int ZIndex => 0;

			public Size DesiredSize => Size.Zero;

			public double AnchorX => 0.5;

			public double AnchorY => 0.5;

			public double Rotation => 0;

			public double RotationX => 0;

			public double RotationY => 0;

			public double Scale => 1;

			public double ScaleX => 1;

			public double ScaleY => 1;

			public double TranslationX => 0;

			public double TranslationY => 0;

			public Size Arrange(Rect bounds) => bounds.Size;

			public void InvalidateArrange()
			{
			}

			public void InvalidateMeasure()
			{
			}

			public Size Measure(double widthConstraint, double heightConstraint) => Size.Zero;

			public bool Focus() => false;

			public void Unfocus()
			{
			}
		}
	}
}
