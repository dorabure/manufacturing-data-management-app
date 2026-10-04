Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Domain.Constants
Imports System.IO
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Data

Namespace Repositories
    Public Class CsvImportConfigRepository
        Implements ICsvImportConfigRepository

        Private ReadOnly _factory As DatabaseConnectionFactory
        Public Sub New(factory As DatabaseConnectionFactory)
            _factory = factory
        End Sub

        Public Function GetAll() As IReadOnlyList(Of CsvImportConfig) Implements ICsvImportConfigRepository.GetAll
            Dim result As New List(Of CsvImportConfig)()
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = CsvImportConfigSql.SelectAll
                    Using reader = command.ExecuteReader()
                        While reader.Read()
                            result.Add(MapConfig(reader))
                        End While
                    End Using
                End Using
            End Using
            Return result
        End Function

        Public Function FindById(configId As Integer) As CsvImportConfig Implements ICsvImportConfigRepository.FindById
            Return Find("ConfigId = $value", configId)
        End Function

        Public Function FindByName(configName As String) As CsvImportConfig Implements ICsvImportConfigRepository.FindByName
            If String.IsNullOrWhiteSpace(configName) Then Return Nothing
            Return Find("ConfigName = $value", configName)
        End Function

        Public Function GetMappings(configId As Integer) As IReadOnlyList(Of CsvColumnMapping) Implements ICsvImportConfigRepository.GetMappings
            Dim result As New List(Of CsvColumnMapping)()
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = CsvImportConfigSql.SelectMappings
                    command.Parameters.AddWithValue("$configId", configId)
                    Using reader = command.ExecuteReader()
                        While reader.Read()
                            result.Add(New CsvColumnMapping With {.MappingId = reader.GetInt32(0), .ConfigId = reader.GetInt32(1), .FieldName = reader.GetString(2), .CsvColumnIndex = reader.GetInt32(3)})
                        End While
                    End Using
                End Using
            End Using
            Return result
        End Function

        Public Sub Save(config As CsvImportConfig, mappings As IReadOnlyList(Of CsvColumnMapping)) Implements ICsvImportConfigRepository.Save
            Using connection = _factory.CreateConnection()
                Using transaction = connection.BeginTransaction()
                    Try
                        If config.ConfigId = 0 Then
                            Using command = connection.CreateCommand()
                                command.Transaction = transaction
                                command.CommandText = CsvImportConfigSql.InsertConfig
                                SetConfigParameters(command, config)
                                config.ConfigId = CInt(command.ExecuteScalar())
                            End Using
                        Else
                            Using command = connection.CreateCommand()
                                command.Transaction = transaction
                                command.CommandText = CsvImportConfigSql.UpdateFormat
                                command.Parameters.AddWithValue("$configId", config.ConfigId)
                                SetConfigParameters(command, config)
                                If command.ExecuteNonQuery() <> 1 Then Throw New InvalidOperationException("更新対象のCSV設定が見つかりません。")
                            End Using
                            Using command = connection.CreateCommand()
                                command.Transaction = transaction
                                command.CommandText = CsvImportConfigSql.DeleteMappings
                                command.Parameters.AddWithValue("$configId", config.ConfigId)
                                command.ExecuteNonQuery()
                            End Using
                        End If
                        For Each mapping In mappings
                            Using command = connection.CreateCommand()
                                command.Transaction = transaction
                                command.CommandText = CsvImportConfigSql.InsertMapping
                                command.Parameters.AddWithValue("$configId", config.ConfigId)
                                command.Parameters.AddWithValue("$fieldName", mapping.FieldName)
                                command.Parameters.AddWithValue("$columnIndex", mapping.CsvColumnIndex)
                                command.ExecuteNonQuery()
                                mapping.ConfigId = config.ConfigId
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

        Public Sub Delete(configId As Integer) Implements ICsvImportConfigRepository.Delete
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = CsvImportConfigSql.DeleteConfig
                    command.Parameters.AddWithValue("$configId", configId)
                    command.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Private Function Find(condition As String, value As Object) As CsvImportConfig
            Using connection = _factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = CsvImportConfigSql.SelectWhere(condition)
                    command.Parameters.AddWithValue("$value", value)
                    Using reader = command.ExecuteReader()
                        If reader.Read() Then Return MapConfig(reader)
                    End Using
                End Using
            End Using
            Return Nothing
        End Function

        Private Shared Sub SetConfigParameters(command As SqliteCommand, config As CsvImportConfig)
            command.Parameters.AddWithValue("$configName", config.ConfigName)
            command.Parameters.AddWithValue("$encoding", config.Encoding)
            command.Parameters.AddWithValue("$delimiter", config.Delimiter)
            command.Parameters.AddWithValue("$hasHeader", If(config.HasHeader, 1, 0))
        End Sub

        Public Sub SaveMonitoringSettings(configId As Integer, mode As ImportMode, folderPath As String, intervalSeconds As Integer) Implements ICsvImportConfigRepository.SaveMonitoringSettings
            If configId <= 0 Then Throw New ArgumentOutOfRangeException(NameOf(configId))
            If intervalSeconds < 1 OrElse intervalSeconds > 604800 Then Throw New ArgumentOutOfRangeException(NameOf(intervalSeconds))
            Dim modeText As String
            Select Case mode
                Case ImportMode.SingleFile : modeText = "SingleFile"
                Case ImportMode.PeriodicFolder : modeText = "PeriodicFolder"
                Case Else : Throw New ArgumentOutOfRangeException(NameOf(mode))
            End Select
            Dim normalized = If(String.IsNullOrWhiteSpace(folderPath), Nothing, Path.GetFullPath(folderPath.Trim()))
            Using connection = _factory.CreateConnection(), command = connection.CreateCommand()
                command.CommandText = CsvImportConfigSql.UpdateMonitoring
                command.Parameters.AddWithValue("$id", configId)
                command.Parameters.AddWithValue("$mode", modeText)
                command.Parameters.AddWithValue("$folder", If(CType(normalized, Object), DBNull.Value))
                command.Parameters.AddWithValue("$seconds", intervalSeconds)
                If command.ExecuteNonQuery() <> 1 Then Throw New InvalidOperationException("対象CSV設定が存在しません。")
            End Using
        End Sub

        Private Shared Function MapConfig(reader As SqliteDataReader) As CsvImportConfig
            If reader.GetString(8) <> "text" OrElse (reader.GetString(9) <> "null" AndAlso reader.GetString(9) <> "text") OrElse reader.GetString(10) <> "integer" Then Throw New InvalidDataException("監視設定のDB型が不正です。")
            Dim mode As ImportMode
            Select Case reader.GetString(5)
                Case "SingleFile" : mode = ImportMode.SingleFile
                Case "PeriodicFolder" : mode = ImportMode.PeriodicFolder
                Case Else : Throw New InvalidDataException("未知のImportModeです。")
            End Select
            Dim seconds = reader.GetInt64(7)
            If seconds < 1 OrElse seconds > 604800 Then Throw New InvalidDataException("監視周期のDB値が不正です。")
            Return New CsvImportConfig With {.ImportMode = mode, .WatchFolderPath = If(reader.IsDBNull(6), Nothing, reader.GetString(6)), .WatchIntervalSeconds = CInt(seconds), .ConfigId = reader.GetInt32(0), .ConfigName = reader.GetString(1), .Encoding = reader.GetString(2), .Delimiter = reader.GetString(3), .HasHeader = reader.GetInt32(4) <> 0}
        End Function
    End Class
End Namespace
