Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces

Namespace Services
    ' Explicit user confirmation only. This service never starts a scheduler.
    Public Class PeriodicImportRecoveryService
        Private ReadOnly _files As IPeriodicImportFileStore
        Private ReadOnly _journal As IImportProcessingJournal
        Private ReadOnly _isStopped As Func(Of Boolean)
        Private ReadOnly _time As TimeProvider

        Public Sub New(files As IPeriodicImportFileStore, journal As IImportProcessingJournal, isStopped As Func(Of Boolean), Optional time As TimeProvider = Nothing)
            ArgumentNullException.ThrowIfNull(isStopped)
            _files = files
            _journal = journal
            _isStopped = isStopped
            _time = If(time, TimeProvider.System)
        End Sub

        Public Function ConfirmCorrection(options As PeriodicImportOptionsDto, failedAttemptId As String, candidateRelativePath As String, Optional expectedFingerprint As FileFingerprintDto = Nothing) As CorrectionBindingDto
            Return ConfirmCore(options, failedAttemptId, candidateRelativePath, False, expectedFingerprint)
        End Function

        Public Function ReconfirmCorrection(options As PeriodicImportOptionsDto, failedAttemptId As String, candidateRelativePath As String, Optional expectedFingerprint As FileFingerprintDto = Nothing) As CorrectionBindingDto
            Return ConfirmCore(options, failedAttemptId, candidateRelativePath, True, expectedFingerprint)
        End Function

        Private Function ConfirmCore(options As PeriodicImportOptionsDto, failedAttemptId As String, candidateRelativePath As String, reconfirm As Boolean, expectedFingerprint As FileFingerprintDto) As CorrectionBindingDto
            If Not _isStopped() Then Throw New InvalidOperationException("停止中のみ対応付けできます。")
            Dim snapshot = options.DeepCopy()
            PeriodicSnapshot.Validate(snapshot)
            Dim root = _files.NormalizeRoot(snapshot.Config.WatchFolderPath)
            snapshot.Config.WatchFolderPath = root
            _files.Prepare(root)
            Using owner = _files.AcquireOwner(root)
                If Not _isStopped() Then Throw New InvalidOperationException("停止中のみ対応付けできます。")
                Dim state = _journal.Load(root)
                PeriodicJournalValidator.Validate(state, root, snapshot.DatabaseIdentity)
                Dim order = state.Orders.SingleOrDefault(Function(o) o.HeadDisposition <> HeadDisposition.Resolved)
                Dim failed = state.Attempts.SingleOrDefault(Function(a) a.AttemptId = failedAttemptId)
                If reconfirm Then
                    If order Is Nothing OrElse failed Is Nothing OrElse order.HeadDisposition <> HeadDisposition.BoundCorrection OrElse
                        order.LatestFailedAttemptId <> failedAttemptId OrElse order.OrderId <> failed.OrderId OrElse order.Cursor <> failed.Position Then
                        Throw New InvalidOperationException("再確認できない状態です。")
                    End If
                    Dim previous = state.Bindings.Single(Function(b) b.CorrectionBindingId = order.CorrectionBindingId)
                    If previous.UsedAttemptId IsNot Nothing OrElse previous.Invalidated Then Throw New InvalidOperationException("使用済みの対応付けは再確認できません。")
                    ' Persist invalidation before any new candidate validation. Failure leaves stopped/unbound.
                    previous.Invalidated = True
                    _journal.SaveBinding(root, previous)
                    order.CorrectionBindingId = Nothing
                    order.HeadDisposition = HeadDisposition.CorrectionPending
                    _journal.SaveOrder(root, order)
                End If
                If order Is Nothing OrElse failed Is Nothing OrElse order.HeadDisposition <> HeadDisposition.CorrectionPending OrElse
                    order.LatestFailedAttemptId <> failedAttemptId OrElse order.OrderId <> failed.OrderId OrElse order.Cursor <> failed.Position OrElse
                    Not PeriodicJournalValidator.IsRejected(failed) OrElse Not failed.Destination.StartsWith("error/", StringComparison.Ordinal) OrElse
                    PeriodicSnapshot.Hash(snapshot) <> failed.ConfigHash Then Throw New InvalidOperationException("修正版として対応付けできない状態です。")
                Using lease = _files.TryRead(root, candidateRelativePath)
                    If lease Is Nothing Then Throw New InvalidOperationException("修正版は書込み中です。")
                    Dim fp = lease.Fingerprint
                    If expectedFingerprint IsNot Nothing AndAlso Not expectedFingerprint.Matches(fp) Then Throw New InvalidOperationException("確認後にCSVが変更されました。")
                    If order.Entries.Skip(order.Cursor + 1).Any(Function(e) String.Equals(e.RelativePath, fp.RelativePath, StringComparison.OrdinalIgnoreCase) OrElse
                        (e.FileId IsNot Nothing AndAlso e.FileId = fp.FileId)) Then Throw New InvalidOperationException("後続CSVは選択できません。")
                    For Each later In order.Entries.Skip(order.Cursor + 1)
                        If Not _files.Exists(root, later.RelativePath) Then Throw New InvalidOperationException("後続CSVを安全に識別できません。")
                        Using laterLease = _files.TryRead(root, later.RelativePath)
                            If laterLease Is Nothing Then
                                If later.FileId Is Nothing OrElse fp.FileId Is Nothing Then Throw New InvalidOperationException("書込み中の後続CSVと区別できません。")
                            Else
                                Dim other = laterLease.Fingerprint
                                If (later.FileId IsNot Nothing AndAlso later.FileId <> other.FileId) OrElse
                                    (fp.FileId IsNot Nothing AndAlso fp.FileId = other.FileId) OrElse
                                    ((fp.FileId Is Nothing OrElse other.FileId Is Nothing) AndAlso fp.Sha256 = other.Sha256 AndAlso fp.Length = other.Length) Then Throw New InvalidOperationException("後続CSVと安全に区別できません。")
                            End If
                        End Using
                    Next
                    For Each attempt In state.Attempts
                        If attempt.Stage <> ProcessingStage.Completed Then
                            If String.Equals(attempt.Fingerprint.RelativePath, fp.RelativePath, StringComparison.OrdinalIgnoreCase) OrElse
                                (attempt.Fingerprint.FileId IsNot Nothing AndAlso attempt.Fingerprint.FileId = fp.FileId) Then Throw New InvalidOperationException("未解決Attemptが所有しています。")
                        ElseIf attempt.Result.Outcome = PeriodicFileOutcome.Succeeded Then
                            If (attempt.Fingerprint.FileId IsNot Nothing AndAlso attempt.Fingerprint.FileId = fp.FileId) OrElse
                                (attempt.Fingerprint.Length = fp.Length AndAlso attempt.Fingerprint.Sha256 = fp.Sha256) Then Throw New InvalidOperationException("成功済みCSVは選択できません。")
                        End If
                    Next
                    Dim binding As New CorrectionBindingDto With {.OrderId = order.OrderId, .Position = order.Cursor,
                        .FailedAttemptId = failedAttemptId, .Candidate = fp, .ConfigHash = order.ConfigHash,
                        .Confirmed = True, .ConfirmedAt = _time.GetUtcNow()}
                    _journal.SaveBinding(root, binding)
                    order.CorrectionBindingId = binding.CorrectionBindingId
                    order.HeadDisposition = HeadDisposition.BoundCorrection
                    _journal.SaveOrder(root, order)
                    Return binding
                End Using
            End Using
        End Function

        Public Function GetRecoveryContext(options As PeriodicImportOptionsDto) As PeriodicRecoveryContextDto
            Dim result As New PeriodicRecoveryContextDto()
            If Not _isStopped() Then Return result
            Try
                Dim snapshot = options.DeepCopy()
                PeriodicSnapshot.Validate(snapshot)
                Dim root = _files.NormalizeRoot(snapshot.Config.WatchFolderPath)
                snapshot.Config.WatchFolderPath = root
                _files.Prepare(root)
                Using owner = _files.AcquireOwner(root)
                    If Not _isStopped() Then Return result
                    Dim state = _journal.Load(root)
                    PeriodicJournalValidator.Validate(state, root, snapshot.DatabaseIdentity)
                    Dim order = state.Orders.SingleOrDefault(Function(o) o.HeadDisposition <> HeadDisposition.Resolved)
                    If order Is Nothing Then Return result
                    result.State = order.HeadDisposition
                    result.ConfigName = order.Snapshot.Config.ConfigName
                    result.Position = order.Cursor + 1
                    result.PendingFollowingCount = order.Entries.Count - order.Cursor - 1
                    result.FailedAttemptId = order.LatestFailedAttemptId
                    result.OriginalFileName = order.Entries(order.Cursor).OriginalFileName
                    Dim binding = state.Bindings.SingleOrDefault(Function(b) b.CorrectionBindingId = order.CorrectionBindingId)
                    result.HasUnusedBinding = binding IsNot Nothing AndAlso binding.UsedAttemptId Is Nothing AndAlso Not binding.Invalidated
                    result.BoundCandidateRelativePath = If(binding Is Nothing, Nothing, binding.Candidate.RelativePath)
                    result.CanConfirmCorrection = order.HeadDisposition = HeadDisposition.CorrectionPending OrElse
                        (order.HeadDisposition = HeadDisposition.BoundCorrection AndAlso result.HasUnusedBinding)
                    result.RequiresRecovery = order.HeadDisposition = HeadDisposition.RecoveryRequired
                    If result.CanConfirmCorrection Then
                        result.FailureSummary = "登録されなかったCSVです。元の順序を維持して修正版を確認してください。"
                        If PeriodicSnapshot.Hash(snapshot) <> order.ConfigHash Then
                            result.CanConfirmCorrection = False
                            result.FailureSummary = "元AttemptとCSV設定が異なります。形式・Mapping・監視設定を元の設定へ戻してから確認してください。"
                        End If
                    ElseIf result.RequiresRecovery Then
                        result.FailureSummary = "DB結果またはJournal整合性の復旧確認が必要です。修正版は指定できません。"
                    Else
                        result.FailureSummary = "監視開始時に保存済みの処理状態を確認します。"
                    End If
                    Return result
                End Using
            Catch
                Return New PeriodicRecoveryContextDto With {.State = HeadDisposition.RecoveryRequired, .RequiresRecovery = True,
                    .FailureSummary = "保存状態を安全に確認できません。復旧確認が必要です。"}
            End Try
        End Function

        Public Function PreviewCandidate(options As PeriodicImportOptionsDto, candidateRelativePath As String) As FileFingerprintDto
            If Not _isStopped() Then Throw New InvalidOperationException("停止中のみ確認できます。")
            PeriodicSnapshot.Validate(options)
            Dim root = _files.NormalizeRoot(options.Config.WatchFolderPath)
            Using owner = _files.AcquireOwner(root)
                If Not _isStopped() Then Throw New InvalidOperationException("停止中のみ確認できます。")
                Using lease = _files.TryRead(root, candidateRelativePath)
                    If lease Is Nothing Then Throw New InvalidOperationException("書込み中のCSVは確認できません。")
                    Return lease.Fingerprint
                End Using
            End Using
        End Function
    End Class
End Namespace
