using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Calcpad.OpenXml
{
    public static class ExcelData
    {
        private const string defaultSheet = "Sheet1";
        private struct CellRef
        {
            internal int Row;
            internal int Col;
            internal CellRef(ReadOnlySpan<char> cellRefString)
            {
                if (!cellRefString.IsEmpty)
                {
                    var index = GetColRowSepIndex(cellRefString);
                    if (index < cellRefString.Length)
                        Row = int.Parse(cellRefString[index..]);

                    if (index > 0)
                        Col = GetColNumber(cellRefString[..index]);
                }
            }

            private static int GetColNumber(ReadOnlySpan<char> colName)
            {
                int sum = 0;
                for (int i = 0; i < colName.Length; i++)
                {
                    sum *= 26;
                    sum += char.ToUpperInvariant(colName[i]) - 'A' + 1;
                }
                return sum;
            }

            public override string ToString() =>
                string.Concat(GetColName(Col), Row.ToString());

            private static string GetColName(int colNumber)
            {
                var colName = string.Empty;
                int A = 'A';
                while (colNumber > 0)
                {
                    int modulo = (colNumber - 1) % 26;
                    colName = Convert.ToChar(A + modulo) + colName;
                    colNumber = (colNumber - modulo) / 26;
                }
                return colName;
            }

            private static int GetColRowSepIndex(ReadOnlySpan<char> cellRef)
            {
                int index = 0, len = cellRef.Length;
                while (index < len && !char.IsDigit(cellRef[index]))
                    ++index;

                return index;
            }

            internal static int GetColNumberFromCellRef(ReadOnlySpan<char> cellRef) =>
                cellRef.IsEmpty ? 0 : GetColNumber(cellRef[..GetColRowSepIndex(cellRef)]);
        }

        private struct CellRange
        {
            internal CellRef Start;
            internal CellRef End;
            internal int RowCount => End.Row - Start.Row + 1;
            internal int ColCount => End.Col - Start.Col + 1;

            internal CellRange(ReadOnlySpan<char> start, ReadOnlySpan<char> end)
            {
                Start = new(start);
                End = new(end);
            }

            public override string ToString() => $"{Start}:{End}";

            internal void Normalize(int minRow, int minCol, int maxRow, int maxCol)
            {
                Start.Row = Math.Clamp(Start.Row, minRow, maxRow);
                Start.Col = Math.Clamp(Start.Col, minCol, maxCol);
                if (End.Row == 0)
                    End.Row = maxRow;
                else
                    End.Row = Math.Clamp(End.Row, minRow, maxRow);

                if (End.Col == 0)
                    End.Col = maxCol;
                else
                    End.Col = Math.Clamp(End.Col, minCol, maxCol);

                if (Start.Row > End.Row)
                    (Start.Row, End.Row) = (End.Row, Start.Row);

                if (Start.Col > End.Col)
                    (Start.Col, End.Col) = (End.Col, Start.Col);
            }

        }

        public static bool IsExcelFile(string ext) =>
            ext.Equals("xlsx", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals("xlsm", StringComparison.OrdinalIgnoreCase);

        public static string[][] Read(string filepath, string sheetName, string rangeStart, string rangeEnd)
        {
            using SpreadsheetDocument document = SpreadsheetDocument.Open(filepath, false);
            return ReadFromDocument(document, sheetName, rangeStart, rangeEnd);
        }

        public static string[][] ReadFromMemory(byte[] contentBytes, string sheetName, string rangeStart, string rangeEnd)
        {
            using var stream = new MemoryStream(contentBytes);
            using SpreadsheetDocument document = SpreadsheetDocument.Open(stream, false);
            return ReadFromDocument(document, sheetName, rangeStart, rangeEnd);
        }

        private static string[][] ReadFromDocument(SpreadsheetDocument document, string sheetName, string rangeStart, string rangeEnd)
        {
            WorkbookPart wbPart = document.WorkbookPart ??
                throw new InvalidOperationException("The Excel workbook is missing or not initialized.");

            Sheet sheet = null;
            if (!string.IsNullOrEmpty(sheetName))
                sheet = wbPart.Workbook.Descendants<Sheet>().FirstOrDefault(s => s.Name == sheetName) ??
                    throw new InvalidOperationException($"Worksheet \"{sheetName}\" not found.");
            else
                sheet = wbPart.Workbook.Descendants<Sheet>().FirstOrDefault() ??
                    throw new InvalidOperationException("This Excel workbook doesn not contain worksheets.");

            var sheetID = sheet?.Id?.Value ?? string.Empty;
            var wsPart = (WorksheetPart)wbPart.GetPartById(sheetID);

            // Cache shared string table as indexed array — O(1) per lookup
            var stringTablePart = wbPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault();
            string[] sharedStrings = stringTablePart?.SharedStringTable
                .Elements<SharedStringItem>()
                .Select(s => s.InnerText)
                .ToArray();

            // Single pass: extract cell values as strings and compute max row/col.
            // Uses Elements<Row>/Elements<Cell> (direct children) instead of
            // Descendants<Cell> (full tree walk). Stores only string values,
            // not Cell DOM objects, so the DOM can be collected sooner.
            var cellValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int maxRow = 0;
            int maxCol = 0;
            var sheetData = wsPart.Worksheet.GetFirstChild<SheetData>();
            foreach (var row in sheetData.Elements<Row>())
            {
                if (row.RowIndex?.Value != null)
                {
                    var rowIdx = (int)row.RowIndex.Value;
                    if (rowIdx > maxRow) maxRow = rowIdx;
                }
                foreach (var cell in row.Elements<Cell>())
                {
                    var cellRef = cell.CellReference?.Value;
                    if (cellRef == null) continue;

                    var colNum = CellRef.GetColNumberFromCellRef(cellRef);
                    if (colNum > maxCol) maxCol = colNum;

                    cellValues[cellRef] = ExtractCellValue(cell, sharedStrings);
                }
            }

            var range = new CellRange(rangeStart, rangeEnd);
            range.Normalize(1, 1, maxRow, maxCol);
            var rowCount = range.RowCount;
            var colCount = range.ColCount;
            if (rowCount < 0 || colCount < 0)
                return null;

            string[][] data = new string[rowCount][];
            var current = range.Start;
            for (int i = 0; i < rowCount; ++i)
            {
                var rowData = new string[colCount];
                current.Col = range.Start.Col;
                for (int j = 0; j < colCount; ++j)
                {
                    rowData[j] = cellValues.TryGetValue(current.ToString(), out var v) ? v : string.Empty;
                    ++current.Col;
                }
                data[i] = rowData;
                ++current.Row;
            }
            return data;
        }

        /// <summary>
        /// Extracts the display value from a Cell as a string.
        /// Called once per cell during the single-pass index build.
        /// </summary>
        private static string ExtractCellValue(Cell cell, string[] sharedStrings)
        {
            string value = cell.InnerText;
            if (cell.DataType?.Value == CellValues.SharedString)
            {
                if (sharedStrings != null && int.TryParse(value, out var index) && index >= 0 && index < sharedStrings.Length)
                    return sharedStrings[index];
            }
            else if (cell.CellFormula is not null)
            {
                var cv = cell.CellValue?.InnerText;
                if (!string.IsNullOrEmpty(cv))
                    return cv;
            }
            return value;
        }

        public static void Write(string filepath, string sheetName, string rangeStart, string rangeEnd, string[][] data, bool append)
        {
            if (!File.Exists(filepath) || !append)
                CreateSpreadsheetWorkbook(filepath, sheetName);

            using SpreadsheetDocument document = SpreadsheetDocument.Open(filepath, true);
            WriteIntoDocument(document, sheetName, rangeStart, rangeEnd, data);
        }

        /// <summary>
        /// In-memory equivalent of <see cref="Write(string, string, string, string, string[][], bool)"/>.
        /// When <paramref name="append"/> is true and <paramref name="existingBytes"/> is non-empty, the
        /// existing workbook is opened, mutated, and re-serialized; otherwise a fresh workbook is created.
        /// </summary>
        public static byte[] Write(byte[] existingBytes, string sheetName, string rangeStart, string rangeEnd, string[][] data, bool append)
        {
            using var ms = new MemoryStream();
            if (append && existingBytes != null && existingBytes.Length > 0)
            {
                ms.Write(existingBytes, 0, existingBytes.Length);
                ms.Position = 0;
                using (SpreadsheetDocument document = SpreadsheetDocument.Open(ms, true))
                    WriteIntoDocument(document, sheetName, rangeStart, rangeEnd, data);
            }
            else
            {
                using (SpreadsheetDocument document = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
                {
                    InitializeEmptyWorkbook(document, sheetName);
                    WriteIntoDocument(document, sheetName, rangeStart, rangeEnd, data);
                }
            }
            return ms.ToArray();
        }

        private static void WriteIntoDocument(SpreadsheetDocument document, string sheetName, string rangeStart, string rangeEnd, string[][] data)
        {
            WorkbookPart wbPart = document.WorkbookPart ?? document.AddWorkbookPart();
            Sheet sheet = null;
            if (string.IsNullOrWhiteSpace(sheetName))
            {
                sheet = wbPart.Workbook.Descendants<Sheet>().FirstOrDefault();
                sheetName = defaultSheet;
            }
            else
                sheet = wbPart.Workbook.Descendants<Sheet>().FirstOrDefault(s => s.Name == sheetName);

            sheet ??= InsertWorksheet(wbPart, sheetName);
            var sheetID = sheet?.Id?.Value ?? string.Empty;
            WorksheetPart wsPart = (WorksheetPart)(wbPart.GetPartById(sheetID));
            var range = new CellRange(rangeStart, rangeEnd);
            var maxRow = Math.Max(range.Start.Row, 1) + data.Length - 1;
            var maxCol = Math.Max(range.Start.Col, 1) + data.Max(r => r.Length) - 1;
            range.Normalize(1, 1, maxRow, maxCol);
            var rowCount = range.RowCount;
            var colCount = range.ColCount;
            var current = range.Start;
            for (int i = 0; i < rowCount; i++)
            {
                Row row = InsertRow(wsPart.Worksheet, (uint)current.Row);
                var rowData = data[i];
                var len = Math.Min(rowData.Length, colCount);
                current.Col = range.Start.Col;
                for (int j = 0; j < len; j++)
                {
                    InsertValue(row, current.ToString(), rowData[j]);
                    ++current.Col;
                }
                ++current.Row;
            }
            wsPart.Worksheet.Save();
            wbPart.Workbook.Save();
        }

        private static void CreateSpreadsheetWorkbook(string filePath, string sheetName)
        {
            using SpreadsheetDocument document = SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook);
            InitializeEmptyWorkbook(document, sheetName);
        }

        private static void InitializeEmptyWorkbook(SpreadsheetDocument document, string sheetName)
        {
            WorkbookPart wbPart = document.AddWorkbookPart();
            wbPart.Workbook = new();
            WorksheetPart wsPart = wbPart.AddNewPart<WorksheetPart>();
            wsPart.Worksheet = new(new SheetData());
            Sheets sheets = wbPart.Workbook.AppendChild(new Sheets());
            Sheet sheet = new()
            {
                Id = wbPart.GetIdOfPart(wsPart),
                SheetId = 1,
                Name = string.IsNullOrWhiteSpace(sheetName) ? defaultSheet : sheetName,
            };
            sheets.Append(sheet);
            wbPart.Workbook.Save();
        }

        private static void InsertValue(Row row, string cellRef, string value)
        {
            Cell cell = InsertCell(row, cellRef);
            if (double.TryParse(value, CultureInfo.CurrentCulture.NumberFormat, out var d))
            {
                cell.CellValue = new CellValue(d);
                cell.DataType = CellValues.Number;
            }
            else
            {
                cell.InlineString = new InlineString { Text = new Text(value) };
                cell.DataType = CellValues.InlineString;
            }
        }
 
        static Sheet InsertWorksheet(WorkbookPart wbPart, string sheetName)
        {
            WorksheetPart wsPart = wbPart.AddNewPart<WorksheetPart>();
            wsPart.Worksheet = new(new SheetData());
            Sheets sheets = wbPart.Workbook.GetFirstChild<Sheets>() ?? 
                wbPart.Workbook.AppendChild(new Sheets());
            string relationshipId = wbPart.GetIdOfPart(wsPart);
            uint sheetId = sheets.Elements<Sheet>().Max(s => s.SheetId?.Value ?? 0) + 1;
            Sheet sheet = new() { 
                Id = relationshipId, 
                SheetId = sheetId, 
                Name = sheetName 
            };
            sheets.Append(sheet);
            return sheet;
        }

        private static Row InsertRow(Worksheet worksheet, uint rowIndex)
        {
            SheetData sheetData = worksheet.GetFirstChild<SheetData>();
            Row row = sheetData.Elements<Row>().FirstOrDefault(r => r.RowIndex == rowIndex);
            if (row is null)
            {
                row = new Row() { RowIndex = rowIndex };
                sheetData.Append(row);
            }
            return row;
        }

        private static Cell InsertCell(Row row, string cellRef)
        {
            var cells = row.Elements<Cell>();
            var cell = cells.FirstOrDefault(c => c.CellReference?.Value == cellRef);
            if (cell is null)
            {
                cell = new() { CellReference = cellRef };
                var refCell = cells.FirstOrDefault(c => string.Compare(c.CellReference?.Value, cellRef, true) > 0);
                if (refCell is null)
                    row.Append(cell);
                else
                    row.InsertBefore(cell, refCell);
            }
            return cell;
        }
    }
}
