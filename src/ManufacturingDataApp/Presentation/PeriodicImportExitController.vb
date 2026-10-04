Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.DTOs

Namespace Presentation
    ' UI-thread owned. Only coordinates existing scheduler contracts; never touches CSV/DB.
    Public NotInheritable Class PeriodicImportExitController
        Private ReadOnly _status As Func(Of PeriodicImportStatusDto)
        Private ReadOnly _stop As Func(Of Task)
        Private ReadOnly _getLifetime As Func(Of Task)
        Private ReadOnly _time As TimeProvider
        Private _requestedAt As Long
        Public ReadOnly Property Requested As Boolean
        Public ReadOnly Property AllowFinalClose As Boolean
        Public ReadOnly Property Failed As Boolean
        Public ReadOnly Property Completion As Task = Task.CompletedTask

        Public Sub New(status As Func(Of PeriodicImportStatusDto), stopAsync As Func(Of Task), completion As Func(Of Task), Optional time As TimeProvider = Nothing)
            _status = status
            _stop = stopAsync
            _getLifetime = completion
            _time = If(time, TimeProvider.System)
        End Sub

        Public Function RequestClose() As Boolean
            If Requested Then Return AllowFinalClose
            Dim status = _status()
            If status.State = PeriodicImportState.Stopped AndAlso status.CleanupCompleted AndAlso _getLifetime().IsCompletedSuccessfully Then Return True
            _Requested = True
            _requestedAt = _time.GetTimestamp()
            _Completion = WaitForSafeStopAsync()
            ' Even synchronous completion must unwind the FIRST FormClosing before Close.
            Return False
        End Function

        Private Async Function WaitForSafeStopAsync() As Task
            Try
                Dim state = _status().State
                If state = PeriodicImportState.Starting OrElse state = PeriodicImportState.Running Then
                    Await _stop()
                End If
                ' Stopping joins the existing lifetime (including a Stop button request).
                ' Stopped may be published just before the lifetime actually completes.
                Await _getLifetime()
                Dim status = _status()
                _AllowFinalClose = status.State = PeriodicImportState.Stopped AndAlso status.CleanupCompleted
                _Failed = Not AllowFinalClose
            Catch
                _Failed = True
                ' No forced exit, auto-recovery, or unsafe cleanup retry.
            End Try
        End Function

        Public ReadOnly Property Message As String
            Get
                If Failed Then Return "安全な停止完了を確認できないため、終了を中止しています。ログを確認してください。強制終了は行いません。"
                If _time.GetElapsedTime(_requestedAt) >= TimeSpan.FromSeconds(30) Then Return "停止処理中：現在のCSV完了・リソース解放を待っています。30秒以上経過していますが、処理を中断せず待機します。"
                Return "停止処理中：現在のCSV完了・リソース解放を待ってから終了します。"
            End Get
        End Property
    End Class
End Namespace
