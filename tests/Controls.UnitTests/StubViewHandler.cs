using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests;

/// <summary>
/// A minimal <see cref="IViewHandler"/> that exposes a platform view and an optional container
/// view. It deliberately does not implement .NET MAUI's <c>IPlatformViewHandler</c>, which is the
/// exact shape the Tizen gesture infrastructure must work without.
/// </summary>
internal sealed class StubViewHandler : IViewHandler
{
	public StubViewHandler(IView? virtualView = null, object? platformView = null, object? containerView = null, IMauiContext? mauiContext = null)
	{
		VirtualView = virtualView;
		PlatformView = platformView ?? new object();
		ContainerView = containerView;
		MauiContext = mauiContext;
	}

	public bool HasContainer { get; set; }

	public object? ContainerView { get; set; }

	public object? PlatformView { get; set; }

	public IView? VirtualView { get; set; }

	public IMauiContext? MauiContext { get; set; }

	IElement? IElementHandler.VirtualView => VirtualView;

	public bool Disconnected { get; private set; }

	public void DisconnectHandler() => Disconnected = true;

	public Size GetDesiredSize(double widthConstraint, double heightConstraint) => Size.Zero;

	public void Invoke(string command, object? args)
	{
	}

	public void PlatformArrange(Rect frame)
	{
	}

	public void SetMauiContext(IMauiContext mauiContext) => MauiContext = mauiContext;

	public void SetVirtualView(IElement view) => VirtualView = view as IView;

	public void UpdateValue(string property)
	{
	}
}

/// <summary>
/// A minimal <see cref="IMauiContext"/> backed by a real service provider so that scoped
/// resolution behaves the way it does in a hosted app.
/// </summary>
internal sealed class StubMauiContext : IMauiContext
{
	public StubMauiContext(IServiceProvider services, IMauiHandlersFactory? handlers = null)
	{
		Services = services;
		_handlers = handlers;
	}

	readonly IMauiHandlersFactory? _handlers;

	public IServiceProvider Services { get; }

	public IMauiHandlersFactory Handlers =>
		_handlers ?? throw new NotSupportedException("Handler resolution is not exercised by these tests.");

	/// <summary>Creates a context over an empty service provider.</summary>
	public static StubMauiContext Empty() =>
		new(new ServiceCollection().BuildServiceProvider());

	/// <summary>Creates a context that can realize page handlers.</summary>
	public static StubMauiContext WithHandlers() =>
		new(new ServiceCollection().BuildServiceProvider(), new StubHandlersFactory());

	/// <summary>
	/// Creates a context over a fresh DI scope, mirroring the per-window scope .NET MAUI creates.
	/// </summary>
	public static (StubMauiContext Context, IServiceScope Scope) CreateWindowScope(IServiceProvider root)
	{
		var scope = root.CreateScope();
		return (new StubMauiContext(scope.ServiceProvider), scope);
	}
}

/// <summary>
/// Hands out <see cref="StubViewHandler"/> instances so modal page realization can be exercised
/// without any platform.
/// </summary>
internal sealed class StubHandlersFactory : IMauiHandlersFactory
{
	public int Created { get; private set; }

	public IElementHandler? GetHandler(Type type)
	{
		Created++;
		return new StubViewHandler();
	}

	public IElementHandler? GetHandler<T>() where T : IElement => GetHandler(typeof(T));

	public Microsoft.Maui.Hosting.IMauiHandlersCollection GetCollection() =>
		throw new NotSupportedException("Not exercised by these tests.");

	public Type? GetHandlerType(Type iview) => typeof(StubViewHandler);

	public IServiceProvider GetServiceProvider() =>
		throw new NotSupportedException("Not exercised by these tests.");

	public object? GetService(Type serviceType) => null;
}
