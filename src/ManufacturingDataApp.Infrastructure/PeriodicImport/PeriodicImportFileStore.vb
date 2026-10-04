Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Security.Cryptography
Imports Microsoft.Win32.SafeHandles
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces

Namespace PeriodicImport
    Public Class PeriodicImportFileStore
        Implements IPeriodicImportFileStore

        Public Function NormalizeRoot(root As String) As String Implements IPeriodicImportFileStore.NormalizeRoot
            If Not OperatingSystem.IsWindows() OrElse String.IsNullOrWhiteSpace(root) OrElse
                Not Path.IsPathFullyQualified(root) OrElse root.StartsWith("\\", StringComparison.Ordinal) Then Throw New IOException("Windowsローカル絶対パスが必要です。")
            Dim full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
            Dim drive As New DriveInfo(Path.GetPathRoot(full))
            If drive.DriveType <> DriveType.Fixed AndAlso drive.DriveType <> DriveType.Removable Then Throw New IOException("ローカル通常ドライブが必要です。")
            CheckAncestors(full)
            If Not Directory.Exists(full) Then Throw New DirectoryNotFoundException()
            Return full
        End Function

        Friend Shared Sub CheckAncestors(pathValue As String)
            Dim current = Path.GetFullPath(pathValue)
            Do
                If File.Exists(current) OrElse Directory.Exists(current) Then
                    If (File.GetAttributes(current) And FileAttributes.ReparsePoint) <> 0 Then Throw New IOException("Reparse pointは禁止です。")
                End If
                Dim parent = Path.GetDirectoryName(current)
                If String.IsNullOrEmpty(parent) OrElse parent = current Then Exit Do
                current = parent
            Loop
        End Sub

        Friend Function Resolve(root As String, relative As String, Optional directOnly As Boolean = False) As String
            root = NormalizeRoot(root)
            If String.IsNullOrWhiteSpace(relative) OrElse Path.IsPathRooted(relative) Then Throw New IOException("相対パスが不正です。")
            Dim parts = relative.Replace("/"c, "\"c).Split("\"c)
            If parts.Any(Function(p) p = "" OrElse p = "." OrElse p = ".." OrElse p.EndsWith(" ", StringComparison.Ordinal) OrElse p.EndsWith(".", StringComparison.Ordinal) OrElse p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) Then Throw New IOException("パス境界が不正です。")
            If directOnly AndAlso parts.Length <> 1 Then Throw New IOException("監視直下のみ指定できます。")
            Dim full = Path.GetFullPath(Path.Combine(root, relative))
            Dim rel = Path.GetRelativePath(root, full)
            If Path.IsPathRooted(rel) OrElse rel.Split(Path.DirectorySeparatorChar).Contains("..") Then Throw New IOException("Root外は禁止です。")
            CheckAncestors(full)
            Return full
        End Function

        Public Sub Prepare(root As String) Implements IPeriodicImportFileStore.Prepare
            root = NormalizeRoot(root)
            For Each folder In {"success", "error", ".periodic-import"}
                Dim target = Resolve(root, folder)
                Directory.CreateDirectory(target)
                CheckAncestors(target)
                Dim probe = Path.Combine(target, "probe-" & Guid.NewGuid().ToString("N") & ".tmp")
                Using stream As New FileStream(probe, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None)
                    stream.WriteByte(0)
                    stream.Flush(True)
                End Using
                File.Delete(probe)
            Next
            ' Force enumeration to test access, without opening CSV data.
            Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly)
        End Sub

        Public Function AcquireOwner(root As String) As IDisposable Implements IPeriodicImportFileStore.AcquireOwner
            Return New FileStream(Resolve(root, ".periodic-import/owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)
        End Function

        Public Function Enumerate(root As String) As IReadOnlyList(Of PeriodicOrderEntryDto) Implements IPeriodicImportFileStore.Enumerate
            root = NormalizeRoot(root)
            Dim entries As New List(Of PeriodicOrderEntryDto)()
            For Each filePath In Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly)
                If Not String.Equals(Path.GetExtension(filePath), ".csv", StringComparison.OrdinalIgnoreCase) Then Continue For
                Resolve(root, Path.GetFileName(filePath), True)
                Dim info As New FileInfo(filePath)
                Dim identity As String = Nothing
                Try
                    Using handle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite Or FileShare.Delete)
                        identity = FileIdentity(handle)
                    End Using
                Catch ex As IOException When IsSharing(ex)
                    ' Writer may not permit read; the original position still gets persisted.
                End Try
                entries.Add(New PeriodicOrderEntryDto With {.RelativePath = info.Name, .OriginalFileName = info.Name,
                    .LastWriteTimeUtc = info.LastWriteTimeUtc, .ObservedLength = info.Length, .FileId = identity})
            Next
            Return SortEntries(entries)
        End Function

        Friend Shared Function SortEntries(entries As IEnumerable(Of PeriodicOrderEntryDto)) As IReadOnlyList(Of PeriodicOrderEntryDto)
            Dim sorted = entries.OrderBy(Function(e) e.LastWriteTimeUtc).
                ThenBy(Function(e) e.OriginalFileName, StringComparer.OrdinalIgnoreCase).
                ThenBy(Function(e) e.OriginalFileName, StringComparer.Ordinal).ToList()
            For i = 0 To sorted.Count - 1
                sorted(i).Position = i
            Next
            Return sorted
        End Function

        Public Function TryRead(root As String, relativePath As String, Optional directOnly As Boolean = True) As IPeriodicReadLease Implements IPeriodicImportFileStore.TryRead
            Dim full = Resolve(root, relativePath, directOnly)
            If Not String.Equals(Path.GetExtension(full), ".csv", StringComparison.OrdinalIgnoreCase) Then Throw New IOException("CSVが必要です。")
            Dim stream As FileStream = Nothing
            Try
                stream = New FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read)
                CheckAncestors(full)
                Dim fp As New FileFingerprintDto With {.RelativePath = relativePath.Replace("\"c, "/"c), .Length = stream.Length,
                    .LastWriteTimeUtc = File.GetLastWriteTimeUtc(full), .Sha256 = Convert.ToHexString(SHA256.HashData(stream)),
                    .FileId = FileIdentity(stream.SafeFileHandle)}
                Return New ReadLease(stream, full, fp)
            Catch ex As IOException When IsSharing(ex)
                stream?.Dispose()
                Return Nothing
            Catch
                stream?.Dispose()
                Throw
            End Try
        End Function

        Public Function Exists(root As String, relativePath As String) As Boolean Implements IPeriodicImportFileStore.Exists
            Dim full = Resolve(root, relativePath)
            Try
                Dim attributes = File.GetAttributes(full)
                If (attributes And FileAttributes.Directory) <> 0 Then Throw New IOException("ファイルの代わりにフォルダが存在します。")
                Return True
            Catch ex As FileNotFoundException
                Return False
            Catch ex As DirectoryNotFoundException
                Return False
            End Try
        End Function

        Public Function Destination(root As String, originalName As String, succeeded As Boolean, candidate As Integer, utc As DateTimeOffset) As String Implements IPeriodicImportFileStore.Destination
            Resolve(root, originalName, True)
            If candidate < 0 OrElse candidate >= 100 Then Throw New ArgumentOutOfRangeException(NameOf(candidate))
            Dim name = originalName
            If candidate > 0 Then
                Dim stem = Path.GetFileNameWithoutExtension(originalName)
                If stem.Length > 180 Then
                    Dim digest = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stem))).Substring(0, 12)
                    stem = stem.Substring(0, 165)
                    If Char.IsHighSurrogate(stem(stem.Length - 1)) Then stem = stem.Substring(0, stem.Length - 1)
                    stem &= "_" & digest
                End If
                name = stem & "_" & utc.UtcDateTime.ToString("yyyyMMdd_HHmmssfff", Globalization.CultureInfo.InvariantCulture) & "_" & candidate.ToString(Globalization.CultureInfo.InvariantCulture) & ".csv"
            End If
            Dim relative = If(succeeded, "success/", "error/") & name
            Resolve(root, relative)
            Return relative
        End Function

        Public Function Move(root As String, source As String, destination As String) As Boolean Implements IPeriodicImportFileStore.Move
            Dim fromPath = Resolve(root, source, True)
            Dim target = Resolve(root, destination)
            Dim parent = Path.GetFileName(Path.GetDirectoryName(target))
            If parent <> "success" AndAlso parent <> "error" Then Throw New IOException("移動先が不正です。")
            If Path.GetDirectoryName(Path.GetDirectoryName(target)) <> NormalizeRoot(root) Then Throw New IOException("移動先が直下ではありません。")
            Try
                File.Move(fromPath, target, overwrite:=False)
                Return True
            Catch ex As IOException When (ex.HResult And &HFFFF) = 80 OrElse (ex.HResult And &HFFFF) = 183
                Return False
            End Try
        End Function

        Private Shared Function IsSharing(ex As IOException) As Boolean
            Return (ex.HResult And &HFFFF) = 32 OrElse (ex.HResult And &HFFFF) = 33
        End Function

        Private Class ReadLease
            Implements IPeriodicReadLease
            Private ReadOnly _stream As FileStream
            Public ReadOnly Property Fingerprint As FileFingerprintDto Implements IPeriodicReadLease.Fingerprint
            Public ReadOnly Property FullPath As String Implements IPeriodicReadLease.FullPath
            Public Sub New(stream As FileStream, pathValue As String, fp As FileFingerprintDto)
                _stream = stream
                FullPath = pathValue
                Fingerprint = fp
            End Sub
            Public Sub Dispose() Implements IDisposable.Dispose
                _stream.Dispose()
            End Sub
        End Class

        <StructLayout(LayoutKind.Sequential, Pack:=4)>
        Private Structure HandleInfo
            Public Attributes As UInteger
            Public Creation As Long
            Public Access As Long
            Public Write As Long
            Public Volume As UInteger
            Public SizeHigh As UInteger
            Public SizeLow As UInteger
            Public Links As UInteger
            Public IndexHigh As UInteger
            Public IndexLow As UInteger
        End Structure

        <DllImport("kernel32.dll", SetLastError:=True)>
        Private Shared Function GetFileInformationByHandle(handle As SafeFileHandle, ByRef info As HandleInfo) As Boolean
        End Function

        Private Shared Function FileIdentity(handle As SafeFileHandle) As String
            Dim info As HandleInfo
            If Not GetFileInformationByHandle(handle, info) Then Return Nothing
            Return info.Volume.ToString("X8") & ":" & info.IndexHigh.ToString("X8") & info.IndexLow.ToString("X8")
        End Function
    End Class
End Namespace
