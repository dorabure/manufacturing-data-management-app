Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Infrastructure.Csv
Imports ManufacturingDataApp.Infrastructure.PeriodicImport
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase4ImportTests
    <Fact>
    Public Sub NormalOrder_Extensions_AndDurableStages()
        Using f As New Phase4Fixture()
            f.Write("c.CsV", 3)
            f.Write("B.CSV", 2)
            f.Write("a.csv", 1)
            f.Write("ignored.csvx", 4)
            f.Write("ignored.txt", 5)
            Directory.CreateDirectory(Path.Combine(f.Root, "child"))
            f.Write("child/nested.csv", 6)
            File.SetLastWriteTimeUtc(Path.Combine(f.Root, "B.CSV"), File.GetLastWriteTimeUtc(Path.Combine(f.Root, "a.csv")))
            f.Prepare()
            f.Executor.Before = Sub(request)
                                    Dim current = f.State()
                                    Dim a = current.Attempts.Single(Function(x) x.Stage = ProcessingStage.ImportStarted)
                                    Assert.Equal(a.AttemptId, current.Orders.Single().Entries(a.Position).AttemptId)
                                End Sub
            f.Run()
            Assert.True(f.Executor.Calls.SequenceEqual({"a.csv", "B.CSV", "c.CsV"}))
            Assert.Equal(3L, f.DataCount())
            Assert.Equal(HeadDisposition.Resolved, f.State().Orders.Single().HeadDisposition)
            Assert.All(f.State().Attempts, Sub(a) Assert.Equal(ProcessingStage.Completed, a.Stage))
            Assert.True(f.Journal.Stages.Take(4).SequenceEqual({ProcessingStage.ImportStarted, ProcessingStage.ResultKnown, ProcessingStage.MovePending, ProcessingStage.Completed}))
            Assert.True(File.Exists(Path.Combine(f.Root, "ignored.csvx")))
        End Using
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("日時,設備ID,設備名,項目,値,単位")>
    Public Sub EmptyCsvIsSuccess_NotEmptyFolder(content As String)
        Using f As New Phase4Fixture()
            File.WriteAllText(Path.Combine(f.Root, "empty.csv"), content)
            f.Prepare()
            f.Run()
            Assert.Single(f.Executor.Calls)
            Assert.Equal(0L, f.DataCount())
            Assert.True(File.Exists(Path.Combine(f.Root, "success/empty.csv")))
            Assert.Equal(PeriodicFileOutcome.Succeeded, f.State().Attempts.Single().Result.Outcome)
            f.Run()
            Assert.Single(f.Executor.Calls)
        End Using
    End Sub

    <Theory>
    <InlineData("success")>
    <InlineData("error")>
    <InlineData(".periodic-import")>
    Public Sub PreparationFailure_NoDbOrSourceChange(folder As String)
        Using f As New Phase4Fixture()
            Dim source = f.Write("A.csv")
            Dim original = File.ReadAllBytes(source)
            File.WriteAllText(Path.Combine(f.Root, folder), "blocks directory")
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Prepare())
            Assert.Empty(f.Executor.Calls)
            Assert.True(original.SequenceEqual(File.ReadAllBytes(source)))
        End Using
    End Sub

    <Fact>
    Public Async Function LockedHead_PersistsOrderAcrossRestartAndMtimeChange() As Task
        Using f As New Phase4Fixture()
            Dim a = f.Write("A.csv", 1)
            f.Write("B.csv", 2)
            f.Write("C.csv", 3)
            Using writer As New FileStream(a, FileMode.Open, FileAccess.Write, FileShare.Read)
                Dim service As New PeriodicImportService(f.Cycle, f.Clock)
                Await service.StartAsync(f.Options)
                Await service.CurrentCycleCompletion
                Assert.Equal(PeriodicImportState.Running, service.Status.State)
                Assert.Empty(f.Executor.Calls)
                Assert.Equal(HeadDisposition.WaitingForReadable, f.State().Orders.Single().HeadDisposition)
                Await service.StopAsync()
                f.Recreate()
                f.Prepare()
                f.Run()
                Assert.Empty(f.Executor.Calls)
            End Using
            File.SetLastWriteTimeUtc(a, New DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            f.Run()
            Assert.True(f.Executor.Calls.SequenceEqual({"A.csv", "B.csv", "C.csv"}))
            Assert.Equal(3L, f.DataCount())
        End Using
    End Function

    <Fact>
    Public Sub ReadLease_DeniesWriteDeleteRename_AllowsExistingAdapter()
        Using f As New Phase4Fixture()
            Dim source = f.Write("A.csv")
            Using lease = f.Files.TryRead(f.Root, "A.csv")
                Assert.NotNull(lease.Fingerprint.FileId)
                Assert.Throws(Of IOException)(Sub()
                                                  Using writer As New FileStream(source, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)
                                                  End Using
                                              End Sub)
                Assert.Throws(Of IOException)(Sub() File.Delete(source))
                Assert.Throws(Of IOException)(Sub() File.Move(source, source & ".other"))
                Assert.Single(New CsvHelperAdapter().Read(source, "UTF-8", ",", True))
            End Using
        End Using
    End Sub

    <Fact>
    Public Sub RejectedHead_StopsAfterPriorSuccess_AndDoesNotAutoBindSameName()
        Using f As New Phase4Fixture()
            f.Write("A.csv", 0)
            f.Write("B.csv", 1, "bad")
            f.Write("C.csv", 2)
            f.Prepare()
            Dim ex = Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Assert.Equal(PeriodicStopReason.CorrectionPending, ex.Reason)
            Assert.True(f.Executor.Calls.SequenceEqual({"A.csv", "B.csv"}))
            Assert.Equal(1L, f.DataCount())
            Assert.True(File.Exists(Path.Combine(f.Root, "success/A.csv")))
            Assert.True(File.Exists(Path.Combine(f.Root, "error/B.csv")))
            Assert.True(File.Exists(Path.Combine(f.Root, "C.csv")))
            f.Write("B.csv", 1)
            f.Recreate()
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Prepare())
            Assert.Equal(2, f.Executor.Calls.Count)
        End Using
    End Sub

    <Theory>
    <InlineData(PeriodicFileOutcome.DatabaseFailed)>
    <InlineData(PeriodicFileOutcome.Unknown)>
    Public Sub UnknownDb_KeepsSourceAndRejectsBinding(outcome As PeriodicFileOutcome)
        Using f As New Phase4Fixture()
            f.Write("A.csv", 0)
            f.Write("B.csv", 1)
            f.Write("C.csv", 2)
            Dim real As New PeriodicCsvImportExecutor(f.Database.Factory)
            f.Executor.OverrideResult = Function(r) If(Path.GetFileName(r.FilePath) = "B.csv",
                New PeriodicFileResultDto With {.Outcome = outcome, .DbOutcomeKnown = False}, real.Execute(r))
            f.Prepare()
            Assert.Equal(PeriodicStopReason.RecoveryRequired, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run()).Reason)
            Assert.Equal(1L, f.DataCount())
            Assert.True(File.Exists(Path.Combine(f.Root, "B.csv")))
            Assert.False(File.Exists(Path.Combine(f.Root, "error/B.csv")))
            Assert.Equal(ProcessingStage.RecoveryRequired, f.State().Attempts.Single(Function(a) a.OriginalFileName = "B.csv").Stage)
            f.Write("fixed.csv", 1)
            Assert.Throws(Of InvalidOperationException)(Function() f.Bind("fixed.csv"))
            f.Recreate()
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Prepare())
            Assert.Equal(2, f.Executor.Calls.Count)
        End Using
    End Sub

    <Theory>
    <InlineData(False)>
    <InlineData(True)>
    Public Sub ExplicitCorrection_KeepsOriginalPositionAndFailureChain(rejectAgain As Boolean)
        Using f As New Phase4Fixture()
            f.Write("A.csv", 0)
            f.Write("B.csv", 1, "bad")
            f.Write("C.csv", 2)
            f.Prepare()
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Dim originalFailure = f.State().Orders.Single().LatestFailedAttemptId
            f.Write("B2.csv", 5, If(rejectAgain, "bad", "10"))
            Dim binding = f.Bind("B2.csv")
            Assert.Equal(2, f.Executor.Calls.Count)
            f.Recreate()
            f.Prepare()
            If rejectAgain Then
                Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
                Dim o = f.State().Orders.Single()
                Assert.Equal(originalFailure, o.RootFailedAttemptId)
                Assert.NotEqual(originalFailure, o.LatestFailedAttemptId)
                Assert.Equal(1, o.Cursor)
                Assert.True(File.Exists(Path.Combine(f.Root, "C.csv")))
                Assert.Equal(1L, f.DataCount())
            Else
                f.Run()
                Assert.True(f.Executor.Calls.SequenceEqual({"A.csv", "B.csv", "B2.csv", "C.csv"}))
                Assert.Equal(3L, f.DataCount())
            End If
            Dim used = f.State().Bindings.Single()
            Assert.NotNull(used.UsedAttemptId)
            Assert.Equal(binding.CorrectionBindingId, used.CorrectionBindingId)
            Dim attempt = f.State().Attempts.Single(Function(a) a.AttemptId = used.UsedAttemptId)
            Assert.Equal(1, attempt.Position)
            Assert.Equal(originalFailure, attempt.RootFailedAttemptId)
            Assert.Equal(originalFailure, attempt.LatestFailedAttemptId)
        End Using
    End Sub

    <Theory>
    <InlineData("content")>
    <InlineData("config")>
    <InlineData("missing")>
    Public Sub ChangedBinding_RequiresReconfirmationWithoutAtomic(change As String)
        Using f As New Phase4Fixture()
            f.Write("B.csv", 1, "bad")
            f.Write("C.csv", 2)
            f.Prepare()
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Dim candidate = f.Write("B2.csv", 3)
            f.Bind("B2.csv")
            Select Case change
                Case "content" : File.AppendAllText(candidate, vbCrLf & "changed")
                Case "config" : f.Options.Config.Delimiter = ";"
                Case "missing" : File.Delete(candidate)
            End Select
            f.Recreate()
            Assert.Equal(PeriodicStopReason.ReconfirmationRequired, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Prepare()).Reason)
            Assert.Single(f.Executor.Calls)
            Assert.True(f.State().Bindings.Single().Invalidated)
            Assert.Equal(HeadDisposition.CorrectionPending, f.State().Orders.Single().HeadDisposition)
        End Using
    End Sub

    <Theory>
    <InlineData("C.csv")>
    <InlineData("copy.csv")>
    <InlineData("../outside.csv")>
    <InlineData("success/A.csv")>
    Public Sub ForbiddenCorrectionCandidates_AreRejected(candidate As String)
        Using f As New Phase4Fixture()
            f.Write("A.csv", 0)
            f.Write("B.csv", 1, "bad")
            f.Write("C.csv", 2)
            f.Prepare()
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            File.Copy(Path.Combine(f.Root, "success/A.csv"), Path.Combine(f.Root, "copy.csv"))
            Assert.NotNull(Record.Exception(Function() f.Bind(candidate)))
            Assert.Empty(f.State().Bindings)
            Assert.Equal(2, f.Executor.Calls.Count)
        End Using
    End Sub

    <Fact>
    Public Sub BoundCorrectionSharingViolation_HoldsFollowingCsv()
        Using f As New Phase4Fixture()
            f.Write("B.csv", 1, "bad")
            f.Write("C.csv", 2)
            f.Prepare()
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Dim candidate = f.Write("B2.csv", 3)
            f.Bind("B2.csv")
            f.Recreate()
            Using writer As New FileStream(candidate, FileMode.Open, FileAccess.Write, FileShare.Read)
                f.Prepare()
                f.Run()
                Assert.Single(f.Executor.Calls)
                Assert.Equal(HeadDisposition.BoundCorrection, f.State().Orders.Single().HeadDisposition)
            End Using
            f.Run()
            Assert.True(f.Executor.Calls.SequenceEqual({"B.csv", "B2.csv", "C.csv"}))
        End Using
    End Sub

    <Fact>
    Public Sub NewOldMtimeArrival_WaitsForNextOrder()
        Using f As New Phase4Fixture()
            f.Write("A.csv", 1)
            f.Write("B.csv", 2)
            f.Executor.Before = Sub(request)
                                    If Path.GetFileName(request.FilePath) = "A.csv" Then f.Write("D.csv", 0)
                                End Sub
            f.Prepare()
            f.Run()
            Assert.True(f.Executor.Calls.SequenceEqual({"A.csv", "B.csv"}))
            Assert.True(File.Exists(Path.Combine(f.Root, "D.csv")))
            f.Run()
            Assert.True(f.Executor.Calls.SequenceEqual({"A.csv", "B.csv", "D.csv"}))
            Assert.Equal(2, f.State().Orders.Count)
        End Using
    End Sub

    <Fact>
    Public Async Function StopBoundary_CompletesCurrentCsv_NotNext_DeepCopiesSettings() As Task
        Using f As New Phase4Fixture()
            f.Write("A.csv", 0)
            f.Write("B.csv", 1)
            Dim entered As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            Dim release As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            f.Executor.Before = Sub(r)
                                    entered.SetResult(True)
                                    release.Task.GetAwaiter().GetResult()
                                    Assert.Equal(",", r.Config.Delimiter)
                                    Assert.Equal(1, r.ColumnMappings(0).CsvColumnIndex)
                                End Sub
            Dim scheduler As New PeriodicImportService(f.Cycle, f.Clock)
            Await scheduler.StartAsync(f.Options)
            Await entered.Task.WaitAsync(TimeSpan.FromSeconds(10))
            f.Options.Config.Delimiter = ";"
            f.Options.ColumnMappings(0).CsvColumnIndex = 99
            Dim stopping = scheduler.StopAsync()
            Assert.False(stopping.IsCompleted)
            release.SetResult(True)
            Await stopping.WaitAsync(TimeSpan.FromSeconds(10))
            Assert.Single(f.Executor.Calls)
            Assert.Equal(1L, f.DataCount())
            Assert.True(File.Exists(Path.Combine(f.Root, "success/A.csv")))
            Assert.True(File.Exists(Path.Combine(f.Root, "B.csv")))
            Assert.Equal(PeriodicImportState.Stopped, scheduler.Status.State)
            Using otherOwner = f.Files.AcquireOwner(f.Root)
            End Using
        End Using
    End Function

    <Fact>
    Public Async Function StopDuringPrepare_ReleasesOwnership_AndCanRestart() As Task
        Using f As New Phase4Fixture()
            Dim entered As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            Dim release As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            f.Files.AfterOwner = Sub()
                                     entered.SetResult(True)
                                     release.Task.GetAwaiter().GetResult()
                                 End Sub
            Dim scheduler As New PeriodicImportService(f.Cycle, f.Clock)
            Dim starting = scheduler.StartAsync(f.Options)
            Await entered.Task.WaitAsync(TimeSpan.FromSeconds(10))
            Dim stopping = scheduler.StopAsync()
            release.SetResult(True)
            Await stopping.WaitAsync(TimeSpan.FromSeconds(10))
            Await Assert.ThrowsAnyAsync(Of OperationCanceledException)(Function() starting)
            f.Files.AfterOwner = Nothing
            Await scheduler.StartAsync(f.Options)
            Await scheduler.StopAsync()
            Using owner = f.Files.AcquireOwner(f.Root)
            End Using
        End Using
    End Function

    <Fact>
    Public Sub Ownership_IsHandleBased_NotLockFileExistence()
        Using f As New Phase4Fixture()
            f.Prepare()
            Assert.Throws(Of IOException)(Function() f.Files.AcquireOwner(f.Root))
            f.Cycle.CleanupAsync().GetAwaiter().GetResult()
            Dim lockPath = Path.Combine(f.Root, ".periodic-import/owner.lock")
            File.WriteAllText(lockPath, "do not truncate")
            Using owner = f.Files.AcquireOwner(f.Root)
                Assert.Throws(Of IOException)(Function() f.Files.AcquireOwner(f.Root))
            End Using
            Assert.Equal("do not truncate", File.ReadAllText(lockPath))
        End Using
    End Sub
End Class

