Public Class OriginalCheckBox
    Inherits CheckBox

    Private Sub OriginalCheckBox_KeyDown(sender As Object, e As KeyEventArgs) Handles Me.KeyDown
        If e.KeyCode = Keys.Enter Then
            SendKeys.Send("{TAB}")
            e.SuppressKeyPress = True
        End If
    End Sub

End Class
