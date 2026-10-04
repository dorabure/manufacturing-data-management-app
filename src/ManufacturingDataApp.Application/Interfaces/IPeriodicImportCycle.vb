Imports System.Threading
Imports System.Threading.Tasks
Imports ManufacturingDataApp.Application.DTOs

Namespace Interfaces
    ' Phase 1 boundary: no knowledge of files, databases, or UI.
    Public Interface IPeriodicImportCycle
        Function PrepareAsync(optionsSnapshot As PeriodicImportOptionsDto, cancellationToken As CancellationToken) As Task
        ' Only file boundaries observe this token; accepted CSV work must finish.
        Function ExecuteAsync(stopBoundaryToken As CancellationToken) As Task
        Function CleanupAsync() As Task
    End Interface
End Namespace
