Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Data

Namespace Repositories
    Public Class MeasurementItemRepository
        Implements IMeasurementItemRepository

        Private ReadOnly _factory As DatabaseConnectionFactory

        Public Sub New(factory As DatabaseConnectionFactory)
            _factory = factory
        End Sub

        Public Function GetAll() As IReadOnlyList(Of MeasurementItemMaster) Implements IMeasurementItemRepository.GetAll
            Dim result As New List(Of MeasurementItemMaster)()
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "SELECT ItemId, ItemName, Unit, MinValue, MaxValue FROM MeasurementItemMaster ORDER BY ItemId;"
                    Using reader = command.ExecuteReader()
                        While reader.Read()
                            result.Add(Map(reader))
                        End While
                    End Using
                End Using
            End Using
            Return result
        End Function

        Public Function FindById(itemId As Integer) As MeasurementItemMaster Implements IMeasurementItemRepository.FindById
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "SELECT ItemId, ItemName, Unit, MinValue, MaxValue FROM MeasurementItemMaster WHERE ItemId = $itemId;"
                    command.Parameters.AddWithValue("$itemId", itemId)
                    Using reader = command.ExecuteReader()
                        If reader.Read() Then Return Map(reader)
                    End Using
                End Using
            End Using
            Return Nothing
        End Function

        Public Sub Add(entity As MeasurementItemMaster) Implements IMeasurementItemRepository.Add
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "INSERT INTO MeasurementItemMaster (ItemName, Unit, MinValue, MaxValue) VALUES ($name, $unit, $minValue, $maxValue); SELECT last_insert_rowid();"
                    SetParameters(command, entity)
                    entity.ItemId = CInt(command.ExecuteScalar())
                End Using
            End Using
        End Sub

        Public Sub Update(entity As MeasurementItemMaster) Implements IMeasurementItemRepository.Update
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "UPDATE MeasurementItemMaster SET Unit = $unit, MinValue = $minValue, MaxValue = $maxValue WHERE ItemId = $itemId;"
                    command.Parameters.AddWithValue("$itemId", entity.ItemId)
                    SetParameters(command, entity)
                    If command.ExecuteNonQuery() <> 1 Then Throw New InvalidOperationException("更新対象の測定項目が見つかりません。")
                End Using
            End Using
        End Sub

        Public Sub Delete(itemId As Integer) Implements IMeasurementItemRepository.Delete
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "DELETE FROM MeasurementItemMaster WHERE ItemId = $itemId;"
                    command.Parameters.AddWithValue("$itemId", itemId)
                    If command.ExecuteNonQuery() <> 1 Then Throw New InvalidOperationException("削除対象の測定項目が見つかりません。")
                End Using
            End Using
        End Sub

        Private Shared Sub SetParameters(command As Microsoft.Data.Sqlite.SqliteCommand, entity As MeasurementItemMaster)
            command.Parameters.AddWithValue("$name", entity.ItemName)
            command.Parameters.AddWithValue("$unit", entity.Unit)
            command.Parameters.AddWithValue("$minValue", If(entity.MinValue.HasValue, entity.MinValue.Value, CType(DBNull.Value, Object)))
            command.Parameters.AddWithValue("$maxValue", If(entity.MaxValue.HasValue, entity.MaxValue.Value, CType(DBNull.Value, Object)))
        End Sub

        Private Shared Function Map(reader As Microsoft.Data.Sqlite.SqliteDataReader) As MeasurementItemMaster
            Dim minValue As Double? = Nothing
            Dim maxValue As Double? = Nothing
            If Not reader.IsDBNull(3) Then minValue = reader.GetDouble(3)
            If Not reader.IsDBNull(4) Then maxValue = reader.GetDouble(4)

            Return New MeasurementItemMaster With {.ItemId = reader.GetInt32(0), .ItemName = reader.GetString(1), .Unit = reader.GetString(2), .MinValue = minValue, .MaxValue = maxValue}
        End Function
    End Class
End Namespace
