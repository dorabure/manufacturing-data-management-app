Imports System.IO
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Logging
Imports ManufacturingDataApp.Infrastructure.PeriodicImport
Imports ManufacturingDataApp.Tests.TestSupport
Imports Microsoft.Data.Sqlite
Imports Xunit

Public Class Phase4BoundaryTests
    <Fact>
    Public Sub ActualSqliteInsertFailure_IsRecovery_NotReadOrValidationFailure()
        Using f As New Phase4Fixture()
            f.Write("A.csv")
            f.Write("B.csv", 1)
            f.Database.Execute("CREATE TRIGGER TestFailure BEFORE INSERT ON MeasurementData BEGIN SELECT RAISE(ABORT, 'injected insert failure'); END;")
            f.Prepare()
            Assert.Equal(PeriodicStopReason.RecoveryRequired, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run()).Reason)
            Assert.Equal(PeriodicFileOutcome.DatabaseFailed, f.State().Attempts.Single().Result.Outcome)
            Assert.False(f.State().Attempts.Single().Result.DbOutcomeKnown)
            Assert.Equal(0L, f.DataCount())
            Assert.Single(f.Executor.Calls)
            Assert.True(File.Exists(Path.Combine(f.Root, "A.csv")))
            Assert.Empty(Directory.GetFiles(Path.Combine(f.Root, "error"), "*.csv"))
        End Using
    End Sub

    <Theory>
    <InlineData("../outside.csv")>
    <InlineData("../watch-other/file.csv")>
    <InlineData("success/../A.csv")>
    <InlineData("A.csv:stream")>
    <InlineData("A.csv.")>
    <InlineData("A.csv ")>
    Public Sub UnsafeRelativePath_RejectedWithoutIoOutsideRoot(relative As String)
        Using f As New Phase4Fixture()
            f.Write("A.csv")
            Assert.Throws(Of IOException)(Function() f.Files.TryRead(f.Root, relative))
            Assert.Equal(0L, f.DataCount())
        End Using
    End Sub

    <Theory>
    <InlineData("\\server\share")>
    <InlineData("\\?\C:\watch")>
    <InlineData("relative")>
    Public Sub UnsupportedRoot_Rejected(root As String)
        Assert.Throws(Of IOException)(Function() New PeriodicImportFileStore().NormalizeRoot(root))
    End Sub

    <Fact>
    Public Sub OrdinalTieBreaker_IsDeterministic()
        Dim entries = {
            New PeriodicOrderEntryDto With {.OriginalFileName = "a.csv"},
            New PeriodicOrderEntryDto With {.OriginalFileName = "B.csv"},
            New PeriodicOrderEntryDto With {.OriginalFileName = "A.csv"}}
        Assert.True(PeriodicImportFileStore.SortEntries(entries).Select(Function(e) e.OriginalFileName).SequenceEqual({"A.csv", "a.csv", "B.csv"}))
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub DestinationCollision_NeverOverwrites_HandlesRace(race As Boolean)
        Using f As New Phase4Fixture()
            f.Write("A.csv")
            f.Prepare()
            Dim existing = Path.Combine(f.Root, "success/A.csv")
            If race Then
                f.Files.BeforeMove = Sub(root, source, target)
                                         If target = "success/A.csv" Then File.WriteAllText(existing, "keep")
                                     End Sub
            Else
                File.WriteAllText(existing, "keep")
            End If
            f.Run()
            Assert.Equal("keep", File.ReadAllText(existing))
            Assert.Equal(2, Directory.GetFiles(Path.Combine(f.Root, "success")).Length)
            Assert.EndsWith("_20260927_000000000_1.csv", f.State().Attempts.Single().Destination)
            Assert.Single(f.Executor.Calls)
            Assert.Equal(1L, f.DataCount())
        End Using
    End Sub

    <Fact>
    Public Sub OneHundredCollisions_StopWithoutOverwriteOrReimport()
        Using f As New Phase4Fixture()
            f.Write("A.csv")
            f.Prepare()
            For i = 0 To 99
                Dim target = f.Files.Destination(f.Root, "A.csv", True, i, f.Clock.GetUtcNow())
                File.WriteAllText(Path.Combine(f.Root, target), "keep")
            Next
            Assert.Equal(PeriodicStopReason.MovePending, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run()).Reason)
            Assert.Equal(1L, f.DataCount())
            Assert.True(File.Exists(Path.Combine(f.Root, "A.csv")))
            Assert.Equal(ProcessingStage.ResultKnown, f.State().Attempts.Single().Stage)
            Assert.Equal(100, Directory.GetFiles(Path.Combine(f.Root, "success")).Length)
        End Using
    End Sub

    <Fact>
    Public Sub LongCollisionName_IsBoundedAndHasHash()
        Using f As New Phase4Fixture()
            Dim original = New String("a"c, 245) & ".csv"
            Dim target = f.Files.Destination(f.Root, original, True, 1, f.Clock.GetUtcNow())
            Assert.True(Path.GetFileName(target).Length <= 255)
            Assert.EndsWith(".csv", target)
            Assert.Contains("_", target)
            Assert.NotEqual(f.Files.Destination(f.Root, New String("a"c, 244) & "b.csv", True, 1, f.Clock.GetUtcNow()), target)
        End Using
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub LogPartialFailure_MovesThenStops_NoRepeatErrors(rejected As Boolean)
        Using f As New Phase4Fixture()
            f.Write("A.csv", 0, If(rejected, "bad", "10"))
            f.Write("B.csv", 1)
            Dim logger As New FailingLog(New DbLogWriter(f.Database.Factory))
            Dim executor As New PeriodicCsvImportExecutor(f.Database.Factory, logger:=logger)
            f.Executor.OverrideResult = Function(r) executor.Execute(r)
            f.Prepare()
            Dim stopped = Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Assert.Equal(If(rejected, PeriodicStopReason.CorrectionPending, PeriodicStopReason.LogFailure), stopped.Reason)
            Assert.Single(f.Executor.Calls)
            Assert.Equal(1, logger.ErrorCalls)
            Assert.Equal(1, logger.OperationCalls)
            Assert.Equal(If(rejected, 0L, 1L), f.DataCount())
            Assert.Equal(If(rejected, 1L, 0L), CLng(f.Database.Scalar("SELECT COUNT(*) FROM ErrorLog;")))
            Assert.True(File.Exists(Path.Combine(f.Root, If(rejected, "error/A.csv", "success/A.csv"))))
            Assert.True(File.Exists(Path.Combine(f.Root, "B.csv")))
            Assert.False(f.State().Attempts.Single().Result.LogRecorded)
        End Using
    End Sub

    <Theory>
    <InlineData("read")>
    <InlineData("database")>
    <InlineData("unknown")>
    Public Sub ExecutorClassifiesOnlyProvenReadBoundaryAndLogsSafeReason(kind As String)
        Using f As New Phase4Fixture()
            f.Write("A.csv")
            f.Write("B.csv", 1)
            Dim adapter As New ThrowingAdapter()
            Select Case kind
                Case "read" : adapter.Failure = New IOException("sensitive payload")
                Case "database" : adapter.Failure = New SqliteException("sensitive payload", 5)
                Case "unknown" : adapter.Failure = New InvalidOperationException("sensitive payload")
            End Select
            Dim executor As New PeriodicCsvImportExecutor(f.Database.Factory, adapter:=adapter)
            f.Executor.OverrideResult = Function(r) executor.Execute(r)
            f.Prepare()
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Dim a = f.State().Attempts.Single()
            Assert.Equal(If(kind = "read", PeriodicFileOutcome.ReadFailed, If(kind = "database", PeriodicFileOutcome.DatabaseFailed, PeriodicFileOutcome.Unknown)), a.Result.Outcome)
            Assert.Equal(kind = "read", a.Result.DbOutcomeKnown)
            Assert.Equal(If(kind = "read", ProcessingStage.Completed, ProcessingStage.RecoveryRequired), a.Stage)
            Assert.Equal(If(kind = "read", "周期CSV取込失敗", "周期CSV取込結果不明"), CStr(f.Database.Scalar("SELECT OperationType FROM OperationLog;")))
            Assert.DoesNotContain("sensitive", CStr(f.Database.Scalar("SELECT ErrorMessage FROM ErrorLog;")))
            Assert.True(File.Exists(Path.Combine(f.Root, If(kind = "read", "error/A.csv", "A.csv"))))
            Assert.Equal(0L, f.DataCount())
            Assert.Single(f.Executor.Calls)
        End Using
    End Sub

    <Fact>
    Public Sub ActualBadCsv_ParseFailure_IsReadFailed()
        Using f As New Phase4Fixture()
            Dim csvPath = f.Write("A.csv")
            ' Seven fields under a six-column header makes the existing adapter fail
            ' outside the parser's documented exceptions: conservative Unknown is correct.
            ' A malformed quote is the CsvHelper BadDataException read-boundary case.
            File.WriteAllText(csvPath, "h1,h2,h3,h4,h5,h6" & vbCrLf & "bad""quote,EQ001,設備,温度,10,℃")
            f.Prepare()
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Assert.Equal(PeriodicFileOutcome.ReadFailed, f.State().Attempts.Single().Result.Outcome)
            Assert.True(File.Exists(Path.Combine(f.Root, "error/A.csv")))
        End Using
    End Sub

    <Theory>
    <InlineData("mixed")>
    <InlineData("csv-duplicate")>
    <InlineData("db-duplicate")>
    Public Sub ValidationFailure_AtomicAndFollowingStopped(kind As String)
        Using f As New Phase4Fixture()
            Dim file = f.Write("A.csv", 0)
            Dim normal = IO.File.ReadAllLines(file)(1)
            Select Case kind
                Case "mixed"
                    IO.File.AppendAllText(file, vbCrLf & normal.Replace("09:00", "09:01") & vbCrLf & normal.Replace("09:00", "09:02") &
                        vbCrLf & normal.Replace("09:00", "09:03").Replace(",10,", ",bad,") & vbCrLf & normal.Replace("09:00", "09:04").Replace(",10,", ",bad,"))
                Case "csv-duplicate" : IO.File.AppendAllText(file, vbCrLf & normal)
                Case "db-duplicate"
                    Dim repository As New ManufacturingDataApp.Infrastructure.Repositories.MeasurementDataRepository(f.Database.Factory)
                    repository.Add(New MeasurementData With {.MeasuredAt = New DateTime(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc), .EquipmentId = "EQ001", .EquipmentName = "設備", .ItemName = "温度", .Value = 10, .Unit = "℃"})
            End Select
            f.Write("B.csv", 6)
            IO.File.SetLastWriteTimeUtc(file, New DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            f.Prepare()
            Assert.Equal(PeriodicStopReason.CorrectionPending, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run()).Reason)
            Assert.Single(f.Executor.Calls)
            Assert.Equal(If(kind = "db-duplicate", 1L, 0L), f.DataCount())
            Dim result = f.State().Attempts.Single().Result
            Assert.Equal(PeriodicFileOutcome.Rejected, result.Outcome)
            If kind = "mixed" Then
                Assert.Equal(5, result.TotalCount)
                Assert.Equal(5, result.UnregisteredCount)
                Assert.Equal(2, result.ValidationErrorRowCount)
                Assert.Equal(3, result.HeldValidRowCount)
            End If
            Assert.True(IO.File.Exists(Path.Combine(f.Root, "B.csv")))
        End Using
    End Sub

    <Fact>
    Public Sub Publication_TmpThenRename_IsNotSeenUntilPublished()
        Using f As New Phase4Fixture()
            Dim tmp = f.Write("A.tmp")
            f.Prepare()
            f.Run()
            Assert.Empty(f.Executor.Calls)
            File.Move(tmp, Path.Combine(f.Root, "A.csv"))
            f.Run()
            Assert.Single(f.Executor.Calls)
            Assert.Equal(1L, f.DataCount())
        End Using
    End Sub

    Private Class ThrowingAdapter
        Implements ICsvFileAdapter
        Public Failure As Exception
        Public Function Read(path As String, encoding As String, delimiter As String, header As Boolean) As IReadOnlyList(Of IReadOnlyDictionary(Of String, String)) Implements ICsvFileAdapter.Read
            Throw Failure
        End Function
        Public Sub Write(path As String, rows As IEnumerable(Of IReadOnlyDictionary(Of String, String)), encoding As String, delimiter As String, header As Boolean) Implements ICsvFileAdapter.Write
            Throw New NotSupportedException()
        End Sub
    End Class

    Private Class FailingLog
        Implements IImportLogWriter
        Private ReadOnly _inner As IImportLogWriter
        Public ErrorCalls As Integer
        Public OperationCalls As Integer
        Public Sub New(inner As IImportLogWriter)
            _inner = inner
        End Sub
        Public Sub WriteErrors(errors As IEnumerable(Of ErrorLog)) Implements IImportLogWriter.WriteErrors
            ErrorCalls += 1
            _inner.WriteErrors(errors)
        End Sub
        Public Sub WriteOperation(operation As OperationLog) Implements IImportLogWriter.WriteOperation
            OperationCalls += 1
            Throw New IOException("log unavailable")
        End Sub
    End Class
End Class
