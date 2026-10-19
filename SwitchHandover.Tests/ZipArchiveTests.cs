using System.IO.Compression;
using SwitchHandover.Core.Services;
using Xunit;

namespace SwitchHandover.Tests;

public class ZipArchiveTests
{
    [Fact]
    public void TestZipDetectionAndCounting()
    {
        string tempZip = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.zip");
        try
        {
            using (var zip = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                // Add 3 valid configs
                var entry1 = zip.CreateEntry("switch1.txt");
                using (var sw = new StreamWriter(entry1.Open())) sw.WriteLine("hostname Switch1");

                var entry2 = zip.CreateEntry("SiteA/switch2.log");
                using (var sw = new StreamWriter(entry2.Open())) sw.WriteLine("hostname Switch2");

                var entry3 = zip.CreateEntry("SiteB/subfolder/core.cfg");
                using (var sw = new StreamWriter(entry3.Open())) sw.WriteLine("hostname Core1");

                // Add 2 non-config files (should be ignored)
                var entry4 = zip.CreateEntry("image.png");
                using (var sw = new StreamWriter(entry4.Open())) sw.WriteLine("binary content");

                var entry5 = zip.CreateEntry("__MACOSX/._switch1.txt");
                using (var sw = new StreamWriter(entry5.Open())) sw.WriteLine("meta");
            }

            Assert.True(ZipArchiveService.IsZipFile(tempZip));
            int count = ZipArchiveService.CountConfigEntriesInZip(tempZip);
            Assert.Equal(3, count);
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }

    [Fact]
    public void TestZipExtraction()
    {
        string tempZip = Path.Combine(Path.GetTempPath(), $"test_extract_{Guid.NewGuid():N}.zip");
        string destDir = Path.Combine(Path.GetTempPath(), $"dest_{Guid.NewGuid():N}");

        try
        {
            using (var zip = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                var entry1 = zip.CreateEntry("Floor1/Cisco_9300.txt");
                using (var sw = new StreamWriter(entry1.Open())) sw.WriteLine("Floor 1 Switch");

                var entry2 = zip.CreateEntry("Floor2/Cisco_9300.txt");
                using (var sw = new StreamWriter(entry2.Open())) sw.WriteLine("Floor 2 Switch");
            }

            var extracted = ZipArchiveService.ExtractConfigsFromZip(tempZip, destDir);

            Assert.Equal(2, extracted.Count);
            Assert.All(extracted, f => Assert.True(File.Exists(f)));

            // Content check
            string content1 = File.ReadAllText(extracted[0]);
            string content2 = File.ReadAllText(extracted[1]);
            Assert.Contains("Switch", content1);
            Assert.Contains("Switch", content2);
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
            if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
        }
    }

    [Fact]
    public void TestResolveAllFilesWithMixedInputs()
    {
        string tempZip = Path.Combine(Path.GetTempPath(), $"mixed_{Guid.NewGuid():N}.zip");
        string looseFile = Path.Combine(Path.GetTempPath(), $"loose_{Guid.NewGuid():N}.txt");
        string extractRoot = Path.Combine(Path.GetTempPath(), $"root_{Guid.NewGuid():N}");

        try
        {
            File.WriteAllText(looseFile, "Loose Switch Config");

            using (var zip = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("Zipped_Switch.log");
                using var sw = new StreamWriter(entry.Open());
                sw.WriteLine("Zipped Switch Config");
            }

            var inputPaths = new List<string> { looseFile, tempZip };
            var resolved = ZipArchiveService.ResolveAllFiles(inputPaths, extractRoot);

            Assert.Equal(2, resolved.Count);
            Assert.Contains(looseFile, resolved);
            Assert.Contains(resolved, f => f.Contains("Zipped_Switch"));
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
            if (File.Exists(looseFile)) File.Delete(looseFile);
            if (Directory.Exists(extractRoot)) Directory.Delete(extractRoot, true);
        }
    }
}
