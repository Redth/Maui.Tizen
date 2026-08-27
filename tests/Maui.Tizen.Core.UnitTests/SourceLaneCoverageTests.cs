using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Pins the relationship between the owned source tree and the compile lanes.
	/// </summary>
	/// <remarks>
	/// Sources are listed explicitly in <c>eng/Maui.Tizen.Core.Sources.props</c> rather than
	/// globbed, because the repository also holds raw unported dotnet/maui sources that must not be
	/// compiled. The cost of that choice is that a newly added file is compiled by <em>nothing</em>
	/// and nobody finds out until someone tries to use it. These tests close that gap.
	/// </remarks>
	public class SourceLaneCoverageTests
	{
		static string RepositoryRoot
		{
			get
			{
				var dir = new DirectoryInfo(AppContext.BaseDirectory);
				while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Maui.Tizen.slnx")))
					dir = dir.Parent;

				return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
			}
		}

		static string SourcesProps => File.ReadAllText(
			Path.Combine(RepositoryRoot, "eng/Maui.Tizen.Core.Sources.props"));

		/// <summary>Every file named on a compile item, regardless of which item group.</summary>
		static string[] CompiledFileNames => Regex
			.Matches(SourcesProps, @"Include=""[^""]*?([A-Za-z0-9_.]+\.cs)""")
			.Select(m => m.Groups[1].Value)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		const string OwnedNamespacePrefix = "Microsoft.Maui.Platforms.Tizen";

		static (string File, string Namespace)[] AllSources => Directory
			.EnumerateFiles(Path.Combine(RepositoryRoot, "src/Maui.Tizen.Core"), "*.cs", SearchOption.AllDirectories)
			.Select(path => (
				File: Path.GetFileName(path),
				Namespace: Regex.Match(File.ReadAllText(path), @"^namespace\s+([A-Za-z0-9_.]+)", RegexOptions.Multiline).Groups[1].Value))
			.Where(x => x.Namespace.Length > 0)
			.ToArray();

		/// <summary>
		/// Every .cs file this workstream owns, identified by its declared namespace.
		/// </summary>
		/// <remarks>
		/// Namespace is the right ownership signal rather than the file name: the repository also
		/// holds raw unported dotnet/maui sources, some of which are Tizen-prefixed
		/// (<c>TizenLifecycleExtensions.cs</c>) but declare neutral MAUI namespaces. Those must
		/// stay out of the compile lanes or they collide with the MAUI package as CS0433. The
		/// namespace boundary and the compile boundary are the same boundary.
		/// </remarks>
		static string[] OwnedSourceFiles => AllSources
			.Where(x => x.Namespace.StartsWith(OwnedNamespacePrefix, StringComparison.Ordinal))
			.Select(x => x.File)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		[Fact]
		public void EveryOwnedSourceFileIsCompiledBySomeLane()
		{
			// The gap the explicit source list creates: add a file, forget the props entry, and it
			// silently compiles nowhere while every lane stays green.
			var compiled = CompiledFileNames;

			var orphaned = OwnedSourceFiles
				.Where(f => !compiled.Contains(f, StringComparer.Ordinal))
				.OrderBy(f => f, StringComparer.Ordinal)
				.ToArray();

			Assert.Empty(orphaned);
		}

		[Fact]
		public void OwnedSourceTreeIsNotEmpty()
		{
			// Guards the guard: if the ownership heuristic ever stopped matching anything, the
			// test above would pass vacuously.
			Assert.True(OwnedSourceFiles.Length > 20, $"Only found {OwnedSourceFiles.Length} owned sources.");
		}

		[Fact]
		public void NoCompiledTypeCollidesWithTheReferencedMauiAssemblies()
		{
			// The real CS0433 invariant, tested directly.
			//
			// An earlier version of this test asserted the *proxy* "nothing in a neutral MAUI
			// namespace may be compiled". That was wrong, and it failed on legitimate code:
			// TizenLifecycle and TizenLifecycleBuilderExtensions deliberately declare
			// Microsoft.Maui.LifecycleEvents so that ConfigureLifecycleEvents(e => e.AddTizen(..))
			// resolves with no extra using, exactly as the in-repo backend did. Sharing a
			// namespace is fine. Sharing a *fully qualified type name* is what breaks consumers.
			//
			// This checks every type name the compile lanes declare against the types the MAUI
			// assemblies actually export, so it stays correct if MAUI ever reintroduces a Tizen
			// type - which is the scenario that would silently poison this package.
			var mauiAssemblies = new[]
			{
				typeof(Microsoft.Maui.IView).Assembly,
				typeof(Microsoft.Maui.Controls.Label).Assembly,
				typeof(Microsoft.Maui.Graphics.Color).Assembly,
				typeof(Microsoft.Maui.Hosting.MauiApp).Assembly,
			}.Distinct().ToArray();

			var compiledPaths = Regex
				.Matches(SourcesProps, @"Include=""\$\(MauiTizenCoreDir\)([^""]+\.cs)""")
				.Select(m => Path.Combine(RepositoryRoot, "src/Maui.Tizen.Core", m.Groups[1].Value))
				.Where(File.Exists)
				.ToArray();

			Assert.NotEmpty(compiledPaths);

			var collisions = compiledPaths
				.SelectMany(DeclaredTypeNames)
				.Distinct(StringComparer.Ordinal)
				.Where(fullName => mauiAssemblies.Any(a => a.GetType(fullName, throwOnError: false) is not null))
				.OrderBy(x => x, StringComparer.Ordinal)
				.ToArray();

			Assert.Empty(collisions);
		}

		/// <summary>Fully qualified names of the top-level types a source file declares.</summary>
		static string[] DeclaredTypeNames(string path)
		{
			var text = File.ReadAllText(path);
			var ns = Regex.Match(text, @"^namespace\s+([A-Za-z0-9_.]+)", RegexOptions.Multiline).Groups[1].Value;

			if (ns.Length == 0)
				return Array.Empty<string>();

			return Regex
				.Matches(text, @"^\t?(?:public|internal)\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+)*(?:class|struct|interface|enum|record)\s+([A-Za-z0-9_]+)", RegexOptions.Multiline)
				.Select(m => $"{ns}.{m.Groups[1].Value}")
				.ToArray();
		}

		[Fact]
		public void RefPackLaneAndProductDefineTheSameSymbols()
		{
			// Drift here is invisible and expensive: a symbol defined only in the ref-pack lane
			// makes code compile there and vanish from the shipping assembly. PLATFORM was defined
			// in exactly that asymmetric way before this test existed.
			var refPack = File.ReadAllText(Path.Combine(
				RepositoryRoot, "tests/Maui.Tizen.Core.RefPackCompile/Maui.Tizen.Core.RefPackCompile.csproj"));

			var defines = Regex.Match(refPack, @"<DefineConstants>([^<]*)</DefineConstants>").Groups[1].Value;

			var symbols = defines
				.Split(';', StringSplitOptions.RemoveEmptyEntries)
				.Select(x => x.Trim())
				.Where(x => !x.StartsWith("$(", StringComparison.Ordinal))
				.ToArray();

			// TIZEN is what the product's own TFM defines via Directory.Build.targets. Anything
			// else added here would exist only in this lane.
			Assert.Equal(new[] { "TIZEN" }, symbols);
		}
	}
}
