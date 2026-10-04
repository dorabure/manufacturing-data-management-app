Imports System.Drawing
Imports System.Windows.Forms
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain.Constants
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Presentation

Namespace Forms
    Public Partial Class MainForm
        Private ReadOnly _periodic As PeriodicImportComposition
        Private ReadOnly _settings As MonitoringSettingsController
        Private searchButton, deleteButton, exportButton, importButton, configButton, masterButton, errorLogButton, operationLogButton As Button
        Private ReadOnly _singleMode As New RadioButton With {.Text = "1ファイル取込", .AutoSize = True}
        Private ReadOnly _periodicMode As New RadioButton With {.Text = "フォルダ周期監視", .AutoSize = True}
        Private ReadOnly _saveMonitoring As Button = CreateButton("監視設定保存", 110)
        Private ReadOnly _periodicPanel As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .FlowDirection = FlowDirection.TopDown, .WrapContents = False}
        Private ReadOnly _watchFolder As New TextBox With {.Width = 530}
        Private ReadOnly _browseFolder As Button = CreateButton("参照", 70)
        Private ReadOnly _watchInterval As New NumericUpDown With {.Minimum = 1, .Maximum = 604800, .Value = 60, .DecimalPlaces = 0, .Width = 90}
        Private ReadOnly _startStop As Button = CreateButton("周期監視開始", 125)
        Private ReadOnly _statusLabel As New Label With {.AutoSize = True, .MaximumSize = New Size(1050, 0), .Text = "停止中です。"}
        Private ReadOnly _history As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 780, .DropDownWidth = 1000}
        Private ReadOnly _correction As Button = CreateButton("修正版を指定／確認し直す", 200)
        Private ReadOnly _notificationTimer As New System.Windows.Forms.Timer With {.Interval = 250}
        Private ReadOnly _historyTip As New ToolTip()
        Private _loadingSettings As Boolean
        Private _updatingHistory As Boolean
        Private _recoveryContext As PeriodicRecoveryContextDto
        Private _startInputError As String

        Private ReadOnly Property CurrentPolicy As PeriodicUiPolicy
            Get
                Return New PeriodicUiPolicy(If(ExitRequested, PeriodicImportState.Stopping, If(_periodic Is Nothing, PeriodicImportState.Stopped, _periodic.Scheduler.Status.State)), _settings.Mode, _settings.ConfigId > 0)
            End Get
        End Property
        Private ReadOnly Property CanMutate As Boolean
            Get
                Return CurrentPolicy.CanMutate
            End Get
        End Property

        Private Sub AddPeriodicControls(commands As TableLayoutPanel)
            commands.RowCount = 4
            commands.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            commands.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            Dim modes As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .AutoSize = True, .WrapContents = False}
            modes.Controls.AddRange({_singleMode, _periodicMode, _saveMonitoring})
            Dim folderRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
            folderRow.Controls.AddRange({CreateLabel("監視フォルダ"), _watchFolder, _browseFolder})
            Dim intervalRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
            intervalRow.Controls.AddRange({CreateLabel("監視周期"), _watchInterval, CreateLabel("秒"), _startStop})
            Dim historyRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
            historyRow.Controls.AddRange({_history, _correction})
            _periodicPanel.Controls.AddRange({CreateLabel("フォルダ周期監視（上のCSV設定を使用）"), folderRow, intervalRow, _statusLabel, CreateLabel("監視履歴（閲覧専用）"), historyRow})
            _historyTip.SetToolTip(_history, "過去の通知を閲覧できます。選択しても監視状態は変わりません。閉じると最新の通知へ戻ります。")
            commands.Controls.Add(modes, 0, 2)
            commands.Controls.Add(_periodicPanel, 0, 3)
        End Sub

        Private Sub InitializePeriodicEvents()
            AddHandler _singleMode.CheckedChanged, AddressOf MonitoringInputChanged
            AddHandler _periodicMode.CheckedChanged, AddressOf MonitoringInputChanged
            AddHandler _watchFolder.TextChanged, AddressOf MonitoringInputChanged
            AddHandler _watchInterval.ValueChanged, AddressOf MonitoringInputChanged
            AddHandler _csvConfigComboBox.SelectedIndexChanged, AddressOf MonitoringConfigChanged
            AddHandler _saveMonitoring.Click, Sub() SaveMonitoringSettings()
            AddHandler _browseFolder.Click, Sub()
                                                If Not CanMutate Then Return
                                                Using dialog As New FolderBrowserDialog With {.SelectedPath = _watchFolder.Text}
                                                    If dialog.ShowDialog(Me) = DialogResult.OK Then _watchFolder.Text = dialog.SelectedPath
                                                End Using
                                            End Sub
            AddHandler _startStop.Click, AddressOf StartStopMonitoring
            AddHandler _correction.Click, Sub() OpenCorrection()
            AddHandler _notificationTimer.Tick, Sub() DrainNotifications()
            AddHandler _history.SelectedIndexChanged, Sub()
                                                         If _updatingHistory OrElse _periodic Is Nothing Then Return
                                                         Dim item = TryCast(_history.SelectedItem, NotificationItem)
                                                         _periodic.Presenter.SelectedEventId = If(item Is Nothing, CType(Nothing, Guid?), item.Notification.EventId)
                                                     End Sub
            AddHandler _history.DropDownClosed, Sub()
                                                   If _periodic IsNot Nothing Then _periodic.Presenter.SelectedEventId = Nothing
                                               End Sub
            If _periodic IsNot Nothing Then AddHandler _periodic.Presenter.CriticalAvailable, AddressOf CriticalNotificationAvailable
            AddHandler Disposed, Sub()
                                     _notificationTimer.Stop()
                                     _notificationTimer.Dispose()
                                     _historyTip.Dispose()
                                     If _periodic IsNot Nothing Then
                                         RemoveHandler _periodic.Presenter.CriticalAvailable, AddressOf CriticalNotificationAvailable
                                         _periodic.Presenter.Dispose()
                                     End If
                                 End Sub
            _notificationTimer.Start()
            RefreshRecoveryContext()
            RefreshPeriodicUi()
        End Sub

        Private Sub CriticalNotificationAvailable()
            ' Never touch controls from a worker, including before handle creation.
            If IsDisposed OrElse Disposing OrElse Not IsHandleCreated Then Return
            Try
                BeginInvoke(New Action(AddressOf DrainNotifications))
            Catch ex As InvalidOperationException
                ' Normal when the window was disposed between checking and posting.
            End Try
        End Sub

        Private Sub MonitoringInputChanged(sender As Object, e As EventArgs)
            If _loadingSettings OrElse Not CanMutate Then Return
            _startInputError = Nothing
            _settings.Mode = If(_periodicMode.Checked, ImportMode.PeriodicFolder, ImportMode.SingleFile)
            _settings.Folder = _watchFolder.Text
            _settings.IntervalSeconds = CInt(_watchInterval.Value)
            RefreshPeriodicUi()
        End Sub

        Private Function AskDirtyChoice() As DirtySettingsChoice
            Dim answer = MessageBox.Show(Me, "変更した監視設定を保存しますか？" & Environment.NewLine & "はい：保存／いいえ：破棄／キャンセル：切替中止", "監視設定", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question)
            Return If(answer = DialogResult.Yes, DirtySettingsChoice.Save, If(answer = DialogResult.No, DirtySettingsChoice.Discard, DirtySettingsChoice.Cancel))
        End Function
        Private Function ResolveDirtySettings() As Boolean
            Try
                Return _settings.ResolveDirty(If(_settings.IsDirty, AskDirtyChoice(), DirtySettingsChoice.Discard))
            Catch
                ShowMonitoringError("監視設定を保存できませんでした。現在の選択と入力を保持します。")
                Return False
            End Try
        End Function

        Private Sub MonitoringConfigChanged(sender As Object, e As EventArgs)
            If _loadingSettings Then Return
            Dim nextConfig = TryCast(_csvConfigComboBox.SelectedItem, CsvImportConfig)
            Dim id = If(nextConfig Is Nothing, 0, nextConfig.ConfigId)
            If id = _settings.ConfigId Then Return
            _loadingSettings = True
            Try
                If CanMutate AndAlso ResolveDirtySettings() Then
                    _settings.Load(id)
                    DisplaySettings()
                Else
                    For index = 0 To _csvConfigComboBox.Items.Count - 1
                        If DirectCast(_csvConfigComboBox.Items(index), CsvImportConfig).ConfigId = _settings.ConfigId Then _csvConfigComboBox.SelectedIndex = index
                    Next
                End If
            Finally
                _loadingSettings = False
            End Try
            RefreshRecoveryContext()
            RefreshPeriodicUi()
        End Sub

        Private Sub DisplaySettings()
            Dim previous = _loadingSettings
            _loadingSettings = True
            Try
                _singleMode.Checked = _settings.Mode = ImportMode.SingleFile
                _periodicMode.Checked = _settings.Mode = ImportMode.PeriodicFolder
                _watchFolder.Text = _settings.Folder
                _watchInterval.Value = _settings.IntervalSeconds
            Finally
                _loadingSettings = previous
            End Try
        End Sub

        Private Sub SaveMonitoringSettings()
            If Not CanMutate Then Return
            Try
                _settings.Save()
                _startInputError = Nothing
                DisplaySettings()
                RefreshRecoveryContext()
                RefreshPeriodicUi()
            Catch
                ShowMonitoringError("監視設定を保存できませんでした。フォルダと周期を確認してください。")
            End Try
        End Sub

        Private Async Sub StartStopMonitoring(sender As Object, e As EventArgs)
            If _periodic Is Nothing OrElse ExitRequested Then Return
            Dim state = _periodic.Scheduler.Status.State
            If state = PeriodicImportState.Running Then
                Dim stopping = _periodic.Scheduler.StopAsync()
                DrainNotifications()
                Try
                    Await stopping
                Catch
                    If Not IsDisposed AndAlso Not Disposing AndAlso Not ExitRequested Then
                        PeriodicNotifications.Send(_periodic.Presenter, PeriodicImportNotificationKind.MonitoringAbnormalStopped, "停止完了を確認できません。ログを確認してください。", critical:=True)
                    End If
                End Try
                If IsDisposed OrElse Disposing OrElse ExitRequested Then Return
                RefreshRecoveryContext()
                DrainNotifications()
                Return
            End If
            If Not CurrentPolicy.CanStart Then Return
            Dim startRequested As Boolean
            _startInputError = Nothing
            Try
                Dim options = _settings.SaveAndCreateStartOptions(_periodic.DatabaseIdentity)
                DisplaySettings()
                _recoveryContext = _periodic.Recovery.GetRecoveryContext(options)
                If _recoveryContext.State = HeadDisposition.CorrectionPending AndAlso Not _recoveryContext.HasUnusedBinding Then
                    _statusLabel.Text = _recoveryContext.FailureSummary
                    OpenCorrection()
                    Return
                End If
                startRequested = True
                Dim starting = _periodic.Scheduler.StartAsync(options)
                DrainNotifications()
                Await starting
                If IsDisposed OrElse Disposing OrElse ExitRequested Then Return
                RefreshPeriodicUi()
                Await _periodic.Scheduler.Completion
            Catch
                If Not IsDisposed AndAlso Not Disposing AndAlso Not ExitRequested Then
                    Dim message = If(startRequested, PeriodicNotifications.SafeFailure(_periodic.Scheduler.Status.LastError), "開始できませんでした。監視設定の保存とフォルダを確認してください。")
                    If Not startRequested Then _startInputError = message
                    PeriodicNotifications.Send(_periodic.Presenter, PeriodicImportNotificationKind.MonitoringAbnormalStopped, message, critical:=True)
                End If
            Finally
                If Not IsDisposed AndAlso Not Disposing AndAlso Not ExitRequested Then
                    RefreshRecoveryContext()
                    DrainNotifications()
                End If
            End Try
        End Sub

        Private Sub RefreshRecoveryContext()
            _recoveryContext = Nothing
            If _periodic Is Nothing OrElse Not CanMutate OrElse _settings.ConfigId = 0 OrElse _settings.Mode <> ImportMode.PeriodicFolder OrElse _settings.IsDirty Then Return
            Try
                _recoveryContext = _periodic.Recovery.GetRecoveryContext(_settings.CreateOptions(_periodic.DatabaseIdentity))
            Catch
                _recoveryContext = New PeriodicRecoveryContextDto With {.RequiresRecovery = True}
            End Try
        End Sub

        Private Sub OpenCorrection()
            If _periodic Is Nothing OrElse Not CanMutate OrElse _settings.IsDirty Then Return
            RefreshRecoveryContext()
            If _recoveryContext Is Nothing OrElse Not _recoveryContext.CanConfirmCorrection OrElse _recoveryContext.RequiresRecovery Then Return
            Using dialog As New PeriodicImportRecoveryForm(_periodic.Recovery, _settings.CreateOptions(_periodic.DatabaseIdentity), _recoveryContext)
                If dialog.ShowDialog(Me) = DialogResult.OK Then
                    _statusLabel.Text = "修正版を対応付けました。確認後、周期監視開始を押してください。"
                    PeriodicNotifications.Send(_periodic.Presenter, PeriodicImportNotificationKind.CorrectionPending, _statusLabel.Text, critical:=True)
                End If
            End Using
            RefreshRecoveryContext()
            RefreshPeriodicUi()
        End Sub

        Private Sub RefreshPeriodicUi()
            Dim policy = CurrentPolicy
            _periodicPanel.Visible = policy.ShowPeriodicPanel OrElse ExitRequested
            _singleMode.Enabled = policy.CanMutate
            _periodicMode.Enabled = policy.CanMutate
            _csvConfigComboBox.Enabled = policy.CanMutate
            configButton.Enabled = policy.CanMutate
            deleteButton.Enabled = policy.CanMutate
            masterButton.Enabled = policy.CanMutate
            importButton.Enabled = policy.CanManualImport
            _watchFolder.Enabled = policy.CanMutate
            _browseFolder.Enabled = policy.CanMutate
            _watchInterval.Enabled = policy.CanMutate
            _saveMonitoring.Enabled = policy.CanMutate AndAlso _settings.ConfigId > 0
            _startStop.Enabled = _periodic IsNot Nothing AndAlso (policy.CanStart OrElse policy.CanStop)
            Dim state = If(_periodic Is Nothing, PeriodicImportState.Stopped, _periodic.Scheduler.Status.State)
            _startStop.Text = If(state = PeriodicImportState.Running OrElse state = PeriodicImportState.Stopping, "周期監視停止", "周期監視開始")
            _correction.Enabled = policy.CanMutate AndAlso Not _settings.IsDirty AndAlso _recoveryContext IsNot Nothing AndAlso _recoveryContext.CanConfirmCorrection AndAlso Not _recoveryContext.RequiresRecovery
            _correction.Visible = policy.CanMutate AndAlso _recoveryContext IsNot Nothing AndAlso _recoveryContext.CanConfirmCorrection AndAlso Not _recoveryContext.RequiresRecovery
        End Sub

        Private Sub DrainNotifications()
            If IsDisposed OrElse Disposing OrElse _periodic Is Nothing Then Return
            Dim presenter = _periodic.Presenter
            Dim items = presenter.Drain()
            Dim selected = presenter.SelectedEventId
            _updatingHistory = True
            _history.BeginUpdate()
            Try
                _history.Items.Clear()
                For Each item In items
                    _history.Items.Add(New NotificationItem(item))
                Next
                Dim index = If(selected.HasValue, items.ToList().FindIndex(Function(n) n.EventId = selected.Value), 0)
                If items.Count > 0 Then _history.SelectedIndex = Math.Max(0, index)
            Finally
                _history.EndUpdate()
                _updatingHistory = False
            End Try
            Dim status = _periodic.Scheduler.Status
            Dim stateText As String
            Select Case status.State
                Case PeriodicImportState.Starting
                    stateText = "監視開始処理中"
                Case PeriodicImportState.Running
                    stateText = "監視中"
                Case PeriodicImportState.Stopping
                    stateText = "監視停止処理中"
                Case Else
                    stateText = "監視停止"
            End Select
            _statusLabel.Text = PeriodicImportStatusPresenter.SafeText(stateText & "：" &
                If(status.State = PeriodicImportState.Stopped AndAlso status.LastError IsNot Nothing, PeriodicNotifications.SafeFailure(status.LastError), presenter.CurrentMessage))
            If status.State = PeriodicImportState.Stopped AndAlso _recoveryContext IsNot Nothing AndAlso
                (_recoveryContext.State = HeadDisposition.CorrectionPending OrElse _recoveryContext.State = HeadDisposition.BoundCorrection OrElse _recoveryContext.RequiresRecovery) Then
                _statusLabel.Text = PeriodicImportStatusPresenter.SafeText("監視停止：" & _recoveryContext.FailureSummary &
                    If(_recoveryContext.HasUnusedBinding, " 対応付け済みです。確認後、手動で開始してください。", ""))
            End If
            If _startInputError IsNot Nothing Then _statusLabel.Text = _startInputError
            If ExitRequested Then _statusLabel.Text = _exitController.Message
            RefreshPeriodicUi()
        End Sub
        Private Sub ShowMonitoringError(message As String)
            MessageBox.Show(Me, message, "周期監視", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Sub
        Private NotInheritable Class NotificationItem
            Public ReadOnly Notification As PeriodicImportNotificationDto
            Public Sub New(value As PeriodicImportNotificationDto)
                Notification = value
            End Sub
            Public Overrides Function ToString() As String
                Return PeriodicImportStatusPresenter.Display(Notification)
            End Function
        End Class
    End Class
End Namespace
