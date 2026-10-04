Imports System.Diagnostics
Imports System.IO
Imports System.Threading
Imports ManufacturingDataApp.Application.DTOs
Imports ManufacturingDataApp.Application.Interfaces
Imports ManufacturingDataApp.Domain.Entities
Imports ManufacturingDataApp.Infrastructure.Csv
Imports ManufacturingDataApp.Infrastructure.PeriodicImport
Imports ManufacturingDataApp.Infrastructure.Repositories

Namespace TestSupport
    Friend Module Phase7Support
        Public Const Header7 As String = "日時,設備ID,設備名,項目,値,単位"
        Public Function Row7(index As Integer, Optional value As String = "10") As String
            Return $"{New DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(index):O},EQ001,設備,温度,{value},℃"
        End Function
        Public Function Write7(root As String, name As String, start As Integer, Optional count As Integer = 1, Optional value As String = "10") As String
            Dim target = Path.Combine(root, name)
            File.WriteAllLines(target, {Header7}.Concat(Enumerable.Range(start, count).Select(Function(i) Row7(i, value))))
            File.SetLastWriteTimeUtc(target, New DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(start))
            Return target
        End Function
    End Module

    Friend Class Phase7Probe
        Implements IPeriodicCsvImportExecutor, ICsvFileAdapter, IMeasurementDataRepository, IEquipmentRepository
        Private ReadOnly _inner As PeriodicCsvImportExecutor
        Private ReadOnly _data As MeasurementDataRepository
        Private ReadOnly _equipment As EquipmentRepository
        Private ReadOnly _adapter As New CsvHelperAdapter()
        Private _active As Integer
        Private _atomicStart As Long
        Private _readStart As Long
        Private _readEnd As Long
        Public ReadOnly Calls As New List(Of String)()
        Public MaximumActive As Integer
        Public ReadMs As Double
        Public ValidationMs As Double
        Public DbMs As Double
        Public AtomicMs As Double
        Public Before As Action
        Public BeforeDb As Action
        Public AfterDb As Action
        Public SearchFailure As Exception
        Public EquipmentFailure As Exception
        Public Sub New(db As Phase2Database)
            _data = New MeasurementDataRepository(db.Factory)
            _equipment = New EquipmentRepository(db.Factory)
            _inner = New PeriodicCsvImportExecutor(db.Factory, adapter:=Me, data:=Me, equipment:=Me)
        End Sub
        Public ReadOnly Property DatabaseIdentity As String Implements IPeriodicCsvImportExecutor.DatabaseIdentity
            Get
                Return _inner.DatabaseIdentity
            End Get
        End Property
        Public Function Execute(request As CsvImportRequestDto) As PeriodicFileResultDto Implements IPeriodicCsvImportExecutor.Execute
            Dim active = Interlocked.Increment(_active)
            MaximumActive = Math.Max(MaximumActive, active)
            Calls.Add(Path.GetFileName(request.FilePath))
            Before?.Invoke()
            _atomicStart = Stopwatch.GetTimestamp()
            Try
                Return _inner.Execute(request)
            Finally
                AtomicMs += Stopwatch.GetElapsedTime(_atomicStart).TotalMilliseconds
                Interlocked.Decrement(_active)
            End Try
        End Function
        Public Function Read(path As String, encoding As String, delimiter As String, header As Boolean) As IReadOnlyList(Of IReadOnlyDictionary(Of String, String)) Implements ICsvFileAdapter.Read
            _readStart = Stopwatch.GetTimestamp()
            Dim rows = _adapter.Read(path, encoding, delimiter, header)
            _readEnd = Stopwatch.GetTimestamp()
            ReadMs += Stopwatch.GetElapsedTime(_readStart, _readEnd).TotalMilliseconds
            Return rows
        End Function
        Public Sub Write(path As String, rows As IEnumerable(Of IReadOnlyDictionary(Of String, String)), encoding As String, delimiter As String, header As Boolean) Implements ICsvFileAdapter.Write
            Throw New NotSupportedException()
        End Sub
        Public Function Search(id As String, item As String, fromDate As DateTime?, toDate As DateTime?) As IReadOnlyList(Of MeasurementData) Implements IMeasurementDataRepository.Search
            If SearchFailure IsNot Nothing Then Throw SearchFailure
            Return _data.Search(id, item, fromDate, toDate)
        End Function
        Public Sub AddRange(rows As IEnumerable(Of MeasurementData)) Implements IMeasurementDataRepository.AddRange
            ValidationMs += Stopwatch.GetElapsedTime(_readEnd).TotalMilliseconds
            BeforeDb?.Invoke()
            Dim timer = Stopwatch.StartNew()
            Try
                _data.AddRange(rows)
            Finally
                DbMs += timer.Elapsed.TotalMilliseconds
            End Try
            AfterDb?.Invoke()
        End Sub
        Public Function EquipmentById(id As String) As EquipmentMaster Implements IEquipmentRepository.FindById
            If EquipmentFailure IsNot Nothing Then Throw EquipmentFailure
            Return _equipment.FindById(id)
        End Function
        Public Function GetAll() As IReadOnlyList(Of EquipmentMaster) Implements IEquipmentRepository.GetAll
            Return _equipment.GetAll()
        End Function
        Public Function FindById(id As Long) As MeasurementData Implements IMeasurementDataRepository.FindById
            Return _data.FindById(id)
        End Function
        Public Sub Add(row As MeasurementData) Implements IMeasurementDataRepository.Add
            _data.Add(row)
        End Sub
        Public Sub Update(row As MeasurementData) Implements IMeasurementDataRepository.Update
            _data.Update(row)
        End Sub
        Public Sub Delete(ids As IEnumerable(Of Long)) Implements IMeasurementDataRepository.Delete
            _data.Delete(ids)
        End Sub
        Public Function IsItemNameInUse(item As String) As Boolean Implements IMeasurementDataRepository.IsItemNameInUse
            Return _data.IsItemNameInUse(item)
        End Function
        Public Sub AddEquipment(row As EquipmentMaster) Implements IEquipmentRepository.Add
            Throw New NotSupportedException()
        End Sub
        Public Sub UpdateEquipment(row As EquipmentMaster) Implements IEquipmentRepository.Update
            Throw New NotSupportedException()
        End Sub
        Public Sub DeleteEquipment(id As String) Implements IEquipmentRepository.Delete
            Throw New NotSupportedException()
        End Sub
    End Class
End Namespace
