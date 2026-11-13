// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.GitBranchStateCache.Tests.Fakes;

using System.IO.Abstractions;
using System.Reflection;

/// <summary>
/// Wraps a filesystem so that one directory behaves as a directory the service user can't read, such
/// as the root-only <c>lost+found</c> on an ext4 volume.
/// </summary>
/// <remarks>
/// A mock filesystem can't model Unix permissions, so this models what the runtime does instead: a
/// recursive walk that reaches the directory throws <see cref="UnauthorizedAccessException"/>, unless
/// it was asked to skip what it can't read with <see cref="EnumerationOptions.IgnoreInaccessible"/>.
/// The <see cref="SearchOption"/> overloads never skip it, as they use
/// <see cref="EnumerationOptions"/>' compatible defaults.
/// </remarks>
internal static class UnreadableDirectoryFileSystem
{
	/// <summary>
	/// Wraps <paramref name="inner"/> so that <paramref name="unreadable"/> can't be read.
	/// </summary>
	/// <param name="inner">The filesystem that holds the files.</param>
	/// <param name="unreadable">The directory to refuse.</param>
	/// <returns>The wrapped filesystem.</returns>
	public static IFileSystem Wrap(IFileSystem inner, string unreadable)
	{
		IDirectory directory = DispatchProxy.Create<IDirectory, DirectoryProxy>();
		((DirectoryProxy)directory).Configure(inner.Directory, unreadable);

		IFileSystem fileSystem = DispatchProxy.Create<IFileSystem, FileSystemProxy>();
		((FileSystemProxy)fileSystem).Configure(inner, directory);
		return fileSystem;
	}

	/// <summary>Forwards everything to the inner filesystem except its <see cref="IFileSystem.Directory"/>.</summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "DispatchProxy.Create instantiates it.")]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1852", Justification = "DispatchProxy derives from it, so it can't be sealed.")]
	public class FileSystemProxy : DispatchProxy
	{
		private IFileSystem _inner = null!;
		private IDirectory _directory = null!;

		internal void Configure(IFileSystem inner, IDirectory directory)
		{
			_inner = inner;
			_directory = directory;
		}

		/// <inheritdoc />
		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
			targetMethod!.Name == "get_Directory"
				? _directory
				: Forward(targetMethod, _inner, args);
	}

	/// <summary>Refuses recursive walks that reach the unreadable directory.</summary>
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "DispatchProxy.Create instantiates it.")]
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1852", Justification = "DispatchProxy derives from it, so it can't be sealed.")]
	public class DirectoryProxy : DispatchProxy
	{
		private IDirectory _inner = null!;
		private string _unreadable = null!;

		internal void Configure(IDirectory inner, string unreadable)
		{
			_inner = inner;
			_unreadable = unreadable;
		}

		/// <inheritdoc />
		protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
		{
			if (targetMethod!.Name is "GetDirectories" or "GetFiles" or "GetFileSystemEntries"
				&& args is { Length: 3 } && args[0] is string path && Reaches(path))
			{
				bool skipsUnreadable = args[2] is EnumerationOptions { IgnoreInaccessible: true };
				bool recurses = args[2] is SearchOption.AllDirectories
					or EnumerationOptions { RecurseSubdirectories: true };

				if (recurses && !skipsUnreadable)
				{
					throw new UnauthorizedAccessException($"Access to the path '{_unreadable}' is denied.");
				}

				string[] found = (string[])Forward(targetMethod, _inner, args)!;
				return found.Where(entry => !IsInside(entry)).ToArray();
			}

			return Forward(targetMethod, _inner, args);
		}

		private bool Reaches(string path) =>
			IsInside(path)
			|| _unreadable.StartsWith(Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

		private bool IsInside(string path) =>
			path == _unreadable || path.StartsWith(_unreadable + Path.DirectorySeparatorChar, StringComparison.Ordinal);
	}

	private static object? Forward(MethodInfo method, object target, object?[]? args)
	{
		try
		{
			return method.Invoke(target, args);
		}
		catch (TargetInvocationException failure) when (failure.InnerException is not null)
		{
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.InnerException).Throw();
			throw;
		}
	}
}
