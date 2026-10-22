Public Class TaxRate
    Inherits Master

    Public Property TaxTreatment As String = "taxable"

    Public ReadOnly Property DisplayName As String
        Get
            Return If(TaxTreatment = "out_of_scope", "対象外", Rate.ToString() & "%")
        End Get
    End Property

    Public ReadOnly Property IsTaxable As Boolean
        Get
            If TaxTreatment <> "taxable" AndAlso TaxTreatment <> "out_of_scope" Then Throw New InvalidOperationException("不正な税設定です。")
            Return TaxTreatment = "taxable"
        End Get
    End Property

    Public ReadOnly Property Rate As Integer
        Get
            Return Convert.ToInt32(Name)
        End Get
    End Property
End Class
