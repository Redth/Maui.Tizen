using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.LifecycleEvents;

namespace Microsoft.Maui.Platforms.Tizen.LifecycleEvents
{
	/// <summary>
	/// Raises lifecycle events registered through <see cref="ILifecycleEventService"/>.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.LifecycleEvents.LifecycleEventServiceExtensions</c> in
	/// dotnet/maui. The public members of that class take an <see cref="ILifecycleEventService"/>,
	/// but the <see cref="IServiceProvider"/> overloads a platform backend actually needs
	/// (<c>InvokeLifecycleEvents</c>, <c>GetLifecycleEventDelegates</c>) are <c>internal</c>. See
	/// docs/net11-status.md ("Required public MAUI API gaps").
	/// </remarks>
	public static class TizenLifecycleEventExtensions
	{
		/// <summary>
		/// Invokes every registered delegate for the event named after <typeparamref name="TDelegate"/>.
		/// </summary>
		/// <typeparam name="TDelegate">The lifecycle delegate type.</typeparam>
		/// <param name="services">The service provider.</param>
		/// <param name="action">Callback that invokes a single delegate.</param>
		public static void InvokeTizenLifecycleEvents<TDelegate>(this IServiceProvider? services, Action<TDelegate> action)
			where TDelegate : Delegate
		{
			if (services is null || action is null)
				return;

			foreach (var del in services.GetTizenLifecycleEventDelegates<TDelegate>())
				action(del);
		}

		/// <summary>
		/// Gets every registered delegate for a lifecycle event.
		/// </summary>
		/// <typeparam name="TDelegate">The lifecycle delegate type.</typeparam>
		/// <param name="services">The service provider.</param>
		/// <param name="eventName">
		/// The event name; defaults to the delegate type's name, matching MAUI's convention.
		/// </param>
		/// <returns>The registered delegates.</returns>
		public static IEnumerable<TDelegate> GetTizenLifecycleEventDelegates<TDelegate>(
			this IServiceProvider? services,
			string? eventName = null)
			where TDelegate : Delegate
		{
			var lifecycleService = services?.GetService<ILifecycleEventService>();
			if (lifecycleService is null)
				yield break;

			eventName ??= typeof(TDelegate).Name;

			foreach (var del in lifecycleService.GetEventDelegates<TDelegate>(eventName))
				yield return del;
		}
	}
}
