Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Data
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports System.IO
Imports Xunit

Public Class RepositoryTests
    <Fact>
    Public Sub Equipment_CanAddFindUpdateAndDelete()
        Using database = New RepositoryTestDatabase()
            Dim repository = New EquipmentRepository(database.Factory)
            Dim equipment = New EquipmentMaster With {.EquipmentId = "EQ-TEST", .EquipmentName = "試験設備", .FactoryName = Nothing, .LineName = "L-1", .CreatedAt = DateTime.UtcNow}

            repository.Add(equipment)
            Dim found = repository.FindById("EQ-TEST")
            Assert.NotNull(found)
            Assert.Null(found.FactoryName)

            found.EquipmentName = "更新済み設備"
            repository.Update(found)
            Assert.Equal("更新済み設備", repository.FindById("EQ-TEST").EquipmentName)

            repository.Delete("EQ-TEST")
            Assert.Null(repository.FindById("EQ-TEST"))
        End Using
    End Sub

    <Fact>
    Public Sub MeasurementItem_CanAddFindUpdateAndDelete()
        Using database = New RepositoryTestDatabase()
            Dim repository = New MeasurementItemRepository(database.Factory)
            Dim item = New MeasurementItemMaster With {.ItemName = "試験項目", .Unit = "V", .MinValue = Nothing, .MaxValue = 20.0}

            repository.Add(item)
            Assert.True(item.ItemId > 0)
            Assert.Null(repository.FindById(item.ItemId).MinValue)

            item.Unit = "mV"
            repository.Update(item)
            Assert.Equal("mV", repository.FindById(item.ItemId).Unit)
            repository.Delete(item.ItemId)
            Assert.Null(repository.FindById(item.ItemId))
        End Using
    End Sub

    <Fact>
    Public Sub MeasurementData_CanAddFindSearchUpdateAndDelete()
        Using database = New RepositoryTestDatabase()
            Dim repository = New MeasurementDataRepository(database.Factory)
            Dim data = New MeasurementData With {.MeasuredAt = New DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc), .EquipmentId = "EQ001", .EquipmentName = "搬送装置01", .ItemName = "温度", .Value = 12.5, .Unit = "℃"}

            repository.Add(data)
            Assert.True(data.Id > 0)
            Assert.Equal(12.5, repository.FindById(data.Id).Value)
            Assert.Single(repository.Search("EQ001", "温度", data.MeasuredAt.AddMinutes(-1), data.MeasuredAt.AddMinutes(1)))

            data.Value = 13.5
            repository.Update(data)
            Assert.Equal(13.5, repository.FindById(data.Id).Value)
            repository.Delete({data.Id})
            Assert.Null(repository.FindById(data.Id))
        End Using
    End Sub

    <Fact>
    Public Sub MeasurementData_AddRange_CommitsAllRecords()
        Using database = New RepositoryTestDatabase()
            Dim repository = New MeasurementDataRepository(database.Factory)
            Dim values = {
                NewMeasurement(New DateTime(2026, 1, 2, 9, 0, 0, DateTimeKind.Utc), "圧力"),
                NewMeasurement(New DateTime(2026, 1, 2, 9, 1, 0, DateTimeKind.Utc), "圧力")
            }

            repository.AddRange(values)
            Assert.All(values, Sub(value) Assert.True(value.Id > 0))
            Assert.Equal(2, repository.Search("EQ001", "圧力", Nothing, Nothing).Count)
        End Using
    End Sub

    <Fact>
    Public Sub MeasurementData_DuplicateKeyIsRejectedAndBatchRollsBack()
        Using database = New RepositoryTestDatabase()
            Dim repository = New MeasurementDataRepository(database.Factory)
            Dim measuredAt = New DateTime(2026, 1, 3, 9, 0, 0, DateTimeKind.Utc)
            repository.Add(NewMeasurement(measuredAt, "振動"))
            Assert.Throws(Of SqliteException)(Sub() repository.Add(NewMeasurement(measuredAt, "振動")))

            Dim batch = {NewMeasurement(measuredAt.AddMinutes(1), "振動"), NewMeasurement(measuredAt, "振動")}
            Assert.Throws(Of SqliteException)(Sub() repository.AddRange(batch))
            Assert.Empty(repository.Search("EQ001", "振動", measuredAt.AddSeconds(1), Nothing))
        End Using
    End Sub

    <Fact>
    Public Sub MeasurementData_Search_AppliesAllSpecifiedConditionsAndDateBoundaries()
        Using database = New RepositoryTestDatabase()
            Dim repository = New MeasurementDataRepository(database.Factory)
            Dim day = New DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
            repository.AddRange({
                NewMeasurement(day, "温度"),
                NewMeasurement(day.AddHours(12), "圧力"),
                NewMeasurement(day.AddHours(23).AddMinutes(59).AddSeconds(59), "温度"),
                NewMeasurement(day.AddDays(1), "温度")})
            repository.Add(New MeasurementData With {.MeasuredAt = day.AddHours(10), .EquipmentId = "EQ002", .EquipmentName = "設備02", .ItemName = "温度", .Value = 2, .Unit = "℃"})

            Assert.Equal(5, repository.Search(Nothing, Nothing, Nothing, Nothing).Count) ' SEARCH-001
            Assert.Equal(4, repository.Search("EQ001", Nothing, Nothing, Nothing).Count) ' SEARCH-002
            Assert.Equal(4, repository.Search(Nothing, "温度", Nothing, Nothing).Count) ' SEARCH-003
            Assert.Equal(5, repository.Search(Nothing, Nothing, day, Nothing).Count) ' SEARCH-004
            Assert.Equal(4, repository.Search(Nothing, Nothing, Nothing, day.AddDays(1)).Count) ' SEARCH-005
            Assert.Single(repository.Search("EQ001", "圧力", day, day.AddDays(1))) ' SEARCH-006
            Assert.Empty(repository.Search("UNKNOWN", Nothing, Nothing, Nothing)) ' SEARCH-007

            Dim boundaryResults = repository.Search("EQ001", "温度", day, day.AddDays(1)) ' SEARCH-008
            Assert.Equal(2, boundaryResults.Count)
            Assert.Contains(boundaryResults, Function(item) item.MeasuredAt = day)
            Assert.Contains(boundaryResults, Function(item) item.MeasuredAt = day.AddHours(23).AddMinutes(59).AddSeconds(59))
            Assert.DoesNotContain(boundaryResults, Function(item) item.MeasuredAt = day.AddDays(1))
        End Using
    End Sub

    <Fact>
    Public Sub MeasurementData_DeleteMultipleRecords_DeletesAllSelectedRecords()
        Using database = New RepositoryTestDatabase()
            Dim repository = New MeasurementDataRepository(database.Factory)
            Dim first = NewMeasurement(New DateTime(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc), "温度")
            Dim second = NewMeasurement(first.MeasuredAt.AddMinutes(1), "温度")
            repository.AddRange({first, second})

            repository.Delete({first.Id, second.Id}) ' DELETE-002

            Assert.Null(repository.FindById(first.Id))
            Assert.Null(repository.FindById(second.Id))
        End Using
    End Sub

    <Fact>
    Public Sub MeasurementData_Delete_RollsBackWhenDatabaseErrorOccursMidTransaction()
        Using database = New RepositoryTestDatabase()
            Dim repository = New MeasurementDataRepository(database.Factory)
            Dim first = NewMeasurement(New DateTime(2026, 9, 23, 9, 0, 0, DateTimeKind.Utc), "温度")
            Dim second = NewMeasurement(first.MeasuredAt.AddMinutes(1), "温度")
            repository.AddRange({first, second})
            Using connection = database.Factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = $"CREATE TRIGGER RejectSecondDelete BEFORE DELETE ON MeasurementData WHEN OLD.Id = {second.Id} BEGIN SELECT RAISE(ABORT, 'test delete failure'); END;"
                    command.ExecuteNonQuery()
                End Using
            End Using

            Assert.Throws(Of SqliteException)(Sub() repository.Delete({first.Id, second.Id})) ' DELETE-003
            Assert.NotNull(repository.FindById(first.Id))
            Assert.NotNull(repository.FindById(second.Id))
        End Using
    End Sub

    <Fact>
    Public Sub MeasurementData_UpdateMissingId_IsNotTreatedAsSuccess()
        Using database = New RepositoryTestDatabase()
            Dim repository = New MeasurementDataRepository(database.Factory)
            Dim missing As New MeasurementData With {.Id = 99999, .MeasuredAt = New DateTime(2026, 9, 24, 9, 0, 0, DateTimeKind.Utc), .EquipmentId = "EQ001", .EquipmentName = "搬送装置01", .ItemName = "温度", .Value = 10, .Unit = "℃"}

            Assert.Throws(Of InvalidOperationException)(Sub() repository.Update(missing)) ' UPDATE-NOTFOUND-001
        End Using
    End Sub

    Private Shared Function NewMeasurement(measuredAt As DateTime, itemName As String) As MeasurementData
        Return New MeasurementData With {.MeasuredAt = measuredAt, .EquipmentId = "EQ001", .EquipmentName = "搬送装置01", .ItemName = itemName, .Value = 1.0, .Unit = "テスト"}
    End Function

    Private NotInheritable Class RepositoryTestDatabase
        Implements IDisposable

        Private ReadOnly _directoryPath As String = Path.Combine(Path.GetTempPath(), $"ManufacturingDataApp.RepositoryTests.{Guid.NewGuid():N}")

        Public Sub New()
            Factory = New DatabaseConnectionFactory(Path.Combine(_directoryPath, "ManufacturingDataApp.db"))
            Dim initializer = New DatabaseInitializer(Factory)
            initializer.Initialize()
        End Sub

        Public ReadOnly Property Factory As DatabaseConnectionFactory

        Public Sub Dispose() Implements IDisposable.Dispose
            SqliteConnection.ClearAllPools()
            If Directory.Exists(_directoryPath) Then Directory.Delete(_directoryPath, recursive:=True)
        End Sub
    End Class
End Class
