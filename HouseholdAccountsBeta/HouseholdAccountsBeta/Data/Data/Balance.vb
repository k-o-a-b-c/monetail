Public Class Balance

    Public Property Seq As Integer
    Friend Property RevisionToken As String
    Public Property [Date] As DateTime
    Public Property Tag As String

    Public Property Groups As List(Of BalanceGroup)
    Public Sub New()
        Groups = New List(Of BalanceGroup)()
    End Sub

    Public ReadOnly Property Total(ByVal user As User) As Integer
        Get
            Dim balance As Integer
            For Each group As BalanceGroup In Groups.Where(Function(g) user.Code = 0 OrElse g.PaymentClassification.User.Code = user.Code)
                balance += group.Balance
            Next
            Return Convert.ToInt32(balance)
        End Get
    End Property

End Class
