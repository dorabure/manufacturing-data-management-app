Namespace DTOs
    Public Class ValidationErrorDto
        Public Property RowNumber As Integer?
        Public Property FieldName As String
        Public Property ErrorType As String = String.Empty
        Public Property Message As String = String.Empty
        Public Property FileName As String
    End Class

    Public Class ValidationResultDto
        Public Property SourceRowCount As Integer
        Public ReadOnly Property Errors As New List(Of ValidationErrorDto)
        Public ReadOnly Property ValidMeasurements As New List(Of ManufacturingDataApp.Domain.Entities.MeasurementData)
        Public ReadOnly Property ValidCandidates As New List(Of CsvImportCandidateDto)
        Public ReadOnly Property IsValid As Boolean
            Get
                Return Errors.Count = 0
            End Get
        End Property
    End Class
End Namespace
