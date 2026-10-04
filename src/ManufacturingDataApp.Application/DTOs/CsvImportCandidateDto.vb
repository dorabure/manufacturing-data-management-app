Imports ManufacturingDataApp.Domain.Entities

Namespace DTOs
    Public Class CsvImportCandidateDto
        Public Property RowNumber As Integer
        Public Property Measurement As New MeasurementData()
    End Class
End Namespace
