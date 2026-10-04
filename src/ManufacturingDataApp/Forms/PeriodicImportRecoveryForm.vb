Imports System.Drawing
Imports System.IO
Imports System.Windows.Forms
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Services
Imports ManufacturingDataApp.Presentation

Namespace Forms
    Public Class PeriodicImportRecoveryForm
        Inherits Form
        Private ReadOnly _service As PeriodicImportRecoveryService
        Private ReadOnly _options As PeriodicImportOptionsDto
        Private ReadOnly _context As PeriodicRecoveryContextDto
        Private ReadOnly _candidate As New Label With {.AutoSize = True, .MaximumSize = New Size(720, 0), .Text = "候補は未選択です。自動選択は行いません。"}
        Private ReadOnly _confirm As New CheckBox With {.AutoSize = True, .Text = "このCSVを表示された元Attemptの修正版として扱います。"}
        Private ReadOnly _save As New Button With {.Text = "対応付けを保存", .AutoSize = True, .Enabled = False}
        Private _fingerprint As FileFingerprintDto
        Public Sub New(service As PeriodicImportRecoveryService, options As PeriodicImportOptionsDto, context As PeriodicRecoveryContextDto)
            _service = service
            _options = options.DeepCopy()
            _context = context
            Text = "修正版を指定／確認し直す"
            ClientSize = New Size(780, 410)
            MinimumSize = New Size(780, 410)
            AutoScaleMode = AutoScaleMode.Font
            StartPosition = FormStartPosition.CenterParent
            Dim layout As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(15), .AutoScroll = True, .FlowDirection = FlowDirection.TopDown, .WrapContents = False}
            For Each line In {$"元Attempt：{context.FailedAttemptId}", $"元ファイル：{context.OriginalFileName}",
                $"CSV設定：{context.ConfigName}", $"元の順序：{context.Position} ／ 後続保留：{context.PendingFollowingCount}件",
                context.FailureSummary, $"既存の候補：{If(context.BoundCandidateRelativePath, "なし")}"}
                layout.Controls.Add(New Label With {.AutoSize = True, .MaximumSize = New Size(720, 0), .Text = PeriodicImportStatusPresenter.SafeText(line), .Margin = New Padding(3, 5, 3, 5)})
            Next
            Dim choose As New Button With {.Text = "修正版CSVを選択", .AutoSize = True, .Enabled = context.CanConfirmCorrection AndAlso Not context.RequiresRecovery}
            Dim cancel As New Button With {.Text = "キャンセル", .AutoSize = True, .DialogResult = DialogResult.Cancel}
            layout.Controls.AddRange({choose, _candidate, _confirm, _save, cancel})
            Controls.Add(layout)
            CancelButton = cancel
            AddHandler choose.Click, AddressOf ChooseCandidate
            AddHandler _confirm.CheckedChanged, Sub() _save.Enabled = _confirm.Checked AndAlso _fingerprint IsNot Nothing
            AddHandler _save.Click, AddressOf SaveBinding
        End Sub

        Private Sub ChooseCandidate(sender As Object, e As EventArgs)
            _confirm.Checked = False
            _fingerprint = Nothing
            _save.Enabled = False
            Using picker As New OpenFileDialog With {.Filter = "CSVファイル (*.csv)|*.csv", .InitialDirectory = _options.Config.WatchFolderPath, .Multiselect = False, .FileName = String.Empty}
                If picker.ShowDialog(Me) <> DialogResult.OK Then Return
                Try
                    Dim relative = Path.GetRelativePath(_options.Config.WatchFolderPath, picker.FileName)
                    _fingerprint = _service.PreviewCandidate(_options, relative)
                    _candidate.Text = PeriodicImportStatusPresenter.SafeText($"選択：{relative} ／ サイズ：{_fingerprint.Length} bytes ／ 更新：{_fingerprint.LastWriteTimeUtc:O} ／ SHA256：{_fingerprint.Sha256.Substring(0, 12)}")
                Catch
                    _candidate.Text = "候補を確認できません。監視フォルダ直下の読み取り可能なCSVを選択してください。"
                End Try
            End Using
        End Sub
        Private Sub SaveBinding(sender As Object, e As EventArgs)
            If Not _confirm.Checked OrElse _fingerprint Is Nothing Then Return
            Try
                If _context.HasUnusedBinding Then
                    _service.ReconfirmCorrection(_options, _context.FailedAttemptId, _fingerprint.RelativePath, _fingerprint)
                Else
                    _service.ConfirmCorrection(_options, _context.FailedAttemptId, _fingerprint.RelativePath, _fingerprint)
                End If
                DialogResult = DialogResult.OK
                Close()
            Catch
                _save.Enabled = False
                _confirm.Checked = False
                MessageBox.Show(Me, "対応付けできませんでした。画面を閉じて状態を確認し、候補を選び直してください。監視は開始していません。", "修正版の確認", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            End Try
        End Sub
    End Class
End Namespace
