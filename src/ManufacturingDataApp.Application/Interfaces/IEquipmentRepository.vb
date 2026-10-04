Imports ManufacturingDataApp.Domain.Entities

Namespace Interfaces
    Public Interface IEquipmentRepository
        Function GetAll() As IReadOnlyList(Of EquipmentMaster)
        Function FindById(equipmentId As String) As EquipmentMaster
        Sub Add(entity As EquipmentMaster)
        Sub Update(entity As EquipmentMaster)
        Sub Delete(equipmentId As String)
    End Interface
End Namespace
