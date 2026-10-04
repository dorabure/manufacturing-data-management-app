Namespace DTOs
    Public NotInheritable Class PeriodicImportStatusDto
        Public Sub New(state As PeriodicImportState, isProcessing As Boolean, skippedCycles As Long, lastError As Exception, Optional cleanupCompleted As Boolean = True)
            Me.State = state
            Me.IsProcessing = isProcessing
            Me.SkippedCycles = skippedCycles
            Me.LastError = lastError
            Me.CleanupCompleted = cleanupCompleted
        End Sub

        Public ReadOnly Property State As PeriodicImportState
        Public ReadOnly Property IsProcessing As Boolean
        Public ReadOnly Property SkippedCycles As Long
        Public ReadOnly Property LastError As Exception
        ' Stopped alone does not prove that owner/resource cleanup succeeded.
        Public ReadOnly Property CleanupCompleted As Boolean
        Public ReadOnly Property Message As String
            Get
                ' Skips are events/statistics, not the current lifecycle state.
                Return State.ToString()
            End Get
        End Property
    End Class
End Namespace
