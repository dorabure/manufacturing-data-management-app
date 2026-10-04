Imports System.Threading
Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces

Namespace Services
    Public Class PeriodicImportCycle
        Implements IPeriodicImportCycle
        Private ReadOnly _files As IPeriodicImportFileStore
        Private ReadOnly _journal As IImportProcessingJournal
        Private ReadOnly _executor As IPeriodicCsvImportExecutor
        Private ReadOnly _time As TimeProvider
        Private ReadOnly _sink As IPeriodicImportNotificationSink
        Private _owner As IDisposable
        Private _snapshot As PeriodicImportOptionsDto
        Private _root As String
        Private _order As PeriodicImportOrderDto

        Public Sub New(files As IPeriodicImportFileStore, journal As IImportProcessingJournal, executor As IPeriodicCsvImportExecutor, Optional time As TimeProvider = Nothing, Optional sink As IPeriodicImportNotificationSink = Nothing)
            _files = files
            _journal = journal
            _executor = executor
            _time = If(time, TimeProvider.System)
            _sink = sink
        End Sub

        Public Function PrepareAsync(options As PeriodicImportOptionsDto, cancellationToken As CancellationToken) As Task Implements IPeriodicImportCycle.PrepareAsync
            If _owner IsNot Nothing Then Throw New InvalidOperationException("Cycleは既に所有中です。")
            Try
                _snapshot = options.DeepCopy()
                PeriodicSnapshot.Validate(_snapshot)
                _root = _files.NormalizeRoot(_snapshot.Config.WatchFolderPath)
                _snapshot.Config.WatchFolderPath = _root
                If Not String.Equals(_snapshot.DatabaseIdentity, _executor.DatabaseIdentity, StringComparison.OrdinalIgnoreCase) Then Throw New ArgumentException("DB識別が一致しません。")
                _snapshot.DatabaseIdentity = _executor.DatabaseIdentity
                cancellationToken.ThrowIfCancellationRequested()
                _files.Prepare(_root)
                _owner = _files.AcquireOwner(_root)
                cancellationToken.ThrowIfCancellationRequested()
                Dim state = LoadChecked()
                _order = state.Orders.SingleOrDefault(Function(o) o.HeadDisposition <> HeadDisposition.Resolved)
                If _order IsNot Nothing Then
                    If _order.HeadDisposition = HeadDisposition.RecoveryRequired Then StopFor(PeriodicStopReason.RecoveryRequired)
                    If _order.ConfigHash <> PeriodicSnapshot.Hash(_snapshot) Then
                        If _order.CorrectionBindingId IsNot Nothing Then
                            Dim c = state.Bindings.Single(Function(x) x.CorrectionBindingId = _order.CorrectionBindingId)
                            If c.UsedAttemptId Is Nothing Then Invalidate(c)
                        End If
                        StopFor(PeriodicStopReason.ReconfirmationRequired)
                    End If
                    Dim entry = _order.Entries(_order.Cursor)
                    Dim binding = CurrentBinding(state)
                    Dim attempt = If(entry.AttemptId Is Nothing, Nothing, state.Attempts.Single(Function(a) a.AttemptId = entry.AttemptId))
                    If attempt IsNot Nothing AndAlso (binding Is Nothing OrElse binding.UsedAttemptId IsNot Nothing) Then
                        Select Case attempt.Stage
                            Case ProcessingStage.ImportStarted, ProcessingStage.RecoveryRequired
                                MarkRecovery(attempt)
                            Case ProcessingStage.ResultKnown, ProcessingStage.MovePending
                                CompleteMove(attempt, recovering:=True)
                                Reconcile(attempt)
                                StopFor(If(PeriodicJournalValidator.IsRejected(attempt), PeriodicStopReason.CorrectionPending, PeriodicStopReason.RecoveryCompleted))
                            Case ProcessingStage.Completed
                                If _order.HeadDisposition <> HeadDisposition.CorrectionPending Then
                                    Reconcile(attempt)
                                    StopFor(If(PeriodicJournalValidator.IsRejected(attempt), PeriodicStopReason.CorrectionPending, PeriodicStopReason.RecoveryCompleted))
                                End If
                        End Select
                    End If
                    If _order.HeadDisposition = HeadDisposition.CorrectionPending Then StopFor(PeriodicStopReason.CorrectionPending)
                    If binding IsNot Nothing Then
                        If binding.Invalidated OrElse binding.UsedAttemptId IsNot Nothing Then StopFor(PeriodicStopReason.ReconfirmationRequired)
                        VerifyBindingReadable(binding)
                    End If
                End If
                _journal.Prune(_root, state, _time.GetUtcNow())
                cancellationToken.ThrowIfCancellationRequested()
                Return Task.CompletedTask
            Catch ex As OperationCanceledException
                Throw
            Catch ex As PeriodicImportStopRequiredException
                Throw
            Catch ex As Exception
                Throw New PeriodicImportStopRequiredException(PeriodicStopReason.RecoveryRequired, ex)
            End Try
        End Function

        Public Function ExecuteAsync(stopBoundaryToken As CancellationToken) As Task Implements IPeriodicImportCycle.ExecuteAsync
            If _owner Is Nothing Then Throw New InvalidOperationException("Prepareが必要です。")
            Try
                If stopBoundaryToken.IsCancellationRequested Then Return Task.CompletedTask
                _files.Prepare(_root)
                Dim state = LoadChecked()
                If _order IsNot Nothing AndAlso Not state.Orders.Any(Function(o) o.OrderId = _order.OrderId AndAlso o.Generation = _order.Generation) Then StopFor(PeriodicStopReason.RecoveryRequired)
                _order = state.Orders.SingleOrDefault(Function(o) o.HeadDisposition <> HeadDisposition.Resolved)
                If _order Is Nothing Then
                    Dim entries = _files.Enumerate(_root).ToList()
                    If entries.Count = 0 OrElse stopBoundaryToken.IsCancellationRequested Then Return Task.CompletedTask
                    _order = New PeriodicImportOrderDto With {.Root = _root, .DatabaseIdentity = _snapshot.DatabaseIdentity,
                        .Snapshot = _snapshot.DeepCopy(), .ConfigHash = PeriodicSnapshot.Hash(_snapshot), .Entries = entries}
                    _journal.SaveOrder(_root, _order)
                End If
                If _order.ConfigHash <> PeriodicSnapshot.Hash(_snapshot) Then StopFor(PeriodicStopReason.ReconfirmationRequired)
                If _order.HeadDisposition = HeadDisposition.CorrectionPending Then StopFor(PeriodicStopReason.CorrectionPending)
                If _order.HeadDisposition = HeadDisposition.RecoveryRequired Then StopFor(PeriodicStopReason.RecoveryRequired)

                While _order.Cursor < _order.Entries.Count
                    If stopBoundaryToken.IsCancellationRequested Then Exit While
                    state = LoadChecked()
                    Dim binding = CurrentBinding(state)
                    Dim entry = _order.Entries(_order.Cursor)
                    If entry.AttemptId IsNot Nothing AndAlso (binding Is Nothing OrElse binding.UsedAttemptId IsNot Nothing) Then StopFor(PeriodicStopReason.RecoveryRequired)
                    Dim relative = If(binding Is Nothing, entry.RelativePath, binding.Candidate.RelativePath)
                    PeriodicNotifications.Send(_sink, PeriodicImportNotificationKind.CsvDetected, "CSVを検出しました。", relative)
                    Dim lease As IPeriodicReadLease = Nothing
                    Try
                        If binding IsNot Nothing AndAlso Not _files.Exists(_root, relative) Then Invalidate(binding)
                        lease = _files.TryRead(_root, relative)
                        If lease Is Nothing Then
                            If binding Is Nothing Then _order.HeadDisposition = HeadDisposition.WaitingForReadable
                            _journal.SaveOrder(_root, _order)
                            PeriodicNotifications.Send(_sink, PeriodicImportNotificationKind.WaitingForReadable, "書込み中のCSVを待機しています。", relative)
                            If _order.Cursor + 1 < _order.Entries.Count Then PeriodicNotifications.Send(_sink, PeriodicImportNotificationKind.FollowingDeferred, "順序維持のため後続CSVを保留しています。")
                            Return Task.CompletedTask
                        End If
                        If binding IsNot Nothing Then
                            If binding.Invalidated OrElse binding.UsedAttemptId IsNot Nothing OrElse Not binding.Candidate.Matches(lease.Fingerprint) Then Invalidate(binding)
                        ElseIf entry.FileId IsNot Nothing AndAlso entry.FileId <> lease.Fingerprint.FileId Then
                            StopFor(PeriodicStopReason.RecoveryRequired)
                        End If
                        If stopBoundaryToken.IsCancellationRequested Then Return Task.CompletedTask
                        Dim attempt As New ImportProcessingRecordDto With {
                            .OrderId = _order.OrderId, .Position = entry.Position, .Root = _root, .DatabaseIdentity = _snapshot.DatabaseIdentity,
                            .OriginalFileName = relative, .Fingerprint = lease.Fingerprint, .Snapshot = _snapshot.DeepCopy(),
                            .ConfigHash = _order.ConfigHash, .StartedAt = _time.GetUtcNow(), .Stage = ProcessingStage.ImportStarted,
                            .RootFailedAttemptId = _order.RootFailedAttemptId, .LatestFailedAttemptId = _order.LatestFailedAttemptId,
                            .CorrectionBindingId = If(binding Is Nothing, Nothing, binding.CorrectionBindingId)}
                        _journal.SaveAttempt(_root, attempt)
                        If binding IsNot Nothing Then
                            binding.UsedAttemptId = attempt.AttemptId
                            _journal.SaveBinding(_root, binding)
                        End If
                        entry.AttemptId = attempt.AttemptId
                        _journal.SaveOrder(_root, _order)
                        ' Re-read persisted graph before DB, never merely trust in-memory linkage.
                        LoadChecked()
                        Dim request As New CsvImportRequestDto With {.FilePath = lease.FullPath, .Config = _snapshot.DeepCopy().Config}
                        request.ColumnMappings.AddRange(_snapshot.DeepCopy().ColumnMappings)
                        PeriodicNotifications.Send(_sink, If(binding Is Nothing, PeriodicImportNotificationKind.CsvImporting, PeriodicImportNotificationKind.CorrectionImporting), "CSVを取り込んでいます。", relative)
                        attempt.Result = _executor.Execute(request)
                        If attempt.Result Is Nothing OrElse Not attempt.Result.DbOutcomeKnown OrElse
                            (attempt.Result.Outcome <> PeriodicFileOutcome.Succeeded AndAlso attempt.Result.Outcome <> PeriodicFileOutcome.Rejected AndAlso attempt.Result.Outcome <> PeriodicFileOutcome.ReadFailed) Then MarkRecovery(attempt)
                        attempt.Stage = ProcessingStage.ResultKnown
                        PeriodicJournalValidator.ValidateResult(attempt)
                        _journal.SaveAttempt(_root, attempt)
                        SetDestination(attempt)
                        lease.Dispose()
                        lease = Nothing
                        CompleteMove(attempt, recovering:=False)
                        Reconcile(attempt)
                        Dim kind = If(attempt.Result.Outcome = PeriodicFileOutcome.Succeeded, PeriodicImportNotificationKind.CsvSucceeded,
                            If(attempt.Result.Outcome = PeriodicFileOutcome.ReadFailed, PeriodicImportNotificationKind.CsvReadFailed, PeriodicImportNotificationKind.CsvRejected))
                        Dim message = If(kind = PeriodicImportNotificationKind.CsvSucceeded,
                            $"{attempt.Result.RegisteredCount}件登録しました。一覧は検索で更新してください。", "CSVを登録しませんでした。修正版の確認が必要です。")
                        PeriodicNotifications.Send(_sink, kind, message, relative, attempt.Result, critical:=kind <> PeriodicImportNotificationKind.CsvSucceeded)
                        If PeriodicJournalValidator.IsRejected(attempt) Then StopFor(PeriodicStopReason.CorrectionPending)
                        If Not attempt.Result.LogRecorded Then StopFor(PeriodicStopReason.LogFailure)
                    Finally
                        lease?.Dispose()
                    End Try
                End While
                Return Task.CompletedTask
            Catch ex As PeriodicImportStopRequiredException
                Throw
            Catch ex As Exception
                ' Persisted ImportStarted/ResultKnown is preserved when later writes fail.
                Throw New PeriodicImportStopRequiredException(PeriodicStopReason.RecoveryRequired, ex)
            End Try
        End Function

        Private Function LoadChecked() As ImportJournalStateDto
            Dim state = _journal.Load(_root)
            PeriodicJournalValidator.Validate(state, _root, _snapshot.DatabaseIdentity)
            Return state
        End Function

        Private Function CurrentBinding(state As ImportJournalStateDto) As CorrectionBindingDto
            If _order Is Nothing OrElse _order.CorrectionBindingId Is Nothing Then Return Nothing
            Return state.Bindings.Single(Function(c) c.CorrectionBindingId = _order.CorrectionBindingId)
        End Function

        Private Sub VerifyBindingReadable(binding As CorrectionBindingDto)
            If Not _files.Exists(_root, binding.Candidate.RelativePath) Then Invalidate(binding)
            Using lease = _files.TryRead(_root, binding.Candidate.RelativePath)
                If lease IsNot Nothing AndAlso Not binding.Candidate.Matches(lease.Fingerprint) Then Invalidate(binding)
            End Using
        End Sub

        Private Sub Invalidate(binding As CorrectionBindingDto)
            binding.Invalidated = True
            _journal.SaveBinding(_root, binding)
            _order.HeadDisposition = HeadDisposition.CorrectionPending
            _order.CorrectionBindingId = Nothing
            _journal.SaveOrder(_root, _order)
            StopFor(PeriodicStopReason.ReconfirmationRequired)
        End Sub

        Private Sub SetDestination(attempt As ImportProcessingRecordDto, Optional firstCandidate As Integer = 0)
            For index = firstCandidate To 99
                Dim target = _files.Destination(_root, attempt.OriginalFileName, attempt.Result.Outcome = PeriodicFileOutcome.Succeeded, index, _time.GetUtcNow())
                If _files.Exists(_root, target) Then Continue For
                attempt.Destination = target
                attempt.Stage = ProcessingStage.MovePending
                _journal.SaveAttempt(_root, attempt)
                Return
            Next
            StopFor(PeriodicStopReason.MovePending)
        End Sub

        Private Sub CompleteMove(attempt As ImportProcessingRecordDto, recovering As Boolean)
            Try
                If attempt.Stage = ProcessingStage.ResultKnown Then SetDestination(attempt)
                Dim sourceExists = _files.Exists(_root, attempt.Fingerprint.RelativePath)
                Dim targetExists = _files.Exists(_root, attempt.Destination)
                If sourceExists AndAlso targetExists AndAlso Not recovering Then
                    SetDestination(attempt, 1)
                    targetExists = False
                End If
                If sourceExists = targetExists Then MarkRecovery(attempt)
                If sourceExists Then
                    Using lease = _files.TryRead(_root, attempt.Fingerprint.RelativePath)
                        If lease Is Nothing Then StopFor(PeriodicStopReason.MovePending)
                        If Not attempt.Fingerprint.Matches(lease.Fingerprint) Then MarkRecovery(attempt)
                    End Using
                    Dim moved = False
                    For tries = 0 To 99
                        Using guard = _files.TryRead(_root, attempt.Fingerprint.RelativePath)
                            If guard Is Nothing Then StopFor(PeriodicStopReason.MovePending)
                            If Not attempt.Fingerprint.Matches(guard.Fingerprint) Then MarkRecovery(attempt)
                        End Using
                        If _files.Move(_root, attempt.Fingerprint.RelativePath, attempt.Destination) Then
                            moved = True
                            Exit For
                        End If
                        ' Only an explicit name collision reaches this branch.
                        If recovering Then MarkRecovery(attempt)
                        SetDestination(attempt, tries + 1)
                    Next
                    If Not moved Then StopFor(PeriodicStopReason.MovePending)
                End If
                Using lease = _files.TryRead(_root, attempt.Destination, directOnly:=False)
                    If lease Is Nothing Then StopFor(PeriodicStopReason.MovePending)
                    If Not attempt.Fingerprint.Matches(lease.Fingerprint, comparePath:=False) Then MarkRecovery(attempt)
                End Using
                attempt.Stage = ProcessingStage.Completed
                attempt.CompletedAt = _time.GetUtcNow()
                _journal.SaveAttempt(_root, attempt)
            Catch ex As PeriodicImportStopRequiredException
                Throw
            Catch ex As Exception
                Throw New PeriodicImportStopRequiredException(PeriodicStopReason.MovePending, ex)
            End Try
        End Sub

        Private Sub Reconcile(attempt As ImportProcessingRecordDto)
            If attempt.Stage <> ProcessingStage.Completed OrElse attempt.Position <> _order.Cursor Then StopFor(PeriodicStopReason.RecoveryRequired)
            If attempt.Result.Outcome = PeriodicFileOutcome.Succeeded Then
                _order.Cursor += 1
                _order.RootFailedAttemptId = Nothing
                _order.LatestFailedAttemptId = Nothing
                _order.CorrectionBindingId = Nothing
                _order.HeadDisposition = If(_order.Cursor = _order.Entries.Count, HeadDisposition.Resolved, HeadDisposition.Ready)
                If _order.HeadDisposition = HeadDisposition.Resolved Then _order.ResolvedAt = _time.GetUtcNow()
            Else
                _order.RootFailedAttemptId = If(attempt.RootFailedAttemptId, attempt.AttemptId)
                _order.LatestFailedAttemptId = attempt.AttemptId
                _order.CorrectionBindingId = Nothing
                _order.HeadDisposition = HeadDisposition.CorrectionPending
            End If
            _journal.SaveOrder(_root, _order)
        End Sub

        Private Sub MarkRecovery(attempt As ImportProcessingRecordDto)
            attempt.Stage = ProcessingStage.RecoveryRequired
            attempt.SafeReason = "DB結果またはファイル・Journal整合性を確認できません。"
            _journal.SaveAttempt(_root, attempt)
            _order.HeadDisposition = HeadDisposition.RecoveryRequired
            _journal.SaveOrder(_root, _order)
            StopFor(PeriodicStopReason.RecoveryRequired)
        End Sub

        Private Shared Sub StopFor(reason As PeriodicStopReason)
            Throw New PeriodicImportStopRequiredException(reason)
        End Sub

        Public Function CleanupAsync() As Task Implements IPeriodicImportCycle.CleanupAsync
            Try
                _owner?.Dispose()
            Finally
                _owner = Nothing
                _order = Nothing
                _snapshot = Nothing
            End Try
            Return Task.CompletedTask
        End Function
    End Class
End Namespace
