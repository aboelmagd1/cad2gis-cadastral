using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Cad2Gis.Core.IO;

public class SimpleXlsxSheet
{
    public string Name { get; set; } = "Sheet1";
    public List<string> Headers { get; set; } = new();
    public List<List<object?>> Rows { get; } = new();

    public void AddRow(params object?[] values)
    {
        Rows.Add(new List<object?>(values));
    }
}

/// <summary>
/// A lightweight, zero-dependency Excel (.xlsx) writer built using System.IO.Compression.ZipArchive.
/// Operates completely independently of DocumentFormat.OpenXml and ClosedXML, making it 100%
/// immune to version mismatches and assembly conflicts inside the ArcGIS Pro host process.
/// </summary>
public class SimpleXlsxWriter
{
    private readonly List<SimpleXlsxSheet> _sheets = new();

    public SimpleXlsxSheet AddSheet(string name, IEnumerable<string>? headers = null)
    {
        var sheet = new SimpleXlsxSheet { Name = SanitizeSheetName(name) };
        if (headers != null)
        {
            sheet.Headers.AddRange(headers);
        }
        _sheets.Add(sheet);
        return sheet;
    }

    public void Save(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(filePath))
        {
            try { File.Delete(filePath); } catch { }
        }

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fileStream, ZipArchiveMode.Create, false, Encoding.UTF8);

        // 1. [Content_Types].xml
        WriteEntry(zip, "[Content_Types].xml", GenerateContentTypesXml());

        // 2. _rels/.rels
        WriteEntry(zip, "_rels/.rels", GenerateRelsXml());

        // 3. xl/_rels/workbook.xml.rels
        WriteEntry(zip, "xl/_rels/workbook.xml.rels", GenerateWorkbookRelsXml());

        // 4. xl/workbook.xml
        WriteEntry(zip, "xl/workbook.xml", GenerateWorkbookXml());

        // 5. xl/styles.xml
        WriteEntry(zip, "xl/styles.xml", GenerateStylesXml());

        // 6. xl/worksheets/sheet{i}.xml
        for (int i = 0; i < _sheets.Count; i++)
        {
            string entryName = $"xl/worksheets/sheet{i + 1}.xml";
            WriteEntry(zip, entryName, GenerateWorksheetXml(_sheets[i]));
        }
    }

    private static void WriteEntry(ZipArchive zip, string entryName, string xmlContent)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(xmlContent);
    }

    private string GenerateContentTypesXml()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">\n");
        sb.Append("  <Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>\n");
        sb.Append("  <Default Extension=\"xml\" ContentType=\"application/xml\"/>\n");
        sb.Append("  <Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>\n");
        sb.Append("  <Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>\n");
        for (int i = 0; i < _sheets.Count; i++)
        {
            sb.Append($"  <Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>\n");
        }
        sb.Append("</Types>");
        return sb.ToString();
    }

    private static string GenerateRelsXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
               "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n" +
               "  <Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>\n" +
               "</Relationships>";
    }

    private string GenerateWorkbookRelsXml()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">\n");
        for (int i = 0; i < _sheets.Count; i++)
        {
            sb.Append($"  <Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>\n");
        }
        sb.Append($"  <Relationship Id=\"rId{_sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>\n");
        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private string GenerateWorkbookXml()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">\n");
        sb.Append("  <sheets>\n");
        for (int i = 0; i < _sheets.Count; i++)
        {
            sb.Append($"    <sheet name=\"{EscapeXml(_sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>\n");
        }
        sb.Append("  </sheets>\n");
        sb.Append("</workbook>");
        return sb.ToString();
    }

    private static string GenerateStylesXml()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n" +
               "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">\n" +
               "  <fonts count=\"2\">\n" +
               "    <font><name val=\"Calibri\"/><sz val=\"11\"/></font>\n" +
               "    <font><b/><name val=\"Calibri\"/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/></font>\n" +
               "  </fonts>\n" +
               "  <fills count=\"3\">\n" +
               "    <fill><patternFill patternType=\"none\"/></fill>\n" +
               "    <fill><patternFill patternType=\"gray125\"/></fill>\n" +
               "    <fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF1B365D\"/></patternFill></fill>\n" +
               "  </fills>\n" +
               "  <borders count=\"1\"><border><left/><right/><top/><bottom/></border></borders>\n" +
               "  <cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>\n" +
               "  <cellXfs count=\"2\">\n" +
               "    <xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>\n" +
               "    <xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>\n" +
               "  </cellXfs>\n" +
               "</styleSheet>";
    }

    private static string GenerateWorksheetXml(SimpleXlsxSheet sheet)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">\n");
        sb.Append("  <sheetData>\n");

        int currentRow = 1;

        // Headers
        if (sheet.Headers.Count > 0)
        {
            sb.Append($"    <row r=\"{currentRow}\">\n");
            for (int col = 0; col < sheet.Headers.Count; col++)
            {
                string cellRef = GetCellReference(col + 1, currentRow);
                string text = EscapeXml(sheet.Headers[col]);
                sb.Append($"      <c r=\"{cellRef}\" t=\"inlineStr\" s=\"1\"><is><t>{text}</t></is></c>\n");
            }
            sb.Append("    </row>\n");
            currentRow++;
        }

        // Rows
        foreach (var rowData in sheet.Rows)
        {
            sb.Append($"    <row r=\"{currentRow}\">\n");
            for (int col = 0; col < rowData.Count; col++)
            {
                var val = rowData[col];
                if (val == null) continue;

                string cellRef = GetCellReference(col + 1, currentRow);

                if (val is int or long or short or byte or double or float or decimal)
                {
                    string numStr = Convert.ToString(val, CultureInfo.InvariantCulture) ?? "0";
                    sb.Append($"      <c r=\"{cellRef}\"><v>{numStr}</v></c>\n");
                }
                else if (val is bool b)
                {
                    sb.Append($"      <c r=\"{cellRef}\" t=\"b\"><v>{(b ? "1" : "0")}</v></c>\n");
                }
                else
                {
                    string str = EscapeXml(val.ToString() ?? string.Empty);
                    sb.Append($"      <c r=\"{cellRef}\" t=\"inlineStr\"><is><t>{str}</t></is></c>\n");
                }
            }
            sb.Append("    </row>\n");
            currentRow++;
        }

        sb.Append("  </sheetData>\n");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    public static string GetCellReference(int col, int row)
    {
        string colName = string.Empty;
        while (col > 0)
        {
            int modulo = (col - 1) % 26;
            colName = (char)('A' + modulo) + colName;
            col = (col - modulo) / 26;
        }
        return $"{colName}{row}";
    }

    private static string SanitizeSheetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Sheet1";
        char[] invalid = { '\\', '/', '?', '*', '[', ']', ':' };
        string clean = name;
        foreach (var c in invalid) clean = clean.Replace(c, '_');
        clean = clean.Trim();
        if (clean.Length > 31) clean = clean.Substring(0, 31);
        return string.IsNullOrEmpty(clean) ? "Sheet1" : clean;
    }

    private static string EscapeXml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("&", "&amp;")
                   .Replace("<", "&lt;")
                   .Replace(">", "&gt;")
                   .Replace("\"", "&quot;")
                   .Replace("'", "&apos;");
    }
}
