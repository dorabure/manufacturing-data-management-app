Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase5RecoveryTests
    Private Shared Sub RejectFirst(f As Phase4Fixture)
        f.Write("failed.csv", value:="bad")
        f.Write("following.csv", 1)
        f.Prepare()
        Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
        f.Cycle.CleanupAsync().GetAwaiter().GetResult()
    End Sub

    <Fact>
    Public Sub QueryAndPreviewDoNotBindOrImport()
        Using f As New Phase4Fixture()
            RejectFirst(f)
            Dim service As New PeriodicImportRecoveryService(f.Files, f.Journal, Function() True)
            Dim context = service.GetRecoveryContext(f.Options)
            Assert.Equal(HeadDisposition.CorrectionPending, context.State)
            Assert.True(context.CanConfirmCorrection)
            Assert.False(context.RequiresRecovery)
            Assert.False(context.HasUnusedBinding)
            Assert.Equal("failed.csv", context.OriginalFileName)
            Assert.Equal(1, context.Position)
            Assert.Equal(1, context.PendingFollowingCount)
            Assert.NotEmpty(context.FailedAttemptId)
            f.Write("correction.csv", value:="20")
            Dim fp = service.PreviewCandidate(f.Options, "correction.csv")
            Assert.Equal(64, fp.Sha256.Length)
            Assert.True(fp.Length > 0)
            Assert.Empty(f.State().Bindings)
            Assert.Single(f.Executor.Calls)
            Assert.Equal(0, f.DataCount())
        End Using
    End Sub

    <Fact>
    Public Sub ReconfirmInvalidatesOldBeforeSavingNewAndNeverImports()
        Using f As New Phase4Fixture()
            RejectFirst(f)
            f.Write("first.csv", value:="20")
            Dim oldBinding = f.Bind("first.csv")
            Dim service As New PeriodicImportRecoveryService(f.Files, f.Journal, Function() True)
            Dim context = service.GetRecoveryContext(f.Options)
            Assert.True(context.HasUnusedBinding)
            Assert.True(context.CanConfirmCorrection)
            Assert.Equal("first.csv", context.BoundCandidateRelativePath)
            f.Write("second.csv", value:="21")
            Dim newBinding = service.ReconfirmCorrection(f.Options, context.FailedAttemptId, "second.csv")
            Dim state = f.State()
            Assert.True(state.Bindings.Single(Function(b) b.CorrectionBindingId = oldBinding.CorrectionBindingId).Invalidated)
            Assert.NotEqual(oldBinding.CorrectionBindingId, newBinding.CorrectionBindingId)
            Assert.Equal(newBinding.CorrectionBindingId, Assert.Single(state.Orders).CorrectionBindingId)
            Assert.Single(f.Executor.Calls)
            Assert.Equal(0, f.DataCount())
        End Using
    End Sub

    <Fact>
    Public Sub InvalidNewCandidateStillInvalidatesOldBinding()
        Using f As New Phase4Fixture()
            RejectFirst(f)
            f.Write("first.csv", value:="20")
            Dim old = f.Bind("first.csv")
            Dim service As New PeriodicImportRecoveryService(f.Files, f.Journal, Function() True)
            Assert.ThrowsAny(Of Exception)(Function() service.ReconfirmCorrection(f.Options, old.FailedAttemptId, "missing.csv"))
            Assert.True(Assert.Single(f.State().Bindings).Invalidated)
            Assert.Equal(HeadDisposition.CorrectionPending, Assert.Single(f.State().Orders).HeadDisposition)
            Assert.Null(Assert.Single(f.State().Orders).CorrectionBindingId)
            Assert.Single(f.Executor.Calls)
        End Using
    End Sub

    <Fact>
    Public Sub UsedBindingCannotBeReconfirmed()
        Using f As New Phase4Fixture()
            RejectFirst(f)
            f.Write("correction.csv", value:="20")
            Dim binding = f.Bind("correction.csv")
            f.Recreate()
            f.Prepare()
            f.Run()
            f.Cycle.CleanupAsync().GetAwaiter().GetResult()
            Dim service As New PeriodicImportRecoveryService(f.Files, f.Journal, Function() True)
            Assert.NotNull(Assert.Single(f.State().Bindings).UsedAttemptId)
            Assert.Throws(Of InvalidOperationException)(Function() service.ReconfirmCorrection(f.Options, binding.FailedAttemptId, "unused.csv"))
            Assert.False(Assert.Single(f.State().Bindings).Invalidated)
        End Using
    End Sub

    <Fact>
    Public Sub PreviewChangeRequiresNewConfirmation()
        Using f As New Phase4Fixture()
            RejectFirst(f)
            f.Write("correction.csv", value:="20")
            Dim service As New PeriodicImportRecoveryService(f.Files, f.Journal, Function() True)
            Dim context = service.GetRecoveryContext(f.Options)
            Dim preview = service.PreviewCandidate(f.Options, "correction.csv")
            f.Write("correction.csv", value:="99")
            Assert.Throws(Of InvalidOperationException)(Function() service.ConfirmCorrection(f.Options, context.FailedAttemptId, "correction.csv", preview))
            Assert.Empty(f.State().Bindings)
        End Using
    End Sub

    <Fact>
    Public Sub RecoveryRequiredIsNotCorrectionPending()
        Using f As New Phase4Fixture()
            f.Write("unknown.csv")
            f.Executor.OverrideResult = Function(request) New PeriodicFileResultDto()
            f.Prepare()
            Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
            f.Cycle.CleanupAsync().GetAwaiter().GetResult()
            Dim service As New PeriodicImportRecoveryService(f.Files, f.Journal, Function() True)
            Dim context = service.GetRecoveryContext(f.Options)
            Assert.True(context.RequiresRecovery)
            Assert.False(context.CanConfirmCorrection)
            Assert.Contains("復旧確認", context.FailureSummary)
        End Using
    End Sub

    <Fact>
    Public Sub RunningCannotQueryCandidateOrBind()
        Using f As New Phase4Fixture()
            Dim service As New PeriodicImportRecoveryService(f.Files, f.Journal, Function() False)
            Assert.False(service.GetRecoveryContext(f.Options).CanConfirmCorrection)
            Assert.Throws(Of InvalidOperationException)(Function() service.PreviewCandidate(f.Options, "x.csv"))
            Assert.Throws(Of InvalidOperationException)(Function() service.ConfirmCorrection(f.Options, "x", "x.csv"))
            Assert.Empty(f.Executor.Calls)
        End Using
    End Sub

    <Fact>
    Public Sub ChangedConfigRequiresRestoringOriginalSettings()
        Using f As New Phase4Fixture()
            RejectFirst(f)
            Dim options = f.Options.DeepCopy()
            options.Config.Delimiter = ";"
            Dim service As New PeriodicImportRecoveryService(f.Files, f.Journal, Function() True)
            Dim context = service.GetRecoveryContext(options)
            Assert.False(context.CanConfirmCorrection)
            Assert.Contains("元の設定へ戻して", context.FailureSummary)
            Assert.Empty(f.State().Bindings)
        End Using
    End Sub

    <Fact>
    Public Sub CorruptJournalQueryReturnsSafeRecoveryContext()
        Using f As New Phase4Fixture()
            RejectFirst(f)
            Dim state = f.State()
            Dim order = Assert.Single(state.Orders)
            order.DatabaseIdentity = "wrong"
            f.Journal.SaveOrder(f.Root, order)
            Dim service As New PeriodicImportRecoveryService(f.Files, f.Journal, Function() True)
            Dim context = service.GetRecoveryContext(f.Options)
            Assert.True(context.RequiresRecovery)
            Assert.False(context.CanConfirmCorrection)
            Assert.DoesNotContain("wrong", context.FailureSummary)
        End Using
    End Sub
End Class
