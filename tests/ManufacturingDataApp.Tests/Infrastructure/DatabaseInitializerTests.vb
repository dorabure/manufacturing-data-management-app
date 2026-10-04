Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Infrastructure.Data
Imports System.IO
Imports Xunit

Public Class DatabaseInitializerTests
    <Fact>
    Public Sub Initialize_CreatesDatabaseFileAndAllTables()
        Using database = New TestDatabase()
            database.Initialize()
            Assert.True(File.Exists(database.DatabasePath))

            Using connection = database.Factory.CreateConnection()
                Dim expectedTables = {"EquipmentMaster", "MeasurementItemMaster", "MeasurementData", "CsvImportConfig", "CsvColumnMapping", "OperationLog", "ErrorLog"}
                For Each tableName In expectedTables
                    Using command = connection.CreateCommand()
                        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tableName;"
                        command.Parameters.AddWithValue("$tableName", tableName)
                        Assert.Equal(1L, CLng(command.ExecuteScalar()))
                    End Using
                Next
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub CreateConnection_EnablesForeignKeys()
        Using database = New TestDatabase()
            database.Initialize()
            Using connection = database.Factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "PRAGMA foreign_keys;"
                    Assert.Equal(1L, CLng(command.ExecuteScalar()))
                End Using
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub MeasurementData_RejectsDuplicateKey()
        Using database = New TestDatabase()
            database.Initialize()
            Using connection = database.Factory.CreateConnection()
                InsertMeasurement(connection, "EQ001", "2026-01-01T00:00:00.0000000Z", "温度")
                Assert.Throws(Of SqliteException)(Sub() InsertMeasurement(connection, "EQ001", "2026-01-01T00:00:00.0000000Z", "温度"))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub CsvColumnMapping_RejectsDuplicateFieldAndColumnIndex()
        Using database = New TestDatabase()
            database.Initialize()
            Using connection = database.Factory.CreateConnection()
                Dim configId = GetConfigId(connection)
                InsertMapping(connection, configId, "テスト項目A", 7)
                Assert.Throws(Of SqliteException)(Sub() InsertMapping(connection, configId, "テスト項目A", 8))
                Assert.Throws(Of SqliteException)(Sub() InsertMapping(connection, configId, "テスト項目B", 7))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub Initialize_PreservesExistingData()
        Using database = New TestDatabase()
            database.Initialize()
            Using connection = database.Factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "INSERT INTO EquipmentMaster (EquipmentId, EquipmentName) VALUES ($equipmentId, $equipmentName);"
                    command.Parameters.AddWithValue("$equipmentId", "EQ-KEEP")
                    command.Parameters.AddWithValue("$equipmentName", "保持確認設備")
                    command.ExecuteNonQuery()
                End Using
            End Using

            database.Initialize()

            Using connection = database.Factory.CreateConnection()
                Using command = connection.CreateCommand()
                    command.CommandText = "SELECT COUNT(*) FROM EquipmentMaster WHERE EquipmentId = $equipmentId;"
                    command.Parameters.AddWithValue("$equipmentId", "EQ-KEEP")
                    Assert.Equal(1L, CLng(command.ExecuteScalar()))
                End Using
            End Using
        End Using
    End Sub

    Private Shared Sub InsertMeasurement(connection As SqliteConnection, equipmentId As String, measuredAt As String, itemName As String)
        Using command = connection.CreateCommand()
            command.CommandText = "INSERT INTO MeasurementData (MeasuredAt, EquipmentId, EquipmentName, ItemName, Value, Unit) VALUES ($measuredAt, $equipmentId, $equipmentName, $itemName, $value, $unit);"
            command.Parameters.AddWithValue("$measuredAt", measuredAt)
            command.Parameters.AddWithValue("$equipmentId", equipmentId)
            command.Parameters.AddWithValue("$equipmentName", "テスト設備")
            command.Parameters.AddWithValue("$itemName", itemName)
            command.Parameters.AddWithValue("$value", 10.0)
            command.Parameters.AddWithValue("$unit", "℃")
            command.ExecuteNonQuery()
        End Using
    End Sub

    Private Shared Function GetConfigId(connection As SqliteConnection) As Long
        Using command = connection.CreateCommand()
            command.CommandText = "SELECT ConfigId FROM CsvImportConfig WHERE ConfigName = 'SCADA_A';"
            Return CLng(command.ExecuteScalar())
        End Using
    End Function

    Private Shared Sub InsertMapping(connection As SqliteConnection, configId As Long, fieldName As String, columnIndex As Integer)
        Using command = connection.CreateCommand()
            command.CommandText = "INSERT INTO CsvColumnMapping (ConfigId, FieldName, CsvColumnIndex) VALUES ($configId, $fieldName, $columnIndex);"
            command.Parameters.AddWithValue("$configId", configId)
            command.Parameters.AddWithValue("$fieldName", fieldName)
            command.Parameters.AddWithValue("$columnIndex", columnIndex)
            command.ExecuteNonQuery()
        End Using
    End Sub

    Private NotInheritable Class TestDatabase
        Implements IDisposable

        Private ReadOnly _directoryPath As String = Path.Combine(Path.GetTempPath(), $"ManufacturingDataApp.Tests.{Guid.NewGuid():N}")

        Public Sub New()
            DatabasePath = Path.Combine(_directoryPath, "ManufacturingDataApp.db")
            Factory = New DatabaseConnectionFactory(DatabasePath)
        End Sub

        Public ReadOnly Property DatabasePath As String
        Public ReadOnly Property Factory As DatabaseConnectionFactory

        Public Sub Initialize()
            Dim initializer = New DatabaseInitializer(Factory)
            initializer.Initialize()
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            If Directory.Exists(_directoryPath) Then
                SqliteConnection.ClearAllPools()
                Directory.Delete(_directoryPath, recursive:=True)
            End If
        End Sub
    End Class
End Class
