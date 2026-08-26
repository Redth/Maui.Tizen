using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

/// <summary>
/// Guards the two integration invariants this backend depends on: the .NET 11 Essentials
/// DI-to-static-facade bridge owns facade assignment, and the MAUI dispatcher bridge owns
/// main-thread marshalling.
/// </summary>
public class FacadeAndMainThreadOwnershipTests
{
	static readonly Assembly Backend = typeof(TizenAppInfo).Assembly;

	/// <summary>
	/// The Essentials contracts MAUI actually bridges from DI onto their static facades, read out of
	/// <c>Microsoft.Maui.Hosting.EssentialsExtensions</c> itself.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is deliberately <b>not</b> a hand-maintained list. An earlier version of this test
	/// declared the expected set by hand and then asserted that this package's registrations were
	/// members of it - which is a tautology, because the same author wrote both sides. It would
	/// have passed just as happily if MAUI bridged nothing at all.
	/// </para>
	/// <para>
	/// Instead the bridge methods' IL is decoded and every generic argument passed to their generic
	/// helpers (<c>BridgeIfRegistered&lt;T&gt;</c>, <c>TrackAndSet&lt;T&gt;</c>,
	/// <c>GetService&lt;T&gt;</c>) is collected. If a future MAUI drops a contract from the bridge,
	/// this set shrinks and the dependent test fails.
	/// </para>
	/// </remarks>
	static readonly Lazy<IReadOnlySet<string>> MauiBridgedContracts = new(DiscoverBridgedContracts);

	static IReadOnlySet<string> DiscoverBridgedContracts()
	{
		var mauiAssembly = typeof(MauiApp).Assembly;

		var essentialsExtensions =
			mauiAssembly.GetType("Microsoft.Maui.Hosting.EssentialsExtensions", throwOnError: true)!;
		var essentialsInitializer =
			mauiAssembly.GetType("Microsoft.Maui.Hosting.EssentialsExtensions+EssentialsInitializer", throwOnError: true)!;

		var contracts = new SortedSet<string>(StringComparer.Ordinal);

		foreach (var declaringType in new[] { essentialsExtensions, essentialsInitializer })
		{
			var typeArguments = declaringType.GetGenericArguments();

			foreach (var method in declaringType.GetMethods(
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
				BindingFlags.Instance | BindingFlags.DeclaredOnly))
			{
				byte[]? il;
				try
				{
					il = method.GetMethodBody()?.GetILAsByteArray();
				}
				catch (Exception)
				{
					continue;
				}

				if (il is null)
					continue;

				CollectGenericInterfaceArguments(method, typeArguments, il, contracts);
			}
		}

		Assert.True(
			contracts.Count > 20,
			$"Only {contracts.Count} bridged contract(s) were discovered in MAUI's Essentials bridge. " +
			"That almost certainly means the IL scan stopped matching, not that MAUI stopped bridging.");

		return contracts;
	}

	static void CollectGenericInterfaceArguments(
		MethodInfo method,
		Type[] typeArguments,
		byte[] il,
		SortedSet<string> contracts)
	{
		const byte Call = 0x28;
		const byte CallVirt = 0x6F;

		var methodArguments = method.GetGenericArguments();

		for (var i = 0; i + 4 < il.Length; i++)
		{
			if (il[i] != Call && il[i] != CallVirt)
				continue;

			var token = BitConverter.ToInt32(il, i + 1);

			MemberInfo? member;
			try
			{
				member = method.Module.ResolveMember(token, typeArguments, methodArguments);
			}
			catch (Exception)
			{
				// Not every 0x28/0x6F byte is an opcode - some are operand bytes of a preceding
				// instruction - so unresolvable tokens are expected noise, not failures.
				continue;
			}

			if (member is not MethodInfo { IsGenericMethod: true } called)
				continue;

			foreach (var argument in called.GetGenericArguments())
			{
				if (argument.IsInterface &&
					argument.Namespace?.StartsWith("Microsoft.Maui", StringComparison.Ordinal) == true)
				{
					contracts.Add(argument.Name);
				}
			}
		}
	}

	[Fact]
	public void EveryRegisteredContractIsActuallyBridgedByMaui()
	{
		var bridged = MauiBridgedContracts.Value;

		var notBridged = TizenEssentialsRegistrationTests.ExpectedRegistrations.Keys
			.Select(t => t.Name)
			.Where(name => !bridged.Contains(name))
			.ToList();

		Assert.Empty(notBridged);
	}

	[Fact]
	public void GeocodingIsBridgedEvenThoughItIsRegisteredThroughAFactory() =>
		Assert.Contains(nameof(IGeocoding), MauiBridgedContracts.Value);

	[Fact]
	public void DoesNotCallSetDefaultOrSetCurrentOnAnyEssentialsFacade()
	{
		var forbidden = new[] { "SetDefault", "SetCurrent" };

		var offenders = ReferencedMemberNames()
			.Where(name => forbidden.Contains(name, StringComparer.Ordinal))
			.Distinct()
			.ToList();

		Assert.Empty(offenders);
	}

	[Fact]
	public void DoesNotUseReflectionToReachEssentialsFacadeBackingFields()
	{
		var reflectionEntryPoints = new[]
		{
			"GetField", "GetFields", "GetRuntimeField", "GetRuntimeFields", "InvokeMember",
		};

		var offenders = ReferencedMemberNames()
			.Where(name => reflectionEntryPoints.Contains(name, StringComparer.Ordinal))
			.Distinct()
			.ToList();

		Assert.Empty(offenders);
	}

	[Fact]
	public void DoesNotShipItsOwnMainThreadImplementation()
	{
		// MainThread marshalling is bridged from the registered IDispatcher by MAUI for
		// non in-box platforms, so this backend must not declare a MainThread type of its own...
		var mainThreadTypes = Backend.GetTypes()
			.Where(t => t.Name.Contains("MainThread", StringComparison.Ordinal))
			.ToList();

		Assert.Empty(mainThreadTypes);
	}

	[Fact]
	public void DoesNotTouchTheEcoreMainLoopDirectly()
	{
		// ...nor reach around the dispatcher into the EFL main loop, which is what the
		// in-box dotnet/maui MainThread.tizen.cs did.
		var offenders = ReferencedTypeNames()
			.Where(name => name.Contains("EcoreMainloop", StringComparison.Ordinal))
			.Distinct()
			.ToList();

		Assert.Empty(offenders);
	}

	[Fact]
	public void MarshalsThroughTheMauiMainThreadFacade()
	{
		// The bridge replaces MainThread's platform delegate, so calling into MainThread is the
		// correct way for this backend to reach the UI thread.
		Assert.Contains("MainThread", ReferencedTypeNames());
	}

	[Fact]
	public void EveryEventRaisingServiceMarshalsThroughMainThread()
	{
		// Sensor, connectivity and display events all originate on native Tizen threads. Each of
		// these types must hand the event back through MainThread rather than raising inline.
		var eventRaisingTypes = new[]
		{
			typeof(TizenAccelerometer),
			typeof(TizenBattery),
			typeof(TizenConnectivity),
			typeof(TizenDeviceDisplay),
			typeof(TizenSensorBase<>),
		};

		var notMarshalling = eventRaisingTypes
			.Where(type => !ReferencesMainThread(type))
			.Select(type => type.Name)
			.ToList();

		Assert.Empty(notMarshalling);
	}

	static bool ReferencesMainThread(Type type)
	{
		// Async methods and lambdas are compiled into nested state-machine/closure types, so the
		// declaring type alone does not contain the call. Scanning nested types too is what makes
		// this assertion meaningful for `async void`/`async Task` event pumps such as
		// TizenConnectivity.RefreshProfilesAsync.
		if (type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Any(ReferencesMainThreadCore))
			return true;

		return ReferencesMainThreadCore(type);
	}

	static bool ReferencesMainThreadCore(Type type)
	{
		foreach (var method in type.GetMethods(
			BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
			BindingFlags.Instance | BindingFlags.DeclaredOnly))
		{
			byte[]? il;
			try
			{
				il = method.GetMethodBody()?.GetILAsByteArray();
			}
			catch (Exception)
			{
				continue;
			}

			if (il is null)
				continue;

			for (var i = 0; i + 4 < il.Length; i++)
			{
				if (il[i] != 0x28 && il[i] != 0x6F)
					continue;

				try
				{
					if (method.Module.ResolveMember(BitConverter.ToInt32(il, i + 1)) is MethodInfo called &&
						called.DeclaringType?.Name == "MainThread")
					{
						return true;
					}
				}
				catch (Exception)
				{
					// Operand bytes misread as opcodes; see CollectGenericInterfaceArguments.
				}
			}
		}

		return false;
	}

	[Fact]
	public void ExposesGeocodingThroughTheTokenAwarePlatformContract() =>
		Assert.True(typeof(IPlatformGeocoding).IsAssignableFrom(typeof(TizenGeocoding)));

	static IEnumerable<string> ReferencedMemberNames()
	{
		using var stream = File.OpenRead(Backend.Location);
		using var peReader = new PEReader(stream);
		var reader = peReader.GetMetadataReader();

		foreach (var handle in reader.MemberReferences)
			yield return reader.GetString(reader.GetMemberReference(handle).Name);
	}

	static IEnumerable<string> ReferencedTypeNames()
	{
		using var stream = File.OpenRead(Backend.Location);
		using var peReader = new PEReader(stream);
		var reader = peReader.GetMetadataReader();

		foreach (var handle in reader.TypeReferences)
			yield return reader.GetString(reader.GetTypeReference(handle).Name);
	}
}
