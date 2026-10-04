Imports System.IO
Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Infrastructure.PeriodicImport
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase7PublicationTests
    <Fact>
    Public Async Function TmpWriterIsIgnoredUntilClosedAndRenamedAtNextTick() As Task
        Using f As New Phase4Fixture()
            Dim temporaryPath = Write7(f.Root, "A.tmp", 0)
            Dim service As New PeriodicImportService(f.Cycle, f.Clock)
            Using writer As New FileStream(temporaryPath, FileMode.Open, FileAccess.Write, FileShare.None)
                Await service.StartAsync(f.Options)
                Await service.CurrentCycleCompletion
                f.Clock.Advance(TimeSpan.FromSeconds(1))
                Await f.Clock.WaitForTimerAsync(2)
                Await service.CurrentCycleCompletion
                Assert.Empty(f.Executor.Calls)
                Assert.Empty(f.State().Orders)
            End Using
            File.Move(temporaryPath, Path.Combine(f.Root, "A.csv"))
            f.Clock.Advance(TimeSpan.FromSeconds(1))
            Await f.Clock.WaitForTimerAsync(3)
            Await service.CurrentCycleCompletion
            Await service.StopAsync()
            Assert.Single(f.Executor.Calls)
            Assert.Equal(1L, f.DataCount())
            Assert.True(File.Exists(Path.Combine(f.Root, "success/A.csv")))
        End Using
    End Function

    <Theory>
    <InlineData(FileShare.None)>
    <InlineData(FileShare.Read)>
    Public Async Function DirectCsvStableWhileOpenRemainsWaitingAcrossRestart(share As FileShare) As Task
        Using f As New Phase4Fixture()
            Dim first = Write7(f.Root, "A.csv", 0)
            Write7(f.Root, "B.csv", 1)
            Write7(f.Root, "C.csv", 2)
            Dim service As New PeriodicImportService(f.Cycle, f.Clock)
            Dim orderId As String
            Using writer As New FileStream(first, FileMode.Open, FileAccess.Write, share)
                Await service.StartAsync(f.Options)
                Await service.CurrentCycleCompletion
                orderId = Assert.Single(f.State().Orders).OrderId
                For tick = 1 To 2
                    f.Clock.Advance(TimeSpan.FromSeconds(1))
                    Await f.Clock.WaitForTimerAsync(tick + 1)
                    Await service.CurrentCycleCompletion
                Next
                Assert.Equal(PeriodicImportState.Running, service.Status.State)
                Assert.Equal(HeadDisposition.WaitingForReadable, Assert.Single(f.State().Orders).HeadDisposition)
                Assert.Equal(0L, f.DataCount())
                Assert.Empty(f.Executor.Calls)
                Assert.Empty(Directory.GetFiles(Path.Combine(f.Root, "error")))
                Await service.StopAsync()
                Assert.All({"A.csv", "B.csv", "C.csv"}, Sub(name) Assert.True(File.Exists(Path.Combine(f.Root, name))))
            End Using
            File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddYears(1))
            Dim probe As New Phase7Probe(f.Database)
            Dim restarted As New PeriodicImportService(New PeriodicImportCycle(f.Files, f.Journal, probe), f.Clock)
            Assert.Equal(PeriodicImportState.Stopped, restarted.Status.State)
            Assert.Empty(probe.Calls)
            Await restarted.StartAsync(f.Options)
            Await restarted.CurrentCycleCompletion
            Await restarted.StopAsync()
            Assert.Equal(New String() {"A.csv", "B.csv", "C.csv"}, probe.Calls)
            Assert.Equal(orderId, Assert.Single(f.State().Orders).OrderId)
            Assert.Equal(3L, f.DataCount())
        End Using
    End Function

    <Fact>
    Public Async Function RejectMoveFailureWithTickRecoversMoveOnlyAndNeverStartsFollowing() As Task
        Using f As New Phase4Fixture()
            Write7(f.Root, "B.csv", 0, value:="bad")
            Write7(f.Root, "C.csv", 1)
            Dim entered = Signal6()
            Dim release = Signal6()
            f.Files.BeforeMove = Sub(root, source, target)
                                     entered.TrySetResult(True)
                                     release.Task.GetAwaiter().GetResult()
                                     Throw New IOException("injected error move")
                                 End Sub
            Dim service As New PeriodicImportService(f.Cycle, f.Clock)
            Await service.StartAsync(f.Options)
            Await entered.Task
            f.Clock.Advance(TimeSpan.FromSeconds(1))
            Await f.Clock.WaitForTimerAsync(2)
            Assert.Equal(1L, service.Status.SkippedCycles)
            release.SetResult(True)
            Await service.Completion
            Assert.Equal(ProcessingStage.MovePending, Assert.Single(f.State().Attempts).Stage)
            Assert.Equal(0L, f.DataCount())
            Assert.Equal(New String() {"B.csv"}, f.Executor.Calls)
            f.Files.BeforeMove = Nothing
            Dim probe As New Phase7Probe(f.Database)
            Dim restarted As New PeriodicImportService(New PeriodicImportCycle(f.Files, f.Journal, probe), f.Clock)
            Await Assert.ThrowsAsync(Of PeriodicImportStopRequiredException)(Function() restarted.StartAsync(f.Options))
            Await restarted.Completion
            Assert.Empty(probe.Calls)
            Assert.Equal(HeadDisposition.CorrectionPending, Assert.Single(f.State().Orders).HeadDisposition)
            Assert.True(File.Exists(Path.Combine(f.Root, "error/B.csv")))
            Assert.True(File.Exists(Path.Combine(f.Root, "C.csv")))
            Assert.Equal(PeriodicImportState.Stopped, restarted.Status.State)
        End Using
    End Function
End Class
