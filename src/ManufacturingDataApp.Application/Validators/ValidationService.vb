Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Domain

Namespace Validators
    Public Class ValidationService
        Public Function ValidateRequired(value As String, fieldName As String, rowNumber As Integer) As ValidationErrorDto
            If String.IsNullOrWhiteSpace(value) Then
                Return New ValidationErrorDto With {.RowNumber = rowNumber, .FieldName = fieldName, .ErrorType = ErrorTypes.Required, .Message = $"{fieldName}は必須です。"}
            End If
            Return Nothing
        End Function

        Public Function ValidateDouble(value As String, fieldName As String, rowNumber As Integer) As ValidationErrorDto
            Dim parsed As Double
            If Not Double.TryParse(value, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, parsed) Then
                Return New ValidationErrorDto With {.RowNumber = rowNumber, .FieldName = fieldName, .ErrorType = ErrorTypes.Type, .Message = $"{fieldName}は数値である必要があります。"}
            End If
            Return Nothing
        End Function

        Public Function ValidateDateTime(value As String, fieldName As String, rowNumber As Integer) As ValidationErrorDto
            Dim parsed As DateTime
            If Not DateTime.TryParse(value, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind, parsed) Then
                Return New ValidationErrorDto With {.RowNumber = rowNumber, .FieldName = fieldName, .ErrorType = ErrorTypes.Type, .Message = $"{fieldName}は日時である必要があります。"}
            End If
            Return Nothing
        End Function

        Public Function ValidateRange(value As Double, minimum As Double?, maximum As Double?, fieldName As String, rowNumber As Integer) As ValidationErrorDto
            If (minimum.HasValue AndAlso value < minimum.Value) OrElse (maximum.HasValue AndAlso value > maximum.Value) Then
                Return New ValidationErrorDto With {.RowNumber = rowNumber, .FieldName = fieldName, .ErrorType = ErrorTypes.Range, .Message = $"{fieldName}が許容範囲外です。"}
            End If
            Return Nothing
        End Function
    End Class
End Namespace
