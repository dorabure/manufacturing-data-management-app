Imports Xunit
Imports ManufacturingDataApp.Domain

Public Class StandardFieldsTests
    <Fact>
    Public Sub StandardFieldOrder_IsDefined()
        Assert.Equal("取得日時", StandardFields.AcquiredAt)
        Assert.Equal("設備ID", StandardFields.EquipmentId)
        Assert.Equal("設備名", StandardFields.EquipmentName)
        Assert.Equal("項目名", StandardFields.ItemName)
        Assert.Equal("測定値", StandardFields.Value)
        Assert.Equal("単位", StandardFields.Unit)
    End Sub
End Class
