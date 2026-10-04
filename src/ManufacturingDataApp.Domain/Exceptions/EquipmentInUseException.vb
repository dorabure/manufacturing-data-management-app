Namespace Exceptions
    Public Class EquipmentInUseException
        Inherits InvalidOperationException
        Public Sub New()
            MyBase.New("この設備は測定データで使用されているため削除できません。")
        End Sub
    End Class
End Namespace
