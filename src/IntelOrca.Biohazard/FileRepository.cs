using System;
using System.Collections.Generic;
using System.IO;

namespace IntelOrca.Biohazard
{
    public class FileRepository : IDisposable
    {
        private readonly List<RE3Archive> _re3Archives = new List<RE3Archive>();
        private readonly Dictionary<string, Func<Stream>> _re3Paths = new Dictionary<string, Func<Stream>>(StringComparer.OrdinalIgnoreCase);
        private readonly string _dataPath;
        private readonly string? _modPath;

        public string DataPath => _dataPath;
        public string ModPath => _modPath!;

        public FileRepository(string dataPath, string? modPath = null)
        {
            _dataPath = dataPath;
            _modPath = modPath;
        }

        public void Dispose()
        {
            foreach (var archive in _re3Archives)
            {
                archive.Dispose();
            }
            _re3Archives.Clear();
        }

        public string GetDataPath(string path)
        {
            return NormalizePath(Path.Combine(_dataPath, path));
        }

        public string GetModPath(string path)
        {
            if (_modPath == null)
                throw new InvalidOperationException("No mod path was specified for this repository.");
            return NormalizePath(Path.Combine(_modPath, path));
        }

        public void AddRE3Archive(string path)
        {
            var re3Archive = new RE3Archive(path);
            _re3Archives.Add(re3Archive);

            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            for (int i = 0; i < re3Archive.NumFiles; i++)
            {
                var path1 = Path.Combine(dir, re3Archive[i]);
                var path2 = NormalizePath(path1);
                var index = i;
                _re3Paths.Add(path2, () => new MemoryStream(re3Archive.GetFileContents(index)));
            }
        }

        public string[] GetFiles(string path)
        {
            var result = new List<string>();
            path = NormalizePath(path);
            foreach (var kvp in _re3Paths)
            {
                var filePath = kvp.Key;
                var directory = Path.GetDirectoryName(filePath);
                if (string.Equals(directory, path, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(filePath);
                }
            }
            return result.ToArray();
        }

        public bool Exists(string path)
        {
            var normalizedPath = NormalizePath(path);
            if (ResolvePathCaseInsensitive(normalizedPath) != null)
                return true;

            return _re3Paths.TryGetValue(normalizedPath, out _);
        }

        public Stream GetStream(string path)
        {
            var normalizedPath = NormalizePath(path);
            var resolvedPath = ResolvePathCaseInsensitive(normalizedPath);
            if (resolvedPath != null)
                return new FileStream(resolvedPath, FileMode.Open, FileAccess.Read);

            if (_re3Paths.TryGetValue(normalizedPath, out var getStream))
            {
                return getStream();
            }
            throw new IOException($"File does not exist: '{normalizedPath}'");
        }

        public byte[] GetBytes(string path)
        {
            using var fs = GetStream(path);
            var len = (int)fs.Length;
            var bytes = new byte[len];
            fs.Read(bytes, 0, len);
            return bytes;
        }

        public void Copy(string src, string dst)
        {
            if (File.Exists(src))
            {
                File.Copy(src, dst, true);
            }
            else
            {
                var stream = GetStream(src);
                using var fs = new FileStream(dst, FileMode.Create);
                stream.CopyTo(fs);
            }
        }

        public void CopyDirectory(string src, string dst)
        {
            var diSource = new DirectoryInfo(src);
            var diTarget = new DirectoryInfo(dst);
            diTarget.Create();
            CopyFilesRecursively(diSource, diTarget);
        }

        private static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target)
        {
            foreach (DirectoryInfo dir in source.GetDirectories())
                CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));
            foreach (FileInfo file in source.GetFiles())
                file.CopyTo(Path.Combine(target.FullName, file.Name), true);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var normalized = path
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);

            // Never trim the separator from a root path such as "/" or "C:\".
            var root = Path.GetPathRoot(normalized) ?? string.Empty;
            if (normalized.Length > root.Length)
            {
                normalized = normalized.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            }
            return normalized;
        }

        /// <summary>
        /// Resolves a path segment by segment with case-insensitive matching.
        /// Windows filesystems already do this; Linux does not, and the classic
        /// game data mixes file name casing (for example Room1061.rdt).
        /// </summary>
        private static string? ResolvePathCaseInsensitive(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (File.Exists(path))
                return path;

            try
            {
                var fullPath = Path.GetFullPath(path);
                var root = Path.GetPathRoot(fullPath);
                if (string.IsNullOrEmpty(root))
                    return null;

                var current = root;
                var relative = fullPath[root.Length..];
                var segments = relative.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries);

                foreach (var segment in segments)
                {
                    if (!Directory.Exists(current))
                        return null;

                    string? match = null;
                    foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                    {
                        if (string.Equals(Path.GetFileName(entry), segment, StringComparison.OrdinalIgnoreCase))
                        {
                            match = entry;
                            break;
                        }
                    }

                    if (match == null)
                        return null;
                    current = match;
                }

                return File.Exists(current) ? current : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
