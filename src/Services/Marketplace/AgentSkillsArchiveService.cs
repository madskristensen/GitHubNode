using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace GitHubNode.Services.Marketplace
{
    internal static class AgentSkillsArchiveService
    {
        private const int TarBlockSize = 512;
        private const int MaxEntries = 2048;
        private const long MaxUncompressedBytes = 50 * 1024 * 1024;

        public static string ExtractArchive(byte[] archiveBytes, Uri artifactUri, string targetDirectory)
        {
            if (archiveBytes == null)
            {
                throw new ArgumentNullException(nameof(archiveBytes));
            }

            if (artifactUri == null)
            {
                throw new ArgumentNullException(nameof(artifactUri));
            }

            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                throw new ArgumentException("Target directory is required.", nameof(targetDirectory));
            }

            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }

            Directory.CreateDirectory(targetDirectory);

            var path = artifactUri.AbsolutePath;
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ExtractZip(archiveBytes, targetDirectory);
            }
            else if (path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            {
                ExtractTarGz(archiveBytes, targetDirectory);
            }
            else
            {
                throw new InvalidOperationException("Unsupported skill archive format. Expected .zip, .tar.gz, or .tgz.");
            }

            var skillPath = Path.Combine(targetDirectory, "SKILL.md");
            if (!File.Exists(skillPath))
            {
                throw new InvalidOperationException("Skill archive does not contain SKILL.md at the archive root.");
            }

            return skillPath;
        }

        private static void ExtractZip(byte[] archiveBytes, string targetDirectory)
        {
            using var memoryStream = new MemoryStream(archiveBytes);
            using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);

            var entryCount = 0;
            var totalBytes = 0L;
            foreach (var entry in archive.Entries)
            {
                entryCount++;
                ValidateLimits(entryCount, totalBytes + entry.Length);
                RejectZipLink(entry);

                var destinationPath = GetSafeDestinationPath(targetDirectory, entry.FullName);
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                using var source = entry.Open();
                using var destination = File.Create(destinationPath);
                source.CopyTo(destination);
                totalBytes += entry.Length;
            }
        }

        private static void ExtractTarGz(byte[] archiveBytes, string targetDirectory)
        {
            using var memoryStream = new MemoryStream(archiveBytes);
            using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
            ExtractTar(gzipStream, targetDirectory);
        }

        private static void ExtractTar(Stream stream, string targetDirectory)
        {
            var header = new byte[TarBlockSize];
            var entryCount = 0;
            var totalBytes = 0L;

            while (true)
            {
                ReadExact(stream, header, TarBlockSize);
                if (header.All(b => b == 0))
                {
                    break;
                }

                entryCount++;
                var name = ReadTarString(header, 0, 100);
                var prefix = ReadTarString(header, 345, 155);
                var fullName = string.IsNullOrWhiteSpace(prefix) ? name : prefix + "/" + name;
                var typeFlag = header[156];
                var size = ReadTarOctal(header, 124, 12);

                ValidateLimits(entryCount, totalBytes + size);

                if (typeFlag == (byte)'1' || typeFlag == (byte)'2')
                {
                    throw new InvalidOperationException($"Skill archive contains a link entry that is not allowed: {fullName}");
                }

                var destinationPath = GetSafeDestinationPath(targetDirectory, fullName);
                if (typeFlag == (byte)'5')
                {
                    Directory.CreateDirectory(destinationPath);
                }
                else if (typeFlag == 0 || typeFlag == (byte)'0')
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    using var destination = File.Create(destinationPath);
                    CopyExact(stream, destination, size);
                    totalBytes += size;
                    SkipPadding(stream, size);
                    continue;
                }
                else
                {
                    throw new InvalidOperationException($"Skill archive contains unsupported tar entry type '{(char)typeFlag}' for {fullName}.");
                }

                SkipPadding(stream, size);
            }
        }

        private static string GetSafeDestinationPath(string targetDirectory, string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
            {
                throw new InvalidOperationException("Skill archive contains an empty entry name.");
            }

            var normalizedName = entryName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalizedName) || normalizedName.Split(Path.DirectorySeparatorChar).Any(part => part == ".."))
            {
                throw new InvalidOperationException($"Skill archive contains an unsafe path: {entryName}");
            }

            var targetRoot = Path.GetFullPath(targetDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var destinationPath = Path.GetFullPath(Path.Combine(targetDirectory, normalizedName));
            if (!destinationPath.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Skill archive entry escapes the target directory: {entryName}");
            }

            return destinationPath;
        }

        private static void RejectZipLink(ZipArchiveEntry entry)
        {
            var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixMode == 0xA000)
            {
                throw new InvalidOperationException($"Skill archive contains a symbolic link that is not allowed: {entry.FullName}");
            }
        }

        private static void ValidateLimits(int entryCount, long totalBytes)
        {
            if (entryCount > MaxEntries)
            {
                throw new InvalidOperationException("Skill archive contains too many entries.");
            }

            if (totalBytes > MaxUncompressedBytes)
            {
                throw new InvalidOperationException("Skill archive exceeds the maximum unpacked size.");
            }
        }

        private static string ReadTarString(byte[] buffer, int offset, int count)
        {
            var end = offset;
            var max = offset + count;
            while (end < max && buffer[end] != 0)
            {
                end++;
            }

            return Encoding.UTF8.GetString(buffer, offset, end - offset).Trim();
        }

        private static long ReadTarOctal(byte[] buffer, int offset, int count)
        {
            var text = ReadTarString(buffer, offset, count).Trim();
            return string.IsNullOrWhiteSpace(text) ? 0 : Convert.ToInt64(text, 8);
        }

        private static void ReadExact(Stream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException("Unexpected end of archive.");
                }

                offset += read;
            }
        }

        private static void CopyExact(Stream source, Stream destination, long count)
        {
            var buffer = new byte[81920];
            var remaining = count;
            while (remaining > 0)
            {
                var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    throw new EndOfStreamException("Unexpected end of archive entry.");
                }

                destination.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        private static void SkipPadding(Stream stream, long size)
        {
            var padding = (TarBlockSize - (size % TarBlockSize)) % TarBlockSize;
            if (padding == 0)
            {
                return;
            }

            var buffer = new byte[padding];
            ReadExact(stream, buffer, buffer.Length);
        }
    }
}
