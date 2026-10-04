Namespace Entities
    Public Class ErrorLog
        Public Property ErrorId As Long
        Public Property OccurredAt As DateTime
        Public Property ConfigId As Integer?
        Public Property FileName As String
        Public Property RowNumber As Integer?
        Public Property FieldName As String
        Public Property ErrorType As String = String.Empty
        Public Property ErrorMessage As String = String.Empty
    End Class
End Namespace
