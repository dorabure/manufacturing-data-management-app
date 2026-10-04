Imports ManufacturingDataApp.Application.DTOs

Namespace Services
    Public NotInheritable Class PeriodicJournalValidator
        Public Shared Sub Validate(state As ImportJournalStateDto, root As String, database As String)
            Try
                If state.Orders.Select(Function(o) o.OrderId).Distinct().Count() <> state.Orders.Count OrElse
                    state.Attempts.Select(Function(a) a.AttemptId).Distinct().Count() <> state.Attempts.Count OrElse
                    state.Bindings.Select(Function(b) b.CorrectionBindingId).Distinct().Count() <> state.Bindings.Count Then Fail()
                If state.Orders.Where(Function(o) o.HeadDisposition <> HeadDisposition.Resolved).Count() > 1 Then Fail()
                For Each o In state.Orders
                    If o.Version <> 1 OrElse o.Generation < 1 OrElse o.Root <> root OrElse o.DatabaseIdentity <> database OrElse
                        Not [Enum].IsDefined(o.HeadDisposition) OrElse o.Cursor < 0 OrElse o.Cursor > o.Entries.Count Then Fail()
                    ValidateSnapshot(o.Snapshot, o.ConfigHash, root, database)
                    If o.Entries.Select(Function(e) e.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() <> o.Entries.Count Then Fail()
                    For i = 0 To o.Entries.Count - 1
                        Dim e = o.Entries(i)
                        If e.Position <> i OrElse e.OriginalFileName <> e.RelativePath OrElse String.IsNullOrEmpty(e.RelativePath) Then Fail()
                        Dim a = If(e.AttemptId Is Nothing, Nothing, state.Attempts.SingleOrDefault(Function(x) x.AttemptId = e.AttemptId))
                        If e.AttemptId IsNot Nothing AndAlso (a Is Nothing OrElse a.OrderId <> o.OrderId OrElse a.Position <> i) Then Fail()
                        If i < o.Cursor AndAlso (a Is Nothing OrElse a.Stage <> ProcessingStage.Completed OrElse a.Result.Outcome <> PeriodicFileOutcome.Succeeded OrElse Not a.Result.DbOutcomeKnown) Then Fail()
                        If i > o.Cursor AndAlso e.AttemptId IsNot Nothing Then Fail()
                    Next
                    If o.HeadDisposition = HeadDisposition.Resolved AndAlso (o.Cursor <> o.Entries.Count OrElse Not o.ResolvedAt.HasValue) Then Fail()
                    If o.HeadDisposition <> HeadDisposition.Resolved AndAlso o.Cursor = o.Entries.Count Then Fail()
                    If o.CorrectionBindingId IsNot Nothing Then
                        Dim binding = state.Bindings.SingleOrDefault(Function(c) c.CorrectionBindingId = o.CorrectionBindingId)
                        If binding Is Nothing OrElse binding.OrderId <> o.OrderId OrElse binding.Position <> o.Cursor Then Fail()
                    End If
                    For Each id In {o.RootFailedAttemptId, o.LatestFailedAttemptId}
                        If id Is Nothing Then Continue For
                        Dim a = state.Attempts.SingleOrDefault(Function(x) x.AttemptId = id)
                        If a Is Nothing OrElse a.OrderId <> o.OrderId OrElse a.Position <> o.Cursor OrElse Not IsRejected(a) Then Fail()
                    Next
                    If o.HeadDisposition = HeadDisposition.CorrectionPending OrElse o.HeadDisposition = HeadDisposition.BoundCorrection Then
                        If o.LatestFailedAttemptId Is Nothing OrElse o.RootFailedAttemptId Is Nothing Then Fail()
                    End If
                    If o.HeadDisposition = HeadDisposition.BoundCorrection AndAlso o.CorrectionBindingId Is Nothing Then Fail()
                Next
                For Each a In state.Attempts
                    Dim o = state.Orders.SingleOrDefault(Function(x) x.OrderId = a.OrderId)
                    If a.Version <> 1 OrElse o Is Nothing OrElse a.Root <> root OrElse a.DatabaseIdentity <> database OrElse
                        Not [Enum].IsDefined(a.Stage) OrElse a.Fingerprint Is Nothing OrElse String.IsNullOrEmpty(a.Fingerprint.Sha256) Then Fail()
                    ValidateSnapshot(a.Snapshot, a.ConfigHash, root, database)
                    If a.ConfigHash <> o.ConfigHash Then Fail()
                    If a.OriginalFileName <> a.Fingerprint.RelativePath OrElse a.Fingerprint.Length < 0 OrElse
                        a.Fingerprint.Sha256.Length <> 64 OrElse Not a.Fingerprint.Sha256.All(Function(c) Uri.IsHexDigit(c)) Then Fail()
                    ' A resolved empty receipt is a deliberately retired graph.
                    If o.HeadDisposition = HeadDisposition.Resolved AndAlso o.Entries.Count = 0 Then
                        If a.Stage <> ProcessingStage.Completed Then Fail()
                        Continue For
                    End If
                    If a.Position < 0 OrElse a.Position >= o.Entries.Count Then Fail()
                    If a.Stage <> ProcessingStage.Completed AndAlso o.Entries(a.Position).AttemptId <> a.AttemptId Then Fail()
                    If o.Entries(a.Position).AttemptId <> a.AttemptId AndAlso
                        Not state.Bindings.Any(Function(c) c.FailedAttemptId = a.AttemptId) AndAlso
                        Not state.Attempts.Any(Function(x) x.RootFailedAttemptId = a.AttemptId OrElse x.LatestFailedAttemptId = a.AttemptId) Then Fail()
                    ValidateResult(a)
                    If a.CorrectionBindingId IsNot Nothing Then
                        Dim c = state.Bindings.SingleOrDefault(Function(x) x.CorrectionBindingId = a.CorrectionBindingId)
                        If c Is Nothing OrElse c.UsedAttemptId <> a.AttemptId OrElse c.OrderId <> a.OrderId OrElse c.Position <> a.Position OrElse
                            c.FailedAttemptId <> a.LatestFailedAttemptId OrElse a.RootFailedAttemptId Is Nothing OrElse Not c.Candidate.Matches(a.Fingerprint) Then Fail()
                        For Each id In {a.RootFailedAttemptId, a.LatestFailedAttemptId}
                            Dim previous = state.Attempts.SingleOrDefault(Function(x) x.AttemptId = id)
                            If previous Is Nothing OrElse Not IsRejected(previous) OrElse previous.OrderId <> a.OrderId OrElse previous.Position <> a.Position Then Fail()
                        Next
                    End If
                Next
                For Each c In state.Bindings
                    Dim o = state.Orders.SingleOrDefault(Function(x) x.OrderId = c.OrderId)
                    If c.Version <> 1 OrElse o Is Nothing OrElse Not c.Confirmed OrElse c.Candidate Is Nothing OrElse c.ConfigHash <> o.ConfigHash Then Fail()
                    If o.HeadDisposition = HeadDisposition.Resolved AndAlso o.Entries.Count = 0 Then Continue For
                    Dim failed = state.Attempts.SingleOrDefault(Function(x) x.AttemptId = c.FailedAttemptId)
                    If failed Is Nothing OrElse Not IsRejected(failed) OrElse failed.OrderId <> c.OrderId OrElse failed.Position <> c.Position Then Fail()
                    If c.UsedAttemptId IsNot Nothing AndAlso Not state.Attempts.Any(Function(a) a.AttemptId = c.UsedAttemptId AndAlso a.CorrectionBindingId = c.CorrectionBindingId) Then Fail()
                    If Not c.Invalidated AndAlso c.UsedAttemptId Is Nothing AndAlso o.CorrectionBindingId <> c.CorrectionBindingId Then Fail()
                    If Not c.Invalidated AndAlso c.UsedAttemptId Is Nothing AndAlso
                        (o.LatestFailedAttemptId <> c.FailedAttemptId OrElse o.Cursor <> c.Position OrElse o.HeadDisposition <> HeadDisposition.BoundCorrection) Then Fail()
                Next
            Catch ex As PeriodicImportStopRequiredException
                Throw
            Catch ex As Exception
                Throw New PeriodicImportStopRequiredException(PeriodicStopReason.RecoveryRequired, ex)
            End Try
        End Sub

        Private Shared Sub ValidateSnapshot(snapshot As PeriodicImportOptionsDto, hash As String, root As String, database As String)
            PeriodicSnapshot.Validate(snapshot)
            If snapshot.Config.WatchFolderPath <> root OrElse snapshot.DatabaseIdentity <> database OrElse PeriodicSnapshot.Hash(snapshot) <> hash Then Fail()
        End Sub

        Public Shared Function IsRejected(a As ImportProcessingRecordDto) As Boolean
            Return a.Stage = ProcessingStage.Completed AndAlso a.Result.DbOutcomeKnown AndAlso a.Result.RegisteredCount = 0 AndAlso
                (a.Result.Outcome = PeriodicFileOutcome.Rejected OrElse a.Result.Outcome = PeriodicFileOutcome.ReadFailed)
        End Function

        Public Shared Sub ValidateResult(a As ImportProcessingRecordDto)
            Dim r = a.Result
            If r Is Nothing OrElse Not [Enum].IsDefined(r.Outcome) Then Fail()
            If a.Stage = ProcessingStage.ImportStarted OrElse a.Stage = ProcessingStage.RecoveryRequired Then Return
            If Not r.DbOutcomeKnown OrElse r.TotalCount < 0 OrElse r.RegisteredCount < 0 OrElse r.UnregisteredCount < 0 OrElse
                r.TotalCount <> r.RegisteredCount + r.UnregisteredCount OrElse r.ValidationErrorRowCount < 0 OrElse r.HeldValidRowCount < 0 Then Fail()
            Select Case r.Outcome
                Case PeriodicFileOutcome.Succeeded
                    If r.UnregisteredCount <> 0 OrElse r.ValidationErrorRowCount <> 0 OrElse r.HeldValidRowCount <> 0 Then Fail()
                Case PeriodicFileOutcome.Rejected
                    If r.RegisteredCount <> 0 OrElse r.ValidationErrorRowCount = 0 OrElse r.ValidationErrorRowCount + r.HeldValidRowCount <> r.TotalCount Then Fail()
                Case PeriodicFileOutcome.ReadFailed
                    If r.RegisteredCount <> 0 Then Fail()
                Case Else
                    Fail()
            End Select
            If (a.Stage = ProcessingStage.MovePending OrElse a.Stage = ProcessingStage.Completed) AndAlso String.IsNullOrEmpty(a.Destination) Then Fail()
            If a.Stage = ProcessingStage.Completed AndAlso Not a.CompletedAt.HasValue Then Fail()
        End Sub

        Private Shared Sub Fail()
            Throw New PeriodicImportStopRequiredException(PeriodicStopReason.RecoveryRequired)
        End Sub
    End Class
End Namespace
