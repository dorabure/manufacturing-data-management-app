Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Application.Validators
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Data
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports System.IO
Imports Xunit

Public Class MeasurementDataServiceTests
    <Fact>
    Public Sub FindById_ReturnsRecordAndNothingForMissingId()
        Using database = New ServiceTestDatabase()
            Dim record = database.AddMeasurement(New DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc), "温度", 10)

            Assert.Equal(record.Id, database.Service.FindById(record.Id).Id) ' EDIT-001
            Assert.Null(database.Service.FindById(99999)) ' EDIT-002
        End Using
    End Sub

    <Fact>
    Public Sub Update_ValidData_PersistsChanges()
        Using database = New ServiceTestDatabase()
            Dim record = database.AddMeasurement(New DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc), "温度", 10)
            record.Value = 20

            database.Service.Update(record) ' EDIT-003

            Assert.Equal(20, database.Repository.FindById(record.Id).Value)
        End Using
    End Sub

    <Fact>
    Public Sub ValidateForUpdate_RejectsRequiredTypeAndRangeErrorsWithoutUpdating()
        Using database = New ServiceTestDatabase()
            Dim record = database.AddMeasurement(New DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc), "温度", 10)
            record.EquipmentId = ""
            Assert.Contains(database.Service.ValidateForUpdate(record), Function(errorItem) errorItem.ErrorType = ErrorTypes.Required) ' EDIT-004

            record.EquipmentId = "EQ001"
            record.Value = Double.NaN
            Assert.Contains(database.Service.ValidateForUpdate(record), Function(errorItem) errorItem.ErrorType = ErrorTypes.Type) ' EDIT-005

            record.Value = 101
            Assert.Contains(database.Service.ValidateForUpdate(record), Function(errorItem) errorItem.ErrorType = ErrorTypes.Range) ' EDIT-006
            Assert.Equal(10, database.Repository.FindById(record.Id).Value)
        End Using
    End Sub

    <Fact>
    Public Sub ValidateForUpdate_RejectsAnotherRecordDuplicateButAllowsOwnKey()
        Using database = New ServiceTestDatabase()
            Dim first = database.AddMeasurement(New DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc), "温度", 10)
            Dim second = database.AddMeasurement(first.MeasuredAt.AddMinutes(1), "温度", 11)

            second.MeasuredAt = first.MeasuredAt
            Assert.Contains(database.Service.ValidateForUpdate(second), Function(errorItem) errorItem.ErrorType = ErrorTypes.Duplicate) ' EDIT-007
            second.MeasuredAt = first.MeasuredAt.AddMinutes(1)
            Assert.Empty(database.Service.ValidateForUpdate(second)) ' EDIT-008
            database.Service.Update(second)
            Assert.Equal(11, database.Repository.FindById(second.Id).Value)
        End Using
    End Sub

    Private NotInheritable Class ServiceTestDatabase
        Implements IDisposable

        Private ReadOnly _directoryPath As String = Path.Combine(Path.GetTempPath(), $"ManufacturingDataApp.ServiceTests.{Guid.NewGuid():N}")

        Public Sub New()
            Dim factory = New DatabaseConnectionFactory(Path.Combine(_directoryPath, "test.db"))
            Dim initializer = New DatabaseInitializer(factory)
            initializer.Initialize()
            Repository = New MeasurementDataRepository(factory)
            Service = New MeasurementDataService(Repository, New MeasurementItemRepository(factory), New ValidationService(), Nothing)
        End Sub

        Public ReadOnly Property Repository As MeasurementDataRepository
        Public ReadOnly Property Service As MeasurementDataService

        Public Function AddMeasurement(measuredAt As DateTime, itemName As String, value As Double) As MeasurementData
            Dim record As New MeasurementData With {.MeasuredAt = measuredAt, .EquipmentId = "EQ001", .EquipmentName = "搬送装置01", .ItemName = itemName, .Value = value, .Unit = "℃"}
            Repository.Add(record)
            Return record
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            SqliteConnection.ClearAllPools()
            If Directory.Exists(_directoryPath) Then Directory.Delete(_directoryPath, recursive:=True)
        End Sub
    End Class
End Class
