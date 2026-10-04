Imports Microsoft.Data.Sqlite
Imports System.IO
Imports System.Text
Imports System.Text.RegularExpressions

Namespace Data
    ' Compare metadata, not CREATE TABLE formatting or autoindex names.
    Friend NotInheritable Class DatabaseSchemaValidator
        Public Shared Sub Validate(connection As SqliteConnection, version As Integer, Optional transaction As SqliteTransaction = Nothing)
            Using expected As New SqliteConnection("Data Source=:memory:")
                expected.Open()
                For Each ddl In DatabaseSchema.CreateStatements
                    Dim sql = ddl
                    If version = 0 AndAlso ddl.Contains("CREATE TABLE IF NOT EXISTS CsvImportConfig ") Then sql = DatabaseMigrations.LegacyConfigTable
                    Execute(expected, sql)
                Next
                Dim actualObjects = Rows(connection, "SELECT type,name,tbl_name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name;", transaction)
                Dim expectedObjects = Rows(expected, "SELECT type,name,tbl_name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name;")
                RequireEqual(actualObjects, expectedObjects, "Table/Index/View/Trigger")
                For Each tableName In {"EquipmentMaster", "MeasurementItemMaster", "MeasurementData", "CsvImportConfig", "CsvColumnMapping", "OperationLog", "ErrorLog"}
                    RequireEqual(Rows(connection, $"PRAGMA table_xinfo('{tableName}');", transaction), Rows(expected, $"PRAGMA table_xinfo('{tableName}');"), tableName & " columns/default/PK")
                    RequireEqual(Rows(connection, $"PRAGMA foreign_key_list('{tableName}');", transaction), Rows(expected, $"PRAGMA foreign_key_list('{tableName}');"), tableName & " FK")
                    RequireEqual(Indexes(connection, tableName, transaction), Indexes(expected, tableName, Nothing), tableName & " UNIQUE/Index")
                    Dim actualSql = CStr(Scalar(connection, $"SELECT sql FROM sqlite_master WHERE type='table' AND name='{tableName}';", transaction))
                    Dim expectedSql = CStr(Scalar(expected, $"SELECT sql FROM sqlite_master WHERE type='table' AND name='{tableName}';"))
                    RequireEqual(Checks(actualSql), Checks(expectedSql), tableName & " CHECK")
                    If Canonical(actualSql).Contains("autoincrement") <> Canonical(expectedSql).Contains("autoincrement") Then Throw New InvalidDataException(tableName & ": AUTOINCREMENT不一致")
                    If Regex.IsMatch(actualSql, "\b(WITHOUT\s+ROWID|STRICT|GENERATED|DEFERRABLE|COLLATE|ON\s+CONFLICT)\b", RegexOptions.IgnoreCase) Then Throw New InvalidDataException(tableName & ": 未対応のSchema修飾")
                Next
            End Using
        End Sub

        Public Shared Sub CheckIntegrity(connection As SqliteConnection, Optional transaction As SqliteTransaction = Nothing)
            Execute(connection, "PRAGMA ignore_check_constraints=OFF;", transaction)
            Dim integrity = Rows(connection, "PRAGMA integrity_check;", transaction)
            If integrity.Count <> 1 OrElse integrity(0) <> "ok" Then Throw New InvalidDataException("integrity_checkに失敗しました。")
            If Rows(connection, "PRAGMA foreign_key_check;", transaction).Count <> 0 Then Throw New InvalidDataException("foreign_key_checkに失敗しました。")
        End Sub

        Public Shared Function Scalar(connection As SqliteConnection, sql As String, Optional transaction As SqliteTransaction = Nothing) As Object
            Using command = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = sql
                Return command.ExecuteScalar()
            End Using
        End Function

        Public Shared Sub Execute(connection As SqliteConnection, sql As String, Optional transaction As SqliteTransaction = Nothing)
            Using command = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = sql
                command.ExecuteNonQuery()
            End Using
        End Sub

        Private Shared Function Rows(connection As SqliteConnection, sql As String, Optional transaction As SqliteTransaction = Nothing) As List(Of String)
            Dim result As New List(Of String)()
            Using command = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = sql
                Using reader = command.ExecuteReader()
                    While reader.Read()
                        Dim values As New List(Of String)()
                        For i = 0 To reader.FieldCount - 1
                            values.Add(If(reader.IsDBNull(i), "<NULL>", Convert.ToString(reader.GetValue(i), Globalization.CultureInfo.InvariantCulture)))
                        Next
                        result.Add(String.Join("|", values))
                    End While
                End Using
            End Using
            Return result
        End Function

        Private Shared Function Indexes(connection As SqliteConnection, tableName As String, transaction As SqliteTransaction) As List(Of String)
            Dim indices As New List(Of String())()
            Using command = connection.CreateCommand()
                command.Transaction = transaction
                command.CommandText = $"PRAGMA index_list('{tableName}');"
                Using reader = command.ExecuteReader()
                    While reader.Read()
                        indices.Add({reader.GetString(1), reader.GetInt64(2).ToString(), reader.GetString(3), reader.GetInt64(4).ToString()})
                    End While
                End Using
            End Using
            Dim result As New List(Of String)()
            For Each item In indices
                ' PRAGMA index_xinfo includes ordering/collation/key membership.
                Dim escapedName = item(0).Replace("'", "''")
                result.Add(String.Join("|", item.Skip(1)) & ":" & String.Join(";", Rows(connection, $"PRAGMA index_xinfo('{escapedName}');", transaction)))
            Next
            result.Sort(StringComparer.Ordinal)
            Return result
        End Function

        Private Shared Sub RequireEqual(actual As List(Of String), expected As List(Of String), label As String)
            If Not actual.SequenceEqual(expected, StringComparer.Ordinal) Then Throw New InvalidDataException("Schema不一致: " & label & "。自動修復は行いません。")
        End Sub

        Private Shared Function Checks(sql As String) As List(Of String)
            Dim normalized = Canonical(sql)
            Dim result As New List(Of String)()
            Dim offset = 0
            Do
                Dim start = normalized.IndexOf("check(", offset, StringComparison.Ordinal)
                If start < 0 Then Exit Do
                Dim depth = 1
                Dim i = start + 6
                Dim quoted As Boolean = False
                While i < normalized.Length AndAlso depth > 0
                    Dim ch = normalized(i)
                    If ch = "'"c Then
                        If quoted AndAlso i + 1 < normalized.Length AndAlso normalized(i + 1) = "'"c Then
                            i += 2
                            Continue While
                        End If
                        quoted = Not quoted
                    ElseIf Not quoted Then
                        If ch = "("c Then depth += 1
                        If ch = ")"c Then depth -= 1
                    End If
                    i += 1
                End While
                If depth <> 0 Then Throw New InvalidDataException("CHECK解析失敗")
                result.Add(normalized.Substring(start, i - start))
                offset = i
            Loop
            result.Sort(StringComparer.Ordinal)
            Return result
        End Function

        Private Shared Function Canonical(sql As String) As String
            Dim result As New StringBuilder()
            Dim quoted As Boolean = False
            Dim i = 0
            While i < sql.Length
                Dim ch = sql(i)
                If ch = "'"c Then
                    result.Append(ch)
                    If quoted AndAlso i + 1 < sql.Length AndAlso sql(i + 1) = "'"c Then
                        result.Append(ch)
                        i += 2
                        Continue While
                    End If
                    quoted = Not quoted
                ElseIf quoted Then
                    result.Append(ch)
                ElseIf Not Char.IsWhiteSpace(ch) Then
                    result.Append(Char.ToLowerInvariant(ch))
                End If
                i += 1
            End While
            Return result.ToString()
        End Function
    End Class
End Namespace
