Public Class TaxRate
    Inherits Master

    ' 課税・対象外の区分を保持する。
    Public Property TaxTreatment As String = "taxable"

    ' 税率または対象外の表示名を返す。
    Public ReadOnly Property DisplayName As String
        Get
            Return If(TaxTreatment = "out_of_scope", "対象外", Rate.ToString() & "%")
        End Get
    End Property

    ' 課税種別を検証して課税対象かを返す。
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
