Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Csv
Imports System.Globalization
Imports System.IO
Imports Xunit

Public Class CsvExportServiceTests
    <Fact>
    Public Sub Export_ThreeResults_WritesThreeDataRowsAndOperationLog()
        Using output = New ExportTestOutput()
            Dim logWriter As New LogWriterStub()
            Dim result = New CsvExportService(New CsvHelperAdapter(), logWriter).Export(output.FilePath, Measurements(), DefaultConfig(True, ","), StandardMappings())

            Assert.Equal(3, result.ExportedCount) ' EXPORT-001
            Assert.True(result.ElapsedMs >= 0)
            Assert.Equal(3, New CsvHelperAdapter().Read(output.FilePath, "UTF-8", ",", True).Count)
            Assert.Equal("CSV出力", Assert.Single(logWriter.Operations).OperationType)
            Assert.Equal(3, logWriter.Operations(0).TotalCount)
        End Using
    End Sub

    <Fact>
    Public Sub Export_HeaderSetting_ControlsHeaderOutput()
        Using headerOutput = New ExportTestOutput(), noHeaderOutput = New ExportTestOutput()
            Dim service = New CsvExportService(New CsvHelperAdapter(), Nothing)
            service.Export(headerOutput.FilePath, Measurements(), DefaultConfig(True, ","), StandardMappings())
            service.Export(noHeaderOutput.FilePath, Measurements(), DefaultConfig(False, ","), StandardMappings())

            Assert.StartsWith("取得日時,設備ID", File.ReadAllLines(headerOutput.FilePath)(0)) ' EXPORT-002
            Assert.False(File.ReadAllLines(noHeaderOutput.FilePath)(0).StartsWith("取得日時", StringComparison.Ordinal)) ' EXPORT-003
        End Using
    End Sub

    <Fact>
    Public Sub Export_AppliesDelimiterAndColumnMappingOrder()
        Using output = New ExportTestOutput()
            Dim mappings As IReadOnlyList(Of CsvColumnMapping) = {
                New CsvColumnMapping With {.FieldName = StandardFields.EquipmentId, .CsvColumnIndex = 1},
                New CsvColumnMapping With {.FieldName = StandardFields.AcquiredAt, .CsvColumnIndex = 2},
                New CsvColumnMapping With {.FieldName = StandardFields.Value, .CsvColumnIndex = 3},
                New CsvColumnMapping With {.FieldName = StandardFields.ItemName, .CsvColumnIndex = 4},
                New CsvColumnMapping With {.FieldName = StandardFields.EquipmentName, .CsvColumnIndex = 5},
                New CsvColumnMapping With {.FieldName = StandardFields.Unit, .CsvColumnIndex = 6}}

            Dim service = New CsvExportService(New CsvHelperAdapter(), Nothing)
            service.Export(output.FilePath, Measurements(), DefaultConfig(True, ";"), mappings)

            Dim lines = File.ReadAllLines(output.FilePath)
            Assert.Equal("設備ID;取得日時;測定値;項目名;設備名;単位", lines(0)) ' EXPORT-004, EXPORT-005
            Assert.StartsWith("EQ001;", lines(1))
        End Using
    End Sub

    <Fact>
    Public Sub Export_Utf8AndSpecialCharacters_RoundTripsThroughExistingReader()
        Using output = New ExportTestOutput()
            Dim records = Measurements().ToList()
            records(0).EquipmentName = "加工装置,第1ライン"
            Dim service = New CsvExportService(New CsvHelperAdapter(), Nothing)
            service.Export(output.FilePath, records, DefaultConfig(True, ","), StandardMappings())

            Dim fileText = File.ReadAllText(output.FilePath, New Text.UTF8Encoding(False))
            Dim readRows = New CsvHelperAdapter().Read(output.FilePath, "UTF-8", ",", True)
            Assert.Contains("加工装置,第1ライン", fileText) ' EXPORT-006
            Assert.Equal("加工装置,第1ライン", readRows(0)(StandardFields.EquipmentName)) ' EXPORT-007, EXPORT-009
        End Using
    End Sub

    <Fact>
    Public Sub Export_DoesNotRoundMeasurementValue()
        Using output = New ExportTestOutput()
            Dim records = Measurements().ToList()
            records(0).Value = 12.3456789
            Dim service = New CsvExportService(New CsvHelperAdapter(), Nothing)
            service.Export(output.FilePath, records, DefaultConfig(True, ","), StandardMappings())

            Dim valueText = New CsvHelperAdapter().Read(output.FilePath, "UTF-8", ",", True)(0)(StandardFields.Value)
            Assert.Equal(12.3456789, Double.Parse(valueText, CultureInfo.InvariantCulture)) ' EXPORT-008
        End Using
    End Sub

    Private Shared Function Measurements() As IReadOnlyList(Of MeasurementData)
        Return {
            New MeasurementData With {.Id = 1, .MeasuredAt = New DateTime(2026, 9, 21, 12, 34, 56, DateTimeKind.Utc), .EquipmentId = "EQ001", .EquipmentName = "搬送装置01", .ItemName = "温度", .Value = 12.5, .Unit = "℃"},
            New MeasurementData With {.Id = 2, .MeasuredAt = New DateTime(2026, 9, 21, 12, 35, 56, DateTimeKind.Utc), .EquipmentId = "EQ002", .EquipmentName = "加工装置01", .ItemName = "圧力", .Value = 1.25, .Unit = "MPa"},
            New MeasurementData With {.Id = 3, .MeasuredAt = New DateTime(2026, 9, 21, 12, 36, 56, DateTimeKind.Utc), .EquipmentId = "EQ003", .EquipmentName = "組立装置01", .ItemName = "振動", .Value = 0.125, .Unit = "mm/s"}}
    End Function

    Private Shared Function DefaultConfig(hasHeader As Boolean, delimiter As String) As CsvImportConfig
        Return New CsvImportConfig With {.Encoding = "UTF-8", .Delimiter = delimiter, .HasHeader = hasHeader}
    End Function

    Private Shared Function StandardMappings() As IReadOnlyList(Of CsvColumnMapping)
        Return {
            New CsvColumnMapping With {.FieldName = StandardFields.AcquiredAt, .CsvColumnIndex = 1},
            New CsvColumnMapping With {.FieldName = StandardFields.EquipmentId, .CsvColumnIndex = 2},
            New CsvColumnMapping With {.FieldName = StandardFields.EquipmentName, .CsvColumnIndex = 3},
            New CsvColumnMapping With {.FieldName = StandardFields.ItemName, .CsvColumnIndex = 4},
            New CsvColumnMapping With {.FieldName = StandardFields.Value, .CsvColumnIndex = 5},
            New CsvColumnMapping With {.FieldName = StandardFields.Unit, .CsvColumnIndex = 6}}
    End Function

    Private NotInheritable Class LogWriterStub
        Implements IImportLogWriter
        Public ReadOnly Property Operations As New List(Of OperationLog)()
        Public Sub WriteOperation(log As OperationLog) Implements IImportLogWriter.WriteOperation
            Operations.Add(log)
        End Sub
        Public Sub WriteErrors(errors As IEnumerable(Of ErrorLog)) Implements IImportLogWriter.WriteErrors
            Throw New NotSupportedException()
        End Sub
    End Class

    Private NotInheritable Class ExportTestOutput
        Implements IDisposable
        Private ReadOnly _directoryPath As String = Path.Combine(Path.GetTempPath(), $"ManufacturingDataApp.ExportTests.{Guid.NewGuid():N}")
        Public Sub New()
            Directory.CreateDirectory(_directoryPath)
            FilePath = Path.Combine(_directoryPath, "output.csv")
        End Sub
        Public ReadOnly Property FilePath As String
        Public Sub Dispose() Implements IDisposable.Dispose
            If Directory.Exists(_directoryPath) Then Directory.Delete(_directoryPath, recursive:=True)
        End Sub
    End Class
End Class
