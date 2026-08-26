using System;
using Microsoft.Maui.LifecycleEvents;

namespace Microsoft.Maui.Platforms.Tizen.LifecycleEvents
{
	/// <summary>
	/// Builder surface for registering <see cref="TizenLifecycle"/> handlers.
	/// </summary>
	public interface ITizenLifecycleBuilder : ILifecycleBuilder
	{
	}

	/// <summary>
	/// Default <see cref="ITizenLifecycleBuilder"/> implementation, delegating to the underlying
	/// MAUI lifecycle builder.
	/// </summary>
	public class TizenLifecycleBuilder : ITizenLifecycleBuilder
	{
		readonly ILifecycleBuilder _builder;

		/// <summary>Initializes a new instance of the <see cref="TizenLifecycleBuilder"/> class.</summary>
		/// <param name="builder">The underlying MAUI lifecycle builder.</param>
		public TizenLifecycleBuilder(ILifecycleBuilder builder) =>
			_builder = builder ?? throw new ArgumentNullException(nameof(builder));

		/// <inheritdoc />
		public void AddEvent<TDelegate>(string eventName, TDelegate action)
			where TDelegate : Delegate =>
			_builder.AddEvent(eventName, action);
	}

	/// <summary>
	/// Registration helpers for Tizen lifecycle events.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.LifecycleEvents.TizenLifecycleBuilderExtensions</c> in
	/// dotnet/maui.
	/// </remarks>
	public static class TizenLifecycleBuilderExtensions
	{
		/// <summary>Adds Tizen-specific lifecycle event handlers.</summary>
		/// <param name="builder">The lifecycle builder.</param>
		/// <param name="configureDelegate">Callback that registers the handlers.</param>
		/// <returns>The lifecycle builder, for chaining.</returns>
		public static ILifecycleBuilder AddTizen(
			this ILifecycleBuilder builder,
			Action<ITizenLifecycleBuilder> configureDelegate)
		{
			ArgumentNullException.ThrowIfNull(builder);
			ArgumentNullException.ThrowIfNull(configureDelegate);

			configureDelegate(new TizenLifecycleBuilder(builder));
			return builder;
		}

		/// <summary>Registers an <see cref="TizenLifecycle.OnPreCreate"/> handler.</summary>
		/// <param name="builder">The builder.</param>
		/// <param name="del">The handler.</param>
		/// <returns>The builder, for chaining.</returns>
		public static ITizenLifecycleBuilder OnPreCreate(this ITizenLifecycleBuilder builder, TizenLifecycle.OnPreCreate del) =>
			builder.OnEvent(nameof(TizenLifecycle.OnPreCreate), del);

		/// <summary>Registers an <see cref="TizenLifecycle.OnCreate"/> handler.</summary>
		/// <param name="builder">The builder.</param>
		/// <param name="del">The handler.</param>
		/// <returns>The builder, for chaining.</returns>
		public static ITizenLifecycleBuilder OnCreate(this ITizenLifecycleBuilder builder, TizenLifecycle.OnCreate del) =>
			builder.OnEvent(nameof(TizenLifecycle.OnCreate), del);

		/// <summary>Registers an <see cref="TizenLifecycle.OnResume"/> handler.</summary>
		/// <param name="builder">The builder.</param>
		/// <param name="del">The handler.</param>
		/// <returns>The builder, for chaining.</returns>
		public static ITizenLifecycleBuilder OnResume(this ITizenLifecycleBuilder builder, TizenLifecycle.OnResume del) =>
			builder.OnEvent(nameof(TizenLifecycle.OnResume), del);

		/// <summary>Registers an <see cref="TizenLifecycle.OnPause"/> handler.</summary>
		/// <param name="builder">The builder.</param>
		/// <param name="del">The handler.</param>
		/// <returns>The builder, for chaining.</returns>
		public static ITizenLifecycleBuilder OnPause(this ITizenLifecycleBuilder builder, TizenLifecycle.OnPause del) =>
			builder.OnEvent(nameof(TizenLifecycle.OnPause), del);

		/// <summary>Registers an <see cref="TizenLifecycle.OnTerminate"/> handler.</summary>
		/// <param name="builder">The builder.</param>
		/// <param name="del">The handler.</param>
		/// <returns>The builder, for chaining.</returns>
		public static ITizenLifecycleBuilder OnTerminate(this ITizenLifecycleBuilder builder, TizenLifecycle.OnTerminate del) =>
			builder.OnEvent(nameof(TizenLifecycle.OnTerminate), del);

		/// <summary>Registers an <see cref="TizenLifecycle.OnAppControlReceived"/> handler.</summary>
		/// <param name="builder">The builder.</param>
		/// <param name="del">The handler.</param>
		/// <returns>The builder, for chaining.</returns>
		public static ITizenLifecycleBuilder OnAppControlReceived(this ITizenLifecycleBuilder builder, TizenLifecycle.OnAppControlReceived del) =>
			builder.OnEvent(nameof(TizenLifecycle.OnAppControlReceived), del);

		/// <summary>Registers an <see cref="TizenLifecycle.OnDeviceOrientationChanged"/> handler.</summary>
		/// <param name="builder">The builder.</param>
		/// <param name="del">The handler.</param>
		/// <returns>The builder, for chaining.</returns>
		public static ITizenLifecycleBuilder OnDeviceOrientationChanged(this ITizenLifecycleBuilder builder, TizenLifecycle.OnDeviceOrientationChanged del) =>
			builder.OnEvent(nameof(TizenLifecycle.OnDeviceOrientationChanged), del);

		/// <summary>Registers an <see cref="TizenLifecycle.OnLocaleChanged"/> handler.</summary>
		/// <param name="builder">The builder.</param>
		/// <param name="del">The handler.</param>
		/// <returns>The builder, for chaining.</returns>
		public static ITizenLifecycleBuilder OnLocaleChanged(this ITizenLifecycleBuilder builder, TizenLifecycle.OnLocaleChanged del) =>
			builder.OnEvent(nameof(TizenLifecycle.OnLocaleChanged), del);

		/// <summary>Registers an <see cref="TizenLifecycle.OnLowBattery"/> handler.</summary>
		/// <param name="builder">The builder.</param>
		/// <param name="del">The handler.</param>
		/// <returns>The builder, for chaining.</returns>
		public static ITizenLifecycleBuilder OnLowBattery(this ITizenLifecycleBuilder builder, TizenLifecycle.OnLowBattery del) =>
			builder.OnEvent(nameof(TizenLifecycle.OnLowBattery), del);

		/// <summary>Registers an <see cref="TizenLifecycle.OnLowMemory"/> handler.</summary>
		/// <param name="builder">The builder.</param>
		/// <param name="del">The handler.</param>
		/// <returns>The builder, for chaining.</returns>
		public static ITizenLifecycleBuilder OnLowMemory(this ITizenLifecycleBuilder builder, TizenLifecycle.OnLowMemory del) =>
			builder.OnEvent(nameof(TizenLifecycle.OnLowMemory), del);

		/// <summary>Registers an <see cref="TizenLifecycle.OnRegionFormatChanged"/> handler.</summary>
		/// <param name="builder">The builder.</param>
		/// <param name="del">The handler.</param>
		/// <returns>The builder, for chaining.</returns>
		public static ITizenLifecycleBuilder OnRegionFormatChanged(this ITizenLifecycleBuilder builder, TizenLifecycle.OnRegionFormatChanged del) =>
			builder.OnEvent(nameof(TizenLifecycle.OnRegionFormatChanged), del);

		/// <summary>Registers an <see cref="TizenLifecycle.OnMauiContextCreated"/> handler.</summary>
		/// <param name="builder">The builder.</param>
		/// <param name="del">The handler.</param>
		/// <returns>The builder, for chaining.</returns>
		public static ITizenLifecycleBuilder OnMauiContextCreated(this ITizenLifecycleBuilder builder, TizenLifecycle.OnMauiContextCreated del) =>
			builder.OnEvent(nameof(TizenLifecycle.OnMauiContextCreated), del);

		static ITizenLifecycleBuilder OnEvent<TDelegate>(this ITizenLifecycleBuilder builder, string eventName, TDelegate del)
			where TDelegate : Delegate
		{
			ArgumentNullException.ThrowIfNull(builder);

			builder.AddEvent(eventName, del);
			return builder;
		}
	}
}
