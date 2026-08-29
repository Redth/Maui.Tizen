using System.Collections;
using System.Reflection;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Reflection over the real MAUI assemblies, which are the authority for what a handler's mapper
/// is supposed to cover. Nothing here is hand-transcribed from upstream source.
/// </summary>
/// <remarks>
/// Every read happens after a real Controls host has been built. Mapper state is process-global and
/// <c>ConfigureControls</c> mutates it: <c>ViewHandler.ViewMapper</c> exposes 29 keys before the
/// host is built and 36 after. Without forcing the host first, the answer would depend on whether
/// some other test happened to build it earlier in the run — which is exactly the flake this guards
/// against, and how the parity manifest first came to be generated from an incomplete key set.
/// </remarks>
public static class NeutralMaui
{
	public static readonly Assembly Core = typeof(Microsoft.Maui.IView).Assembly;
	public static readonly Assembly Controls = typeof(Microsoft.Maui.Controls.Element).Assembly;

	/// <summary>Every public type name declared by the MAUI assemblies.</summary>
	public static IReadOnlySet<string> PublicTypeNames { get; } =
		Hosted(new[] { Core, Controls })
			.SelectMany(a => a.GetExportedTypes())
			.Select(t => t.Name)
			.ToHashSet(StringComparer.Ordinal);

	/// <summary>Keys contributed by the shared view mapper, which every view handler chains.</summary>
	public static IReadOnlySet<string> ViewMapperKeys { get; } = LoadViewMapperKeys();

	/// <summary>Forces the Controls host before any mapper state is read.</summary>
	static T Hosted<T>(T value)
	{
		ControlsHost.EnsureBuilt();
		return value;
	}

	public static Type? FindHandler(string name) =>
		Hosted(Core).GetExportedTypes().FirstOrDefault(t => t.Name == name)
		?? Controls.GetExportedTypes().FirstOrDefault(t => t.Name == name);

	/// <summary>Reads <c>GetKeys()</c> off a handler's static <paramref name="fieldName"/> mapper.</summary>
	public static IReadOnlyList<string> MapperKeys(Type handler, string fieldName)
	{
		var field = handler.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
		var mapper = field?.GetValue(null);

		return Keys(mapper);
	}

	static IReadOnlyList<string> Keys(object? mapper)
	{
		if (mapper is null)
		{
			return Array.Empty<string>();
		}

		var getKeys = mapper.GetType().GetMethod("GetKeys", Type.EmptyTypes);

		if (getKeys?.Invoke(mapper, null) is IEnumerable values)
			return values.Cast<object?>().Select(v => v?.ToString() ?? string.Empty).ToList();

		for (Type? type = mapper.GetType(); type is not null; type = type.BaseType)
		{
			var storage = type.GetField("_mapper", BindingFlags.NonPublic | BindingFlags.Instance)
				?? type.GetField("_commandMapper", BindingFlags.NonPublic | BindingFlags.Instance);
			if (storage?.GetValue(mapper) is IDictionary entries)
			{
				return entries.Keys.Cast<object?>()
					.Select(value => value?.ToString() ?? string.Empty)
					.ToList();
			}
		}

		return Array.Empty<string>();
	}

	static IReadOnlySet<string> LoadViewMapperKeys()
	{
		ControlsHost.EnsureBuilt();

		var viewHandler = Core.GetExportedTypes().First(t => t.FullName == "Microsoft.Maui.Handlers.ViewHandler");
		var keys = MapperKeys(viewHandler, "ViewMapper").ToHashSet(StringComparer.Ordinal);

		var element = Core.GetExportedTypes().FirstOrDefault(t => t.FullName == "Microsoft.Maui.Handlers.ElementHandler");

		if (element is not null)
		{
			foreach (var key in MapperKeys(element, "ElementMapper"))
			{
				keys.Add(key);
			}
		}

		return keys;
	}
}
