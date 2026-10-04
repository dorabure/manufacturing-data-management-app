Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Application.Validators
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Domain.Exceptions
Imports ManufacturingDataApp.Infrastructure.Csv
Imports ManufacturingDataApp.Infrastructure.Data
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports System.IO
Imports Xunit

Public Class MasterDataServiceTests
    <Fact>
    Public Sub Equipment_CanListCreateUpdateAndDelete()
        Using database = New MasterTestDatabase()
            Assert.NotEmpty(database.Service.GetEquipment()) ' MASTER-EQ-001
            Dim equipment As New EquipmentMaster With {.EquipmentId = "EQ-NEW", .EquipmentName = "新規設備"}
            database.Service.SaveEquipment(equipment, True) ' MASTER-EQ-002
            equipment.EquipmentName = "更新設備"
            database.Service.SaveEquipment(equipment, False) ' MASTER-EQ-006
            Assert.Equal("更新設備", database.EquipmentRepository.FindById("EQ-NEW").EquipmentName)
            database.Service.DeleteEquipment("EQ-NEW") ' MASTER-EQ-008
            Assert.Null(database.EquipmentRepository.FindById("EQ-NEW"))
            Assert.Contains(database.LogWriter.Operations, Function(log) log.OperationType = "設備マスタ登録" AndAlso log.ElapsedMs >= 0)
        End Using
    End Sub

    <Fact>
    Public Sub Equipment_RejectsInvalidDuplicateMissingAndReferencedOperations()
        Using database = New MasterTestDatabase()
            Assert.Throws(Of ArgumentException)(Sub() database.Service.SaveEquipment(New EquipmentMaster With {.EquipmentId = "", .EquipmentName = "設備"}, True)) ' MASTER-EQ-003
            Assert.Throws(Of ArgumentException)(Sub() database.Service.SaveEquipment(New EquipmentMaster With {.EquipmentId = "EQ-X", .EquipmentName = ""}, True)) ' MASTER-EQ-004
            Assert.Throws(Of ArgumentException)(Sub() database.Service.SaveEquipment(New EquipmentMaster With {.EquipmentId = "EQ001", .EquipmentName = "重複"}, True)) ' MASTER-EQ-005
            Assert.Throws(Of InvalidOperationException)(Sub() database.Service.SaveEquipment(New EquipmentMaster With {.EquipmentId = "UNKNOWN", .EquipmentName = "不存在"}, False)) ' MASTER-EQ-007
            database.MeasurementRepository.Add(New MeasurementData With {.MeasuredAt = New DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc), .EquipmentId = "EQ001", .EquipmentName = "搬送装置01", .ItemName = "温度", .Value = 10, .Unit = "℃"})
            Assert.Throws(Of EquipmentInUseException)(Sub() database.Service.DeleteEquipment("EQ001")) ' MASTER-EQ-009
        End Using
    End Sub

    <Fact>
    Public Sub MeasurementItem_CanCreateUpdateWithNullBoundsAndDelete()
        Using database = New MasterTestDatabase()
            Assert.NotEmpty(database.Service.GetMeasurementItems()) ' MASTER-ITEM-001
            Dim item As New MeasurementItemMaster With {.ItemName = "電流", .Unit = "A", .MinValue = Nothing, .MaxValue = Nothing}
            database.Service.SaveMeasurementItem(item, True) ' MASTER-ITEM-002, MASTER-ITEM-006
            Assert.Null(database.ItemRepository.FindById(item.ItemId).MinValue)
            item.MinValue = 1 : item.MaxValue = 10
            database.Service.SaveMeasurementItem(item, False) ' MASTER-ITEM-008
            database.Service.DeleteMeasurementItem(item.ItemId) ' MASTER-ITEM-009
            Assert.Null(database.ItemRepository.FindById(item.ItemId))
        End Using
    End Sub

    <Fact>
    Public Sub MeasurementItem_Delete_RejectsItemUsedByMeasurementDataWithoutChangingData()
        Using database = New MasterTestDatabase()
            Dim unused As New MeasurementItemMaster With {.ItemName = "未使用項目", .Unit = "V"}
            database.Service.SaveMeasurementItem(unused, True)
            database.Service.DeleteMeasurementItem(unused.ItemId) ' MASTER-ITEM-010
            Assert.Null(database.ItemRepository.FindById(unused.ItemId))

            Dim used = database.ItemRepository.GetAll().First(Function(item) item.ItemName = "温度")
            database.MeasurementRepository.Add(New MeasurementData With {.MeasuredAt = New DateTime(2026, 9, 22, 9, 0, 0, DateTimeKind.Utc), .EquipmentId = "EQ001", .EquipmentName = "搬送装置01", .ItemName = "温度", .Value = 20, .Unit = "℃"})
            Assert.Throws(Of MeasurementItemInUseException)(Sub() database.Service.DeleteMeasurementItem(used.ItemId)) ' MASTER-ITEM-011
            Assert.NotNull(database.ItemRepository.FindById(used.ItemId)) ' MASTER-ITEM-012
            Assert.Single(database.MeasurementRepository.Search("EQ001", "温度", Nothing, Nothing))
        End Using
    End Sub

    <Fact>
    Public Sub MeasurementItem_ValidationAndRangeChanges_AffectImportAndEdit()
        Using database = New MasterTestDatabase()
            Assert.Throws(Of ArgumentException)(Sub() database.Service.SaveMeasurementItem(New MeasurementItemMaster With {.ItemName = "", .Unit = "V"}, True)) ' MASTER-ITEM-003
            Assert.Throws(Of ArgumentException)(Sub() database.Service.SaveMeasurementItem(New MeasurementItemMaster With {.ItemName = "電圧", .Unit = ""}, True)) ' MASTER-ITEM-004
            Assert.Throws(Of ArgumentException)(Sub() database.Service.SaveMeasurementItem(New MeasurementItemMaster With {.ItemName = "温度", .Unit = "℃"}, True)) ' MASTER-ITEM-005
            Assert.Throws(Of ArgumentException)(Sub() database.Service.SaveMeasurementItem(New MeasurementItemMaster With {.ItemName = "不正", .Unit = "V", .MinValue = 100, .MaxValue = 10}, True)) ' MASTER-ITEM-007

            Dim temperature = database.ItemRepository.GetAll().First(Function(item) item.ItemName = "温度")
            temperature.MinValue = 10 : temperature.MaxValue = 80
            database.Service.SaveMeasurementItem(temperature, False)
            Dim sourcePath = Path.Combine(database.DirectoryPath, "range.csv")
            File.WriteAllText(sourcePath, "取得日時,設備ID,設備名,項目名,測定値,単位" & Environment.NewLine & "2026-09-21T09:00:00.0000000Z,EQ001,搬送装置01,温度,5,℃", New Text.UTF8Encoding(False))
            Dim request As New CsvImportRequestDto With {.FilePath = sourcePath, .Config = New CsvImportConfig With {.Encoding = "UTF-8", .Delimiter = ",", .HasHeader = True}}
            request.ColumnMappings.AddRange(database.StandardMappings())
            Assert.Contains(database.ImportService.Import(request).Errors, Function(errorItem) errorItem.ErrorType = ErrorTypes.Range) ' MASTER-INT-001

            Dim data As New MeasurementData With {.Id = 1, .MeasuredAt = New DateTime(2026, 9, 21, 10, 0, 0), .EquipmentId = "EQ001", .EquipmentName = "搬送装置01", .ItemName = "温度", .Value = 5, .Unit = "℃"}
            Assert.Contains(database.MeasurementService.ValidateForUpdate(data), Function(errorItem) errorItem.ErrorType = ErrorTypes.Range) ' MASTER-INT-002
        End Using
    End Sub

    Private NotInheritable Class MasterTestDatabase
        Implements IDisposable
        Public ReadOnly DirectoryPath As String = Path.Combine(Path.GetTempPath(), $"ManufacturingDataApp.MasterTests.{Guid.NewGuid():N}")
        Public Sub New()
            Dim factory = New DatabaseConnectionFactory(Path.Combine(DirectoryPath, "test.db"))
            Dim initializer = New DatabaseInitializer(factory)
            initializer.Initialize()
            EquipmentRepository = New EquipmentRepository(factory)
            ItemRepository = New MeasurementItemRepository(factory)
            MeasurementRepository = New MeasurementDataRepository(factory)
            LogWriter = New LogWriterStub()
            Service = New MasterDataService(EquipmentRepository, ItemRepository, LogWriter, MeasurementRepository)
            ImportService = New CsvImportService(New CsvHelperAdapter(), New ValidationService(), ItemRepository)
            MeasurementService = New MeasurementDataService(MeasurementRepository, ItemRepository, New ValidationService(), Nothing)
        End Sub
        Public ReadOnly Property EquipmentRepository As EquipmentRepository
        Public ReadOnly Property ItemRepository As MeasurementItemRepository
        Public ReadOnly Property MeasurementRepository As MeasurementDataRepository
        Public ReadOnly Property Service As MasterDataService
        Public ReadOnly Property ImportService As CsvImportService
        Public ReadOnly Property MeasurementService As MeasurementDataService
        Public ReadOnly Property LogWriter As LogWriterStub
        Public Function StandardMappings() As IReadOnlyList(Of CsvColumnMapping)
            Return {New CsvColumnMapping With {.FieldName = StandardFields.AcquiredAt, .CsvColumnIndex = 1}, New CsvColumnMapping With {.FieldName = StandardFields.EquipmentId, .CsvColumnIndex = 2}, New CsvColumnMapping With {.FieldName = StandardFields.EquipmentName, .CsvColumnIndex = 3}, New CsvColumnMapping With {.FieldName = StandardFields.ItemName, .CsvColumnIndex = 4}, New CsvColumnMapping With {.FieldName = StandardFields.Value, .CsvColumnIndex = 5}, New CsvColumnMapping With {.FieldName = StandardFields.Unit, .CsvColumnIndex = 6}}
        End Function
        Public Sub Dispose() Implements IDisposable.Dispose
            SqliteConnection.ClearAllPools()
            If Directory.Exists(DirectoryPath) Then Directory.Delete(DirectoryPath, recursive:=True)
        End Sub
    End Class

    Public NotInheritable Class LogWriterStub
        Implements IImportLogWriter
        Public ReadOnly Property Operations As New List(Of OperationLog)()
        Public Sub WriteOperation(log As OperationLog) Implements IImportLogWriter.WriteOperation
            Operations.Add(log)
        End Sub
        Public Sub WriteErrors(errors As IEnumerable(Of ErrorLog)) Implements IImportLogWriter.WriteErrors
            Throw New NotSupportedException()
        End Sub
    End Class
End Class
