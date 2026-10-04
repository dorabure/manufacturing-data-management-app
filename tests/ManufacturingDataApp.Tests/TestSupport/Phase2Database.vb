Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Infrastructure.Data
Imports System.IO
Imports System.Text

Namespace TestSupport
    Friend NotInheritable Class Phase2Database
        Implements IDisposable

        Public ReadOnly DirectoryPath As String = Path.Combine(Path.GetTempPath(), "ManufacturingDataApp.Phase2." & Guid.NewGuid().ToString("N"))
        Public ReadOnly Factory As DatabaseConnectionFactory
        Public Sub New(Optional legacy As Boolean = False, Optional pooling As Boolean = True)
            Directory.CreateDirectory(DirectoryPath)
            Factory = New DatabaseConnectionFactory(Path.Combine(DirectoryPath, "test.db"), pooling)
            If legacy Then
                Using connection = Factory.CreateConnection()
                    For Each statement In LegacyDatabaseSchema.CreateStatements
                        DatabaseSchemaValidator.Execute(connection, statement)
                    Next
                    DatabaseSchemaValidator.Execute(connection, LegacyDatabaseSchema.SeedDataStatement)
                    DatabaseSchemaValidator.Execute(connection, "DELETE FROM EquipmentMaster WHERE EquipmentId='EQ003'; UPDATE CsvImportConfig SET ConfigName='UserConfig',Delimiter=';',HasHeader=0; INSERT INTO MeasurementData(Id,MeasuredAt,EquipmentId,EquipmentName,ItemName,Value,Unit) VALUES(71,'2026-01-01','EQ001','保持設備','温度',23.5,'℃'); INSERT INTO OperationLog(LogId,OperationType,ElapsedMs,TotalCount,SuccessCount,FailureCount) VALUES(81,'旧ログ',123,4,3,1); INSERT INTO ErrorLog(ErrorId,ConfigId,FileName,RowNumber,FieldName,ErrorType,ErrorMessage) VALUES(91,1,'old.csv',3,'温度','Validation','保持エラー');")
                End Using
            End If
        End Sub

        Public Sub Initialize(Optional checkpoint As Action(Of String, SqliteConnection, SqliteTransaction) = Nothing)
            Dim initializer As New DatabaseInitializer(Factory, New DatabaseMigrator(checkpoint))
            initializer.Initialize()
        End Sub

        Public Sub Execute(sql As String)
            Using connection = Factory.CreateConnection()
                DatabaseSchemaValidator.Execute(connection, sql)
            End Using
        End Sub

        Public Function Scalar(sql As String) As Object
            Using connection = Factory.CreateConnection()
                Return DatabaseSchemaValidator.Scalar(connection, sql)
            End Using
        End Function

        Public Function Snapshot() As String
            Dim result As New StringBuilder()
            Using connection = Factory.CreateConnection()
                For Each tableName In {"EquipmentMaster", "MeasurementItemMaster", "MeasurementData", "CsvImportConfig", "CsvColumnMapping", "OperationLog", "ErrorLog"}
                    result.AppendLine(tableName)
                    Using command = connection.CreateCommand()
                        Dim columns = If(tableName = "CsvImportConfig", "ConfigId,ConfigName,Encoding,Delimiter,HasHeader", "*")
                        command.CommandText = $"SELECT {columns} FROM {tableName} ORDER BY 1;"
                        Using reader = command.ExecuteReader()
                            While reader.Read()
                                For index = 0 To reader.FieldCount - 1
                                    Dim value = If(reader.IsDBNull(index), "<NULL>", Convert.ToString(reader.GetValue(index), Globalization.CultureInfo.InvariantCulture))
                                    result.Append(value.Length).Append(":").Append(value).Append("|")
                                Next
                                result.AppendLine()
                            End While
                        End Using
                    End Using
                Next
            End Using
            Return result.ToString()
        End Function

        Public Function Backups() As String()
            Return Directory.GetFiles(DirectoryPath, "*.db.bak")
        End Function

        Public Sub Dispose() Implements IDisposable.Dispose
            SqliteConnection.ClearAllPools()
            Directory.Delete(DirectoryPath, recursive:=True)
        End Sub
    End Class
End Namespace
