Imports System.Globalization
Imports System.IO
Imports System.Text
Imports CsvHelper
Imports CsvHelper.Configuration
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain.Entities

Namespace Csv
    Public Class CsvHelperAdapter
        Implements ICsvFileAdapter, ICsvExporter

        Public Function Read(filePath As String, encodingName As String, delimiter As String, hasHeader As Boolean) As IReadOnlyList(Of IReadOnlyDictionary(Of String, String)) Implements ICsvFileAdapter.Read
            Dim result As New List(Of IReadOnlyDictionary(Of String, String))()
            Dim encoding = ResolveEncoding(encodingName)
            Dim config = New CsvConfiguration(CultureInfo.InvariantCulture) With {.Delimiter = delimiter, .HasHeaderRecord = hasHeader}
            Using reader = New StreamReader(filePath, encoding, detectEncodingFromByteOrderMarks:=True)
                Using csv = New CsvReader(reader, config)
                    Dim headers As String() = Nothing
                    If hasHeader Then
                        If Not csv.Read() Then Return result
                        csv.ReadHeader()
                        headers = csv.HeaderRecord
                    End If

                    While csv.Read()
                        Dim fields = csv.Parser.Record
                        If headers Is Nothing Then headers = Enumerable.Range(1, fields.Length).Select(Function(index) $"Column{index}").ToArray()
                        Dim row As New Dictionary(Of String, String)(StringComparer.Ordinal)
                        For index = 0 To fields.Length - 1
                            row(headers(index)) = fields(index)
                            row($"Column{index + 1}") = fields(index)
                        Next
                        result.Add(row)
                    End While
                End Using
            End Using
            Return result
        End Function

        Public Sub Write(filePath As String, rows As IEnumerable(Of IReadOnlyDictionary(Of String, String)), encodingName As String, delimiter As String, includeHeader As Boolean) Implements ICsvFileAdapter.Write
            Dim encoding = ResolveEncoding(encodingName)
            Dim config = New CsvConfiguration(CultureInfo.InvariantCulture) With {.Delimiter = delimiter, .HasHeaderRecord = includeHeader}
            Using writer = New StreamWriter(filePath, append:=False, encoding:=encoding)
                Using csv = New CsvWriter(writer, config)
                    Dim firstRow = rows.FirstOrDefault()
                    If firstRow Is Nothing Then Return
                    Dim headers = firstRow.Keys.OrderBy(Function(key) key, StringComparer.Ordinal).ToList()
                    If includeHeader Then
                        For Each header In headers
                            csv.WriteField(header)
                        Next
                        csv.NextRecord()
                    End If
                    WriteDictionaryRow(csv, firstRow, headers)
                    For Each row In rows.Skip(1)
                        WriteDictionaryRow(csv, row, headers)
                    Next
                End Using
            End Using
        End Sub

        Public Sub Write(filePath As String, headers As IReadOnlyList(Of String), rows As IEnumerable(Of IReadOnlyList(Of String)), config As CsvImportConfig) Implements ICsvExporter.Write
            Dim encoding = ResolveEncoding(config.Encoding)
            Dim csvConfig = New CsvConfiguration(CultureInfo.InvariantCulture) With {.Delimiter = config.Delimiter, .HasHeaderRecord = config.HasHeader}
            Using writer = New StreamWriter(filePath, append:=False, encoding:=encoding)
                Using csv = New CsvWriter(writer, csvConfig)
                    If config.HasHeader Then
                        For Each header In headers
                            csv.WriteField(header)
                        Next
                        csv.NextRecord()
                    End If
                    For Each row In rows
                        For Each field In row
                            csv.WriteField(field)
                        Next
                        csv.NextRecord()
                    Next
                End Using
            End Using
        End Sub

        Private Shared Sub WriteDictionaryRow(csv As CsvWriter, row As IReadOnlyDictionary(Of String, String), headers As IReadOnlyList(Of String))
            For Each header In headers
                Dim value As String = Nothing
                row.TryGetValue(header, value)
                csv.WriteField(value)
            Next
            csv.NextRecord()
        End Sub

        Private Shared Function ResolveEncoding(name As String) As Encoding
            Select Case name.ToUpperInvariant()
                Case "UTF-8", "UTF8" : Return New UTF8Encoding(False)
                Case "SHIFT_JIS", "SHIFT-JIS", "SJIS" : Return Encoding.GetEncoding(932)
                Case "UNICODE", "UTF-16", "UTF16" : Return Encoding.Unicode
                Case Else : Return New UTF8Encoding(False)
            End Select
        End Function
    End Class
End Namespace
