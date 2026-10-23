using System;
using System.IO;
using System.IO.Compression;
using Cad2Gis.Core.IO;
using Xunit;

namespace Cad2Gis.Tests;

public class SimpleXlsxWriterTests
{
    [Fact]
    public void SimpleXlsxWriter_CreatesValidZipAndSheets()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"test_writer_{Guid.NewGuid():N}.xlsx");
        try
        {
            var writer = new SimpleXlsxWriter();

            // Sheet 1: Summary
            var s1 = writer.AddSheet("Summary", new[] { "Key", "Value" });
            s1.AddRow("Plan No", "629/Jadadh");
            s1.AddRow("Parcels Count", 1250);
            s1.AddRow("Total Area", 54321.98);

            // Sheet 2: Parcels
            var s2 = writer.AddSheet("Parcels", new[] { "PLOT_ID", "USAGE", "AREA" });
            s2.AddRow("101", "RSD", 600.50);
            s2.AddRow("102", "COM", 1200.00);
            s2.AddRow("103", "سكني تجاري", 850.25);

            writer.Save(tempFile);

            Assert.True(File.Exists(tempFile));
            var fileInfo = new FileInfo(tempFile);
            Assert.True(fileInfo.Length > 500);

            // Verify it can be opened as a ZipArchive and contains all required OpenXML parts
            using var zip = ZipFile.OpenRead(tempFile);
            Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
            Assert.NotNull(zip.GetEntry("_rels/.rels"));
            Assert.NotNull(zip.GetEntry("xl/_rels/workbook.xml.rels"));
            Assert.NotNull(zip.GetEntry("xl/workbook.xml"));
            Assert.NotNull(zip.GetEntry("xl/styles.xml"));
            Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
            Assert.NotNull(zip.GetEntry("xl/worksheets/sheet2.xml"));

            // Verify sheet 2 XML contains Arabic text
            var sheet2Entry = zip.GetEntry("xl/worksheets/sheet2.xml");
            using var reader = new StreamReader(sheet2Entry!.Open());
            string xml = reader.ReadToEnd();
            Assert.Contains("سكني تجاري", xml);
            Assert.Contains("1200", xml);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}
