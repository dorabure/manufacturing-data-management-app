Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Data

Namespace Logging
    Public Class DbLogWriter
        Implements IImportLogWriter
        Private ReadOnly _factory As DatabaseConnectionFactory
        Public Sub New(factory As DatabaseConnectionFactory)
            _factory = factory
        End Sub

        Public Sub WriteOperation(log As OperationLog) Implements IImportLogWriter.WriteOperation
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "INSERT INTO OperationLog (ExecutedAt, OperationType, ElapsedMs, TotalCount, SuccessCount, FailureCount) VALUES ($executedAt,$operationType,$elapsedMs,$totalCount,$successCount,$failureCount);"
                    command.Parameters.AddWithValue("$executedAt", log.ExecutedAt.ToString("O"))
                    command.Parameters.AddWithValue("$operationType", log.OperationType)
                    command.Parameters.AddWithValue("$elapsedMs", log.ElapsedMs)
                    command.Parameters.AddWithValue("$totalCount", log.TotalCount)
                    command.Parameters.AddWithValue("$successCount", log.SuccessCount)
                    command.Parameters.AddWithValue("$failureCount", log.FailureCount)
                    command.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Public Sub WriteErrors(errors As IEnumerable(Of ErrorLog)) Implements IImportLogWriter.WriteErrors
            Using connection = _factory.CreateConnection()
                Using transaction = connection.BeginTransaction()
                    Try
                        For Each errorLog In errors
                            Using command = connection.CreateCommand()
                                command.Transaction = transaction
                                command.CommandText = "INSERT INTO ErrorLog (OccurredAt, ConfigId, FileName, RowNumber, FieldName, ErrorType, ErrorMessage) VALUES ($occurredAt, $configId, $fileName, $rowNumber, $fieldName, $errorType, $errorMessage);"
                                command.Parameters.AddWithValue("$occurredAt", errorLog.OccurredAt.ToString("O"))
                                command.Parameters.AddWithValue("$configId", If(errorLog.ConfigId.HasValue, errorLog.ConfigId.Value, CType(DBNull.Value, Object)))
                                command.Parameters.AddWithValue("$fileName", If(errorLog.FileName, CType(DBNull.Value, Object)))
                                command.Parameters.AddWithValue("$rowNumber", If(errorLog.RowNumber.HasValue, errorLog.RowNumber.Value, CType(DBNull.Value, Object)))
                                command.Parameters.AddWithValue("$fieldName", If(errorLog.FieldName, CType(DBNull.Value, Object)))
                                command.Parameters.AddWithValue("$errorType", errorLog.ErrorType)
                                command.Parameters.AddWithValue("$errorMessage", errorLog.ErrorMessage)
                                command.ExecuteNonQuery()
                            End Using
                        Next
                        transaction.Commit()
                    Catch
                        transaction.Rollback()
                        Throw
                    End Try
                End Using
            End Using
        End Sub
    End Class
End Namespace
