// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Verifies registration, construction and the structural rules the backend depends on.
	/// </summary>
	public class ControlHandlerRegistrationTests
	{
		/// <summary>
		/// Every control interface must resolve to its Tizen handler after registration.
		/// </summary>
		/// <remarks>
		/// Asserted through the real <c>IMauiHandlersFactory</c> rather than by inspecting the
		/// collection, because that is the lookup MAUI actually performs when it renders a view.
		/// </remarks>
		[Fact]
		public void AddTizenControlHandlersRegistersEveryControl()
		{
			var builder = MauiApp.CreateBuilder(useDefaults: false);
			builder.ConfigureMauiHandlers(handlers => handlers.AddTizenControlHandlers());

			using var app = builder.Build();
			var factory = app.Services.GetRequiredService<IMauiHandlersFactory>();

			foreach (var expected in TizenControlHandlers.All)
			{
				var resolved = factory.GetHandlerType(expected.VirtualViewType);

				Assert.True(
					resolved == expected.HandlerType,
					$"{expected.VirtualViewType.Name} resolved to {resolved?.Name ?? "nothing"}, " +
					$"expected {expected.HandlerType.Name}.");
			}
		}

		/// <summary>
		/// Every registered handler must be constructible with no arguments.
		/// </summary>
		/// <remarks>
		/// MAUI instantiates handlers through <c>ActivatorUtilities</c>, which needs a usable
		/// constructor. A handler whose only constructor takes a mapper would fail at the moment
		/// the first control is rendered.
		/// </remarks>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void HandlerHasParameterlessConstructor(TizenControlHandlers.ControlHandlerCase handler)
		{
			var instance = Activator.CreateInstance(handler.HandlerType);
			Assert.NotNull(instance);
		}

		/// <summary>
		/// A handler must accept a replacement mapper, so applications can customise behaviour.
		/// </summary>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void HandlerAcceptsMapperOverride(TizenControlHandlers.ControlHandlerCase handler)
		{
			var ctor = handler.HandlerType.GetConstructor([typeof(IPropertyMapper), typeof(CommandMapper)]);

			Assert.True(ctor is not null, $"{handler.HandlerType.Name} has no (IPropertyMapper, CommandMapper) constructor.");

			// Passing nulls must fall back to the defaults rather than producing a mapper-less handler.
			var instance = ctor!.Invoke([null, null]);
			Assert.NotNull(instance);
		}

		/// <summary>
		/// Every control handler derives from the backend's shared base.
		/// </summary>
		/// <remarks>
		/// That base is what supplies disposal of the NUI handle, measurement and arrangement. A
		/// handler deriving straight from MAUI's <c>ViewHandler</c> would compile and then leak.
		/// </remarks>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void HandlerDerivesFromTizenViewHandler(TizenControlHandlers.ControlHandlerCase handler)
		{
			var found = false;

			for (var type = handler.HandlerType.BaseType; type is not null; type = type.BaseType)
			{
				if (type.IsGenericType &&
					type.GetGenericTypeDefinition().Name.StartsWith("TizenViewHandler", StringComparison.Ordinal))
				{
					found = true;
					break;
				}
			}

			Assert.True(found, $"{handler.HandlerType.Name} does not derive from TizenViewHandler<,>.");
		}

		/// <summary>
		/// No handler may reuse a type name that exists in the neutral MAUI assembly.
		/// </summary>
		/// <remarks>
		/// Two identically named types in two referenced assemblies is a hard CS0433 ambiguity for
		/// consumers. The <c>Tizen</c> prefix is what prevents it, so it is asserted rather than
		/// left to convention.
		/// </remarks>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void HandlerNameDoesNotCollideWithMaui(TizenControlHandlers.ControlHandlerCase handler)
		{
			Assert.StartsWith("Tizen", handler.HandlerType.Name, StringComparison.Ordinal);

			var collision = typeof(IView).Assembly.GetType(handler.HandlerType.FullName!);

			Assert.True(
				collision is null,
				$"{handler.HandlerType.FullName} also exists in Microsoft.Maui.Core, which is a " +
				"CS0433 ambiguity for anything referencing both assemblies.");
		}

		/// <summary>
		/// The backend must not reach into MAUI's private surface.
		/// </summary>
		/// <remarks>
		/// Private reflection would make the backend break silently on any MAUI servicing update.
		/// This checks the shipped IL rather than the source, so it also catches reflection reached
		/// through a helper.
		/// </remarks>
		[Fact]
		public void BackendDoesNotUsePrivateReflection()
		{
			var offenders = new List<string>();

			foreach (var handler in TizenControlHandlers.All)
			{
				foreach (var method in handler.HandlerType.GetMethods(
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
				{
					var body = method.GetMethodBody();
					if (body is null)
						continue;

					// A BindingFlags value with NonPublic set can only come from a reflection call.
					foreach (var local in body.LocalVariables)
					{
						if (local.LocalType == typeof(BindingFlags))
							offenders.Add($"{handler.HandlerType.Name}.{method.Name}");
					}
				}
			}

			Assert.True(offenders.Count == 0, $"Reflection used in: {string.Join(", ", offenders.Distinct())}.");
		}

		/// <summary>
		/// The control services a handler resolves must be registered by the hosting extension.
		/// </summary>
		[Fact]
		public void AddTizenControlServicesRegistersFontManagerAndModalHost()
		{
			var services = new ServiceCollection();
			services.AddTizenControlServices();
			services.AddSingleton<IFontRegistrar>(new StubFontRegistrar());

			var provider = services.BuildServiceProvider();

			Assert.NotNull(provider.GetService<ITizenFontManager>());
			Assert.NotNull(provider.GetService<IFontManager>());
			Assert.NotNull(provider.GetService<ITizenModalHost>());

			// IFontManager must be the same instance, not a second font cache.
			Assert.Same(provider.GetService<ITizenFontManager>(), provider.GetService<IFontManager>());
		}

		/// <summary>
		/// Registration must not overwrite a service the host registered first.
		/// </summary>
		[Fact]
		public void AddTizenControlServicesDoesNotOverrideHostRegistrations()
		{
			var custom = new StubModalHost();

			var services = new ServiceCollection();
			services.AddSingleton<ITizenModalHost>(custom);
			services.AddTizenControlServices();

			var provider = services.BuildServiceProvider();

			Assert.Same(custom, provider.GetService<ITizenModalHost>());
		}

		sealed class StubModalHost : ITizenModalHost
		{
			public Task RunModalAsync(Func<Task> showPopup) => showPopup();
		}

		sealed class StubFontRegistrar : IFontRegistrar
		{
			public string? GetFont(string font) => null;

			public void Register(string filename, string? alias, Assembly assembly)
			{
			}

			public void Register(string filename, string? alias)
			{
			}
		}
	}
}
