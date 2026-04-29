using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Calcpad.OpenXml;

namespace Calcpad.Core
{
    public partial class ExpressionParser
    {
        private static class DataExchange
        {
            internal static string[][] Read(ReadWriteOptions options, ClientFileCache clientFileCache = null)
            {
                var fileName = $"{options.Path}.{options.Ext}";
                if (fileName == ".")
                    throw Exceptions.MissingFileName();

                var fullPath = options.FullPath;
                var fileExists = File.Exists(fullPath);

                if (!fileExists && clientFileCache != null)
                {
                    if (clientFileCache.TryGetErrorMultiKey(fullPath, fileName, out var cachedError))
                        throw new MathParserException(cachedError);

                    try
                    {
                        // Excel files need raw bytes; CSV/text files need a UTF-8 string
                        if (options.IsExcel)
                        {
                            if (!ExcelData.IsExcelFile(options.Ext.ToString()))
                                throw Exceptions.FileFormatNotSupported(options.Ext.ToString());

                            if (clientFileCache.TryGetBytes(fullPath, out var cachedBytes) ||
                                clientFileCache.TryGetBytes(fileName, out cachedBytes))
                                return ReadExcelFromMemory(options, cachedBytes);
                        }
                        else if (clientFileCache.TryGetContentMultiKey(fullPath, fileName, out var cachedContent))
                        {
                            return ReadCSVFromString(options, cachedContent);
                        }
                    }
                    catch (Exception e)
                    {
                        throw new MathParserException(e.Message);
                    }
                }

                if (!fileExists)
                    throw Exceptions.FileNotFound(fileName);

                try
                {
                    if (options.IsExcel)
                    {
                        if (!ExcelData.IsExcelFile(options.Ext.ToString()))
                            throw Exceptions.FileFormatNotSupported(options.Ext.ToString());

                        return ReadExcel(options);
                    }
                    return ReadCSV(options);
                }
                catch (Exception e)
                {
                    throw new MathParserException(e.Message);
                }
            }

            private static string[][] ReadCSVFromString(ReadWriteOptions options, string content)
            {
                int i = 0;
                var (start, end) = ParseBounds(options.Start, options.End);
                var lines = new List<string>();
                using var reader = new StringReader(content);
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    ++i;
                    if (i >= start.row)
                    {
                        if (end.row > 0 && i > end.row)
                            break;

                        lines.Add(line);
                    }
                }
                return FormatCSVData(options, lines, start, end);
            }

            private static string[][] ReadCSV(ReadWriteOptions options)
            {
                int i = 0;
                var (start, end) = ParseBounds(options.Start, options.End);
                var lines = new List<string>();
                using var reader = new StreamReader(options.FullPath);
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    ++i;
                    if (i >= start.row)
                    {
                        if (end.row > 0 && i > end.row)
                            break;

                        lines.Add(line);
                    }
                }
                return FormatCSVData(options, lines, start, end);
            }

            private static string[][] FormatCSVData(ReadWriteOptions options, List<string> lines, (int row, int col) start, (int row, int col) end)
            {
                string[][] data = new string[lines.Count][];
                var j0 = Math.Max(0, start.col - 1);
                var n = lines.Count;
                for (int i = 0; i < n; ++i)
                {
                    if (start.col == 0 && end.col == 0)
                        data[i] = lines[i].Split(options.Separator, StringSplitOptions.TrimEntries);
                    else
                    {
                        var rowValues = lines[i].Split(options.Separator, StringSplitOptions.TrimEntries);
                        var m = rowValues.Length - j0;
                        if (end.col > 0)
                            m = Math.Min(m, end.col - j0);
                        if (m > 0)
                        {
                            data[i] = new string[m];
                            for (int j = 0; j < m; ++j)
                                data[i][j] = rowValues[j + j0];
                        }
                        else
                            data[i] = [];
                    }
                }
                return data;
            }

            private static ((int row, int col) start, (int row, int col) end) ParseBounds(ReadOnlySpan<char> startSpan, ReadOnlySpan<char> endSpan)
            {
                var start = ParseBound(startSpan);
                var end = ParseBound(endSpan);

                if (start.row > end.row && end.row > 0)
                    (start.row, end.row) = (end.row, start.row);

                if (start.col > end.col && end.col > 0)
                    (start.col, end.col) = (end.col, start.col);

                return (start, end);
            }

            private static (int col, int row) ParseBound(ReadOnlySpan<char> s)
            {
                if (s.IsEmpty)
                    return (0, 0);

                var ir = s.IndexOf('R') + 1;
                var ic = s.IndexOf('C') + 1;
                if (ir == 0) ir = s.Length + 1;
                if (ic == 0) ic = s.Length + 1;
                if (ir != 1 && ic != 1)
                    throw Exceptions.InvalidSyntax(s.ToString());

                ReadOnlySpan<char> sr, sc;
                if (ir < ic)
                {
                    sr = s[ir..(ic - 1)];
                    sc = ic < s.Length ? s[ic..] : "0";
                }
                else
                {
                    sc = s[ic..(ir - 1)];
                        sr = ir < s.Length ? s[ir..] : "0";
                }
                if (int.TryParse(sc, out var col) && int.TryParse(sr, out var row))
                    return (row, col);

                throw Exceptions.InvalidSyntax(s.ToString());
            }

            private static string[][] ReadExcel(ReadWriteOptions options)
            {
                var sheet = options.Sheet.ToString();
                var start = options.Start.ToString();
                var end = options.End.ToString();
                return ExcelData.Read(options.FullPath, sheet, start, end);
            }

            private static string[][] ReadExcelFromMemory(ReadWriteOptions options, byte[] contentBytes)
            {
                var sheet = options.Sheet.ToString();
                var start = options.Start.ToString();
                var end = options.End.ToString();
                return ExcelData.ReadFromMemory(contentBytes, sheet, start, end);
            }

            internal static string ReadString(ReadWriteOptions options, ClientFileCache clientFileCache = null)
            {
                var fileName = $"{options.Path}.{options.Ext}";
                if (fileName == ".")
                    throw Exceptions.MissingFileName();

                var fullPath = options.FullPath;
                var fileExists = File.Exists(fullPath);

                if (!fileExists && clientFileCache != null)
                {
                    if (clientFileCache.TryGetErrorMultiKey(fullPath, fileName, out var cachedError))
                        throw new MathParserException(cachedError);

                    if (clientFileCache.TryGetContentMultiKey(fullPath, fileName, out var cachedContent))
                        return NormalizeLinesToSeparator(cachedContent);
                }

                if (!fileExists)
                    throw Exceptions.FileNotFound(fileName);

                try
                {
                    var content = File.ReadAllText(fullPath);
                    return NormalizeLinesToSeparator(content);
                }
                catch (Exception e)
                {
                    throw new MathParserException(e.Message);
                }
            }

            internal static void WriteString(ReadWriteOptions options, string content, WriteCache writeCache = null)
            {
                var fileName = $"{options.Path}.{options.Ext}";
                if (fileName == ".")
                    throw Exceptions.MissingFileName();

                try
                {
                    var text = content.Replace("|", Environment.NewLine);

                    if (writeCache != null)
                    {
                        var bytes = new UTF8Encoding(false).GetBytes(text);
                        var ct = GuessTextContentType(options.Ext);
                        if (options.Append)
                            writeCache.AppendBytes(fileName, ct, bytes);
                        else
                            writeCache.PutBytes(fileName, ct, bytes);
                        return;
                    }

                    var fullPath = options.FullPath;
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(dir))
                        throw Exceptions.PathNotFound(dir);

                    if (options.Append)
                        File.AppendAllText(fullPath, text);
                    else
                        File.WriteAllText(fullPath, text);
                }
                catch (Exception e)
                {
                    throw new MathParserException(e.Message);
                }
            }

            internal static void Write(ReadWriteOptions options, string[][] data, WriteCache writeCache = null)
            {
                var fileName = $"{options.Path}.{options.Ext}";
                if (fileName == ".")
                    throw Exceptions.MissingFileName();

                if (writeCache == null)
                {
                    var dir = Path.GetDirectoryName(options.FullPath);
                    if (!Directory.Exists(dir))
                        throw Exceptions.PathNotFound(dir);
                }

                try
                {
                    if (options.IsExcel)
                    {
                        if (!ExcelData.IsExcelFile(options.Ext.ToString()))
                            throw Exceptions.FileFormatNotSupported(options.Ext.ToString());

                        WriteExcel(options, data, writeCache, fileName);
                    }
                    else
                        WriteCSV(options, data, writeCache, fileName);
                }
                catch (Exception e)
                {
                    throw new MathParserException(e.Message);
                }
            }

            private static void WriteCSV(ReadWriteOptions options, string[][] data, WriteCache writeCache, string fileName)
            {
                var (start, end) = ParseBounds(options.Start, options.End);
                var i0 = Math.Max(0, start.row - 1);
                var n = data.Length;
                if (end.row > 0)
                    n = Math.Min(n, end.row);

                var j0 = Math.Max(0, start.col - 1);

                if (writeCache != null)
                {
                    using var ms = new MemoryStream();
                    using (var writer = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true))
                    {
                        WriteCSVRows(writer, options, data, i0, n, j0, end.col);
                    }
                    var bytes = ms.ToArray();
                    var ct = options.Ext.Equals("csv", StringComparison.OrdinalIgnoreCase)
                        ? "text/csv"
                        : GuessTextContentType(options.Ext);
                    if (options.Append)
                        writeCache.AppendBytes(fileName, ct, bytes);
                    else
                        writeCache.PutBytes(fileName, ct, bytes);
                    return;
                }

                using var fileWriter = new StreamWriter(options.FullPath, options.Append);
                WriteCSVRows(fileWriter, options, data, i0, n, j0, end.col);
                fileWriter.Close();
            }

            private static void WriteCSVRows(StreamWriter writer, ReadWriteOptions options, string[][] data, int i0, int n, int j0, int endCol)
            {
                for (int i = i0; i < n; ++i)
                {
                    var m = data[i].Length - j0;
                    if (endCol > 0)
                        m = Math.Min(m, endCol - j0);

                    if (m > 0)
                    {
                        var row = new ArraySegment<string>(data[i], j0, m);
                        writer.WriteLine(string.Join<string>(options.Separator, row));
                    }
                    else
                        writer.WriteLine();
                }
            }

            private static void WriteExcel(ReadWriteOptions options, string[][] matrix, WriteCache writeCache, string fileName)
            {
                var sheet = options.Sheet.ToString();
                var start = options.Start.ToString();
                var end = options.End.ToString();

                if (writeCache != null)
                {
                    byte[] existing = null;
                    if (options.Append)
                        writeCache.TryGetBytes(fileName, out existing);
                    var resultBytes = ExcelData.Write(existing, sheet, start, end, matrix, options.Append);
                    writeCache.PutBytes(
                        fileName,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        resultBytes);
                    return;
                }

                ExcelData.Write(options.FullPath, sheet, start, end, matrix, options.Append);
            }

            private static string GuessTextContentType(ReadOnlySpan<char> ext)
            {
                if (ext.Equals("csv", StringComparison.OrdinalIgnoreCase)) return "text/csv";
                if (ext.Equals("json", StringComparison.OrdinalIgnoreCase)) return "application/json";
                if (ext.Equals("xml", StringComparison.OrdinalIgnoreCase)) return "application/xml";
                if (ext.Equals("html", StringComparison.OrdinalIgnoreCase) || ext.Equals("htm", StringComparison.OrdinalIgnoreCase)) return "text/html";
                return "text/plain";
            }
        }

        private static string NormalizeLinesToSeparator(string content)
            => content.Replace("\r\n", "|").Replace("\n", "|").Replace("\r", "|");
    }
}