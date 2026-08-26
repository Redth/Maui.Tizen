using System;
using System.Collections.Generic;
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
		static MauiApp BuildApp(Action<MauiAppBuilder>? configure = null)
		{
			var builder = MauiApp.CreateBuilder(useDefaults: false);
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

			var ticker = app.Services.GetService<ITicker>();

			Assert.NotNull(ticker);
			Assert.IsType<TizenTicker>(ticker);
		}

		[Fact]
		public void AnimationManagerIsRegistered() =>
			Assert.NotNull(BuildApp().Services.GetService<IAnimationManager>());

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

		sealed class StubApplication : IApplication
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
