Public Class TaxRate
    Inherits Master

    Public ReadOnly Property Rate As Integer
        Get
            Return Convert.ToInt32(Name)
        End Get
    End Property
End Class
