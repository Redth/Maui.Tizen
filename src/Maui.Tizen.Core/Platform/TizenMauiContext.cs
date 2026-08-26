using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// <see cref="IMauiContext"/> implementation that can overlay platform-specific instances (the
	/// NUI window, the Tizen application) on top of an existing <see cref="IServiceProvider"/>.
	/// </summary>
	/// <remarks>
	/// MAUI ships <c>Microsoft.Maui.MauiContext</c>, but its <c>AddSpecific</c> /
	/// <c>AddWeakSpecific</c> members are <c>internal</c>, so an out-of-repo backend has no
	/// supported way to publish the platform window or platform application into a scope. This type
	/// implements the public <see cref="IMauiContext"/> contract directly instead. See
	/// docs/net11-status.md ("Required public MAUI API gaps").
	/// </remarks>
	public class TizenMauiContext : IMauiContext, IServiceProvider
	{
		readonly IServiceProvider _inner;
		readonly Dictionary<Type, object> _specific;

		/// <summary>Initializes a new instance of the <see cref="TizenMauiContext"/> class.</summary>
		/// <param name="services">The service provider to delegate to.</param>
		public TizenMauiContext(IServiceProvider services)
		{
			_inner = services ?? throw new ArgumentNullException(nameof(services));
			_specific = new Dictionary<Type, object>();
		}

		TizenMauiContext(IServiceProvider services, Dictionary<Type, object> specific)
		{
			_inner = services;
			_specific = specific;
		}

		/// <inheritdoc />
		public IServiceProvider Services => this;

		/// <inheritdoc />
		public IMauiHandlersFactory Handlers =>
			_inner.GetRequiredService<IMauiHandlersFactory>();

		/// <summary>Registers a platform-specific instance visible to this context.</summary>
		/// <typeparam name="TService">The service type to register the instance under.</typeparam>
		/// <param name="instance">The instance.</param>
		/// <returns>This context, for chaining.</returns>
		public TizenMauiContext AddSpecific<TService>(TService instance)
			where TService : class
		{
			ArgumentNullException.ThrowIfNull(instance);

			_specific[typeof(TService)] = instance;
			return this;
		}

		/// <summary>
		/// Creates a context that shares this context's specific instances but resolves everything
		/// else from another provider - used when entering a DI scope.
		/// </summary>
		/// <param name="services">The scoped service provider.</param>
		/// <returns>The derived context.</returns>
		public TizenMauiContext WithServices(IServiceProvider services)
		{
			ArgumentNullException.ThrowIfNull(services);

			return new TizenMauiContext(services, new Dictionary<Type, object>(_specific));
		}

		/// <inheritdoc />
		public object? GetService(Type serviceType)
		{
			ArgumentNullException.ThrowIfNull(serviceType);

			if (_specific.TryGetValue(serviceType, out var instance))
				return instance;

			return _inner.GetService(serviceType);
		}
	}
}
