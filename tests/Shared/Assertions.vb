Imports System.Collections.Generic

Module Assertions
    Private failures As Integer
    Private cases As Integer

    Public Sub Check(name As String, action As Action)
        cases += 1
        Try
            action()
            Console.WriteLine("PASS " & name)
        Catch ex As Exception
            failures += 1
            Console.Error.WriteLine("FAIL " & name & ": " & ex.ToString())
        End Try
    End Sub

    Public Sub Equal(Of T)(expected As T, actual As T, Optional message As String = "")
        If Not EqualityComparer(Of T).Default.Equals(expected, actual) Then
            Throw New Exception(message & " expected=" & Convert.ToString(expected) & ", actual=" & Convert.ToString(actual))
        End If
    End Sub

    Public Sub Throws(Of T As Exception)(action As Action)
        Try
            action()
        Catch ex As T
            Return
        End Try
        Throw New Exception("Expected " & GetType(T).Name)
    End Sub

    Public Function Finish() As Integer
        Console.WriteLine($"{cases - failures}/{cases} checks passed.")
        Return If(failures = 0, 0, 1)
    End Function
End Module
