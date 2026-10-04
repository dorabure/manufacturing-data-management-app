Imports ManufacturingDataApp.Domain.Entities

Namespace Interfaces
    Public Interface IMeasurementItemRepository
        Function GetAll() As IReadOnlyList(Of MeasurementItemMaster)
        Function FindById(itemId As Integer) As MeasurementItemMaster
        Sub Add(entity As MeasurementItemMaster)
        Sub Update(entity As MeasurementItemMaster)
        Sub Delete(itemId As Integer)
    End Interface
End Namespace
