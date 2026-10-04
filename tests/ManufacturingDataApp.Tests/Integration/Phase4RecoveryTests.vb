Imports System.IO
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase4RecoveryTests
    <Fact>
    Public Sub CorrectedSuccessCompletedBeforeCursor_RepairsWithoutReuse()
        Using f As New Phase4Fixture()
            f.Write("B.csv", 1, "bad")
            f.Write("C.csv", 2)
            f.Prepare()
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            f.Write("fixed.csv", 3)
            f.Bind("fixed.csv")
            f.Recreate()
            f.Prepare()
            f.Journal.BeforeOrder = Sub(o)
                                        If o.Cursor = 1 Then Throw New IOException("crash before cursor")
                                    End Sub
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Assert.Equal(1L, f.DataCount())
            f.Journal.BeforeOrder = Nothing
            f.Recreate()
            Assert.Equal(PeriodicStopReason.RecoveryCompleted, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Prepare()).Reason)
            Assert.Equal(2, f.Executor.Calls.Count)
            Assert.Equal(1, f.State().Orders.Single().Cursor)
            f.Recreate()
            f.Prepare()
            f.Run()
            Assert.True(f.Executor.Calls.SequenceEqual({"B.csv", "fixed.csv", "C.csv"}))
            Assert.Equal(2L, f.DataCount())
        End Using
    End Sub

    <Theory>
    <InlineData(ProcessingStage.ImportStarted)>
    <InlineData(ProcessingStage.ResultKnown)>
    <InlineData(ProcessingStage.MovePending)>
    <InlineData(ProcessingStage.Completed)>
    Public Sub RealJsonRestart_NeverReexecutesAtomic(stage As ProcessingStage)
        Using f As New Phase4Fixture()
            f.Write("A.csv", 0)
            f.Write("B.csv", 1)
            f.Prepare()
            If stage = ProcessingStage.Completed Then
                f.Journal.BeforeOrder = Sub(o)
                                            If o.Cursor = 1 Then Throw New IOException("crash before cursor")
                                        End Sub
            ElseIf stage = ProcessingStage.ImportStarted Then
                f.Journal.AfterOrder = Sub(o)
                                          If o.Entries(0).AttemptId IsNot Nothing Then Throw New IOException("crash after linked ImportStarted")
                                      End Sub
            Else
                f.Journal.AfterAttempt = Sub(a)
                                             If a.Stage = stage Then Throw New IOException("crash after durable stage")
                                         End Sub
            End If
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Assert.Equal(stage, f.State().Attempts.Single().Stage)
            Dim calls = f.Executor.Calls.Count
            f.Journal.BeforeOrder = Nothing
            f.Journal.AfterOrder = Nothing
            f.Journal.AfterAttempt = Nothing
            f.Recreate()
            Dim stopped = Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Prepare())
            Assert.Equal(calls, f.Executor.Calls.Count)
            If stage = ProcessingStage.ImportStarted Then
                Assert.Equal(0L, f.DataCount())
                Assert.Equal(PeriodicStopReason.RecoveryRequired, stopped.Reason)
                Assert.True(File.Exists(Path.Combine(f.Root, "A.csv")))
                Assert.Equal(ProcessingStage.RecoveryRequired, f.State().Attempts.Single().Stage)
            Else
                Assert.Equal(PeriodicStopReason.RecoveryCompleted, stopped.Reason)
                Assert.Equal(1L, f.DataCount())
                Assert.Equal(1, f.State().Orders.Single().Cursor)
                Assert.True(File.Exists(Path.Combine(f.Root, "success/A.csv")))
                Assert.True(File.Exists(Path.Combine(f.Root, "B.csv")))
                f.Recreate()
                f.Prepare()
                f.Run()
                Assert.Equal(2L, f.DataCount())
                Assert.True(f.Executor.Calls.SequenceEqual({"A.csv", "B.csv"}))
            End If
        End Using
    End Sub

    <Fact>
    Public Sub MovedBeforeCompleted_RepairsFromDestinationOnly()
        Using f As New Phase4Fixture()
            f.Write("A.csv")
            f.Prepare()
            f.Journal.BeforeAttempt = Sub(a)
                                         If a.Stage = ProcessingStage.Completed Then Throw New IOException("crash before Completed")
                                     End Sub
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Assert.Equal(ProcessingStage.MovePending, f.State().Attempts.Single().Stage)
            Assert.False(File.Exists(Path.Combine(f.Root, "A.csv")))
            Assert.True(File.Exists(Path.Combine(f.Root, "success/A.csv")))
            f.Journal.BeforeAttempt = Nothing
            f.Recreate()
            Assert.Equal(PeriodicStopReason.RecoveryCompleted, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Prepare()).Reason)
            Assert.Single(f.Executor.Calls)
            Assert.Equal(ProcessingStage.Completed, f.State().Attempts.Single().Stage)
        End Using
    End Sub

    <Fact>
    Public Sub CompletedRejectWithoutOrderUpdate_RestoresCorrectionPending()
        Using f As New Phase4Fixture()
            f.Write("B.csv", 1, "bad")
            f.Write("C.csv", 2)
            f.Prepare()
            f.Journal.BeforeOrder = Sub(o)
                                        If o.HeadDisposition = HeadDisposition.CorrectionPending Then Throw New IOException("crash")
                                    End Sub
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Assert.Equal(ProcessingStage.Completed, f.State().Attempts.Single().Stage)
            f.Journal.BeforeOrder = Nothing
            f.Recreate()
            Assert.Equal(PeriodicStopReason.CorrectionPending, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Prepare()).Reason)
            Assert.Single(f.Executor.Calls)
            Assert.Equal(HeadDisposition.CorrectionPending, f.State().Orders.Single().HeadDisposition)
            Assert.True(File.Exists(Path.Combine(f.Root, "C.csv")))
        End Using
    End Sub

    <Fact>
    Public Sub MoveFailure_RestartMovesOnlyAndStops()
        Using f As New Phase4Fixture()
            f.Write("A.csv")
            f.Write("B.csv", 1)
            f.Prepare()
            f.Files.BeforeMove = Sub(r, s, d) Throw New UnauthorizedAccessException("move blocked")
            Assert.Equal(PeriodicStopReason.MovePending, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run()).Reason)
            Assert.Equal(1L, f.DataCount())
            Assert.Equal(ProcessingStage.MovePending, f.State().Attempts.Single().Stage)
            f.Files.BeforeMove = Nothing
            f.Recreate()
            Assert.Equal(PeriodicStopReason.RecoveryCompleted, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Prepare()).Reason)
            Assert.Single(f.Executor.Calls)
            Assert.True(File.Exists(Path.Combine(f.Root, "B.csv")))
            Assert.Equal(1L, f.DataCount())
        End Using
    End Sub

    <Theory>
    <InlineData("both")>
    <InlineData("neither")>
    <InlineData("mismatch")>
    Public Sub MoveRecovery_AmbiguousPhysicalStateRequiresRecovery(kind As String)
        Using f As New Phase4Fixture()
            Dim source = f.Write("A.csv")
            f.Prepare()
            f.Files.BeforeMove = Sub(r, s, d) Throw New IOException("hold")
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Dim target = Path.Combine(f.Root, f.State().Attempts.Single().Destination)
            Select Case kind
                Case "both" : File.Copy(source, target)
                Case "neither" : File.Delete(source)
                Case "mismatch" : File.AppendAllText(source, "changed")
            End Select
            f.Files.BeforeMove = Nothing
            f.Recreate()
            Assert.Equal(PeriodicStopReason.RecoveryRequired, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Prepare()).Reason)
            Assert.Single(f.Executor.Calls)
            Assert.Equal(ProcessingStage.RecoveryRequired, f.State().Attempts.Single().Stage)
            Assert.Equal(1L, f.DataCount())
        End Using
    End Sub

    <Theory>
    <InlineData("corrupt")>
    <InlineData("unfinished-temp")>
    <InlineData("missing-attempt")>
    <InlineData("multiple-orders")>
    <InlineData("cursor-ahead")>
    <InlineData("position")>
    <InlineData("hash")>
    <InlineData("database")>
    <InlineData("missing-binding")>
    Public Sub JournalInconsistency_DoesNotBecomeEmptyOrRetry(kind As String)
        Using f As New Phase4Fixture()
            Dim source = f.Write("A.csv")
            f.Write("B.csv", 1)
            f.Prepare()
            f.Journal.AfterAttempt = Sub(record)
                                         If record.Stage = ProcessingStage.ImportStarted Then Throw New IOException("crash")
                                     End Sub
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            f.Journal.AfterAttempt = Nothing
            Dim state = f.State()
            Dim o = state.Orders.Single()
            Dim a = state.Attempts.Single()
            Select Case kind
                Case "corrupt" : File.WriteAllText(Path.Combine(f.Root, ".periodic-import/order-" & o.OrderId & ".json"), "{")
                Case "unfinished-temp" : File.WriteAllText(Path.Combine(f.Root, ".periodic-import/write-" & Guid.NewGuid().ToString("N") & ".tmp"), "{")
                Case "missing-attempt"
                    o.Entries(0).AttemptId = a.AttemptId
                    f.Journal.SaveOrder(f.Root, o)
                    File.Delete(Path.Combine(f.Root, ".periodic-import/attempt-" & a.AttemptId & ".json"))
                Case "multiple-orders"
                    o.OrderId = Guid.NewGuid().ToString("N")
                    o.Generation = 0
                    f.Journal.SaveOrder(f.Root, o)
                Case "cursor-ahead"
                    o.Entries(0).AttemptId = a.AttemptId
                    o.Cursor = 1
                    f.Journal.SaveOrder(f.Root, o)
                Case "position"
                    a.Position = 7
                    f.Journal.SaveAttempt(f.Root, a)
                Case "hash"
                    o.ConfigHash = "wrong"
                    f.Journal.SaveOrder(f.Root, o)
                Case "database"
                    o.DatabaseIdentity = "wrong"
                    f.Journal.SaveOrder(f.Root, o)
                Case "missing-binding"
                    o.CorrectionBindingId = Guid.NewGuid().ToString("N")
                    f.Journal.SaveOrder(f.Root, o)
            End Select
            f.Recreate()
            Assert.Equal(PeriodicStopReason.RecoveryRequired, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Prepare()).Reason)
            Assert.Empty(f.Executor.Calls)
            Assert.True(File.Exists(source))
            Assert.Equal(0L, f.DataCount())
        End Using
    End Sub

    <Fact>
    Public Sub UsedCorrectionBinding_CrashCannotReuseIt()
        Using f As New Phase4Fixture()
            f.Write("B.csv", 1, "bad")
            f.Write("C.csv", 2)
            f.Prepare()
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            f.Write("fixed.csv", 3)
            f.Bind("fixed.csv")
            f.Recreate()
            f.Prepare()
            f.Executor.Before = Sub(r) Throw New InvalidOperationException("before DB, after linked ImportStarted")
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Assert.NotNull(f.State().Bindings.Single().UsedAttemptId)
            f.Executor.Before = Nothing
            f.Recreate()
            Assert.Equal(PeriodicStopReason.RecoveryRequired, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Prepare()).Reason)
            Assert.Equal(2, f.Executor.Calls.Count)
            Assert.Equal(0L, f.DataCount())
            Assert.True(File.Exists(Path.Combine(f.Root, "fixed.csv")))
            Assert.Throws(Of InvalidOperationException)(Function() f.Bind("fixed.csv"))
        End Using
    End Sub

    <Fact>
    Public Sub Retention_ReferencedCompletedKept_UntilThirtyDaysAfterResolution()
        Using f As New Phase4Fixture()
            f.Write("A.csv", 0)
            f.Write("B.csv", 1, "bad")
            f.Write("C.csv", 2)
            f.Prepare()
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            f.Clock.SetUtcNow(f.Clock.GetUtcNow().AddDays(40))
            f.Journal.Prune(f.Root, f.State(), f.Clock.GetUtcNow())
            Assert.Equal(2, f.State().Attempts.Count)
            f.Write("fixed.csv", 3)
            f.Bind("fixed.csv")
            f.Recreate()
            f.Prepare()
            f.Run()
            Assert.Equal(4, f.State().Attempts.Count)
            f.Clock.SetUtcNow(f.Clock.GetUtcNow().AddDays(29))
            f.Journal.Prune(f.Root, f.State(), f.Clock.GetUtcNow())
            Assert.Equal(4, f.State().Attempts.Count)
            f.Clock.SetUtcNow(f.Clock.GetUtcNow().AddDays(2))
            f.Journal.Prune(f.Root, f.State(), f.Clock.GetUtcNow())
            Assert.Empty(f.State().Attempts)
            Assert.Empty(f.State().Bindings)
            Assert.Empty(f.State().Orders)
            Assert.Equal(3, Directory.GetFiles(Path.Combine(f.Root, "success"), "*.csv").Length)
            Assert.Single(Directory.GetFiles(Path.Combine(f.Root, "error"), "*.csv"))
            Assert.True(File.Exists(Path.Combine(f.Root, ".periodic-import/owner.lock")))
        End Using
    End Sub
End Class
