Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Data
Namespace Repositories
    Public Class OperationLogRepository
        Implements IOperationLogRepository
        Private ReadOnly _factory As DatabaseConnectionFactory
        Public Sub New(factory As DatabaseConnectionFactory)
            _factory = factory
        End Sub
        Public Function Search(fromDate As DateTime?, toDate As DateTime?, operationType As String) As IReadOnlyList(Of OperationLog) Implements IOperationLogRepository.Search
            Dim c As New List(Of String) From {"1=1"} : Dim result As New List(Of OperationLog)
            If fromDate.HasValue Then c.Add("ExecutedAt >= $from")
            If toDate.HasValue Then c.Add("ExecutedAt < $to")
            If Not String.IsNullOrWhiteSpace(operationType) Then c.Add("OperationType = $type")
            Using cn = _factory.CreateConnection(), cmd = cn.CreateCommand()
                cmd.CommandText = $"SELECT LogId,ExecutedAt,OperationType,ElapsedMs,TotalCount,SuccessCount,FailureCount FROM OperationLog WHERE {String.Join(" AND ", c)} ORDER BY ExecutedAt DESC, LogId DESC;"
                If fromDate.HasValue Then cmd.Parameters.AddWithValue("$from", fromDate.Value.ToString("O"))
                If toDate.HasValue Then cmd.Parameters.AddWithValue("$to", toDate.Value.ToString("O"))
                If Not String.IsNullOrWhiteSpace(operationType) Then cmd.Parameters.AddWithValue("$type", operationType)
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        result.Add(New OperationLog With {.LogId = r.GetInt64(0), .ExecutedAt = DateTime.Parse(r.GetString(1), Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind), .OperationType = r.GetString(2), .ElapsedMs = r.GetInt64(3), .TotalCount = r.GetInt32(4), .SuccessCount = r.GetInt32(5), .FailureCount = r.GetInt32(6)})
                    End While
                End Using
            End Using
            Return result
        End Function
    End Class
End Namespace
