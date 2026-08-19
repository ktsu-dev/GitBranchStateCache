// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tool;

using System.CommandLine;
using ktsu.Essentials;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Entry point for the gitbranchstatecache host.
/// </summary>
/// <remarks>
/// This same binary is the dotnet tool payload and the container entrypoint, so the two cannot drift.
/// The flags exist for the tool: nobody running this on their own machine should have to type
/// <c>--GitBranchStateCache:MirrorRoot=</c>. In a container every value normally arrives through
/// environment variables instead, so every flag is optional and only overrides what configuration
/// already holds.
/// </remarks>
internal static class Program
{
	/// <summary>The configuration key the <c>--mirror-root</c> flag overrides.</summary>
	private const string MirrorRootKey = "GitBranchStateCache:MirrorRoot";

	private static async Task<int> Main(string[] args)
	{
		Option<int?> port = new("--port", "-p")
		{
			Description = "Port to listen on. Defaults to 8080.",
		};

		Option<string?> mirrorRoot = new("--mirror-root", "-m")
		{
			Description =
				"Directory to keep bare mirrors in. Defaults to a per-user application data directory.",
		};

		Option<string?> configFile = new("--config", "-c")
		{
			Description =
				"Path to a JSON configuration file to load. Layered over any appsettings.json in the working directory, and still overridden by the flags below.",
		};

		Option<string?> gitExecutable = new("--git")
		{
			Description = "The git executable to invoke. Resolved through PATH when it is a bare name.",
		};

		Option<string[]> upstreams = new("--upstream", "-u")
		{
			Description = "An upstream as name=url, for example github=https://github.com. Repeatable.",
			AllowMultipleArgumentsPerToken = false,
		};

		Option<string[]> allow = new("--allow", "-a")
		{
			Description =
				"A repository this upstream may mirror, as name=pattern, for example github=studio/game.git. Repeatable. Required at least once per upstream, and every pattern must name a literal path segment.",
			AllowMultipleArgumentsPerToken = false,
		};

		RootCommand root = new("A cross-branch state cache for Git repositories.")
		{
			port,
			configFile,
			mirrorRoot,
			gitExecutable,
			upstreams,
			allow,
		};

		root.SetAction(async (parseResult, cancellationToken) =>
		{
			Dictionary<string, string?> overrides = [];
			int listenPort = parseResult.GetValue(port) ?? 8080;
			string? configPath = null;

			if (parseResult.GetValue(configFile) is string requestedConfig)
			{
				configPath = Path.GetFullPath(requestedConfig);

				// Checked here rather than left to the configuration provider, which throws a
				// FileNotFoundException with a stack trace. Someone who mistyped a path should get one
				// line naming the path they gave.
				if (!File.Exists(configPath))
				{
					return await FailAsync($"No configuration file at '{configPath}'.").ConfigureAwait(false);
				}
			}

			if (parseResult.GetValue(mirrorRoot) is string root_)
			{
				overrides[MirrorRootKey] = root_;
			}

			if (parseResult.GetValue(gitExecutable) is string git)
			{
				overrides["GitBranchStateCache:GitExecutable"] = git;
			}

			if (!TryApplyUpstreams(parseResult.GetValue(upstreams), overrides, out string? invalidUpstream))
			{
				return await FailAsync(
					$"'{invalidUpstream}' is not a valid upstream. Use name=url, for example github=https://github.com.")
					.ConfigureAwait(false);
			}

			if (!TryApplyAllows(parseResult.GetValue(allow), overrides, out string? invalidAllow))
			{
				return await FailAsync(
					$"'{invalidAllow}' is not a valid allow entry. Use name=pattern, for example github=studio/game.git.")
					.ConfigureAwait(false);
			}

			return await RunAsync(overrides, listenPort, configPath, cancellationToken).ConfigureAwait(false);
		});

		return await root.Parse(args)
			.InvokeAsync(configuration: null, cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);
	}

	private static async Task<int> RunAsync(
		Dictionary<string, string?> overrides,
		int port,
		string? configPath,
		CancellationToken cancellationToken)
	{
		WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			// The command line is parsed by System.CommandLine, not by the configuration provider, so
			// the friendly flags above are not also read as configuration keys.
			Args = [],
			ApplicationName = "ktsu.GitBranchStateCache",
		});

		// Added after the builder's own sources so an explicitly named file beats an appsettings.json
		// that happens to be in the working directory, and before ApplyDefaults, which reads
		// configuration to decide what still needs a default and would otherwise not see this file.
		if (configPath is not null)
		{
			builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: false);
		}

		ApplyDefaults(builder.Configuration, overrides);
		builder.Configuration.AddInMemoryCollection(overrides);
		builder.Configuration["Kestrel:Endpoints:Http:Url"] = $"http://*:{port}";

		builder.Services.AddGitBranchStateCache(builder.Configuration);

		// Behind an ingress the request this service sees is not the one the client made. Nothing here
		// builds a URL from the request, so this exists for the client address in the logs rather than
		// for correctness of any response.
		builder.Services.Configure<ForwardedHeadersOptions>(options =>
		{
			options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
				| ForwardedHeaders.XForwardedProto
				| ForwardedHeaders.XForwardedHost;
			options.KnownIPNetworks.Clear();
			options.KnownProxies.Clear();
		});

		WebApplication app = builder.Build();

		app.UseForwardedHeaders();
		app.MapGitBranchStateCache();

		await app.RunAsync(cancellationToken).ConfigureAwait(false);
		return 0;
	}

	/// <summary>
	/// Fills in the values a local run should not have to supply.
	/// </summary>
	/// <remarks>
	/// Only the mirror root. There is deliberately no default for the upstreams or the repositories
	/// they may mirror, because a default for either would be this service deciding on its own what to
	/// clone onto a shared volume.
	/// </remarks>
	private static void ApplyDefaults(ConfigurationManager configuration, Dictionary<string, string?> overrides)
	{
		bool hasRoot = overrides.ContainsKey(MirrorRootKey)
			|| !string.IsNullOrWhiteSpace(configuration[MirrorRootKey]);

		if (!hasRoot)
		{
			overrides[MirrorRootKey] =
				UserDirectories.GetApplicationDataDirectory("ktsu", "GitBranchStateCache");
		}
	}

	/// <summary>
	/// Writes one line to standard error and reports the exit code to return.
	/// </summary>
	/// <param name="message">What was wrong with the arguments.</param>
	/// <returns>The failing exit code.</returns>
	private static async Task<int> FailAsync(string message)
	{
		await Console.Error.WriteLineAsync(message).ConfigureAwait(false);
		return 1;
	}

	/// <summary>
	/// Splits a repeatable <c>name=value</c> flag.
	/// </summary>
	/// <remarks>
	/// A separator at either end is refused rather than producing an empty name or an empty value,
	/// both of which would bind configuration that cannot work and fail later with a worse message.
	/// </remarks>
	/// <param name="entry">The flag value as typed.</param>
	/// <param name="name">The part before the separator.</param>
	/// <param name="value">The part after it.</param>
	/// <returns><see langword="true"/> when the entry had both halves.</returns>
	private static bool TrySplitPair(string entry, out string name, out string value)
	{
		name = string.Empty;
		value = string.Empty;

		int separator = entry.IndexOf('=', StringComparison.Ordinal);

		if (separator <= 0 || separator == entry.Length - 1)
		{
			return false;
		}

		name = entry[..separator];
		value = entry[(separator + 1)..];
		return true;
	}

	/// <summary>
	/// Binds every <c>--upstream</c> flag to configuration.
	/// </summary>
	/// <param name="entries">The flag values, or null when the flag was not given.</param>
	/// <param name="overrides">Configuration to add to.</param>
	/// <param name="invalid">The first entry that could not be read, when one could not.</param>
	/// <returns><see langword="true"/> when every entry was well formed.</returns>
	private static bool TryApplyUpstreams(
		string[]? entries,
		Dictionary<string, string?> overrides,
		out string? invalid)
	{
		invalid = null;

		foreach (string entry in entries ?? [])
		{
			if (!TrySplitPair(entry, out string name, out string url))
			{
				invalid = entry;
				return false;
			}

			overrides[$"GitBranchStateCache:Upstreams:{name}:BaseUrl"] = url;
		}

		return true;
	}

	/// <summary>
	/// Binds every <c>--allow</c> flag to configuration.
	/// </summary>
	/// <remarks>
	/// Indexed per upstream so repeating the flag appends rather than overwrites, which is what a
	/// repeatable option has to do to be useful.
	/// </remarks>
	/// <param name="entries">The flag values, or null when the flag was not given.</param>
	/// <param name="overrides">Configuration to add to.</param>
	/// <param name="invalid">The first entry that could not be read, when one could not.</param>
	/// <returns><see langword="true"/> when every entry was well formed.</returns>
	private static bool TryApplyAllows(
		string[]? entries,
		Dictionary<string, string?> overrides,
		out string? invalid)
	{
		invalid = null;
		Dictionary<string, int> counts = new(StringComparer.OrdinalIgnoreCase);

		foreach (string entry in entries ?? [])
		{
			if (!TrySplitPair(entry, out string name, out string pattern))
			{
				invalid = entry;
				return false;
			}

			int index = counts.TryGetValue(name, out int used) ? used : 0;
			counts[name] = index + 1;

			overrides[$"GitBranchStateCache:Upstreams:{name}:Repositories:{index}"] = pattern;
		}

		return true;
	}
}
