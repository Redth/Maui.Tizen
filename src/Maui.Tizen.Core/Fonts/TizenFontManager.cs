// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using System;
using System.Collections.Concurrent;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// The Tizen-typed font manager contract.
	/// </summary>
	/// <remarks>
	/// The neutral <c>Microsoft.Maui.IFontManager</c> only carries <c>DefaultFontSize</c>; the
	/// font-resolution members exist solely in each platform's own build of MAUI. This backend
	/// consumes the neutral assembly, so it declares the resolution contract itself.
	/// </remarks>
	public interface ITizenFontManager : IFontManager
	{
		/// <summary>Resolves a <see cref="Font"/> to a NUI font family name.</summary>
		string GetFont(Font font);

		/// <summary>Resolves a MAUI font family alias to a NUI font family name.</summary>
		string GetFontFamily(string? fontFamily);
	}

	/// <summary>
	/// Resolves MAUI font descriptions to the family names NUI understands.
	/// </summary>
	/// <remarks>
	/// Named with a <c>Tizen</c> prefix because <c>Microsoft.Maui.FontManager</c> already exists
	/// in the neutral assembly.
	/// </remarks>
	public class TizenFontManager : ITizenFontManager
	{
		readonly ConcurrentDictionary<(string? Family, float Size, FontSlant Slant), string> _fonts = new();
		readonly IFontRegistrar _fontRegistrar;

		public TizenFontManager(IFontRegistrar fontRegistrar)
		{
			ArgumentNullException.ThrowIfNull(fontRegistrar);
			_fontRegistrar = fontRegistrar;
		}

		/// <remarks>14sp, matching the Tizen platform default.</remarks>
		public double DefaultFontSize => 14;

		public string GetFont(Font font)
		{
			var size = font.Size <= 0 || double.IsNaN(font.Size)
				? (float)DefaultFontSize
				: (float)font.Size;

			return _fonts.GetOrAdd((font.Family, size, font.Slant), static (key, manager) => manager.ResolveFamily(key.Family), this);
		}

		public string GetFontFamily(string? fontFamily) => ResolveFamily(fontFamily);

		string ResolveFamily(string? family)
		{
			if (string.IsNullOrEmpty(family))
				return string.Empty;

			var cleansed = CleanseFontName(family);
			if (string.IsNullOrEmpty(cleansed))
				return string.Empty;

			// NUI takes a family name, not a PostScript name, so a trailing "-Bold"/"-Italic"
			// style suffix has to be stripped; weight and slant are applied separately through
			// FontAttributes.
			var index = cleansed.LastIndexOf('-');
			return index != -1 ? cleansed[..index] : cleansed;
		}

		string? CleanseFontName(string fontName)
		{
			// An explicitly registered alias always wins.
			if (_fontRegistrar.GetFont(fontName) is string registered)
				return registered;

			var fontFile = FontFile.FromString(fontName);

			if (!string.IsNullOrWhiteSpace(fontFile.Extension))
			{
				if (_fontRegistrar.GetFont(fontFile.FileNameWithExtension()) is string filePath)
					return filePath;
			}
			else
			{
				foreach (var ext in FontFile.Extensions)
				{
					if (_fontRegistrar.GetFont(fontFile.FileNameWithExtension(ext)) is string filePath)
						return filePath;
				}
			}

			return fontFile.PostScriptName;
		}
	}
}
