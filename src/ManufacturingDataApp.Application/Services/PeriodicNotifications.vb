Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces

Namespace Services
    Public Module PeriodicNotifications
        Public Sub Send(sink As IPeriodicImportNotificationSink, kind As PeriodicImportNotificationKind, message As String,
                        Optional fileName As String = Nothing, Optional result As PeriodicFileResultDto = Nothing,
                        Optional critical As Boolean = False)
            Try
                sink?.Publish(New PeriodicImportNotificationDto With {.Kind = kind, .Message = message,
                    .FileName = fileName, .IsCritical = critical, .TotalCount = If(result Is Nothing, 0, result.TotalCount),
                    .RegisteredCount = If(result Is Nothing, 0, result.RegisteredCount),
                    .UnregisteredCount = If(result Is Nothing, 0, result.UnregisteredCount)})
            Catch
                ' Presentation failure must never change a durable import result.
            End Try
        End Sub

        Public Function SafeFailure(errorValue As Exception) As String
            Dim stopError = TryCast(errorValue, PeriodicImportStopRequiredException)
            If stopError Is Nothing Then Return "監視処理を停止しました。状態を確認してください。"
            Select Case stopError.Reason
                Case PeriodicStopReason.CorrectionPending, PeriodicStopReason.ReconfirmationRequired
                    Return "修正版の確認が必要です。停止中に修正版を指定／確認し直してください。"
                Case PeriodicStopReason.RecoveryCompleted
                    Return "ファイル移動の復旧が完了しました。確認後、手動で監視を開始してください。"
                Case PeriodicStopReason.LogFailure
                    Return "ログの記録に失敗しました。取込結果を確認してください。"
                Case Else
                    Return "復旧確認が必要です。DB・ファイル・Journalの整合性を確認してください。"
            End Select
        End Function
    End Module
End Namespace
