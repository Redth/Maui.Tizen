using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Framework;
using MSBuildTask = Microsoft.Build.Utilities.Task;

using Maui.Tizen.Build.Tasks;

namespace Maui.Tizen.UnitTests;

/// <summary>Captures MSBuild diagnostics so tests can assert on warnings and errors.</summary>
public sealed class RecordingBuildEngine : IBuildEngine
{
	public List<string> Errors { get; } = new();

	public List<string> ErrorCodes { get; } = new();

	public List<string> Warnings { get; } = new();

	public List<string> Messages { get; } = new();

	public bool ContinueOnError => false;

	public int LineNumberOfTaskNode => 0;

	public int ColumnNumberOfTaskNode => 0;

	public string ProjectFileOfTaskNode => "test.proj";

	public void LogErrorEvent(BuildErrorEventArgs e)
	{
		Errors.Add(e.Message ?? string.Empty);
		ErrorCodes.Add(e.Code ?? string.Empty);
	}

	public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e.Message ?? string.Empty);

	public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e.Message ?? string.Empty);

	public void LogCustomEvent(CustomBuildEventArgs e) => Messages.Add(e.Message ?? string.Empty);

	public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs)
		=> throw new NotSupportedException();
}

public static class TaskExtensions
{
	public static RecordingBuildEngine UseRecordingEngine(this MSBuildTask task)
	{
		var engine = new RecordingBuildEngine();
		task.BuildEngine = engine;
		return engine;
	}

	public static string AllWarnings(this RecordingBuildEngine engine) => string.Join(Environment.NewLine, engine.Warnings);

	public static string AllErrors(this RecordingBuildEngine engine) => string.Join(Environment.NewLine, engine.Errors);
}
