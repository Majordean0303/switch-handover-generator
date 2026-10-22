using System.IO.Compression;
using System.Text.RegularExpressions;

namespace SwitchHandover.Core.Services;

/// <summary>
/// Service for inspecting, extracting, and bundling ZIP archives containing Cisco configuration logs and handover reports.
/// </summary>
public sealed record ExtractionLimits(int MaxEntries=1000,long MaxEntryBytes=134217728,long MaxTotalBytes=536870912);

public static class ZipArchiveService
{
    private static readonly HashSet<string> ValidConfigExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".cfg", ".conf", ".config", ".ios", ".cli"
    };

    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bin", ".iso", ".zip", ".tar", ".gz", ".7z",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".pdf",
        ".doc", ".docx", ".xls", ".xlsx", ".csv", ".mp4", ".avi", ".pcap"
    };

    /// <summary>
    /// Checks if a given file path is a ZIP archive by extension or magic header bytes.
    /// </summary>
    public static bool IsZipFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        if (Path.GetExtension(filePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 4) return false;

            byte[] header = new byte[4];
            int read = fs.Read(header, 0, 4);
            return read == 4 && header[0] == 0x50 && header[1] == 0x4B &&
                   ((header[2] == 0x03 && header[3] == 0x04) ||
                    (header[2] == 0x05 && header[3] == 0x06) ||
                    (header[2] == 0x07 && header[3] == 0x08));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Inspects a ZIP archive and counts eligible configuration/log entries without extracting.
    /// </summary>
    public static int CountConfigEntriesInZip(string zipPath)
    {
        if (!IsZipFile(zipPath))
            return 0;

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            return archive.Entries.Count(IsValidConfigEntry);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Determines whether a ZipArchiveEntry is an eligible Cisco configuration log entry.
    /// </summary>
    public static bool IsValidConfigEntry(ZipArchiveEntry entry)
    {
        // Skip directory entries
        if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            return false;

        // Skip MacOS resource fork metadata and hidden files
        if (entry.FullName.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(entry.FullName).StartsWith("._", StringComparison.OrdinalIgnoreCase))
            return false;

        // Skip empty entries
        if (entry.Length == 0)
            return false;

        string ext = Path.GetExtension(entry.Name);
        if (ValidConfigExtensions.Contains(ext))
            return true;

        if (ExcludedExtensions.Contains(ext))
            return false;

        // Default accept text-like entries
        return true;
    }

    /// <summary>
    /// Extracts all eligible configuration/log files from a ZIP archive into the destination directory.
    /// Preserves uniqueness across nested folder structures.
    /// </summary>
    public static List<string> ExtractConfigsFromZip(string zipPath, string destinationDirectory, Action<string, string>? log = null, ExtractionLimits? limits = null, CancellationToken token = default)
    {
        var extractedFiles = new List<string>();
        if (!File.Exists(zipPath))
            return extractedFiles;

        Directory.CreateDirectory(destinationDirectory);

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            int count = 0;
            limits ??= new();
            var eligible=archive.Entries.Where(IsValidConfigEntry).ToArray();
            if(limits.MaxEntries<1||limits.MaxEntryBytes<1||limits.MaxTotalBytes<1||eligible.Length>limits.MaxEntries||eligible.Any(e=>e.Length>limits.MaxEntryBytes)||eligible.Sum(e=>e.Length)>limits.MaxTotalBytes)throw new InvalidDataException("Archive exceeds extraction limits.");

            foreach (var entry in eligible)
            {
                token.ThrowIfCancellationRequested();
                if (!IsValidConfigEntry(entry))
                    continue;

                // Flatten subdirectories into unique filenames: e.g. "SiteA/Switch1.txt" -> "SiteA_Switch1.txt"
                string safeRelative = entry.FullName.Replace('\\', '/').Trim('/');
                string safeName = Regex.Replace(safeRelative, @"[/\\:;*?""<>|]", "_");
                if (string.IsNullOrWhiteSpace(safeName))
                    safeName = $"entry_{count + 1}.txt";

                string destPath = Path.Combine(destinationDirectory, safeName);

                // Handle collisions
                int counter = 1;
                string dir = Path.GetDirectoryName(destPath)!;
                string baseName = Path.GetFileNameWithoutExtension(destPath);
                string extension = Path.GetExtension(destPath);
                while (File.Exists(destPath))
                {
                    destPath = Path.Combine(dir, $"{baseName}_{counter++}{extension}");
                }

                entry.ExtractToFile(destPath, overwrite: true);
                extractedFiles.Add(destPath);
                count++;
            }

            log?.Invoke("ZIP", $"Extracted {count} configuration log(s) from '{Path.GetFileName(zipPath)}'.");
        }
        catch (Exception ex)
        {
            foreach(var extracted in extractedFiles)File.Delete(extracted);
            log?.Invoke("Error", $"Failed to extract ZIP '{Path.GetFileName(zipPath)}': {ex.Message}");
            throw;
        }

        return extractedFiles;
    }

    /// <summary>
    /// Resolves an arbitrary list of file or zip paths into a flattened list of runnable text config file paths.
    /// Automatically unpacks any ZIP archives into subdirectories of tempExtractionRoot.
    /// </summary>
    public static List<string> ResolveAllFiles(
        IEnumerable<string> paths,
        string tempExtractionRoot,
        Action<string, string>? log = null)
    {
        var resolved = new List<string>();

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
                {
                    if (IsZipFile(file))
                    {
                        string subDir = Path.Combine(tempExtractionRoot, Path.GetFileNameWithoutExtension(file) + "_" + Guid.NewGuid().ToString("N")[..8]);
                        resolved.AddRange(ExtractConfigsFromZip(file, subDir, log));
                    }
                    else if (!ExcludedExtensions.Contains(Path.GetExtension(file)))
                    {
                        resolved.Add(file);
                    }
                }
            }
            else if (File.Exists(path))
            {
                if (IsZipFile(path))
                {
                    string subDir = Path.Combine(tempExtractionRoot, Path.GetFileNameWithoutExtension(path) + "_" + Guid.NewGuid().ToString("N")[..8]);
                    resolved.AddRange(ExtractConfigsFromZip(path, subDir, log));
                }
                else
                {
                    resolved.Add(path);
                }
            }
        }

        return resolved;
    }

    /// <summary>
    /// Bundles an Excel workbook and a collection of CSV reports into a single ZIP archive.
    /// </summary>
    public static void CreateReportsBundleZip(
        string destinationZipPath,
        string? excelFilePath,
        IDictionary<string, string> csvReports)
    {
        string? dir = Path.GetDirectoryName(destinationZipPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(destinationZipPath))
            File.Delete(destinationZipPath);

        using var archive = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create);

        if (!string.IsNullOrEmpty(excelFilePath) && File.Exists(excelFilePath))
        {
            archive.CreateEntryFromFile(excelFilePath, Path.GetFileName(excelFilePath), CompressionLevel.Optimal);
        }

        foreach (var (filename, content) in csvReports)
        {
            var entry = archive.CreateEntry(filename, CompressionLevel.Optimal);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    }

    /// <summary>
    /// Creates a ZIP archive from a directory containing captured switch configurations.
    /// </summary>
    public static void CreateBackupsZip(string sourceDirectory, string destinationZipPath)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Source directory '{sourceDirectory}' not found.");

        string? dir = Path.GetDirectoryName(destinationZipPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(destinationZipPath))
            File.Delete(destinationZipPath);

        ZipFile.CreateFromDirectory(sourceDirectory, destinationZipPath, CompressionLevel.Optimal, false);
    }
}
