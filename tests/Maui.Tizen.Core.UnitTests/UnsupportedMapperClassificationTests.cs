// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Maui;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Keeps unsupported classifications equal to direct and delegated terminal no-op bodies.
	/// </summary>
	public class UnsupportedMapperClassificationTests
	{
		static readonly Regex MapperEntry = new(
			@"\[(?:nameof\([\w.]*?(\w+)\)|""(\w+)"")\]\s*=\s*(Map\w+)",
			RegexOptions.Compiled);

		[Fact]
		public void MapperTerminalsMatchUnsupportedClassification()
		{
			var expected = UnsupportedMapperMappings.All
				.Select(ToIdentity)
				.Order(StringComparer.Ordinal)
				.ToArray();
			var observed = FindUnsupportedMappings()
				.Select(ToIdentity)
				.Order(StringComparer.Ordinal)
				.ToArray();

			Assert.Equal(expected, observed);
		}

		[Fact]
		public void EveryUnsupportedMappingIsReachableAndHasEvidence()
		{
			foreach (var mapping in UnsupportedMapperMappings.All)
			{
				Assert.False(string.IsNullOrWhiteSpace(mapping.Evidence));

				var type = typeof(IView).Assembly.GetType(
					$"Microsoft.Maui.Platforms.Tizen.Handlers.{mapping.Owner}")
					?? typeof(Microsoft.Maui.Platforms.Tizen.Handlers.TizenButtonHandler).Assembly.GetType(
						$"Microsoft.Maui.Platforms.Tizen.Handlers.{mapping.Owner}");

				Assert.NotNull(type);

				if (mapping.Kind == "command")
				{
					Assert.NotNull(TizenControlHandlers.GetCommandMapperCommand(type!, mapping.Key));
					continue;
				}

				var mapper = type!
					.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
					.Select(field => field.GetValue(null) as IPropertyMapper)
					.FirstOrDefault(candidate => candidate?.GetKeys().Contains(mapping.Key) == true);

				Assert.True(
					mapper is not null,
					$"{mapping.Owner}.{mapping.Key} is not reachable from a public property mapper.");

				var mapperSource = ReadHandlerSource(mapping.Owner);
				var mapperBody = GetMethodBody(StripComments(mapperSource), mapping.Method);

				if (mapping.TerminalMethod is null)
				{
					Assert.True(
						string.IsNullOrWhiteSpace(mapperBody),
						$"{mapping.Owner}.{mapping.Method} gained behavior but is still unsupported.");
					continue;
				}

				Assert.Contains(mapping.TerminalMethod, mapperBody, StringComparison.Ordinal);

				var terminalPath = Path.Combine(
					TestRepositoryPaths.Root,
					"src",
					"Maui.Tizen.Core",
					"Platform",
					"Tizen",
					mapping.TerminalFile!);
				Assert.True(
					IsCompiledTerminal(mapping.TerminalFile!),
					$"{mapping.TerminalFile} is classified as a terminal but is not in the product compile closure.");
				var terminalSource = StripComments(File.ReadAllText(terminalPath));
				var terminalBody = GetMethodBody(terminalSource, mapping.TerminalMethod);

				Assert.True(
					string.IsNullOrWhiteSpace(terminalBody),
					$"{mapping.TerminalFile}.{mapping.TerminalMethod} gained behavior but " +
					$"{mapping.Owner}.{mapping.Key} is still unsupported.");
			}
		}

		static IEnumerable<UnsupportedMapperMappings.UnsupportedMapping> FindUnsupportedMappings()
		{
			var handlers = Path.Combine(
				TestRepositoryPaths.Root,
				"src",
				"Maui.Tizen.Core",
				"Handlers");

			foreach (var file in Directory.EnumerateFiles(handlers, "Tizen*.cs"))
			{
				var owner = Path.GetFileNameWithoutExtension(file);
				var source = File.ReadAllText(file);
				var stripped = StripComments(source);

				foreach (Match entry in MapperEntry.Matches(source))
				{
					var key = entry.Groups[1].Success ? entry.Groups[1].Value : entry.Groups[2].Value;
					var method = entry.Groups[3].Value;
					var mapperBody = GetMethodBody(stripped, method);

					var kind = source.LastIndexOf("CommandMapper", entry.Index, StringComparison.Ordinal) >
						source.LastIndexOf("PropertyMapper", entry.Index, StringComparison.Ordinal)
						? "command"
						: "property";

					if (string.IsNullOrWhiteSpace(mapperBody))
					{
						yield return new(owner, key, method, kind, Evidence: string.Empty);
						continue;
					}

					foreach (Match terminal in Regex.Matches(mapperBody, @"\b(Update\w+)\s*\("))
					{
						var terminalMethod = terminal.Groups[1].Value;
						var terminalFile = ResolveTerminalFile(owner, mapperBody, terminalMethod);

						if (terminalFile is null)
							continue;

						var terminalPath = Path.Combine(
							TestRepositoryPaths.Root,
							"src",
							"Maui.Tizen.Core",
							"Platform",
							"Tizen",
							terminalFile);
						var terminalSource = StripComments(File.ReadAllText(terminalPath));

						if (!HasMethod(terminalSource, terminalMethod))
							continue;

						if (!string.IsNullOrWhiteSpace(GetMethodBody(terminalSource, terminalMethod)))
							continue;

						yield return new(
							owner,
							key,
							method,
							kind,
							Evidence: string.Empty,
							terminalFile,
							terminalMethod);
					}
				}
			}
		}

		static string GetMethodBody(string strippedSource, string method)
		{
			var declaration = Regex.Match(
				strippedSource,
				$@"\bstatic\s+[\w<>,?.\[\]\s]+\b{Regex.Escape(method)}\s*\(");

			Assert.True(declaration.Success, $"Could not find mapper method {method}.");

			var expressionBody = strippedSource.IndexOf("=>", declaration.Index + declaration.Length, StringComparison.Ordinal);
			var openBrace = strippedSource.IndexOf('{', declaration.Index + declaration.Length);

			if (expressionBody >= 0 && (openBrace < 0 || expressionBody < openBrace))
			{
				var semicolon = strippedSource.IndexOf(';', expressionBody);
				Assert.True(semicolon >= 0, $"Unterminated expression body for mapper method {method}.");
				return strippedSource[(expressionBody + 2)..semicolon];
			}

			Assert.True(openBrace >= 0, $"Could not find body for mapper method {method}.");

			var depth = 0;

			for (var i = openBrace; i < strippedSource.Length; i++)
			{
				switch (strippedSource[i])
				{
					case '{':
						depth++;
						break;
					case '}':
						depth--;

						if (depth == 0)
							return strippedSource[(openBrace + 1)..i];
						break;
				}
			}

			throw new InvalidOperationException($"Unterminated body for mapper method {method}.");
		}

		static bool HasMethod(string strippedSource, string method) =>
			Regex.IsMatch(
				strippedSource,
				$@"\bstatic\s+[\w<>,?.\[\]\s]+\b{Regex.Escape(method)}\s*\(");

		static string? ResolveTerminalFile(string owner, string mapperBody, string terminalMethod)
		{
			string fileName;

			if (owner == "TizenViewMappers")
				fileName = "TizenPlatformExtensions.cs";
			else if (mapperBody.Contains($".Entry.{terminalMethod}", StringComparison.Ordinal))
				fileName = "TizenEntryExtensions.cs";
			else
				fileName = owner.Replace("Handler", "Extensions", StringComparison.Ordinal) + ".cs";

			var path = Path.Combine(
				TestRepositoryPaths.Root,
				"src",
				"Maui.Tizen.Core",
				"Platform",
				"Tizen",
				fileName);

			return File.Exists(path) && IsCompiledTerminal(fileName) ? fileName : null;
		}

		static bool IsCompiledTerminal(string fileName)
		{
			var sources = File.ReadAllText(Path.Combine(
				TestRepositoryPaths.Root,
				"eng",
				"Maui.Tizen.Core.Sources.props"));

			return sources.Contains(
				$"Platform/Tizen/{fileName}",
				StringComparison.Ordinal);
		}

		static string ReadHandlerSource(string owner) =>
			File.ReadAllText(Path.Combine(
				TestRepositoryPaths.Root,
				"src",
				"Maui.Tizen.Core",
				"Handlers",
				owner + ".cs"));

		static string StripComments(string source)
		{
			var withoutBlocks = Regex.Replace(
				source,
				@"/\*.*?\*/",
				match => new string(' ', match.Length),
				RegexOptions.Singleline);

			return Regex.Replace(
				withoutBlocks,
				@"//[^\r\n]*",
				match => new string(' ', match.Length));
		}

		static string ToIdentity(UnsupportedMapperMappings.UnsupportedMapping mapping) =>
			$"{mapping.Owner}|{mapping.Kind}|{mapping.Key}|{mapping.Method}|" +
			$"{mapping.TerminalFile}|{mapping.TerminalMethod}";
	}
}
