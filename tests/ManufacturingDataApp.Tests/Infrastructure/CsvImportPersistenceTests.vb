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

Public Class CsvImportPersistenceTests
    <Fact>
    Public Sub ImportAndSave_NormalThreeRows_ReturnsExpectedCounts()
        Using database = New ImportTestDatabase()
            Dim result = database.Service.ImportAndSave(database.CreateRequest("2026-09-21T09:00:00.0000000Z,EQ001,搬送装置01,温度,10.0,℃", "2026-09-21T09:01:00.0000000Z,EQ002,加工装置01,温度,11.0,℃", "2026-09-21T09:02:00.0000000Z,EQ003,組立装置01,温度,12.0,℃"))

            Assert.Equal(3, result.TotalCount)
            Assert.Equal(3, result.SuccessCount)
            Assert.Equal(0, result.FailureCount)
            Assert.Equal(3, database.MeasurementRepository.Search(Nothing, Nothing, Nothing, Nothing).Count)
        End Using
    End Sub

    <Fact>
    Public Sub IncludedSampleCsv_ImportsWithSeedDataAndWritesOperationLog()
        Using database = New ImportTestDatabase()
            Dim configRepository = New CsvImportConfigRepository(database.Factory)
            Dim config = Assert.Single(configRepository.GetAll())
            Dim request As New CsvImportRequestDto With {.FilePath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample_measurement.csv"), .Config = config}
            request.ColumnMappings.AddRange(configRepository.GetMappings(config.ConfigId))

            Dim result = database.Service.ImportAndSave(request)

            Assert.Equal(3, result.TotalCount)
            Assert.Equal(3, result.SuccessCount)
            Assert.Equal(0, result.FailureCount)
            Assert.Equal(3, database.MeasurementRepository.Search(Nothing, Nothing, Nothing, Nothing).Count)
            Using connection = database.Factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "SELECT SuccessCount FROM OperationLog WHERE OperationType = $operationType;"
                    command.Parameters.AddWithValue("$operationType", "CSV取込")
                    Assert.Equal(3L, CLng(command.ExecuteScalar()))
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub ImportAndSave_ThreeValidAndTwoInvalidRows_ReturnsExpectedCounts()
        Using database = New ImportTestDatabase()
            Dim result = database.Service.ImportAndSave(database.CreateRequest("2026-09-21T09:00:00.0000000Z,EQ001,搬送装置01,温度,10.0,℃", "2026-09-21T09:01:00.0000000Z,EQ002,加工装置01,温度,11.0,℃", "2026-09-21T09:02:00.0000000Z,EQ003,組立装置01,温度,12.0,℃", "2026-09-21T09:03:00.0000000Z,EQ001,搬送装置01,温度,abc,℃", "2026-09-21T09:04:00.0000000Z,,搬送装置01,温度,15.0,℃"))

            Assert.Equal(5, result.TotalCount)
            Assert.Equal(3, result.SuccessCount)
            Assert.Equal(2, result.FailureCount)
        End Using
    End Sub

    <Fact>
    Public Sub ImportAndSave_MultipleErrorsInOneRow_CountsOneFailedRow()
        Using database = New ImportTestDatabase()
            Dim result = database.Service.ImportAndSave(database.CreateRequest(",, , ,abc,"))

            Assert.Equal(1, result.FailureCount)
            Assert.True(result.ValidationResult.Errors.Count > 1)
            Using connection = database.Factory.CreateConnection()
                Assert.True(Count(connection, "ErrorLog") > 1)
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub ImportAndSave_ExistingDatabaseDuplicate_IsLoggedWithSourceRowNumber()
        Using database = New ImportTestDatabase()
            Dim measuredAt = New DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc)
            database.MeasurementRepository.Add(New MeasurementData With {.MeasuredAt = measuredAt, .EquipmentId = "EQ001", .EquipmentName = "搬送装置01", .ItemName = "温度", .Value = 10, .Unit = "℃"})

            Dim result = database.Service.ImportAndSave(database.CreateRequest("2026-09-21T09:00:00.0000000Z,EQ001,搬送装置01,温度,10.0,℃"))

            Assert.Equal(0, result.SuccessCount)
            Assert.Equal(1, result.FailureCount)
            Assert.True(result.ValidationResult.Errors.Any(Function(errorItem) errorItem.ErrorType = ErrorTypes.Duplicate AndAlso errorItem.RowNumber.HasValue AndAlso errorItem.RowNumber.Value = 2))
            Assert.Single(database.MeasurementRepository.Search("EQ001", "温度", Nothing, Nothing))
            Using connection = database.Factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "SELECT RowNumber FROM ErrorLog WHERE ErrorType = $errorType;"
                    command.Parameters.AddWithValue("$errorType", ErrorTypes.Duplicate)
                    Assert.Equal(2L, CLng(command.ExecuteScalar()))
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub ImportAndSave_PersistsValidRowsAndLogsValidationErrors()
        Dim directoryPath = Path.Combine(Path.GetTempPath(), $"ManufacturingDataApp.ImportTests.{Guid.NewGuid():N}")
        Try
            Dim factory = New DatabaseConnectionFactory(Path.Combine(directoryPath, "test.db"))
            Dim initializer = New DatabaseInitializer(factory)
            initializer.Initialize()
            Dim service = New CsvImportService(New CsvHelperAdapter(), New ValidationService(), New MeasurementItemRepository(factory), New MeasurementDataRepository(factory), New DbLogWriter(factory))
            Dim request = New CsvImportRequestDto With {.FilePath = Path.Combine(AppContext.BaseDirectory, "TestData", "required-error.csv"), .Config = New CsvImportConfig With {.Encoding = "UTF-8", .Delimiter = ",", .HasHeader = True}}
            request.ColumnMappings.AddRange({Mapping(StandardFields.AcquiredAt, 1), Mapping(StandardFields.EquipmentId, 2), Mapping(StandardFields.EquipmentName, 3), Mapping(StandardFields.ItemName, 4), Mapping(StandardFields.Value, 5), Mapping(StandardFields.Unit, 6)})

            Dim result = service.ImportAndSave(request)

            Assert.Equal(0, result.SuccessCount)
            Assert.Equal(1, result.FailureCount)
            Assert.True(result.ElapsedMs >= 0)
            Using connection = factory.CreateConnection()
                Assert.Equal(0L, Count(connection, "MeasurementData"))
                Assert.Equal(1L, Count(connection, "ErrorLog"))
                Assert.Equal(1L, Count(connection, "OperationLog"))
            End Using
        Finally
            SqliteConnection.ClearAllPools()
            If Directory.Exists(directoryPath) Then Directory.Delete(directoryPath, recursive:=True)
        End Try
    End Sub

    Private Shared Function Mapping(fieldName As String, columnIndex As Integer) As CsvColumnMapping
        Return New CsvColumnMapping With {.FieldName = fieldName, .CsvColumnIndex = columnIndex}
    End Function

    Private Shared Function Count(connection As SqliteConnection, tableName As String) As Long
        Using command = connection.CreateCommand()
            command.CommandText = $"SELECT COUNT(*) FROM {tableName};"
            Return CLng(command.ExecuteScalar())
        End Using
    End Function

    Private NotInheritable Class ImportTestDatabase
        Implements IDisposable

        Private ReadOnly _directoryPath As String = Path.Combine(Path.GetTempPath(), $"ManufacturingDataApp.ImportTest.{Guid.NewGuid():N}")

        Public Sub New()
            Factory = New DatabaseConnectionFactory(Path.Combine(_directoryPath, "test.db"))
            Dim initializer = New DatabaseInitializer(Factory)
            initializer.Initialize()
            MeasurementRepository = New MeasurementDataRepository(Factory)
            Service = New CsvImportService(New CsvHelperAdapter(), New ValidationService(), New MeasurementItemRepository(Factory), MeasurementRepository, New DbLogWriter(Factory))
        End Sub

        Public ReadOnly Property Factory As DatabaseConnectionFactory
        Public ReadOnly Property MeasurementRepository As MeasurementDataRepository
        Public ReadOnly Property Service As CsvImportService

        Public Function CreateRequest(ParamArray records As String()) As CsvImportRequestDto
            Dim filePath = Path.Combine(_directoryPath, "import.csv")
            File.WriteAllText(filePath, "取得日時,設備ID,設備名,項目名,測定値,単位" & Environment.NewLine & String.Join(Environment.NewLine, records), New Text.UTF8Encoding(False))
            Dim request As New CsvImportRequestDto With {.FilePath = filePath, .Config = New CsvImportConfig With {.Encoding = "UTF-8", .Delimiter = ",", .HasHeader = True}}
            request.ColumnMappings.AddRange({Mapping(StandardFields.AcquiredAt, 1), Mapping(StandardFields.EquipmentId, 2), Mapping(StandardFields.EquipmentName, 3), Mapping(StandardFields.ItemName, 4), Mapping(StandardFields.Value, 5), Mapping(StandardFields.Unit, 6)})
            Return request
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            SqliteConnection.ClearAllPools()
            If Directory.Exists(_directoryPath) Then Directory.Delete(_directoryPath, recursive:=True)
        End Sub
    End Class
End Class
