#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using SkiaSharp;

namespace Maui.Tizen.Build.Tasks
{
	/// <summary>
	/// Colour parsing compatible with the <c>Color</c> / <c>DarkColor</c> metadata accepted by
	/// <c>MauiSplashScreen</c> in dotnet/maui. Hex values are handled by SkiaSharp, named values
	/// come from <see cref="SKColors"/> with the usual "grey" spelling aliases.
	/// </summary>
	static class TizenColorTable
	{
		static readonly Lazy<Dictionary<string, SKColor>> NamedColors = new Lazy<Dictionary<string, SKColor>>(BuildTable);

		static Dictionary<string, SKColor> BuildTable()
		{
			var colors = new Dictionary<string, SKColor>(StringComparer.OrdinalIgnoreCase);

			foreach (var field in typeof(SKColors).GetFields(BindingFlags.Public | BindingFlags.Static))
			{
				if (field.FieldType == typeof(SKColor) && field.GetValue(null) is SKColor color)
					colors[field.Name] = color;
			}

			AddAlias(colors, "DarkGrey", "DarkGray");
			AddAlias(colors, "DarkSlateGrey", "DarkSlateGray");
			AddAlias(colors, "DimGrey", "DimGray");
			AddAlias(colors, "Grey", "Gray");
			AddAlias(colors, "LightGrey", "LightGray");
			AddAlias(colors, "LightSlateGrey", "LightSlateGray");
			AddAlias(colors, "SlateGrey", "SlateGray");

			return colors;
		}

		static void AddAlias(Dictionary<string, SKColor> colors, string alias, string existing)
		{
			if (!colors.ContainsKey(alias) && colors.TryGetValue(existing, out var color))
				colors[alias] = color;
		}

		public static bool TryGetNamedColor(string name, out SKColor color)
			=> NamedColors.Value.TryGetValue(name, out color);

		public static SKColor? Parse(string? value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;

			var trimmed = value!.Trim();

			if (SKColor.TryParse(trimmed, out var parsed))
				return parsed;

			if (TryGetNamedColor(trimmed, out parsed))
				return parsed;

			return null;
		}
	}
}
