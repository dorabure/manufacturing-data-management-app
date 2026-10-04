Imports System.Windows.Forms
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Presentation

Namespace Forms
    Public Partial Class MainForm
        Private _exitController As PeriodicImportExitController
        Private _checkingCloseSettings As Boolean

        Private ReadOnly Property ExitRequested As Boolean
            Get
                Return _exitController IsNot Nothing AndAlso _exitController.Requested
            End Get
        End Property

        Private Sub InitializeClosing()
            If _periodic IsNot Nothing Then
                _exitController = New PeriodicImportExitController(Function() _periodic.Scheduler.Status,
                    AddressOf _periodic.Scheduler.StopAsync, Function() _periodic.Scheduler.Completion)
            End If
            AddHandler FormClosing, AddressOf CloseSafely
        End Sub

        Private Async Sub CloseSafely(sender As Object, e As FormClosingEventArgs)
            If e.Cancel Then Return
            If _checkingCloseSettings Then
                e.Cancel = True
                Return
            End If
            If Not ExitRequested AndAlso CanMutate AndAlso _settings.IsDirty Then
                _checkingCloseSettings = True
                Try
                    If Not ResolveDirtySettings() Then
                        e.Cancel = True
                        Return
                    End If
                Finally
                    _checkingCloseSettings = False
                End Try
            End If
            If _exitController Is Nothing Then Return
            Dim alreadyRequested = ExitRequested
            If _exitController.RequestClose() Then Return
            e.Cancel = True
            If alreadyRequested Then Return
            DrainNotifications()
            Await _exitController.Completion
            If IsDisposed OrElse Disposing Then Return
            If Not _exitController.AllowFinalClose Then
                PeriodicNotifications.Send(_periodic.Presenter, PeriodicImportNotificationKind.MonitoringAbnormalStopped,
                    _exitController.Message, critical:=True)
                DrainNotifications()
                Return
            End If
            ' Always post, even when Stop completed synchronously: never recurse into Close.
            If Not IsHandleCreated Then Return
            Try
                BeginInvoke(New Action(Sub()
                                           If Not IsDisposed AndAlso Not Disposing AndAlso IsHandleCreated Then Close()
                                       End Sub))
            Catch ex As InvalidOperationException
                ' The handle may have been destroyed after the check; do not force exit.
            End Try
        End Sub
    End Class
End Namespace
