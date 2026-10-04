Imports System.Threading.Tasks
Imports System.Windows.Forms
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Forms
Imports ManufacturingDataApp.Domain.Constants
Imports ManufacturingDataApp.Infrastructure.PeriodicImport
Imports ManufacturingDataApp.Infrastructure.Repositories
Imports ManufacturingDataApp.Presentation
Imports ManufacturingDataApp.Tests.TestSupport
Imports Xunit

Public Class Phase5MainFormTests
    <Fact>
    Public Sub T21_ModeSaveRecreationNeverAutoStarts()
        RunSta(Sub()
                   Using db As New Phase2Database()
                       db.Initialize()
                       Dim composition As New PeriodicImportComposition(db.Factory)
                       Using form = CreateMain(db, composition)
                           Assert.True(Field(Of Button)(form, "importButton").Enabled)
                           Field(Of RadioButton)(form, "_periodicMode").Checked = True
                           Assert.False(Field(Of Button)(form, "importButton").Enabled)
                           Field(Of TextBox)(form, "_watchFolder").Text = db.DirectoryPath
                           Field(Of NumericUpDown)(form, "_watchInterval").Value = 77
                           Invoke(form, "SaveMonitoringSettings")
                           Field(Of RadioButton)(form, "_singleMode").Checked = True
                           Assert.Equal(db.DirectoryPath, Field(Of TextBox)(form, "_watchFolder").Text)
                           Assert.Equal(77D, Field(Of NumericUpDown)(form, "_watchInterval").Value)
                           Field(Of RadioButton)(form, "_periodicMode").Checked = True
                           Assert.Equal(PeriodicImportState.Stopped, composition.Scheduler.Status.State)
                       End Using
                       Dim recreated As New PeriodicImportComposition(db.Factory)
                       Using form = CreateMain(db, recreated)
                           Assert.True(Field(Of RadioButton)(form, "_periodicMode").Checked)
                           Assert.Equal(77D, Field(Of NumericUpDown)(form, "_watchInterval").Value)
                           Assert.Equal(db.DirectoryPath, Field(Of TextBox)(form, "_watchFolder").Text)
                           Assert.False(Field(Of Button)(form, "importButton").Enabled)
                           Assert.True(Field(Of Button)(form, "_startStop").Enabled)
                           Assert.Equal("周期監視開始", Field(Of Button)(form, "_startStop").Text)
                           Assert.Equal(PeriodicImportState.Stopped, recreated.Scheduler.Status.State)
                       End Using
                       Assert.Equal(0L, CLng(db.Scalar("SELECT COUNT(*) FROM OperationLog")))
                   End Using
               End Sub)
    End Sub

    <Theory>
    <InlineData(PeriodicImportState.Starting)>
    <InlineData(PeriodicImportState.Running)>
    <InlineData(PeriodicImportState.Stopping)>
    Public Sub T22_ActualControlsAndEventGuards(state As PeriodicImportState)
        RunSta(Sub()
                   Using db As New Phase2Database()
                       db.Initialize()
                       Dim prepare As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                       Dim execute As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
                       Dim fake As New PeriodicImportCycleFake With {.Preparation = Function(token) prepare.Task, .Execution = Function() execute.Task}
                       Dim presenter As New PeriodicImportStatusPresenter()
                       Dim scheduler As New PeriodicImportService(fake, sink:=presenter)
                       Dim recovery As New PeriodicImportRecoveryService(New PeriodicImportFileStore(), New JsonImportProcessingJournal(), Function() scheduler.Status.State = PeriodicImportState.Stopped)
                       Dim composition As New PeriodicImportComposition(db.Factory.DatabasePath, presenter, scheduler, recovery)
                       Using form = CreateMain(db, composition)
                           Dim before = db.Snapshot()
                           Dim starting = scheduler.StartAsync(New PeriodicImportOptionsDto())
                           If state <> PeriodicImportState.Starting Then
                               prepare.SetResult(True)
                               starting.GetAwaiter().GetResult()
                               fake.WaitForCountAsync(1).GetAwaiter().GetResult()
                           End If
                           If state = PeriodicImportState.Stopping Then scheduler.StopAsync()
                           Assert.Equal(state, scheduler.Status.State)
                           Invoke(form, "RefreshPeriodicUi")
                           For Each fieldName In {"importButton", "configButton", "deleteButton", "masterButton", "_saveMonitoring", "_browseFolder"}
                               Assert.False(Field(Of Button)(form, fieldName).Enabled)
                           Next
                           For Each fieldName In {"searchButton", "exportButton", "errorLogButton", "operationLogButton"}
                               Assert.True(Field(Of Button)(form, fieldName).Enabled)
                           Next
                           Assert.False(Field(Of ComboBox)(form, "_csvConfigComboBox").Enabled)
                           Assert.False(Field(Of RadioButton)(form, "_singleMode").Enabled)
                           Assert.False(Field(Of RadioButton)(form, "_periodicMode").Enabled)
                           Assert.False(Field(Of TextBox)(form, "_watchFolder").Enabled)
                           Assert.False(Field(Of NumericUpDown)(form, "_watchInterval").Enabled)
                           Assert.Equal(state = PeriodicImportState.Running, Field(Of Button)(form, "_startStop").Enabled)
                           For Each methodName In {"OpenCsvImport", "OpenCsvConfig", "OpenMaster", "DeleteSelected"}
                               ' Direct handler invocation bypasses Button.Enabled: no dialogs/DB operations allowed.
                               Invoke(form, methodName, Nothing, EventArgs.Empty)
                           Next
                           Invoke(form, "GridCellDoubleClick", Nothing, New DataGridViewCellEventArgs(2, 0))
                           Assert.Equal(before, db.Snapshot())
                           Dim stopping = scheduler.StopAsync()
                           prepare.TrySetResult(True)
                           execute.TrySetResult(True)
                           stopping.GetAwaiter().GetResult()
                           ' Observe expected cancellation of a start stopped during preparation.
                           If starting.IsCanceled Then
                               Assert.ThrowsAny(Of OperationCanceledException)(Sub() starting.GetAwaiter().GetResult())
                           End If
                           Invoke(form, "RefreshPeriodicUi")
                           Assert.True(Field(Of Button)(form, "importButton").Enabled)
                           Assert.True(Field(Of Button)(form, "configButton").Enabled)
                           Assert.True(Field(Of Button)(form, "deleteButton").Enabled)
                           Assert.True(Field(Of Button)(form, "masterButton").Enabled)
                       End Using
                   End Using
               End Sub)
    End Sub

    <Fact>
    Public Sub T23_NotificationDoesNotSearchOrChangeSelection()
        RunSta(Sub()
                   Using db As New Phase2Database()
                       db.Initialize()
                       Dim composition As New PeriodicImportComposition(db.Factory)
                       Using form = CreateMain(db, composition)
                           Dim grid = Field(Of DataGridView)(form, "_grid")
                           Dim originalData = grid.DataSource
                           Field(Of TextBox)(form, "_equipmentIdTextBox").Text = "keep-search"
                           composition.Presenter.Publish(New PeriodicImportNotificationDto With {.Kind = PeriodicImportNotificationKind.CsvSucceeded, .RegisteredCount = 8, .Message = "8件登録しました。一覧は検索で更新してください。"})
                           Invoke(form, "DrainNotifications")
                           Assert.Same(originalData, grid.DataSource)
                           Assert.Equal("keep-search", Field(Of TextBox)(form, "_equipmentIdTextBox").Text)
                           Assert.Contains("一覧は検索で更新", Field(Of Label)(form, "_statusLabel").Text)
                           Dim history = Field(Of ComboBox)(form, "_history")
                           Assert.Equal(ComboBoxStyle.DropDownList, history.DropDownStyle)
                           Assert.Equal(1, history.Items.Count)
                           Dim firstId = composition.Presenter.Drain()(0).EventId
                           history.SelectedIndex = -1
                           history.SelectedIndex = 0
                           composition.Presenter.Publish(New PeriodicImportNotificationDto With {.Message = "newest"})
                           Invoke(form, "DrainNotifications")
                           Assert.Equal(firstId, composition.Presenter.SelectedEventId.Value)
                           Assert.Equal(1, history.SelectedIndex)
                           Assert.Contains("newest", Field(Of Label)(form, "_statusLabel").Text)
                           Assert.Equal(PeriodicImportState.Stopped, composition.Scheduler.Status.State)
                       End Using
                   End Using
               End Sub)
    End Sub

    <Fact>
    Public Sub NoConfigDisablesSaveAndStart()
        RunSta(Sub()
                   Using db As New Phase2Database()
                       db.Initialize()
                       db.Execute("DELETE FROM CsvColumnMapping; DELETE FROM CsvImportConfig")
                       Using form = CreateMain(db, New PeriodicImportComposition(db.Factory))
                           Field(Of RadioButton)(form, "_periodicMode").Checked = True
                           Assert.False(Field(Of Button)(form, "_saveMonitoring").Enabled)
                           Assert.False(Field(Of Button)(form, "_startStop").Enabled)
                       End Using
                   End Using
               End Sub)
    End Sub

    <Fact>
    Public Async Function RealCompositionUsesOnlySuppliedTemporaryDatabase() As Task
        Using f As New Phase4Fixture()
            f.Write("ok.csv")
            Dim composition As New PeriodicImportComposition(f.Database.Factory)
            Assert.Equal(f.Database.Factory.DatabasePath, composition.DatabaseIdentity)
            Assert.Equal(PeriodicImportState.Stopped, composition.Scheduler.Status.State)
            Await composition.Scheduler.StartAsync(f.Options)
            Await composition.Scheduler.CurrentCycleCompletion
            Await composition.Scheduler.StopAsync()
            Assert.Equal(1, f.DataCount())
            Assert.Equal(1L, CLng(f.Database.Scalar("SELECT COUNT(*) FROM OperationLog WHERE OperationType='周期監視開始'")))
            Assert.Equal(1L, CLng(f.Database.Scalar("SELECT COUNT(*) FROM OperationLog WHERE OperationType='周期監視停止'")))
            Assert.Equal(1L, CLng(f.Database.Scalar("SELECT COUNT(*) FROM OperationLog WHERE OperationType='周期CSV取込'")))
            Assert.Contains(composition.Presenter.Drain(), Function(n) n.Kind = PeriodicImportNotificationKind.CsvSucceeded)
        End Using
    End Function

    <Fact>
    Public Sub T43_TwoSavedConfigsRestoreIndependentlyInActualControls()
        RunSta(Sub()
                   Using db As New Phase2Database()
                       db.Initialize()
                       db.Execute("UPDATE CsvImportConfig SET ConfigName='A'; INSERT INTO CsvImportConfig(ConfigName,Encoding,Delimiter,HasHeader) VALUES('B','UTF-8',',',1)")
                       Dim repo As New CsvImportConfigRepository(db.Factory)
                       Dim configs = repo.GetAll()
                       repo.SaveMonitoringSettings(configs(0).ConfigId, ImportMode.SingleFile, db.DirectoryPath, 60)
                       repo.SaveMonitoringSettings(configs(1).ConfigId, ImportMode.PeriodicFolder, IO.Path.Combine(db.DirectoryPath, "B"), 10)
                       Using form = CreateMain(db, New PeriodicImportComposition(db.Factory))
                           Dim combo = Field(Of ComboBox)(form, "_csvConfigComboBox")
                           combo.SelectedIndex = 1
                           Assert.True(Field(Of RadioButton)(form, "_periodicMode").Checked)
                           Assert.Equal(10D, Field(Of NumericUpDown)(form, "_watchInterval").Value)
                           Assert.Equal(IO.Path.Combine(db.DirectoryPath, "B"), Field(Of TextBox)(form, "_watchFolder").Text)
                           combo.SelectedIndex = 0
                           Assert.True(Field(Of RadioButton)(form, "_singleMode").Checked)
                           Assert.Equal(60D, Field(Of NumericUpDown)(form, "_watchInterval").Value)
                           Assert.Equal(db.DirectoryPath, Field(Of TextBox)(form, "_watchFolder").Text)
                       End Using
                   End Using
               End Sub)
    End Sub

    <Fact>
    Public Sub T70_RecoveryDialogStartsUnselectedAndCancelDoesNothing()
        RunSta(Sub()
                   Using f As New Phase4Fixture()
                       f.Write("failed.csv", value:="bad")
                       f.Write("following.csv", 1)
                       f.Prepare()
                       Assert.Throws(Of PeriodicImportStopRequiredException)(Sub() f.Run())
                       f.Cycle.CleanupAsync().GetAwaiter().GetResult()
                       f.Write("failed.csv", value:="20")
                       Dim composition As New PeriodicImportComposition(f.Database.Factory)
                       Dim context = composition.Recovery.GetRecoveryContext(f.Options)
                       Using form As New PeriodicImportRecoveryForm(composition.Recovery, f.Options, context)
                           Assert.Null(Field(Of FileFingerprintDto)(form, "_fingerprint"))
                           Assert.False(Field(Of CheckBox)(form, "_confirm").Checked)
                           Assert.False(Field(Of Button)(form, "_save").Enabled)
                           form.DialogResult = DialogResult.Cancel
                       End Using
                       Assert.Empty(f.State().Bindings)
                       Assert.Single(f.Executor.Calls)
                       Assert.Equal(0, f.DataCount())
                       Assert.Equal(PeriodicImportState.Stopped, composition.Scheduler.Status.State)
                   End Using
               End Sub)
    End Sub

    <Fact>
    Public Sub OperationLogTypeListKeepsEditableOldAndNewTypes()
        RunSta(Sub()
                   Using db As New Phase2Database()
                       db.Initialize()
                       Using form As New OperationLogForm(New LogViewService(New ErrorLogRepository(db.Factory), New OperationLogRepository(db.Factory)))
                           Dim combo = Field(Of ComboBox)(form, "_type")
                           Assert.Equal(ComboBoxStyle.DropDown, combo.DropDownStyle)
                           For Each name In {"CSV取込", "CSV出力", "編集", "削除", "設備マスタ登録", "周期CSV取込", "周期CSV取込失敗", "周期CSV取込結果不明", "周期監視開始", "周期監視停止", "周期監視異常停止"}
                               Assert.True(combo.Items.Contains(name))
                           Next
                       End Using
                   End Using
               End Sub)
    End Sub
End Class
