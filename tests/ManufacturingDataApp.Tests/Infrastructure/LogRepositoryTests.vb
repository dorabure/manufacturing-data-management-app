Imports Microsoft.Data.Sqlite
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Data
Imports ManufacturingDataApp.Infrastructure.Logging
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports System.IO
Imports Xunit

Public Class LogRepositoryTests
    <Fact>
    Public Sub ErrorLog_Search_AppliesAllConditionsBoundariesAndSortOrder()
        Using db = New LogTestDatabase()
            Dim day = New DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
            db.Writer.WriteErrors({New ErrorLog With {.OccurredAt = day, .FileName = "a.csv", .RowNumber = 1, .FieldName = "設備ID", .ErrorType = "必須", .ErrorMessage = "a"}, New ErrorLog With {.OccurredAt = day.AddHours(23).AddMinutes(59).AddSeconds(59), .FileName = "b.csv", .RowNumber = 2, .FieldName = "測定値", .ErrorType = "型", .ErrorMessage = "b"}, New ErrorLog With {.OccurredAt = day.AddDays(1), .FileName = "a.csv", .RowNumber = 3, .FieldName = "測定値", .ErrorType = "範囲", .ErrorMessage = "c"}})
            Dim all = db.Errors.Search(Nothing, Nothing, Nothing, Nothing) ' LOG-ERR-001, LOG-ERR-009
            Assert.Equal(3, all.Count) : Assert.True(all(0).OccurredAt >= all(1).OccurredAt)
            Assert.Equal(2, db.Errors.Search(day.AddHours(1), Nothing, Nothing, Nothing).Count) ' LOG-ERR-002
            Assert.Equal(2, db.Errors.Search(Nothing, day.AddDays(1), Nothing, Nothing).Count) ' LOG-ERR-003 boundary
            Assert.Single(db.Errors.Search(day, day.AddDays(1), "b.csv", "型")) ' LOG-ERR-004..007
            Assert.Empty(db.Errors.Search(Nothing, Nothing, "none.csv", Nothing)) ' LOG-ERR-008
        End Using
    End Sub
    <Fact>
    Public Sub OperationLog_Search_AppliesAllConditionsBoundariesAndSortOrder()
        Using db = New LogTestDatabase()
            Dim day = New DateTime(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc)
            db.Writer.WriteOperation(New OperationLog With {.ExecutedAt = day, .OperationType = "CSV取込"})
            db.Writer.WriteOperation(New OperationLog With {.ExecutedAt = day.AddHours(23).AddMinutes(59).AddSeconds(59), .OperationType = "CSV出力"})
            db.Writer.WriteOperation(New OperationLog With {.ExecutedAt = day.AddDays(1), .OperationType = "編集"})
            Dim all = db.Operations.Search(Nothing, Nothing, Nothing) ' LOG-OP-001, LOG-OP-008
            Assert.Equal(3, all.Count) : Assert.True(all(0).ExecutedAt >= all(1).ExecutedAt)
            Assert.Equal(2, db.Operations.Search(day.AddHours(1), Nothing, Nothing).Count) ' LOG-OP-002
            Assert.Equal(2, db.Operations.Search(Nothing, day.AddDays(1), Nothing).Count) ' LOG-OP-003 boundary
            Assert.Single(db.Operations.Search(day, day.AddDays(1), "CSV出力")) ' LOG-OP-004..006
            Assert.Empty(db.Operations.Search(Nothing, Nothing, "不存在")) ' LOG-OP-007
        End Using
    End Sub
    Private NotInheritable Class LogTestDatabase
        Implements IDisposable
        Private ReadOnly p As String = Path.Combine(Path.GetTempPath(), $"log-{Guid.NewGuid():N}")
        Public Sub New()
            Dim f = New DatabaseConnectionFactory(Path.Combine(p, "test.db")) : Dim i = New DatabaseInitializer(f) : i.Initialize()
            Writer = New DbLogWriter(f) : Errors = New ErrorLogRepository(f) : Operations = New OperationLogRepository(f)
        End Sub
        Public ReadOnly Property Writer As DbLogWriter
        Public ReadOnly Property Errors As ErrorLogRepository
        Public ReadOnly Property Operations As OperationLogRepository
        Public Sub Dispose() Implements IDisposable.Dispose
            SqliteConnection.ClearAllPools() : If Directory.Exists(p) Then Directory.Delete(p, recursive:=True)
        End Sub
    End Class
End Class
