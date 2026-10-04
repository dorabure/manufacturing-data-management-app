Namespace DTOs
    Public Class PeriodicRecoveryContextDto
        Public Property State As HeadDisposition = HeadDisposition.Ready
        Public Property FailedAttemptId As String
        Public Property OriginalFileName As String
        Public Property ConfigName As String
        Public Property Position As Integer
        Public Property PendingFollowingCount As Integer
        Public Property FailureSummary As String = "対応付けが必要なCSVはありません。"
        Public Property HasUnusedBinding As Boolean
        Public Property BoundCandidateRelativePath As String
        Public Property CanConfirmCorrection As Boolean
        Public Property RequiresRecovery As Boolean
    End Class
End Namespace
