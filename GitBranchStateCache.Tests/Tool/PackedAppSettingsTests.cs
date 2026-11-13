// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Tool;

using ktsu.GitBranchStateCache.Tool;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Installed as a dotnet tool, the host runs from whatever directory the user is in, so the
/// appsettings.json packed next to the tool has to be read from there rather than from the content
/// root (#145).
/// </summary>
[TestClass]
public sealed class PackedAppSettingsTests
{
	private string _workingDirectory = null!;
	private string _toolDirectory = null!;

	[TestInitialize]
	public void CreateDirectories()
	{
		string scratch = Path.Combine(Path.GetTempPath(), "gitbranchstatecache-appsettings-" + Guid.NewGuid().ToString("N"));
		_workingDirectory = Directory.CreateDirectory(Path.Combine(scratch, "cwd")).FullName;
		_toolDirectory = Directory.CreateDirectory(Path.Combine(scratch, "tool")).FullName;
	}

	[TestCleanup]
	public void DeleteDirectories() => Directory.Delete(Path.GetDirectoryName(_workingDirectory)!, recursive: true);

	/// <summary>Builds the host's configuration as the tool does, run from the stand-in working directory.</summary>
	private ConfigurationManager Build()
	{
		WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			Args = [],
			ApplicationName = "ktsu.GitBranchStateCache",
			ContentRootPath = _workingDirectory,
		});

		Program.AddPackedAppSettings(builder.Configuration, _toolDirectory);
		return builder.Configuration;
	}

	private static void Write(string directory, string level) =>
		File.WriteAllText(
			Path.Combine(directory, "appsettings.json"),
			$$"""{ "Logging": { "LogLevel": { "Microsoft.AspNetCore": "{{level}}" } } }""");

	[TestMethod]
	public void RunFromAnotherDirectory_ReadsThePackedFile()
	{
		Write(_toolDirectory, "Warning");

		Assert.AreEqual("Warning", Build()["Logging:LogLevel:Microsoft.AspNetCore"]);
	}

	[TestMethod]
	public void WorkingDirectoryFile_StillBeatsThePackedFile()
	{
		Write(_toolDirectory, "Warning");
		Write(_workingDirectory, "Debug");

		Assert.AreEqual("Debug", Build()["Logging:LogLevel:Microsoft.AspNetCore"]);
	}

	[TestMethod]
	public void RunFromTheToolDirectory_ReadsTheFileOnce()
	{
		// The container runs from the directory that holds the file, as the content root.
		Write(_workingDirectory, "Warning");
		WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			Args = [],
			ContentRootPath = _workingDirectory,
		});
		int before = builder.Configuration.Sources.Count;

		Program.AddPackedAppSettings(builder.Configuration, _workingDirectory);

		Assert.AreEqual(before, builder.Configuration.Sources.Count);
	}
}
