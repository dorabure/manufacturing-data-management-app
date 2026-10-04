Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Data

Namespace Repositories
    Public Class MeasurementDataRepository
        Implements IMeasurementDataRepository

        Private ReadOnly _factory As DatabaseConnectionFactory

        Public Sub New(factory As DatabaseConnectionFactory)
            _factory = factory
        End Sub

        Public Function Search(equipmentId As String, itemName As String, fromDate As DateTime?, toDate As DateTime?) As IReadOnlyList(Of MeasurementData) Implements IMeasurementDataRepository.Search
            Dim conditions As New List(Of String) From {"1 = 1"}
            Dim result As New List(Of MeasurementData)()
            If Not String.IsNullOrWhiteSpace(equipmentId) Then conditions.Add("EquipmentId = $equipmentId")
            If Not String.IsNullOrWhiteSpace(itemName) Then conditions.Add("ItemName = $itemName")
            If fromDate.HasValue Then conditions.Add("MeasuredAt >= $fromDate")
            If toDate.HasValue Then conditions.Add("MeasuredAt < $toDate")

            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = $"SELECT Id, MeasuredAt, EquipmentId, EquipmentName, ItemName, Value, Unit FROM MeasurementData WHERE {String.Join(" AND ", conditions)} ORDER BY MeasuredAt DESC;"
                    If Not String.IsNullOrWhiteSpace(equipmentId) Then command.Parameters.AddWithValue("$equipmentId", equipmentId)
                    If Not String.IsNullOrWhiteSpace(itemName) Then command.Parameters.AddWithValue("$itemName", itemName)
                    If fromDate.HasValue Then command.Parameters.AddWithValue("$fromDate", fromDate.Value.ToString("O"))
                    If toDate.HasValue Then command.Parameters.AddWithValue("$toDate", toDate.Value.ToString("O"))
                    Using reader = command.ExecuteReader()
                        While reader.Read()
                            result.Add(Map(reader))
                        End While
                    End Using
                End Using
            End Using
            Return result
        End Function

        Public Function FindById(id As Long) As MeasurementData Implements IMeasurementDataRepository.FindById
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "SELECT Id, MeasuredAt, EquipmentId, EquipmentName, ItemName, Value, Unit FROM MeasurementData WHERE Id = $id;"
                    command.Parameters.AddWithValue("$id", id)
                    Using reader = command.ExecuteReader()
                        If reader.Read() Then Return Map(reader)
                    End Using
                End Using
            End Using
            Return Nothing
        End Function

        Public Sub Add(entity As MeasurementData) Implements IMeasurementDataRepository.Add
            Using connection = _factory.CreateConnection()
                Insert(connection, Nothing, entity)
            End Using
        End Sub

        Public Sub AddRange(entities As IEnumerable(Of MeasurementData)) Implements IMeasurementDataRepository.AddRange
            Using connection = _factory.CreateConnection()
                Using transaction = connection.BeginTransaction()
                    Try
                        For Each entity In entities
                            Insert(connection, transaction, entity)
                        Next
                        transaction.Commit()
                    Catch
                        transaction.Rollback()
                        Throw
                    End Try
                End Using
            End Using
        End Sub

        Public Sub Update(entity As MeasurementData) Implements IMeasurementDataRepository.Update
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "UPDATE MeasurementData SET MeasuredAt = $measuredAt, EquipmentId = $equipmentId, EquipmentName = $equipmentName, ItemName = $itemName, Value = $value, Unit = $unit WHERE Id = $id;"
                    command.Parameters.AddWithValue("$id", entity.Id)
                    SetParameters(command, entity)
                    If command.ExecuteNonQuery() <> 1 Then Throw New InvalidOperationException("更新対象の測定データが見つかりません。")
                End Using
            End Using
        End Sub

        Public Sub Delete(ids As IEnumerable(Of Long)) Implements IMeasurementDataRepository.Delete
            Using connection = _factory.CreateConnection()
                Using transaction = connection.BeginTransaction()
                    Try
                        For Each id In ids
                            Using command = connection.CreateCommand()
                                command.Transaction = transaction
                                command.CommandText = "DELETE FROM MeasurementData WHERE Id = $id;"
                                command.Parameters.AddWithValue("$id", id)
                                If command.ExecuteNonQuery() <> 1 Then Throw New InvalidOperationException("削除対象の測定データが見つかりません。")
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

        Public Function IsItemNameInUse(itemName As String) As Boolean Implements IMeasurementDataRepository.IsItemNameInUse
            Using connection = _factory.CreateConnection(), command = connection.CreateCommand()
                command.CommandText = "SELECT EXISTS(SELECT 1 FROM MeasurementData WHERE ItemName = $itemName);"
                command.Parameters.AddWithValue("$itemName", itemName)
                Return Convert.ToInt32(command.ExecuteScalar()) <> 0
            End Using
        End Function

        Private Shared Sub Insert(connection As SqliteConnection, transaction As SqliteTransaction, entity As MeasurementData)
            Using command = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = "INSERT INTO MeasurementData (MeasuredAt, EquipmentId, EquipmentName, ItemName, Value, Unit) VALUES ($measuredAt, $equipmentId, $equipmentName, $itemName, $value, $unit); SELECT last_insert_rowid();"
                SetParameters(command, entity)
                entity.Id = CLng(command.ExecuteScalar())
            End Using
        End Sub

        Private Shared Sub SetParameters(command As SqliteCommand, entity As MeasurementData)
            command.Parameters.AddWithValue("$measuredAt", entity.MeasuredAt.ToString("O"))
            command.Parameters.AddWithValue("$equipmentId", entity.EquipmentId)
            command.Parameters.AddWithValue("$equipmentName", entity.EquipmentName)
            command.Parameters.AddWithValue("$itemName", entity.ItemName)
            command.Parameters.AddWithValue("$value", entity.Value)
            command.Parameters.AddWithValue("$unit", entity.Unit)
        End Sub

        Private Shared Function Map(reader As SqliteDataReader) As MeasurementData
            Return New MeasurementData With {.Id = reader.GetInt64(0), .MeasuredAt = DateTime.Parse(reader.GetString(1), Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind), .EquipmentId = reader.GetString(2), .EquipmentName = reader.GetString(3), .ItemName = reader.GetString(4), .Value = reader.GetDouble(5), .Unit = reader.GetString(6)}
        End Function
    End Class
End Namespace
