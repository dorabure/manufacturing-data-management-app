Imports ManufacturingDataApp.Application.DTOs

Namespace Interfaces
    Public Interface IPeriodicImportNotificationSink
        Sub Publish(notification As PeriodicImportNotificationDto)
    End Interface
End Namespace
