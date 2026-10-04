Imports Xunit
Imports ManufacturingDataApp.Application.Validators
Imports ManufacturingDataApp.Domain

Public Class ValidationServiceTests
    <Fact>
    Public Sub ValidateRequired_ReturnsErrorForBlank()
        Dim sut = New ValidationService()
        Dim result = sut.ValidateRequired("", StandardFields.EquipmentId, 2)
        Assert.NotNull(result)
        Assert.Equal(ErrorTypes.Required, result.ErrorType)
    End Sub

    <Fact>
    Public Sub ValidateDouble_ReturnsErrorForNonNumeric()
        Dim sut = New ValidationService()
        Dim result = sut.ValidateDouble("abc", StandardFields.Value, 2)
        Assert.NotNull(result)
        Assert.Equal(ErrorTypes.Type, result.ErrorType)
    End Sub
End Class
