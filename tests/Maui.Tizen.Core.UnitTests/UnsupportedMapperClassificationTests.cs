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
	/// Keeps the unsupported classification equal to the real explicit no-op mapper bodies.
	/// </summary>
	public class UnsupportedMapperClassificationTests
	{
		static readonly Regex MapperEntry = new(
			@"\[(?:nameof\([\w.]*?(\w+)\)|""(\w+)"")\]\s*=\s*(Map\w+)",
			RegexOptions.Compiled);

		[Fact]
		public void ExplicitEmptyMapperBodiesMatchUnsupportedClassification()
		{
			var expected = UnsupportedMapperMappings.All
				.Select(ToIdentity)
				.Order(StringComparer.Ordinal)
				.ToArray();
			var observed = FindExplicitEmptyMappings()
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
			}
		}

		static IEnumerable<UnsupportedMapperMappings.UnsupportedMapping> FindExplicitEmptyMappings()
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

					if (!MethodBodyIsEmpty(stripped, method))
						continue;

					var kind = source.LastIndexOf("CommandMapper", entry.Index, StringComparison.Ordinal) >
						source.LastIndexOf("PropertyMapper", entry.Index, StringComparison.Ordinal)
						? "command"
						: "property";

					yield return new(owner, key, method, kind, Evidence: string.Empty);
				}
			}
		}

		static bool MethodBodyIsEmpty(string strippedSource, string method)
		{
			var declaration = Regex.Match(
				strippedSource,
				$@"\bstatic\s+[\w<>,?.\[\]\s]+\b{Regex.Escape(method)}\s*\(");

			Assert.True(declaration.Success, $"Could not find mapper method {method}.");

			var openBrace = strippedSource.IndexOf('{', declaration.Index + declaration.Length);
			Assert.True(openBrace >= 0, $"Could not find body for mapper method {method}.");

			var expressionBody = strippedSource.IndexOf("=>", declaration.Index + declaration.Length, StringComparison.Ordinal);
			if (expressionBody >= 0 && expressionBody < openBrace)
				return false;

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
						{
							var body = strippedSource[(openBrace + 1)..i];
							return string.IsNullOrWhiteSpace(body);
						}
						break;
				}
			}

			throw new InvalidOperationException($"Unterminated body for mapper method {method}.");
		}

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
			$"{mapping.Owner}|{mapping.Kind}|{mapping.Key}|{mapping.Method}";
	}
}
