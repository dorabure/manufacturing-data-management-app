Namespace DTOs
    Public Class CsvImportExecutionResultDto
        Public Property TotalCount As Integer
        Public Property SuccessCount As Integer
        Public Property FailureCount As Integer
        Public Property ElapsedMs As Long
        Public Property ValidationResult As New ValidationResultDto()
        Public Property LogRecorded As Boolean
        Public Property LogErrorMessage As String
        ' Validation messages may be multiple per source row.
        Public ReadOnly Property ValidationErrorRowCount As Integer
            Get
                Return ValidationResult.Errors.Where(Function(e) e.RowNumber.HasValue).Select(Function(e) e.RowNumber.Value).Distinct().Count()
            End Get
        End Property
        Public Property HeldValidRowCount As Integer
    End Class
End Namespace
