Imports System.Diagnostics
Imports System.Globalization
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities

Namespace Services
    Public Class CsvExportService
        Private ReadOnly _exporter As ICsvExporter
        Private ReadOnly _logWriter As IImportLogWriter

        Public Sub New(exporter As ICsvExporter, logWriter As IImportLogWriter)
            _exporter = exporter
            _logWriter = logWriter
        End Sub

        Public Function Export(filePath As String, measurements As IReadOnlyList(Of MeasurementData), config As CsvImportConfig, mappings As IReadOnlyList(Of CsvColumnMapping)) As CsvExportResultDto
            If measurements Is Nothing OrElse measurements.Count = 0 Then Throw New InvalidOperationException("出力するデータがありません。")
            Dim orderedMappings = ValidateAndOrderMappings(mappings)
            Dim headers = orderedMappings.Select(Function(mapping) mapping.FieldName).ToList()
            Dim rows = measurements.Select(Function(measurement) CType(orderedMappings.Select(Function(mapping) GetFieldValue(measurement, mapping.FieldName)).ToList(), IReadOnlyList(Of String))).ToList()
            Dim timer = Stopwatch.StartNew()
            _exporter.Write(filePath, headers, rows, config)
            timer.Stop()
            Dim result As New CsvExportResultDto With {.ExportedCount = measurements.Count, .ElapsedMs = timer.ElapsedMilliseconds}
            If _logWriter IsNot Nothing Then
                _logWriter.WriteOperation(New OperationLog With {.ExecutedAt = DateTime.UtcNow, .OperationType = "CSV出力", .ElapsedMs = result.ElapsedMs, .TotalCount = result.ExportedCount, .SuccessCount = result.ExportedCount, .FailureCount = 0})
            End If
            Return result
        End Function

        Private Shared Function ValidateAndOrderMappings(mappings As IReadOnlyList(Of CsvColumnMapping)) As IReadOnlyList(Of CsvColumnMapping)
            If mappings Is Nothing Then Throw New ArgumentException("CSV列設定がありません。")
            Dim requiredFields = {StandardFields.AcquiredAt, StandardFields.EquipmentId, StandardFields.EquipmentName, StandardFields.ItemName, StandardFields.Value, StandardFields.Unit}
            Dim orderedMappings = mappings.OrderBy(Function(mapping) mapping.CsvColumnIndex).ToList()
            If orderedMappings.Count <> requiredFields.Length OrElse orderedMappings.Any(Function(mapping) mapping.CsvColumnIndex < 1) OrElse orderedMappings.Select(Function(mapping) mapping.CsvColumnIndex).Distinct().Count() <> orderedMappings.Count OrElse requiredFields.Any(Function(fieldName) Not orderedMappings.Any(Function(mapping) mapping.FieldName = fieldName)) Then
                Throw New ArgumentException("CSV列設定が不正です。")
            End If
            Return orderedMappings
        End Function

        Private Shared Function GetFieldValue(measurement As MeasurementData, fieldName As String) As String
            Select Case fieldName
                Case StandardFields.AcquiredAt : Return measurement.MeasuredAt.ToString("O", CultureInfo.InvariantCulture)
                Case StandardFields.EquipmentId : Return measurement.EquipmentId
                Case StandardFields.EquipmentName : Return measurement.EquipmentName
                Case StandardFields.ItemName : Return measurement.ItemName
                Case StandardFields.Value : Return measurement.Value.ToString("R", CultureInfo.InvariantCulture)
                Case StandardFields.Unit : Return measurement.Unit
                Case Else : Throw New ArgumentException("未対応のCSV出力項目です。")
            End Select
        End Function
    End Class
End Namespace
