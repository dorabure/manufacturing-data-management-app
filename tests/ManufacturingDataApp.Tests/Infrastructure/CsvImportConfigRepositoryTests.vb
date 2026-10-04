Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Data
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports System.IO
Imports Xunit

Public Class CsvImportConfigRepositoryTests
    <Fact>
    Public Sub GetAllAndFindById_ReturnSeededConfigAndMappings()
        Using database = New ConfigTestDatabase()
            Dim seeded = Assert.Single(database.Repository.GetAll()) ' CONFIG-REP-001
            Assert.Equal("SCADA_A", seeded.ConfigName)
            Assert.Equal(6, database.Repository.GetMappings(seeded.ConfigId).Count) ' CONFIG-REP-002
        End Using
    End Sub

    <Fact>
    Public Sub Save_CreatesAndUpdatesConfigWithMappings()
        Using database = New ConfigTestDatabase()
            Dim config = New CsvImportConfig With {.ConfigName = "PLC_B", .Encoding = "SHIFT-JIS", .Delimiter = ";", .HasHeader = True}
            database.Repository.Save(config, StandardMappings()) ' CONFIG-REP-003
            Assert.True(config.ConfigId > 0)

            config.Delimiter = vbTab
            Dim mappings = StandardMappings()
            mappings(0).CsvColumnIndex = 2
            mappings(1).CsvColumnIndex = 1
            database.Repository.Save(config, mappings) ' CONFIG-REP-004

            Assert.Equal(vbTab, database.Repository.FindById(config.ConfigId).Delimiter)
            Assert.Equal(StandardFields.EquipmentId, database.Repository.GetMappings(config.ConfigId)(0).FieldName)
        End Using
    End Sub

    <Fact>
    Public Sub Delete_CascadesMappingsAndDuplicateConstraintsRejectInvalidData()
        Using database = New ConfigTestDatabase()
            Dim config = New CsvImportConfig With {.ConfigName = "PLC_C", .Encoding = "UTF-8", .Delimiter = ",", .HasHeader = True}
            database.Repository.Save(config, StandardMappings())
            database.Repository.Delete(config.ConfigId) ' CONFIG-REP-005
            Assert.Null(database.Repository.FindById(config.ConfigId))
            Assert.Empty(database.Repository.GetMappings(config.ConfigId))

            Dim duplicateName = New CsvImportConfig With {.ConfigName = "SCADA_A", .Encoding = "UTF-8", .Delimiter = ",", .HasHeader = True}
            Assert.Throws(Of SqliteException)(Sub() database.Repository.Save(duplicateName, StandardMappings())) ' CONFIG-REP-006

            Dim duplicateColumns = StandardMappings()
            duplicateColumns(1).CsvColumnIndex = 1
            Dim invalid = New CsvImportConfig With {.ConfigName = "ROLLBACK", .Encoding = "UTF-8", .Delimiter = ",", .HasHeader = True}
            Assert.Throws(Of SqliteException)(Sub() database.Repository.Save(invalid, duplicateColumns)) ' CONFIG-REP-007, CONFIG-REP-008
            Assert.Null(database.Repository.FindByName("ROLLBACK"))
        End Using
    End Sub

    Private Shared Function StandardMappings() As List(Of CsvColumnMapping)
        Return New List(Of CsvColumnMapping) From {
            New CsvColumnMapping With {.FieldName = StandardFields.AcquiredAt, .CsvColumnIndex = 1},
            New CsvColumnMapping With {.FieldName = StandardFields.EquipmentId, .CsvColumnIndex = 2},
            New CsvColumnMapping With {.FieldName = StandardFields.EquipmentName, .CsvColumnIndex = 3},
            New CsvColumnMapping With {.FieldName = StandardFields.ItemName, .CsvColumnIndex = 4},
            New CsvColumnMapping With {.FieldName = StandardFields.Value, .CsvColumnIndex = 5},
            New CsvColumnMapping With {.FieldName = StandardFields.Unit, .CsvColumnIndex = 6}}
    End Function

    Private NotInheritable Class ConfigTestDatabase
        Implements IDisposable
        Private ReadOnly _directoryPath As String = Path.Combine(Path.GetTempPath(), $"ManufacturingDataApp.ConfigTests.{Guid.NewGuid():N}")
        Public Sub New()
            Dim factory = New DatabaseConnectionFactory(Path.Combine(_directoryPath, "test.db"))
            Dim initializer = New DatabaseInitializer(factory)
            initializer.Initialize()
            Repository = New CsvImportConfigRepository(factory)
        End Sub
        Public ReadOnly Property Repository As CsvImportConfigRepository
        Public Sub Dispose() Implements IDisposable.Dispose
            SqliteConnection.ClearAllPools()
            If Directory.Exists(_directoryPath) Then Directory.Delete(_directoryPath, recursive:=True)
        End Sub
    End Class
End Class
