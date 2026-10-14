Imports System.ComponentModel
Imports System.Text.RegularExpressions

Public Class OriginalDateTimeTextBox
    Inherits OriginalTextBox

    <DefaultValue(DateTimeMode.Date)>
    Public Property Mode As DateTimeMode

    Private _Value As DateTime
    Public Property Value As DateTime
        Get
            Return _Value
        End Get
        Set(value As DateTime)
            _Value = value
            RaiseEvent ValueChanged(Me, New EventArgs())
        End Set
    End Property

    Private Property ValueEnabled As Boolean

    Public Event ValueChanged(sender As Object, e As EventArgs)

    Public Sub New()
        InitializeComponent()
    End Sub

    Private Sub OriginalDateTimeTextBox_Validated(sender As Object, e As EventArgs) Handles Me.Validated
        If Mode = DateTimeMode.Date Then
            Value = StringToDateValue(Me.Text)
        ElseIf Mode = DateTimeMode.Time Then
            Value = StringToTimeValue(Me.Text)
        End If
    End Sub

    Public Shared Function StringToDateValue(ByVal str As String) As DateTime

        Dim inputStrings() As String = Regex.Split(str, "[^\d]+").Where(Function(s) Not String.IsNullOrEmpty(s)).Take(3).ToArray()
        Dim dateStrings(2) As String

        '入力された文字列を年月日に分解する
        Select Case inputStrings.Length
            Case 1
                Select Case inputStrings(0).Length
                    Case 8  'yyyymmdd
                        dateStrings(0) = inputStrings(0).Substring(0, 4)
                        inputStrings(0) = inputStrings(0).Substring(4, 4)
                    Case 6  'yymmdd
                        dateStrings(0) = inputStrings(0).Substring(0, 2)
                        inputStrings(0) = inputStrings(0).Substring(2, 4)
                End Select
                Select Case inputStrings(0).Length
                    Case 4  'mmdd
                        dateStrings(1) = inputStrings(0).Substring(0, 2)
                        dateStrings(2) = inputStrings(0).Substring(2, 2)
                    Case 3  'mdd
                        dateStrings(1) = inputStrings(0).Substring(0, 1)
                        dateStrings(2) = inputStrings(0).Substring(1, 2)
                    Case 2  'dd
                        dateStrings(2) = inputStrings(0)
                    Case 1  'd
                        dateStrings(2) = inputStrings(0)
                End Select
            Case 2  'mm/dd
                dateStrings(1) = inputStrings(0)
                dateStrings(2) = inputStrings(1)
            Case 3  'yyyy/mm/dd
                dateStrings = inputStrings
        End Select

        '年を補完する
        Dim isComplement As Boolean = dateStrings(0) Is Nothing
        If isComplement Then
            dateStrings(0) = Date.Today.Year
        End If

        '年を補完する2
        Dim isComplementCentury As Boolean = dateStrings(0).Length < 4
        If isComplementCentury Then
            If Integer.TryParse(dateStrings(0), Nothing) Then
                dateStrings(0) = Date.Today.Year \ 100 * 100 + Integer.Parse(dateStrings(0))
            End If
        End If

        '月を補完する
        Dim isMonthComplement As Boolean = dateStrings(1) Is Nothing
        If isMonthComplement Then
            dateStrings(1) = Date.Today.Month
        End If

        '年月日をDateTimeに変換する
        Dim parsedDate As DateTime
        If Not DateTime.TryParse(String.Join("/", dateStrings), parsedDate) Then
            Dim Month As Integer
            If Integer.TryParse(dateStrings(1), Month) Then
                dateStrings(1) = Month - 1
                If Not DateTime.TryParse(String.Join("/", dateStrings), parsedDate) Then
                    parsedDate = DateTime.Today
                End If
            Else
                parsedDate = DateTime.Today
            End If
        End If

        If isMonthComplement AndAlso parsedDate > DateTime.Today Then
            parsedDate = parsedDate.AddMonths(-1)
        ElseIf isMonthComplement AndAlso parsedDate > DateTime.Today Then
            parsedDate = parsedDate.AddYears(-100)
        ElseIf isComplement AndAlso parsedDate > DateTime.Today Then
            parsedDate = parsedDate.AddYears(-1)
        End If

        Return parsedDate

    End Function

    Public Shared Function StringToTimeValue(ByVal str As String) As DateTime

        Dim inputStrings() As String = Regex.Split(str, "[^\d]+").Where(Function(s) Not String.IsNullOrEmpty(s)).Take(3).ToArray()
        Dim timeStrings(2) As String

        '入力された文字列を時分秒に分解する
        Select Case inputStrings.Length
            Case 1
                Select Case inputStrings(0).Length
                    Case 6  'hhmmss
                        timeStrings(0) = inputStrings(0).Substring(0, 2)
                        timeStrings(1) = inputStrings(0).Substring(2, 2)
                        timeStrings(2) = inputStrings(0).Substring(4, 2)
                    Case 5  'hmmss
                        timeStrings(0) = inputStrings(0).Substring(0, 1)
                        timeStrings(1) = inputStrings(0).Substring(1, 2)
                        timeStrings(2) = inputStrings(0).Substring(3, 2)
                    Case 4  'hhmm
                        timeStrings(0) = inputStrings(0).Substring(0, 2)
                        timeStrings(1) = inputStrings(0).Substring(2, 2)
                    Case 3  'hmm
                        timeStrings(0) = inputStrings(0).Substring(0, 1)
                        timeStrings(1) = inputStrings(0).Substring(1, 2)
                    Case 1, 2   'hh
                        timeStrings(0) = inputStrings(0)
                End Select
            Case 2  'hh:mm
                timeStrings(0) = inputStrings(0)
                timeStrings(1) = inputStrings(1)
            Case 3  'hh:mm:ss
                timeStrings = inputStrings
        End Select

        '分を補完する
        If timeStrings(1) Is Nothing Then
            timeStrings(1) = "0"
        End If

        '秒を補完する
        If timeStrings(2) Is Nothing Then
            timeStrings(2) = "0"
        End If

        '時分秒をDateTimeに変換する
        Dim parsedTime As DateTime
        If DateTime.TryParse(String.Join(":", timeStrings), parsedTime) Then
        Else
            parsedTime = DateTime.Today
        End If

        Return parsedTime

    End Function

    Private Sub OriginalDateTimeTextBox_ValueChanged(sender As Object, e As EventArgs) Handles Me.ValueChanged
        Select Case Mode
            Case DateTimeMode.Date
                Me.Text = Value.ToShortDateString()
            Case DateTimeMode.Time
                Me.Text = Value.ToLongTimeString()
        End Select
    End Sub

    Private Sub InitializeComponent()
        Me.SuspendLayout()
        '
        'OriginalDateTimeTextBox
        '
        Me.ImeMode = System.Windows.Forms.ImeMode.Off
        Me.Value = DateTime.Now
        Me.ResumeLayout(False)

    End Sub
End Class

Public Enum DateTimeMode As Integer
    [Date]
    Time
End Enum
