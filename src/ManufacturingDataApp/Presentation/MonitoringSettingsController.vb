Imports System.IO
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Domain.Constants
Imports ManufacturingDataApp.Domain.Entities

Namespace Presentation
    Public Enum DirtySettingsChoice
        Save
        Discard
        Cancel
    End Enum

    Public Class MonitoringSettingsController
        Private ReadOnly _service As CsvConfigService
        Private _saved As CsvImportConfig
        Public Property Mode As ImportMode
        Public Property Folder As String = String.Empty
        Public Property IntervalSeconds As Integer = 60
        Public ReadOnly Property ConfigId As Integer
            Get
                Return If(_saved Is Nothing, 0, _saved.ConfigId)
            End Get
        End Property
        Public ReadOnly Property IsDirty As Boolean
            Get
                Return _saved IsNot Nothing AndAlso (Mode <> _saved.ImportMode OrElse Folder <> If(_saved.WatchFolderPath, String.Empty) OrElse IntervalSeconds <> _saved.WatchIntervalSeconds)
            End Get
        End Property
        Public Sub New(service As CsvConfigService)
            _service = service
        End Sub
        Public Sub Load(configId As Integer)
            _saved = If(configId = 0, Nothing, _service.GetById(configId))
            Mode = If(_saved Is Nothing, ImportMode.SingleFile, _saved.ImportMode)
            Folder = If(_saved?.WatchFolderPath, String.Empty)
            IntervalSeconds = If(_saved Is Nothing, 60, _saved.WatchIntervalSeconds)
        End Sub
        Public Function ResolveDirty(choice As DirtySettingsChoice) As Boolean
            If Not IsDirty Then Return True
            Select Case choice
                Case DirtySettingsChoice.Cancel
                    Return False
                Case DirtySettingsChoice.Save
                    Save()
                Case DirtySettingsChoice.Discard
                    Load(ConfigId)
            End Select
            Return True
        End Function
        Public Function SwitchConfig(id As Integer, choice As DirtySettingsChoice) As Boolean
            If Not ResolveDirty(choice) Then Return False
            Load(id)
            Return True
        End Function
        Public Sub Save()
            If ConfigId = 0 Then Throw New InvalidOperationException("CSV設定を選択してください。")
            ValidateInputs(False)
            _service.SaveMonitoringSettings(ConfigId, Mode, Folder, IntervalSeconds)
            Load(ConfigId)
        End Sub
        Private Sub ValidateInputs(forStart As Boolean)
            If Not [Enum].IsDefined(Mode) OrElse IntervalSeconds < 1 OrElse IntervalSeconds > 604800 Then Throw New ArgumentException("監視設定が不正です。")
            If forStart AndAlso (String.IsNullOrWhiteSpace(Folder) OrElse Not Directory.Exists(Folder)) Then Throw New ArgumentException("監視フォルダを確認してください。")
            If Not String.IsNullOrWhiteSpace(Folder) Then Path.GetFullPath(Folder.Trim())
        End Sub
        Public Function SaveAndCreateStartOptions(databaseIdentity As String) As PeriodicImportOptionsDto
            If Mode <> ImportMode.PeriodicFolder Then Throw New InvalidOperationException("フォルダ周期監視を選択してください。")
            If ConfigId = 0 Then Throw New InvalidOperationException("CSV設定を選択してください。")
            ValidateInputs(True)
            Save()
            Return CreateOptions(databaseIdentity)
        End Function
        Public Function CreateOptions(databaseIdentity As String) As PeriodicImportOptionsDto
            Dim config = _service.GetById(ConfigId)
            If config Is Nothing Then Throw New InvalidOperationException("CSV設定を選択してください。")
            Dim mappings = _service.GetMappings(ConfigId)
            Dim options As New PeriodicImportOptionsDto With {.Config = config, .WatchIntervalSeconds = config.WatchIntervalSeconds, .DatabaseIdentity = databaseIdentity}
            options.ColumnMappings.AddRange(mappings)
            Return options
        End Function
    End Class

    Public Class PeriodicUiPolicy
        Public ReadOnly Property CanMutate As Boolean
        Public ReadOnly Property CanManualImport As Boolean
        Public ReadOnly Property ShowPeriodicPanel As Boolean
        Public ReadOnly Property CanStart As Boolean
        Public ReadOnly Property CanStop As Boolean
        Public Sub New(state As PeriodicImportState, mode As ImportMode, hasConfig As Boolean)
            CanMutate = state = PeriodicImportState.Stopped
            CanManualImport = CanMutate AndAlso mode = ImportMode.SingleFile
            ShowPeriodicPanel = mode = ImportMode.PeriodicFolder
            CanStart = CanMutate AndAlso ShowPeriodicPanel AndAlso hasConfig
            CanStop = state = PeriodicImportState.Running
        End Sub
    End Class
End Namespace
