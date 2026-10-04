Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Application.Validators
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Csv
Imports ManufacturingDataApp.Infrastructure.Data
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports System.IO
Imports Xunit

Public Class CsvConfigIntegrationTests
    <Fact>
    Public Sub DatabaseConfig_ControlsCsvImportColumnOrder()
        Using database = New ConfigCsvTestDatabase()
            Dim config = database.SaveReorderedConfig()
            Dim sourcePath = Path.Combine(database.DirectoryPath, "input.csv")
            File.WriteAllText(sourcePath, "A,B,C,D,E,F" & Environment.NewLine & "EQ001,12.5,2026-09-21T12:34:56.0000000Z,温度,℃,搬送装置01", New Text.UTF8Encoding(False))
            Dim request As New CsvImportRequestDto With {.FilePath = sourcePath, .Config = config}
            request.ColumnMappings.AddRange(database.ConfigService.GetMappings(config.ConfigId))

            Dim result = database.ImportService.Import(request) ' CONFIG-CSV-001, CONFIG-CSV-003

            Dim measurement = Assert.Single(result.ValidMeasurements)
            Assert.Equal("EQ001", measurement.EquipmentId)
            Assert.Equal(12.5, measurement.Value)
        End Using
    End Sub

    <Fact>
    Public Sub DatabaseConfig_ControlsCsvExportColumnOrder()
        Using database = New ConfigCsvTestDatabase()
            Dim config = database.SaveReorderedConfig()
            Dim outputPath = Path.Combine(database.DirectoryPath, "output.csv")
            Dim records As IReadOnlyList(Of MeasurementData) = {New MeasurementData With {.MeasuredAt = New DateTime(2026, 9, 21, 12, 34, 56, DateTimeKind.Utc), .EquipmentId = "EQ001", .EquipmentName = "搬送装置01", .ItemName = "温度", .Value = 12.5, .Unit = "℃"}}

            database.ExportService.Export(outputPath, records, config, database.ConfigService.GetMappings(config.ConfigId)) ' CONFIG-CSV-002, CONFIG-CSV-004

            Assert.StartsWith("設備ID,測定値,取得日時", File.ReadAllLines(outputPath)(0))
        End Using
    End Sub

    Private NotInheritable Class ConfigCsvTestDatabase
        Implements IDisposable
        Public ReadOnly DirectoryPath As String = Path.Combine(Path.GetTempPath(), $"ManufacturingDataApp.ConfigCsvTests.{Guid.NewGuid():N}")
        Public Sub New()
            Dim factory = New DatabaseConnectionFactory(Path.Combine(DirectoryPath, "test.db"))
            Dim initializer = New DatabaseInitializer(factory)
            initializer.Initialize()
            ConfigService = New CsvConfigService(New CsvImportConfigRepository(factory))
            ImportService = New CsvImportService(New CsvHelperAdapter(), New ValidationService(), New MeasurementItemRepository(factory))
            ExportService = New CsvExportService(New CsvHelperAdapter(), Nothing)
        End Sub
        Public ReadOnly Property ConfigService As CsvConfigService
        Public ReadOnly Property ImportService As CsvImportService
        Public ReadOnly Property ExportService As CsvExportService
        Public Function SaveReorderedConfig() As CsvImportConfig
            Dim config As New CsvImportConfig With {.ConfigName = "PLC_REORDERED", .Encoding = "UTF-8", .Delimiter = ",", .HasHeader = True}
            Dim mappings As IReadOnlyList(Of CsvColumnMapping) = {
                New CsvColumnMapping With {.FieldName = StandardFields.EquipmentId, .CsvColumnIndex = 1},
                New CsvColumnMapping With {.FieldName = StandardFields.Value, .CsvColumnIndex = 2},
                New CsvColumnMapping With {.FieldName = StandardFields.AcquiredAt, .CsvColumnIndex = 3},
                New CsvColumnMapping With {.FieldName = StandardFields.ItemName, .CsvColumnIndex = 4},
                New CsvColumnMapping With {.FieldName = StandardFields.Unit, .CsvColumnIndex = 5},
                New CsvColumnMapping With {.FieldName = StandardFields.EquipmentName, .CsvColumnIndex = 6}}
            ConfigService.Save(config, mappings)
            Return config
        End Function
        Public Sub Dispose() Implements IDisposable.Dispose
            SqliteConnection.ClearAllPools()
            If Directory.Exists(DirectoryPath) Then Directory.Delete(DirectoryPath, recursive:=True)
        End Sub
    End Class
End Class
