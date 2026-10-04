Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Data
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports System.IO
Imports Xunit

Public Class CsvConfigServiceTests
    <Fact>
    Public Sub Save_ValidConfigAndOwnNameUpdate_Succeed()
        Using database = New ConfigServiceTestDatabase()
            Dim config = New CsvImportConfig With {.ConfigName = "PLC_B", .Encoding = "UTF-8", .Delimiter = ",", .HasHeader = True}
            database.Service.Save(config, CreateMappings()) ' CONFIG-SVC-001
            database.Service.Save(config, CreateMappings()) ' CONFIG-SVC-006
            Assert.Equal("PLC_B", database.Service.GetById(config.ConfigId).ConfigName)
        End Using
    End Sub

    <Theory>
    <InlineData("", False, False)>
    <InlineData("PLC_B", True, False)>
    <InlineData("PLC_B", False, True)>
    Public Sub Save_InvalidConfig_IsRejected(name As String, missingMapping As Boolean, duplicateColumn As Boolean)
        Using database = New ConfigServiceTestDatabase()
            Dim mappings = CreateMappings()
            If missingMapping Then mappings.RemoveAt(0) ' CONFIG-SVC-003
            If duplicateColumn Then mappings(1).CsvColumnIndex = 1 ' CONFIG-SVC-004
            Assert.Throws(Of ArgumentException)(Sub() database.Service.Save(New CsvImportConfig With {.ConfigName = name, .Encoding = "UTF-8", .Delimiter = ",", .HasHeader = True}, mappings)) ' CONFIG-SVC-002
        End Using
    End Sub

    <Fact>
    Public Sub Save_DuplicateName_IsRejected()
        Using database = New ConfigServiceTestDatabase()
            Assert.Throws(Of ArgumentException)(Sub() database.Service.Save(New CsvImportConfig With {.ConfigName = "SCADA_A", .Encoding = "UTF-8", .Delimiter = ",", .HasHeader = True}, CreateMappings())) ' CONFIG-SVC-005
        End Using
    End Sub

    Private Shared Function CreateMappings() As List(Of CsvColumnMapping)
        Return New List(Of CsvColumnMapping) From {
            New CsvColumnMapping With {.FieldName = StandardFields.AcquiredAt, .CsvColumnIndex = 1},
            New CsvColumnMapping With {.FieldName = StandardFields.EquipmentId, .CsvColumnIndex = 2},
            New CsvColumnMapping With {.FieldName = StandardFields.EquipmentName, .CsvColumnIndex = 3},
            New CsvColumnMapping With {.FieldName = StandardFields.ItemName, .CsvColumnIndex = 4},
            New CsvColumnMapping With {.FieldName = StandardFields.Value, .CsvColumnIndex = 5},
            New CsvColumnMapping With {.FieldName = StandardFields.Unit, .CsvColumnIndex = 6}}
    End Function

    Private NotInheritable Class ConfigServiceTestDatabase
        Implements IDisposable
        Private ReadOnly _directoryPath As String = Path.Combine(Path.GetTempPath(), $"ManufacturingDataApp.ConfigServiceTests.{Guid.NewGuid():N}")
        Public Sub New()
            Dim factory = New DatabaseConnectionFactory(Path.Combine(_directoryPath, "test.db"))
            Dim initializer = New DatabaseInitializer(factory)
            initializer.Initialize()
            Service = New CsvConfigService(New CsvImportConfigRepository(factory))
        End Sub
        Public ReadOnly Property Service As CsvConfigService
        Public Sub Dispose() Implements IDisposable.Dispose
            SqliteConnection.ClearAllPools()
            If Directory.Exists(_directoryPath) Then Directory.Delete(_directoryPath, recursive:=True)
        End Sub
    End Class
End Class
