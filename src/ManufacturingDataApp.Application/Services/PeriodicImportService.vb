Imports System.Threading
Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain.Entities

Namespace Services
    Public NotInheritable Class PeriodicImportService
        Private ReadOnly _sync As New Object()
        Private ReadOnly _cycle As IPeriodicImportCycle
        Private ReadOnly _time As TimeProvider
        Private ReadOnly _sink As IPeriodicImportNotificationSink
        Private ReadOnly _log As IImportLogWriter
        Private _state As PeriodicImportState = PeriodicImportState.Stopped
        Private _session As Session
        Private _lastError As Exception
        Private _cleanupCompleted As Boolean = True

        ' Raised on the scheduler thread, outside the state lock. Handlers must not
        ' synchronously wait for StopAsync; a future presenter owns UI dispatch.
        Public Event CycleSkipped(status As PeriodicImportStatusDto)

        Private NotInheritable Class Session
            Public ReadOnly Cancellation As New CancellationTokenSource()
            Public ReadOnly BoundaryCancellation As New CancellationTokenSource()
            Public ReadOnly Gate As New SemaphoreSlim(1, 1)
            Public ReadOnly Launch As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            Public ReadOnly Started As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            Public Lifetime As Task
            Public Preparation As Task = Task.CompletedTask
            Public Scheduler As Task = Task.CompletedTask
            Public ActiveCycle As Task = Task.CompletedTask
            Public CancelTask As Task = Task.CompletedTask
            Public Processing As Boolean
            Public Skipped As Long
            Public StopTimestamp As Long?
        End Class

        Public Sub New(cycle As IPeriodicImportCycle, Optional timeProvider As TimeProvider = Nothing,
                       Optional sink As IPeriodicImportNotificationSink = Nothing, Optional log As IImportLogWriter = Nothing)
            ArgumentNullException.ThrowIfNull(cycle)
            _cycle = cycle
            _time = If(timeProvider, TimeProvider.System)
            _sink = sink
            _log = log
        End Sub

        ' A bounded snapshot, not an ever-growing notification/history queue.
        Public ReadOnly Property Status As PeriodicImportStatusDto
            Get
                SyncLock _sync
                    Return New PeriodicImportStatusDto(_state,
                        _session IsNot Nothing AndAlso _session.Processing,
                        If(_session Is Nothing, 0L, _session.Skipped), _lastError, _cleanupCompleted)
                End SyncLock
            End Get
        End Property

        Public Function StartAsync(options As PeriodicImportOptionsDto) As Task
            ArgumentNullException.ThrowIfNull(options)
            Dim seconds = options.WatchIntervalSeconds
            Dim snapshot = options.DeepCopy()
            If seconds < 1 OrElse seconds > 604800 Then
                Throw New ArgumentOutOfRangeException(NameOf(options), "周期は1～604800秒で指定してください。")
            End If
            Dim current As Session
            SyncLock _sync
                If _state <> PeriodicImportState.Stopped Then Throw New InvalidOperationException("周期監視は既に開始または停止処理中です。")
                current = New Session()
                _session = current
                _lastError = Nothing
                _cleanupCompleted = False
                _state = PeriodicImportState.Starting
                ' Register before allowing any user-supplied preparation code to run.
                current.Lifetime = Task.Run(Function() RunSessionAsync(current, TimeSpan.FromSeconds(seconds), snapshot))
            End SyncLock
            PeriodicNotifications.Send(_sink, PeriodicImportNotificationKind.MonitoringStarting, "監視を開始しています。")
            current.Launch.SetResult(True)
            Return current.Started.Task
        End Function

        ' Snapshots of tracked work, useful to callers coordinating shutdown/diagnostics.
        Public ReadOnly Property Completion As Task
            Get
                SyncLock _sync
                    Return If(_session Is Nothing, Task.CompletedTask, _session.Lifetime)
                End SyncLock
            End Get
        End Property

        Public ReadOnly Property CurrentCycleCompletion As Task
            Get
                SyncLock _sync
                    Return If(_session Is Nothing, Task.CompletedTask, _session.ActiveCycle)
                End SyncLock
            End Get
        End Property

        Public Function StopAsync() As Task
            Dim lifetime As Task
            Dim notify As Boolean
            SyncLock _sync
                If _state = PeriodicImportState.Stopped Then Return Task.CompletedTask
                notify = _state <> PeriodicImportState.Stopping
                RequestStopLocked(_session)
                lifetime = _session.Lifetime
            End SyncLock
            If notify Then PeriodicNotifications.Send(_sink, PeriodicImportNotificationKind.MonitoringStopping, "処理中のCSVの完了を待って停止します。", critical:=True)
            Return lifetime
        End Function

        Private Sub RequestStopLocked(current As Session)
            If _state = PeriodicImportState.Stopping Then Return
            _state = PeriodicImportState.Stopping
            current.StopTimestamp = _time.GetTimestamp()
            ' Boundary token is polling-only: no callback registration by a cycle.
            current.BoundaryCancellation.Cancel()
            ' Cancellation callbacks run off the state lock. Track them before disposal.
            current.CancelTask = Task.Run(Sub() current.Cancellation.Cancel())
        End Sub

        Private Sub RecordFailure(current As Session, failure As Exception)
            SyncLock _sync
                If _lastError Is Nothing Then _lastError = failure
                RequestStopLocked(current)
            End SyncLock
        End Sub

        Private Async Function RunSessionAsync(current As Session, period As TimeSpan, snapshot As PeriodicImportOptionsDto) As Task
            Dim startFailure As Exception = Nothing
            Try
                Await current.Launch.Task.ConfigureAwait(False)
                current.Preparation = _cycle.PrepareAsync(snapshot, current.Cancellation.Token)
                Await current.Preparation.ConfigureAwait(False)
                Dim origin = _time.GetTimestamp()
                Dim canRun As Boolean
                SyncLock _sync
                    canRun = _state = PeriodicImportState.Starting
                    If canRun Then _state = PeriodicImportState.Running
                End SyncLock
                If canRun Then
                    WriteLifecycleLog("周期監視開始", 0)
                    PeriodicNotifications.Send(_sink, PeriodicImportNotificationKind.MonitoringStarted, "周期監視中です。")
                    TryStartCycle(current)
                    current.Scheduler = ScheduleAsync(current, origin, period)
                    current.Started.TrySetResult(True)
                    Await current.Scheduler.ConfigureAwait(False)
                End If
            Catch ex As OperationCanceledException When current.Cancellation.IsCancellationRequested
                ' Stop during preparation or scheduler delay is an expected exit.
            Catch ex As Exception
                startFailure = ex
                RecordFailure(current, ex)
            End Try

            ' Supervisor owns teardown; a cycle never awaits its own lifetime.
            SyncLock _sync
                RequestStopLocked(current)
            End SyncLock
            Try
                Await current.CancelTask.ConfigureAwait(False)
            Catch ex As Exception
                RecordFailure(current, ex)
            End Try
            Await current.ActiveCycle.ConfigureAwait(False)
            Try
                Await _cycle.CleanupAsync().ConfigureAwait(False)
                SyncLock _sync
                    _cleanupCompleted = True
                End SyncLock
            Catch ex As Exception
                RecordFailure(current, ex)
                If startFailure Is Nothing Then startFailure = ex
            End Try
            current.Gate.Dispose()
            current.Cancellation.Dispose()
            current.BoundaryCancellation.Dispose()
            Dim failure = Status.LastError
            Dim abnormal = failure IsNot Nothing
            WriteLifecycleLog(If(abnormal, "周期監視異常停止", "周期監視停止"),
                If(current.StopTimestamp.HasValue, CLng(_time.GetElapsedTime(current.StopTimestamp.GetValueOrDefault()).TotalMilliseconds), 0L))
            SyncLock _sync
                _state = PeriodicImportState.Stopped
            End SyncLock
            If abnormal Then
                Dim reason = TryCast(failure, PeriodicImportStopRequiredException)
                Dim kind = PeriodicImportNotificationKind.MonitoringAbnormalStopped
                If reason IsNot Nothing Then
                    Select Case reason.Reason
                        Case PeriodicStopReason.CorrectionPending, PeriodicStopReason.ReconfirmationRequired
                            kind = PeriodicImportNotificationKind.CorrectionPending
                        Case PeriodicStopReason.RecoveryRequired, PeriodicStopReason.MovePending
                            kind = PeriodicImportNotificationKind.RecoveryRequired
                        Case PeriodicStopReason.LogFailure
                            kind = PeriodicImportNotificationKind.LogFailure
                        Case PeriodicStopReason.RecoveryCompleted
                            kind = PeriodicImportNotificationKind.RecoveryCompleted
                    End Select
                End If
                PeriodicNotifications.Send(_sink, kind, PeriodicNotifications.SafeFailure(failure), critical:=True)
            Else
                PeriodicNotifications.Send(_sink, PeriodicImportNotificationKind.MonitoringStopped, "停止中です。", critical:=True)
            End If
            If startFailure IsNot Nothing Then
                current.Started.TrySetException(startFailure)
            Else
                current.Started.TrySetCanceled()
            End If
        End Function

        Private Async Function ScheduleAsync(current As Session, origin As Long, period As TimeSpan) As Task
            Dim nextDue = period.Ticks
            Do
                Dim elapsed = _time.GetElapsedTime(origin).Ticks
                Dim remaining = nextDue - elapsed
                If remaining > 0 Then
                    ' System timers have millisecond resolution; never execute early
                    ' when a fractional-millisecond delay is rounded down.
                    Dim waitTicks = Math.Max(TimeSpan.TicksPerMillisecond, remaining)
                    Await Task.Delay(TimeSpan.FromTicks(waitTicks), _time, current.Cancellation.Token).ConfigureAwait(False)
                End If
                current.Cancellation.Token.ThrowIfCancellationRequested()
                If _time.GetElapsedTime(origin).Ticks < nextDue Then Continue Do
                TryStartCycle(current)
                ' One decision for a delayed wake-up, then the next FUTURE boundary.
                elapsed = _time.GetElapsedTime(origin).Ticks
                nextDue = (elapsed \ period.Ticks + 1L) * period.Ticks
            Loop
        End Function

        Private Sub TryStartCycle(current As Session)
            Dim launch As TaskCompletionSource(Of Boolean) = Nothing
            Dim skipped As PeriodicImportStatusDto = Nothing
            SyncLock _sync
                If _state <> PeriodicImportState.Running Then Return
                If Not current.Gate.Wait(0) Then
                    If current.Skipped < Long.MaxValue Then current.Skipped += 1
                    skipped = New PeriodicImportStatusDto(_state, current.Processing, current.Skipped, _lastError, _cleanupCompleted)
                Else
                    current.Processing = True
                    launch = New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                    Dim captured = launch
                    current.ActiveCycle = Task.Run(Function() RunCycleAsync(current, captured.Task))
                End If
            End SyncLock
            If skipped IsNot Nothing Then
                PeriodicNotifications.Send(_sink, PeriodicImportNotificationKind.CycleSkipped, "前回処理中のため周期スキップ")
                RaiseEvent CycleSkipped(skipped)
            Else
                launch.SetResult(True)
            End If
        End Sub

        Private Async Function RunCycleAsync(current As Session, launch As Task) As Task
            Try
                Await launch.ConfigureAwait(False)
                Await _cycle.ExecuteAsync(current.BoundaryCancellation.Token).ConfigureAwait(False)
            Catch ex As Exception
                RecordFailure(current, ex)
            Finally
                SyncLock _sync
                    current.Processing = False
                    current.Gate.Release()
                End SyncLock
            End Try
        End Function

        Private Sub WriteLifecycleLog(operationType As String, elapsed As Long)
            Try
                _log?.WriteOperation(New OperationLog With {.ExecutedAt = _time.GetLocalNow().DateTime,
                    .OperationType = operationType, .ElapsedMs = Math.Max(0, elapsed)})
            Catch
                PeriodicNotifications.Send(_sink, PeriodicImportNotificationKind.LogFailure, "監視ログの記録に失敗しました。", critical:=True)
            End Try
        End Sub
    End Class
End Namespace
