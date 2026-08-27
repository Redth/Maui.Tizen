// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// The TizenFX half of the embedded font loader. Compiled only into the lanes that have real
// TizenFX; the logic it serves lives in TizenEmbeddedFontLoader.cs, which is deliberately NUI-free.

using TApplication = global::Tizen.Applications.Application;
using TFontClient = global::Tizen.NUI.FontClient;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Resolves the application's directories and registers font directories with NUI's font client.
	/// </summary>
	public sealed class TizenPlatformFontDirectoryProvider : ITizenFontDirectoryProvider
	{
		/// <inheritdoc/>
		public string ResourceDirectory => TApplication.Current.DirectoryInfo.Resource;

		/// <inheritdoc/>
		public string DataDirectory => TApplication.Current.DirectoryInfo.Data;

		/// <inheritdoc/>
		public void AddCustomFontDirectory(string path) =>
			TFontClient.Instance.AddCustomFontDirectory(path);
	}
}
