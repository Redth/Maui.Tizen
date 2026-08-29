// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Locates the repository root from the test assembly's output directory.
	/// </summary>
	/// <remarks>
	/// A local copy rather than a reference to <c>Maui.Tizen.UnitTests.RepositoryPaths</c>: that
	/// project is the repository-invariant suite, and making these two test assemblies depend on
	/// one another to share eight lines would couple them for no benefit.
	/// </remarks>
	public static class TestRepositoryPaths
	{
		static readonly Lazy<string> _root = new(Find);

		/// <summary>The repository root directory.</summary>
		public static string Root => _root.Value;

		static string Find()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);

			while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Maui.Tizen.slnx")))
				dir = dir.Parent;

			return dir?.FullName
				?? throw new InvalidOperationException(
					$"Could not locate the repository root above '{AppContext.BaseDirectory}'.");
		}
	}
}
