Imports System.IO

Module MigrationProgram
    Function Main(args As String()) As Integer
        If args.Length <> 3 Then
            Console.Error.WriteLine("使用方法: DetailTaxMigration.exe <元DB> <新しいコピーDB> <新しい比較CSV>")
            Return 2
        End If
        Try
            MigrationFiles.MigrateCopy(args(0), args(1), args(2))
            Console.WriteLine("移行コピーと比較CSVを作成しました。旧Windows版ではコピーを開かないでください。")
            Console.WriteLine("税率の適用履歴は未設定です。アプリ切替と期間データ確認が完了するまで実運用には使用できません。")
            Return 0
        Catch ex As Exception
            Console.Error.WriteLine("移行失敗: " & ex.Message)
            Return 1
        End Try
    End Function
End Module
