Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Data
Imports ManufacturingDataApp.Domain.Exceptions

Namespace Repositories
    Public Class EquipmentRepository
        Implements IEquipmentRepository

        Private ReadOnly _factory As DatabaseConnectionFactory

        Public Sub New(factory As DatabaseConnectionFactory)
            _factory = factory
        End Sub

        Public Function GetAll() As IReadOnlyList(Of EquipmentMaster) Implements IEquipmentRepository.GetAll
            Dim result As New List(Of EquipmentMaster)()
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "SELECT EquipmentId, EquipmentName, FactoryName, LineName, CreatedAt FROM EquipmentMaster ORDER BY EquipmentId;"
                    Using reader = command.ExecuteReader()
                        While reader.Read()
                            result.Add(Map(reader))
                        End While
                    End Using
                End Using
            End Using
            Return result
        End Function

        Public Function FindById(equipmentId As String) As EquipmentMaster Implements IEquipmentRepository.FindById
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "SELECT EquipmentId, EquipmentName, FactoryName, LineName, CreatedAt FROM EquipmentMaster WHERE EquipmentId = $equipmentId;"
                    command.Parameters.AddWithValue("$equipmentId", equipmentId)
                    Using reader = command.ExecuteReader()
                        If reader.Read() Then Return Map(reader)
                    End Using
                End Using
            End Using
            Return Nothing
        End Function

        Public Sub Add(entity As EquipmentMaster) Implements IEquipmentRepository.Add
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "INSERT INTO EquipmentMaster (EquipmentId, EquipmentName, FactoryName, LineName, CreatedAt) VALUES ($id, $name, $factory, $line, $createdAt);"
                    SetParameters(command, entity)
                    command.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Public Sub Update(entity As EquipmentMaster) Implements IEquipmentRepository.Update
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "UPDATE EquipmentMaster SET EquipmentName = $name, FactoryName = $factory, LineName = $line WHERE EquipmentId = $id;"
                    command.Parameters.AddWithValue("$id", entity.EquipmentId)
                    command.Parameters.AddWithValue("$name", entity.EquipmentName)
                    command.Parameters.AddWithValue("$factory", If(entity.FactoryName, CType(DBNull.Value, Object)))
                    command.Parameters.AddWithValue("$line", If(entity.LineName, CType(DBNull.Value, Object)))
                    If command.ExecuteNonQuery() <> 1 Then Throw New InvalidOperationException("更新対象の設備が見つかりません。")
                End Using
            End Using
        End Sub

        Public Sub Delete(equipmentId As String) Implements IEquipmentRepository.Delete
            Try
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "DELETE FROM EquipmentMaster WHERE EquipmentId = $equipmentId;"
                    command.Parameters.AddWithValue("$equipmentId", equipmentId)
                    If command.ExecuteNonQuery() <> 1 Then Throw New InvalidOperationException("削除対象の設備が見つかりません。")
                End Using
            End Using
            Catch ex As Microsoft.Data.Sqlite.SqliteException When ex.SqliteErrorCode = 19
                Throw New EquipmentInUseException()
            End Try
        End Sub

        Private Shared Sub SetParameters(command As Microsoft.Data.Sqlite.SqliteCommand, entity As EquipmentMaster)
            command.Parameters.AddWithValue("$id", entity.EquipmentId)
            command.Parameters.AddWithValue("$name", entity.EquipmentName)
            command.Parameters.AddWithValue("$factory", If(entity.FactoryName, CType(DBNull.Value, Object)))
            command.Parameters.AddWithValue("$line", If(entity.LineName, CType(DBNull.Value, Object)))
            command.Parameters.AddWithValue("$createdAt", entity.CreatedAt.ToString("O"))
        End Sub

        Private Shared Function Map(reader As Microsoft.Data.Sqlite.SqliteDataReader) As EquipmentMaster
            Return New EquipmentMaster With {.EquipmentId = reader.GetString(0), .EquipmentName = reader.GetString(1), .FactoryName = If(reader.IsDBNull(2), Nothing, reader.GetString(2)), .LineName = If(reader.IsDBNull(3), Nothing, reader.GetString(3)), .CreatedAt = DateTime.Parse(reader.GetString(4), Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.RoundtripKind)}
        End Function
    End Class
End Namespace
