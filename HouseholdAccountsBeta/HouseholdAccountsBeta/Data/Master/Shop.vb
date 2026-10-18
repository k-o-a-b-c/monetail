Public Class Shop
    Inherits Master
    Public Property DefaultPointCode As Integer
    Public Property DefaultGroupClassificationCode As Integer

    Private _taxRoundingMode As String = "floor"

    Public Property TaxRoundingMode As String
        Get
            Return _taxRoundingMode
        End Get
        Set(ByVal value As String)
            Calculation.ValidateTaxRoundingMode(value)
            _taxRoundingMode = value
        End Set
    End Property
End Class
