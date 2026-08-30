using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Maui.Authentication;
using Microsoft.Maui.Storage;

namespace Maui.Tizen.SourceTests;

public class MauiPublicApiBlockerTests
{
	[Fact]
	public void PasskeyResponsesHaveNoPublicConstructionOrFactorySurface()
	{
		Assert.Empty(typeof(PasskeyCreationResponse).GetConstructors());
		Assert.Empty(typeof(PasskeyAssertionResponse).GetConstructors());
		Assert.True(typeof(PasskeyCreationResponse).IsSealed);
		Assert.True(typeof(PasskeyAssertionResponse).IsSealed);

		var diagnostics = Compile(
			"""
			using Microsoft.Maui.Authentication;
			class Probe
			{
				PasskeyCreationResponse Create(string json) => new(json);
				PasskeyAssertionResponse Assert(string json) => new(json);
			}
			""");

		Assert.Contains(diagnostics, diagnostic =>
			diagnostic.Severity == DiagnosticSeverity.Error &&
			diagnostic.Id is "CS1729" or "CS0122");
	}

	[Fact]
	public void FileResultHasNoPublicPathOpenOverrideSeam()
	{
		var platformOpen = typeof(FileBase).GetMethod(
			"PlatformOpenReadAsync",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

		Assert.NotNull(platformOpen);
		Assert.True(platformOpen!.IsAssembly);

		var diagnostics = Compile(
			"""
			using System.IO;
			using System.Threading.Tasks;
			using Microsoft.Maui.Storage;
			class TizenFileResult : FileResult
			{
				public TizenFileResult(string path) : base(path) { }
				internal override Task<Stream> PlatformOpenReadAsync() => Task.FromResult<Stream>(File.OpenRead(FullPath));
			}
			""");

		Assert.Contains(diagnostics, diagnostic =>
			diagnostic.Severity == DiagnosticSeverity.Error &&
			diagnostic.Id == "CS0115");
	}

	static IReadOnlyList<Diagnostic> Compile(string source)
	{
		var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
			?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
			.Select(path => MetadataReference.CreateFromFile(path))
			.ToList() ?? [];
		trustedPlatformAssemblies.Add(MetadataReference.CreateFromFile(typeof(FileBase).Assembly.Location));

		return CSharpCompilation
			.Create(
				"PublicApiProbe",
				[CSharpSyntaxTree.ParseText(source)],
				trustedPlatformAssemblies,
				new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
			.GetDiagnostics();
	}
}
