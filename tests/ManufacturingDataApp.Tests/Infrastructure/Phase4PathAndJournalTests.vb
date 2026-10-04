Imports System.Diagnostics
Imports System.IO
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase4PathAndJournalTests
    <Theory>
    <InlineData("root")>
    <InlineData("ancestor")>
    <InlineData("success")>
    <InlineData("error")>
    <InlineData(".periodic-import")>
    <InlineData("A.csv")>
    Public Sub JunctionPaths_AreRejectedWithoutTargetMutation(position As String)
        Using f As New Phase4Fixture()
            Dim target = Path.Combine(f.Database.DirectoryPath, "external")
            Directory.CreateDirectory(target)
            Directory.CreateDirectory(Path.Combine(target, "child"))
            File.WriteAllText(Path.Combine(target, "keep.txt"), "keep")
            Dim link = Path.Combine(f.Root, If(position = "root" OrElse position = "ancestor", "link", position))
            CreateJunction(link, target)
            Try
                If position = "root" OrElse position = "ancestor" Then
                    Assert.Throws(Of IOException)(Function() f.Files.NormalizeRoot(If(position = "root", link, Path.Combine(link, "child"))))
                ElseIf position = "A.csv" Then
                    Assert.Throws(Of IOException)(Function() f.Files.TryRead(f.Root, "A.csv"))
                Else
                    Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Prepare())
                End If
                Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "keep.txt")))
                Assert.Empty(f.Executor.Calls)
            Finally
                ' Delete only the verified junction itself, never recursively or its target.
                Assert.True((File.GetAttributes(link) And FileAttributes.ReparsePoint) <> 0)
                Directory.Delete(link)
            End Try
        End Using
    End Sub

    <Fact>
    Public Sub OrphanUnresolvedAttempt_BlocksCorrectionBinding()
        Using f As New Phase4Fixture()
            f.Write("B.csv", 1, "bad")
            f.Write("C.csv", 2)
            f.Prepare()
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            f.Write("candidate.csv", 3)
            Dim state = f.State()
            Dim failed = state.Attempts.Single()
            Using lease = f.Files.TryRead(f.Root, "candidate.csv")
                Dim orphan As New ImportProcessingRecordDto With {.OrderId = failed.OrderId, .Position = failed.Position,
                    .Root = failed.Root, .DatabaseIdentity = failed.DatabaseIdentity, .Snapshot = failed.Snapshot,
                    .ConfigHash = failed.ConfigHash, .Fingerprint = lease.Fingerprint, .OriginalFileName = "candidate.csv",
                    .Stage = ProcessingStage.ImportStarted}
                f.Journal.SaveAttempt(f.Root, orphan)
            End Using
            Assert.Throws(Of PeriodicImportStopRequiredException)(Function() f.Bind("candidate.csv"))
            Assert.Empty(f.State().Bindings)
            Assert.Single(f.Executor.Calls)
        End Using
    End Sub

    <Fact>
    Public Sub SaveImportStartedFailure_PreventsDbCall()
        Using f As New Phase4Fixture()
            f.Write("A.csv")
            f.Prepare()
            f.Journal.BeforeAttempt = Sub(a) Throw New IOException("disk full")
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            Assert.Empty(f.Executor.Calls)
            Assert.Equal(0L, f.DataCount())
            Assert.True(File.Exists(Path.Combine(f.Root, "A.csv")))
        End Using
    End Sub

    <Fact>
    Public Sub SourceReplacedAfterDbBeforeMove_RequiresRecovery()
        Using f As New Phase4Fixture()
            Dim source = f.Write("A.csv")
            f.Prepare()
            f.Files.BeforeMove = Sub(root, relative, destination)
                                     File.AppendAllText(source, "changed after guard release")
                                 End Sub
            Assert.Equal(PeriodicStopReason.RecoveryRequired, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run()).Reason)
            Assert.Single(f.Executor.Calls)
            Assert.Equal(1L, f.DataCount())
            Assert.Equal(ProcessingStage.RecoveryRequired, f.State().Attempts.Single().Stage)
        End Using
    End Sub

    <Fact>
    Public Sub GeneralMoveIoFailure_IsNotRetriedAsCollision()
        Using f As New Phase4Fixture()
            f.Write("A.csv")
            f.Prepare()
            f.Files.BeforeMove = Sub(root, source, target) Throw New IOException("not a collision")
            Assert.Equal(PeriodicStopReason.MovePending, Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run()).Reason)
            Assert.Equal(1, f.Files.MoveCalls)
            Assert.Single(f.Executor.Calls)
        End Using
    End Sub

    Private Shared Sub CreateJunction(link As String, target As String)
        Dim start As New ProcessStartInfo("cmd.exe") With {.UseShellExecute = False, .CreateNoWindow = True,
            .RedirectStandardOutput = True, .RedirectStandardError = True}
        start.Arguments = "/c mklink /J """ & link & """ """ & target & """"
        Using process = Diagnostics.Process.Start(start)
            Dim output = process.StandardOutput.ReadToEnd()
            Dim errors = process.StandardError.ReadToEnd()
            process.WaitForExit()
            Assert.True(process.ExitCode = 0, output & errors)
        End Using
    End Sub
End Class

