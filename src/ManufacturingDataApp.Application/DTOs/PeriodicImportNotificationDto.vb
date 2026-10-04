Namespace DTOs
    Public Enum PeriodicImportNotificationKind
        MonitoringStarting
        MonitoringStarted
        CsvDetected
        CsvImporting
        CsvSucceeded
        CsvRejected
        CsvReadFailed
        WaitingForReadable
        FollowingDeferred
        CorrectionPending
        CorrectionImporting
        RecoveryRequired
        CycleSkipped
        MonitoringStopping
        MonitoringStopped
        MonitoringAbnormalStopped
        LogFailure
        RecoveryCompleted
    End Enum

    ' Contains display-safe summaries only, never exceptions or CSV contents.
    Public Class PeriodicImportNotificationDto
        Public Property EventId As Guid = Guid.NewGuid()
        Public Property OccurredAt As DateTimeOffset = DateTimeOffset.UtcNow
        Public Property Kind As PeriodicImportNotificationKind
        Public Property FileName As String
        Public Property Message As String
        Public Property TotalCount As Integer
        Public Property RegisteredCount As Integer
        Public Property UnregisteredCount As Integer
        Public Property IsCritical As Boolean
        Public Property Repetitions As Long = 1
    End Class
End Namespace
