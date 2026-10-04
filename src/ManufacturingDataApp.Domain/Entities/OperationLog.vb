Namespace Entities
    Public Class OperationLog
        Public Property LogId As Long
        Public Property ExecutedAt As DateTime
        Public Property OperationType As String = String.Empty
        Public Property ElapsedMs As Long
        Public Property TotalCount As Integer
        Public Property SuccessCount As Integer
        Public Property FailureCount As Integer
    End Class
End Namespace
