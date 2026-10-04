Namespace Exceptions
    Public Class MeasurementItemInUseException
        Inherits InvalidOperationException
        Public Sub New()
            MyBase.New("この測定項目は測定データで使用されているため削除できません。")
        End Sub
    End Class
End Namespace
