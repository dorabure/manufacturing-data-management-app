Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Application.Services

Namespace PeriodicImport
    Public Class JsonImportProcessingJournal
        Implements IImportProcessingJournal
        Private ReadOnly _paths As New PeriodicImportFileStore()
        Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {.WriteIndented = True, .UnmappedMemberHandling = Serialization.JsonUnmappedMemberHandling.Disallow}

        Private Class Envelope
            Public Property Version As Integer = 1
            Public Property Payload As String
            Public Property Sha256 As String
        End Class

        Public Function Load(root As String) As ImportJournalStateDto Implements IImportProcessingJournal.Load
            Try
                Dim state As New ImportJournalStateDto()
                Dim directory = _paths.Resolve(root, ".periodic-import")
                For Each file In IO.Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
                    Dim name = Path.GetFileName(file)
                    If name = "owner.lock" Then Continue For
                    _paths.Resolve(root, ".periodic-import/" & name)
                    If name.StartsWith("write-", StringComparison.Ordinal) AndAlso name.EndsWith(".tmp", StringComparison.Ordinal) Then Throw New InvalidDataException("未確定Journalがあります。")
                    If name.StartsWith("order-", StringComparison.Ordinal) Then
                        Dim order = Read(Of PeriodicImportOrderDto)(file)
                        If name <> "order-" & ValidId(order.OrderId) & ".json" OrElse order.Version <> 1 OrElse order.Generation < 1 Then Throw New InvalidDataException()
                        state.Orders.Add(order)
                    ElseIf name.StartsWith("attempt-", StringComparison.Ordinal) Then
                        Dim attempt = Read(Of ImportProcessingRecordDto)(file)
                        If name <> "attempt-" & ValidId(attempt.AttemptId) & ".json" OrElse attempt.Version <> 1 Then Throw New InvalidDataException()
                        state.Attempts.Add(attempt)
                    ElseIf name.StartsWith("binding-", StringComparison.Ordinal) Then
                        Dim binding = Read(Of CorrectionBindingDto)(file)
                        If name <> "binding-" & ValidId(binding.CorrectionBindingId) & ".json" OrElse binding.Version <> 1 Then Throw New InvalidDataException()
                        state.Bindings.Add(binding)
                    Else
                        ' Unknown files are neither adopted nor deleted.
                        Throw New InvalidDataException("未知の管理ファイルがあります。")
                    End If
                Next
                For Each o In state.Orders
                    If o.Root <> _paths.NormalizeRoot(root) Then Throw New InvalidDataException("Root不一致")
                    For Each e In o.Entries
                        _paths.Resolve(root, e.RelativePath, True)
                    Next
                Next
                For Each a In state.Attempts
                    If a.Root <> _paths.NormalizeRoot(root) Then Throw New InvalidDataException("Root不一致")
                    _paths.Resolve(root, a.Fingerprint.RelativePath, True)
                    If a.Destination IsNot Nothing Then
                        _paths.Resolve(root, a.Destination)
                        Dim parts = a.Destination.Split("/"c)
                        If parts.Length <> 2 OrElse parts(0) <> If(a.Result.Outcome = PeriodicFileOutcome.Succeeded, "success", "error") Then Throw New InvalidDataException("配置不一致")
                    End If
                Next
                For Each binding In state.Bindings
                    _paths.Resolve(root, binding.Candidate.RelativePath, True)
                Next
                Return state
            Catch ex As PeriodicImportStopRequiredException
                Throw
            Catch ex As Exception
                Throw New PeriodicImportStopRequiredException(PeriodicStopReason.RecoveryRequired, ex)
            End Try
        End Function

        Public Sub SaveOrder(root As String, order As PeriodicImportOrderDto) Implements IImportProcessingJournal.SaveOrder
            Dim pathValue = RecordPath(root, "order", order.OrderId)
            If File.Exists(pathValue) Then
                If Read(Of PeriodicImportOrderDto)(pathValue).Generation <> order.Generation Then Throw New InvalidDataException("Order世代不一致")
            ElseIf order.Generation <> 0 Then
                Throw New InvalidDataException("Orderが消失しました。")
            End If
            order.Generation += 1
            Write(pathValue, order)
        End Sub

        Public Sub SaveAttempt(root As String, attempt As ImportProcessingRecordDto) Implements IImportProcessingJournal.SaveAttempt
            Write(RecordPath(root, "attempt", attempt.AttemptId), attempt)
        End Sub

        Public Sub SaveBinding(root As String, binding As CorrectionBindingDto) Implements IImportProcessingJournal.SaveBinding
            Write(RecordPath(root, "binding", binding.CorrectionBindingId), binding)
        End Sub

        Private Function RecordPath(root As String, kind As String, id As String) As String
            Return _paths.Resolve(root, ".periodic-import/" & kind & "-" & ValidId(id) & ".json")
        End Function

        Private Shared Function ValidId(id As String) As String
            Dim parsed As Guid
            If Not Guid.TryParseExact(id, "N", parsed) Then Throw New InvalidDataException("Journal ID不正")
            Return id
        End Function

        Private Shared Function Read(Of T)(pathValue As String) As T
            Dim envelope = JsonSerializer.Deserialize(Of Envelope)(File.ReadAllBytes(pathValue), JsonOptions)
            If envelope Is Nothing OrElse envelope.Version <> 1 OrElse envelope.Payload Is Nothing OrElse
                envelope.Sha256 <> Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.Payload))) Then Throw New InvalidDataException("Journal検証失敗")
            Dim value = JsonSerializer.Deserialize(Of T)(envelope.Payload, JsonOptions)
            If value Is Nothing Then Throw New InvalidDataException()
            Return value
        End Function

        Private Shared Sub Write(Of T)(pathValue As String, value As T)
            Dim payload = JsonSerializer.Serialize(value, JsonOptions)
            Dim envelope As New Envelope With {.Payload = payload, .Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))}
            Dim temp = Path.Combine(Path.GetDirectoryName(pathValue), "write-" & Guid.NewGuid().ToString("N") & ".tmp")
            ' Failed writes remain detectable. Never fall back to truncating the destination.
            Using stream As New FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                JsonSerializer.Serialize(stream, envelope, JsonOptions)
                stream.Flush(True)
            End Using
            If File.Exists(pathValue) Then
                File.Replace(temp, pathValue, Nothing)
            Else
                File.Move(temp, pathValue)
            End If
        End Sub

        Public Sub Prune(root As String, state As ImportJournalStateDto, now As DateTimeOffset) Implements IImportProcessingJournal.Prune
            state = Load(root)
            If state.Orders.Count = 0 Then Return
            PeriodicJournalValidator.Validate(state, root, state.Orders(0).DatabaseIdentity)
            ' Delete only a fully resolved graph after the later resolution/completion date.
            ' Remove references first. Completed orphan records are safe to retain on interruption.
            For Each order In state.Orders.Where(Function(o) o.HeadDisposition = HeadDisposition.Resolved AndAlso o.ResolvedAt.HasValue).ToArray()
                Dim attempts = state.Attempts.Where(Function(a) a.OrderId = order.OrderId).ToList()
                Dim bindings = state.Bindings.Where(Function(c) c.OrderId = order.OrderId).ToList()
                If attempts.Any(Function(a) a.Stage <> ProcessingStage.Completed OrElse Not a.CompletedAt.HasValue) Then Continue For
                Dim retainFrom = order.ResolvedAt.Value
                For Each a In attempts
                    If a.CompletedAt.Value > retainFrom Then retainFrom = a.CompletedAt.Value
                Next
                If now < retainFrom.AddDays(30) Then Continue For
                ' Full current graph must have been validated by the caller, but also compare
                ' persisted bytes semantically before deleting our own exact record paths.
                Dim persisted = Read(Of PeriodicImportOrderDto)(RecordPath(root, "order", order.OrderId))
                If persisted.Generation <> order.Generation OrElse persisted.HeadDisposition <> HeadDisposition.Resolved Then Throw New InvalidDataException()
                ' Conservative: keep graphs with cross-order references.
                Dim ids = attempts.Select(Function(a) a.AttemptId).ToHashSet(StringComparer.Ordinal)
                If state.Orders.Any(Function(o) o.OrderId <> order.OrderId AndAlso (ids.Contains(o.RootFailedAttemptId) OrElse ids.Contains(o.LatestFailedAttemptId))) OrElse
                    state.Bindings.Any(Function(c) c.OrderId <> order.OrderId AndAlso ids.Contains(c.FailedAttemptId)) Then Continue For
                ' An atomic tombstone order makes interruption during pruning distinguishable.
                ' Empty resolved order survives only an interrupted cleanup; delete it last.
                order.Entries.Clear()
                order.Cursor = 0
                order.RootFailedAttemptId = Nothing
                order.LatestFailedAttemptId = Nothing
                order.CorrectionBindingId = Nothing
                SaveOrder(root, order)
                For Each c In bindings
                    File.Delete(RecordPath(root, "binding", c.CorrectionBindingId))
                Next
                For Each a In attempts
                    File.Delete(RecordPath(root, "attempt", a.AttemptId))
                Next
                File.Delete(RecordPath(root, "order", order.OrderId))
            Next
        End Sub
    End Class
End Namespace
