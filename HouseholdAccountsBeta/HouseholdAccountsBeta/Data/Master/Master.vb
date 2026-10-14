Public Class Master
    Public Property Code As Integer
    Public Property Name As String

    Public Overrides Function ToString() As String
        Return Name
    End Function

End Class
