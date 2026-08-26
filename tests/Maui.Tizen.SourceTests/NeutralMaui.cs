using System.Collections;
using System.Reflection;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Reflection over the real MAUI assemblies, which are the authority for what a handler's mapper
/// is supposed to cover. Nothing here is hand-transcribed from upstream source.
/// </summary>
public static class NeutralMaui
{
	public static readonly Assembly Core = typeof(Microsoft.Maui.IView).Assembly;
	public static readonly Assembly Controls = typeof(Microsoft.Maui.Controls.Element).Assembly;

	/// <summary>Every public type name declared by the MAUI assemblies.</summary>
	public static IReadOnlySet<string> PublicTypeNames { get; } =
		new[] { Core, Controls }
			.SelectMany(a => a.GetExportedTypes())
			.Select(t => t.Name)
			.ToHashSet(StringComparer.Ordinal);

	/// <summary>Keys contributed by the shared view mapper, which every view handler chains.</summary>
	public static IReadOnlySet<string> ViewMapperKeys { get; } = LoadViewMapperKeys();

	public static Type? FindHandler(string name) =>
		Core.GetExportedTypes().FirstOrDefault(t => t.Name == name)
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

		if (getKeys?.Invoke(mapper, null) is not IEnumerable values)
		{
			return Array.Empty<string>();
		}

		return values.Cast<object?>().Select(v => v?.ToString() ?? string.Empty).ToList();
	}

	static IReadOnlySet<string> LoadViewMapperKeys()
	{
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
