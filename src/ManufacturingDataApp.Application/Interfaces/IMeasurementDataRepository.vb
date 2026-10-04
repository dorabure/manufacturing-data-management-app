Imports ManufacturingDataApp.Domain.Entities

Namespace Interfaces
    Public Interface IMeasurementDataRepository
        Function Search(equipmentId As String, itemName As String, fromDate As DateTime?, toDate As DateTime?) As IReadOnlyList(Of MeasurementData)
        Function FindById(id As Long) As MeasurementData
        Sub Add(entity As MeasurementData)
        Sub AddRange(entities As IEnumerable(Of MeasurementData))
        Sub Update(entity As MeasurementData)
        Sub Delete(ids As IEnumerable(Of Long))
        Function IsItemNameInUse(itemName As String) As Boolean
    End Interface
End Namespace
