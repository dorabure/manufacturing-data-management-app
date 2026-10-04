Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Application.Validators
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Csv
Imports ManufacturingDataApp.Infrastructure.Data
Imports ManufacturingDataApp.Infrastructure.Logging
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports System.IO
Imports Xunit

Public Class BusinessFlowIntegrationTests
    <Fact>
    Public Sub SearchIds_FindDetailsAndDeleteOneOfThreePersistedRecords()
        Using db = New FlowDatabase()
            db.Import("2026-09-21T09:00:00.0000000Z,EQ001,搬送装置01,温度,10,℃", "2026-09-21T09:01:00.0000000Z,EQ002,加工装置01,温度,11,℃", "2026-09-21T09:02:00.0000000Z,EQ003,組立装置01,温度,12,℃")
            Dim records = db.Service.Search(Nothing, Nothing, Nothing, Nothing)
            Assert.Equal(3, records.Count)
            Assert.Equal(3, records.Select(Function(item) item.Id).Distinct().Count())
            For Each item In records
                Assert.True(item.Id > 0)
                Dim detail = db.Service.FindById(item.Id)
                Assert.NotNull(detail)
                Assert.Equal(item.Id, detail.Id)
                Assert.Equal(item.EquipmentId, detail.EquipmentId)
                Assert.Equal(item.MeasuredAt, detail.MeasuredAt)
                Assert.Equal(item.Value, detail.Value)
            Next
            Dim target = records(1)
            db.Service.Delete({target.Id})
            Dim remaining = db.Service.Search(Nothing, Nothing, Nothing, Nothing)
            Assert.Equal(2, remaining.Count)
            Assert.Null(db.Service.FindById(target.Id))
            Assert.Equal(records.Where(Function(item) item.Id <> target.Id).Select(Function(item) item.Id), remaining.Select(Function(item) item.Id))
        End Using
    End Sub
    <Fact>
    Public Sub ImportNormalCsv_PersistsSearchesAndWritesOperationLog()
        Using db = New FlowDatabase()
            Dim result = db.Import("2026-09-21T09:00:00.0000000Z,EQ001,搬送装置01,温度,10,℃", "2026-09-21T09:01:00.0000000Z,EQ002,加工装置01,温度,11,℃", "2026-09-21T09:02:00.0000000Z,EQ003,組立装置01,温度,12,℃")
            Assert.Equal(3, result.TotalCount) : Assert.Equal(3, result.SuccessCount) : Assert.Equal(0, result.FailureCount)
            Dim records = db.Measurements.Search(Nothing, Nothing, Nothing, Nothing)
            Assert.Equal(3, records.Count)
            Dim first = Assert.Single(records.Where(Function(item) item.EquipmentId = "EQ001"))
            Assert.Equal("搬送装置01", first.EquipmentName) : Assert.Equal("温度", first.ItemName) : Assert.Equal(10, first.Value) : Assert.Equal("℃", first.Unit) : Assert.Equal(New DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc), first.MeasuredAt)
            Dim log = Assert.Single(db.Operations.Search(Nothing, Nothing, "CSV取込"))
            Assert.Equal(3, log.TotalCount) : Assert.Equal(3, log.SuccessCount) : Assert.True(log.ElapsedMs >= 0)
        End Using
    End Sub
    <Fact>
    Public Sub ImportMixedCsv_PersistsErrorsAndOnlyValidMeasurements()
        Using db = New FlowDatabase()
            Dim result = db.Import("2026-09-21T09:00:00.0000000Z,EQ001,搬送装置01,温度,10,℃", "2026-09-21T09:01:00.0000000Z,EQ002,加工装置01,温度,11,℃", "2026-09-21T09:02:00.0000000Z,EQ003,組立装置01,温度,12,℃", "2026-09-21T09:03:00.0000000Z,EQ001,搬送装置01,温度,bad,℃", "2026-09-21T09:04:00.0000000Z,,搬送装置01,温度,10,℃")
            Assert.Equal(5, result.TotalCount) : Assert.Equal(3, result.SuccessCount) : Assert.Equal(2, result.FailureCount)
            Assert.Equal(3, db.Measurements.Search(Nothing, Nothing, Nothing, Nothing).Count)
            Dim errors = db.Errors.Search(Nothing, Nothing, "flow.csv", Nothing)
            Assert.True(errors.Count >= 2)
            Assert.Contains(errors, Function(item) item.RowNumber.HasValue AndAlso item.RowNumber.Value = 5 AndAlso item.ErrorType = ErrorTypes.Type)
            Assert.Contains(errors, Function(item) item.RowNumber.HasValue AndAlso item.RowNumber.Value = 6 AndAlso item.ErrorType = ErrorTypes.Required)
            Dim log = Assert.Single(db.Operations.Search(Nothing, Nothing, "CSV取込"))
            Assert.Equal(5, log.TotalCount) : Assert.Equal(3, log.SuccessCount) : Assert.Equal(2, log.FailureCount)
        End Using
    End Sub
    <Fact>
    Public Sub EditSearchAndLog_PersistsOnlyTargetChange()
        Using db = New FlowDatabase()
            db.Import("2026-09-21T09:00:00.0000000Z,EQ001,搬送装置01,温度,10,℃", "2026-09-21T09:01:00.0000000Z,EQ002,加工装置01,温度,11,℃")
            Dim target = Assert.Single(db.Service.Search("EQ001", "温度", Nothing, Nothing))
            target.Value = 20 : db.Service.Update(target)
            Assert.Equal(20, Assert.Single(db.Service.Search("EQ001", "温度", Nothing, Nothing)).Value)
            Assert.Equal(11, Assert.Single(db.Service.Search("EQ002", "温度", Nothing, Nothing)).Value)
            Dim log = Assert.Single(db.Operations.Search(Nothing, Nothing, "編集"))
            Assert.Equal(1, log.TotalCount) : Assert.Equal(1, log.SuccessCount) : Assert.Equal(0, log.FailureCount) : Assert.True(log.ElapsedMs >= 0)
        End Using
    End Sub
    <Fact>
    Public Sub DeleteMultipleSearchAndLog_DeletesOnlySpecifiedRecords()
        Using db = New FlowDatabase()
            db.Import("2026-09-21T09:00:00.0000000Z,EQ001,搬送装置01,温度,10,℃", "2026-09-21T09:01:00.0000000Z,EQ002,加工装置01,温度,11,℃", "2026-09-21T09:02:00.0000000Z,EQ003,組立装置01,温度,12,℃")
            Dim records = db.Service.Search(Nothing, Nothing, Nothing, Nothing)
            Dim remainingId = records(2).Id
            db.Service.Delete({records(0).Id, records(1).Id})
            Assert.Equal(remainingId, Assert.Single(db.Service.Search(Nothing, Nothing, Nothing, Nothing)).Id)
            Dim log = Assert.Single(db.Operations.Search(Nothing, Nothing, "削除"))
            Assert.Equal(2, log.TotalCount) : Assert.Equal(2, log.SuccessCount) : Assert.Equal(0, log.FailureCount) : Assert.True(log.ElapsedMs >= 0)
        End Using
    End Sub
    <Fact>
    Public Sub ExportSearchResultsAndLog_WritesCsvAndOperationLog()
        Using db = New FlowDatabase()
            db.Import("2026-09-21T09:00:00.0000000Z,EQ001,搬送装置01,温度,10,℃", "2026-09-21T09:01:00.0000000Z,EQ002,加工装置01,温度,11,℃")
            Dim output = System.IO.Path.Combine(db.DirectoryPath, "output.csv")
            Dim result = db.Exporter.Export(output, db.Service.Search(Nothing, Nothing, Nothing, Nothing), New CsvImportConfig With {.Encoding = "UTF-8", .Delimiter = ",", .HasHeader = True}, db.Mappings())
            Assert.True(File.Exists(output)) : Assert.Equal(2, result.ExportedCount)
            Dim log = Assert.Single(db.Operations.Search(Nothing, Nothing, "CSV出力"))
            Assert.Equal(2, log.TotalCount) : Assert.Equal(2, log.SuccessCount) : Assert.Equal(0, log.FailureCount) : Assert.True(log.ElapsedMs >= 0)
        End Using
    End Sub
    Private NotInheritable Class FlowDatabase
        Implements IDisposable
        Private ReadOnly p As String = Path.Combine(Path.GetTempPath(), $"flow-{Guid.NewGuid():N}")
        Private ReadOnly importer As CsvImportService
        Public Sub New()
            Dim f = New DatabaseConnectionFactory(Path.Combine(p, "test.db")) : Dim i = New DatabaseInitializer(f) : i.Initialize()
            Measurements = New MeasurementDataRepository(f) : Dim items = New MeasurementItemRepository(f) : Dim writer = New DbLogWriter(f)
            importer = New CsvImportService(New CsvHelperAdapter(), New ValidationService(), items, Measurements, writer)
            Service = New MeasurementDataService(Measurements, items, New ValidationService(), writer)
            Exporter = New CsvExportService(New CsvHelperAdapter(), writer)
            Errors = New ErrorLogRepository(f) : Operations = New OperationLogRepository(f)
        End Sub
        Public ReadOnly Property Measurements As MeasurementDataRepository
        Public ReadOnly Property Errors As ErrorLogRepository
        Public ReadOnly Property Operations As OperationLogRepository
        Public ReadOnly Property Service As MeasurementDataService
        Public ReadOnly Property Exporter As CsvExportService
        Public ReadOnly Property DirectoryPath As String
            Get
                Return p
            End Get
        End Property
        Public Function Import(ParamArray rows As String()) As CsvImportExecutionResultDto
            Dim filePath = System.IO.Path.Combine(p, "flow.csv") : File.WriteAllText(filePath, "取得日時,設備ID,設備名,項目名,測定値,単位" & Environment.NewLine & String.Join(Environment.NewLine, rows), New Text.UTF8Encoding(False))
            Dim r As New CsvImportRequestDto With {.FilePath = filePath, .Config = New CsvImportConfig With {.Encoding = "UTF-8", .Delimiter = ",", .HasHeader = True}}
            r.ColumnMappings.AddRange({New CsvColumnMapping With {.FieldName = StandardFields.AcquiredAt, .CsvColumnIndex = 1}, New CsvColumnMapping With {.FieldName = StandardFields.EquipmentId, .CsvColumnIndex = 2}, New CsvColumnMapping With {.FieldName = StandardFields.EquipmentName, .CsvColumnIndex = 3}, New CsvColumnMapping With {.FieldName = StandardFields.ItemName, .CsvColumnIndex = 4}, New CsvColumnMapping With {.FieldName = StandardFields.Value, .CsvColumnIndex = 5}, New CsvColumnMapping With {.FieldName = StandardFields.Unit, .CsvColumnIndex = 6}})
            Return importer.ImportAndSave(r)
        End Function
        Public Function Mappings() As IReadOnlyList(Of CsvColumnMapping)
            Return {New CsvColumnMapping With {.FieldName = StandardFields.AcquiredAt, .CsvColumnIndex = 1}, New CsvColumnMapping With {.FieldName = StandardFields.EquipmentId, .CsvColumnIndex = 2}, New CsvColumnMapping With {.FieldName = StandardFields.EquipmentName, .CsvColumnIndex = 3}, New CsvColumnMapping With {.FieldName = StandardFields.ItemName, .CsvColumnIndex = 4}, New CsvColumnMapping With {.FieldName = StandardFields.Value, .CsvColumnIndex = 5}, New CsvColumnMapping With {.FieldName = StandardFields.Unit, .CsvColumnIndex = 6}}
        End Function
        Public Sub Dispose() Implements IDisposable.Dispose
            SqliteConnection.ClearAllPools() : If Directory.Exists(p) Then Directory.Delete(p, recursive:=True)
        End Sub
    End Class
End Class
