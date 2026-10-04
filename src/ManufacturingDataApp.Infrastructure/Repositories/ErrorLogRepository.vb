Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Data
Namespace Repositories
    Public Class ErrorLogRepository
        Implements IErrorLogRepository
        Private ReadOnly _factory As DatabaseConnectionFactory
        Public Sub New(factory As DatabaseConnectionFactory)
            _factory = factory
        End Sub
        Public Function Search(fromDate As DateTime?, toDate As DateTime?, fileName As String, errorType As String) As IReadOnlyList(Of ErrorLog) Implements IErrorLogRepository.Search
            Dim conditions As New List(Of String) From {"1=1"} : Dim result As New List(Of ErrorLog)
            If fromDate.HasValue Then conditions.Add("OccurredAt >= $from")
            If toDate.HasValue Then conditions.Add("OccurredAt < $to")
            If Not String.IsNullOrWhiteSpace(fileName) Then conditions.Add("FileName = $file")
            If Not String.IsNullOrWhiteSpace(errorType) Then conditions.Add("ErrorType = $type")
            Using cn = _factory.CreateConnection(), cmd = cn.CreateCommand()
                cmd.CommandText = $"SELECT ErrorId,OccurredAt,ConfigId,FileName,RowNumber,FieldName,ErrorType,ErrorMessage FROM ErrorLog WHERE {String.Join(" AND ", conditions)} ORDER BY OccurredAt DESC, ErrorId DESC;"
                If fromDate.HasValue Then cmd.Parameters.AddWithValue("$from", fromDate.Value.ToString("O"))
                If toDate.HasValue Then cmd.Parameters.AddWithValue("$to", toDate.Value.ToString("O"))
                If Not String.IsNullOrWhiteSpace(fileName) Then cmd.Parameters.AddWithValue("$file", fileName)
                If Not String.IsNullOrWhiteSpace(errorType) Then cmd.Parameters.AddWithValue("$type", errorType)
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        result.Add(New ErrorLog With {.ErrorId = r.GetInt64(0), .OccurredAt = DateTime.Parse(r.GetString(1), Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind), .ConfigId = If(r.IsDBNull(2), CType(Nothing, Integer?), r.GetInt32(2)), .FileName = If(r.IsDBNull(3), Nothing, r.GetString(3)), .RowNumber = If(r.IsDBNull(4), CType(Nothing, Integer?), r.GetInt32(4)), .FieldName = If(r.IsDBNull(5), Nothing, r.GetString(5)), .ErrorType = r.GetString(6), .ErrorMessage = r.GetString(7)})
                    End While
                End Using
            End Using
            Return result
        End Function
    End Class
End Namespace
