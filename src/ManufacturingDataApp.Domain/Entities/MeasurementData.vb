Namespace Entities
    Public Class MeasurementData
        Public Property Id As Long
        Public Property MeasuredAt As DateTime
        Public Property EquipmentId As String = String.Empty
        Public Property EquipmentName As String = String.Empty
        Public Property ItemName As String = String.Empty
        Public Property Value As Double
        Public Property Unit As String = String.Empty
    End Class
End Namespace
