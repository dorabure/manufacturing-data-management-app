Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Application.Validators
Imports ManufacturingDataApp.Domain
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Csv
Imports System.IO
Imports Xunit

Public Class CsvImportServiceTests
    <Theory>
    <InlineData("valid.csv", 1, 0)>
    <InlineData("required-error.csv", 0, 1)>
    <InlineData("type-error.csv", 0, 2)>
    <InlineData("duplicate-error.csv", 1, 1)>
    <InlineData("range-error.csv", 0, 1)>
    Public Sub Import_SeparatesValidAndInvalidRows(fileName As String, validCount As Integer, errorCount As Integer)
        Dim result = CreateService().Import(CreateRequest(fileName, StandardMappings()))

        Assert.Equal(validCount, result.ValidMeasurements.Count)
        Assert.Equal(errorCount, result.Errors.Count)
        Assert.All(result.Errors, Sub(errorItem) Assert.Equal(fileName, errorItem.FileName))
    End Sub

    <Fact>
    Public Sub Import_MapsReorderedColumns()
        Dim mappings = New List(Of CsvColumnMapping) From {
            Mapping(StandardFields.EquipmentId, 1), Mapping(StandardFields.Value, 2), Mapping(StandardFields.AcquiredAt, 3),
            Mapping(StandardFields.ItemName, 4), Mapping(StandardFields.Unit, 5), Mapping(StandardFields.EquipmentName, 6)}

        Dim result = CreateService().Import(CreateRequest("reordered.csv", mappings))

        Assert.True(result.IsValid)
        Assert.Equal("EQ001", Assert.Single(result.ValidMeasurements).EquipmentId)
    End Sub

    <Fact>
    Public Sub Import_MapsColumnsByIndexInsteadOfDictionaryEnumerationOrder()
        Dim row As New Dictionary(Of String, String) From {
            {"Column6", "搬送装置01"}, {"Column5", "℃"}, {"Column4", "温度"},
            {"Column3", "2026-01-01T09:00:00.0000000Z"}, {"Column2", "25.5"}, {"Column1", "EQ001"}}
        Dim mappings = New List(Of CsvColumnMapping) From {
            Mapping(StandardFields.EquipmentId, 1), Mapping(StandardFields.Value, 2), Mapping(StandardFields.AcquiredAt, 3),
            Mapping(StandardFields.ItemName, 4), Mapping(StandardFields.Unit, 5), Mapping(StandardFields.EquipmentName, 6)}
        Dim service = New CsvImportService(New FixedCsvAdapter(row), New ValidationService(), New ItemRepositoryStub())
        Dim request = New CsvImportRequestDto With {.FilePath = "out-of-order.csv", .Config = New CsvImportConfig With {.HasHeader = False}}
        request.ColumnMappings.AddRange(mappings)

        Dim result = service.Import(request)

        Assert.True(result.IsValid)
        Assert.Equal("EQ001", Assert.Single(result.ValidMeasurements).EquipmentId)
    End Sub

    <Fact>
    Public Sub ReadRaw_ReadsUtf8SemicolonCsvWithHeader()
        Dim temporaryFilePath = Path.Combine(Path.GetTempPath(), $"csv-{Guid.NewGuid():N}.csv")
        Try
            File.WriteAllText(temporaryFilePath, "A;B" & Environment.NewLine & "1;2", New Text.UTF8Encoding(False))
            Dim rows = CreateService().ReadRaw(temporaryFilePath, "UTF-8", ";", True)
            Assert.Equal("1", Assert.Single(rows)("A"))
        Finally
            If File.Exists(temporaryFilePath) Then File.Delete(temporaryFilePath)
        End Try
    End Sub

    Private Shared Function CreateService() As CsvImportService
        Return New CsvImportService(New CsvHelperAdapter(), New ValidationService(), New ItemRepositoryStub())
    End Function

    Private Shared Function CreateRequest(fileName As String, mappings As IEnumerable(Of CsvColumnMapping)) As CsvImportRequestDto
        Dim request As New CsvImportRequestDto With {.FilePath = Path.Combine(AppContext.BaseDirectory, "TestData", fileName), .Config = New CsvImportConfig With {.Encoding = "UTF-8", .Delimiter = ",", .HasHeader = True}}
        request.ColumnMappings.AddRange(mappings)
        Return request
    End Function

    Private Shared Function StandardMappings() As IEnumerable(Of CsvColumnMapping)
        Return {Mapping(StandardFields.AcquiredAt, 1), Mapping(StandardFields.EquipmentId, 2), Mapping(StandardFields.EquipmentName, 3), Mapping(StandardFields.ItemName, 4), Mapping(StandardFields.Value, 5), Mapping(StandardFields.Unit, 6)}
    End Function

    Private Shared Function Mapping(fieldName As String, columnIndex As Integer) As CsvColumnMapping
        Return New CsvColumnMapping With {.FieldName = fieldName, .CsvColumnIndex = columnIndex}
    End Function

    Private NotInheritable Class ItemRepositoryStub
        Implements IMeasurementItemRepository

        Public Function GetAll() As IReadOnlyList(Of MeasurementItemMaster) Implements IMeasurementItemRepository.GetAll
            Return {New MeasurementItemMaster With {.ItemId = 1, .ItemName = "温度", .Unit = "℃", .MinValue = 0, .MaxValue = 100}}
        End Function
        Public Function FindById(itemId As Integer) As MeasurementItemMaster Implements IMeasurementItemRepository.FindById
            Return Nothing
        End Function
        Public Sub Add(entity As MeasurementItemMaster) Implements IMeasurementItemRepository.Add
            Throw New NotSupportedException()
        End Sub
        Public Sub Update(entity As MeasurementItemMaster) Implements IMeasurementItemRepository.Update
            Throw New NotSupportedException()
        End Sub
        Public Sub Delete(itemId As Integer) Implements IMeasurementItemRepository.Delete
            Throw New NotSupportedException()
        End Sub
    End Class

    Private NotInheritable Class FixedCsvAdapter
        Implements ICsvFileAdapter

        Private ReadOnly _row As IReadOnlyDictionary(Of String, String)

        Public Sub New(row As IReadOnlyDictionary(Of String, String))
            _row = row
        End Sub

        Public Function Read(filePath As String, encodingName As String, delimiter As String, hasHeader As Boolean) As IReadOnlyList(Of IReadOnlyDictionary(Of String, String)) Implements ICsvFileAdapter.Read
            Return {_row}
        End Function

        Public Sub Write(filePath As String, rows As IEnumerable(Of IReadOnlyDictionary(Of String, String)), encodingName As String, delimiter As String, includeHeader As Boolean) Implements ICsvFileAdapter.Write
            Throw New NotSupportedException()
        End Sub
    End Class
End Class
