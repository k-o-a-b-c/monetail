"""Regression tests for the new-violation-only WinUI guard."""

import unittest

from check_winui_structure import changed_lines, mainwindow_findings, new_line_findings


XAML_ROOT = '<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">{content}</Window>'


def diff(path: str, text: str) -> str:
    return f'diff --git a/{path} b/{path}\n+++ b/{path}\n@@ -0,0 +1 @@\n+{text}\n'


class DiffTests(unittest.TestCase):
    def test_line_numbers_from_multiple_hunks(self):
        patch = ('+++ b/src/Monetail.WinUI/MainWindow.Example.cs\n'
                 '@@ -2,0 +3,2 @@\n+foo\n+bar\n'
                 '@@ -10,1 +11,1 @@\n-old\n+new\n')
        self.assertEqual([3, 4, 11], [line for _, line, _ in changed_lines(patch)])

    def test_new_showpage_call_is_forbidden_but_other_views_are_allowed(self):
        old = diff('src/Monetail.WinUI/MainWindow.Example.cs', 'ShowPage(HomePage);')
        self.assertEqual(1, len(new_line_findings(old)))
        view = diff('src/Monetail.WinUI/Views/Example.cs', 'ShowPage(HomePage);')
        self.assertEqual([], new_line_findings(view))

    def test_wpf_and_uwp_are_rejected_but_system_windows_input_is_allowed(self):
        for text in ('using Windows.UI.Xaml;', 'using System.Windows;', 'xmlns:old="using:Windows.UI.Xaml"'):
            with self.subTest(text=text):
                self.assertEqual(1, len(new_line_findings(diff('src/Monetail.WinUI/Views/Example.xaml', text))))
        self.assertEqual([], new_line_findings(diff('src/Monetail.WinUI/Views/Example.cs', 'using System.Windows.Input;')))

    def test_internal_visibility_and_comments_are_not_errors(self):
        xaml = diff('src/Monetail.WinUI/Views/Example.xaml', '<Grid Visibility="Collapsed"/>')
        comment = diff('src/Monetail.WinUI/MainWindow.cs', '// ShowPage(HomePage);')
        self.assertEqual([], new_line_findings(xaml))
        self.assertEqual([], new_line_findings(comment))


class MainWindowTests(unittest.TestCase):
    def test_new_screen_panel_is_rejected_even_when_attributes_are_multiline(self):
        old = XAML_ROOT.format(content='<Grid x:Name="LoginPage" Visibility="Collapsed"/>')
        new = XAML_ROOT.format(content='<Grid x:Name="LoginPage" Visibility="Collapsed"/><Grid\nx:Name="TransferPage" Visibility="Collapsed"/>')
        issues = mainwindow_findings(old, new)
        self.assertEqual(1, len(issues))
        self.assertIn('TransferPage', issues[0].message)

    def test_new_dialog_is_rejected_but_existing_dialog_can_be_edited(self):
        old = XAML_ROOT.format(content='<ContentDialog x:Name="OldDialog"/>')
        self.assertEqual([], mainwindow_findings(old, old))
        new = XAML_ROOT.format(content='<ContentDialog x:Name="OldDialog"/><ContentDialog x:Name="Other"/>')
        self.assertEqual(1, len(mainwindow_findings(old, new)))

    def test_legitimate_content_frame_and_internal_visibility_are_allowed(self):
        old = XAML_ROOT.format(content='<Grid x:Name="LoginPage"/>')
        new = XAML_ROOT.format(content='<Grid x:Name="LoginPage"/><Frame x:Name="ContentFrame"/><Grid x:Name="LoadingPanel" Visibility="Collapsed"/>')
        self.assertEqual([], mainwindow_findings(old, new))

    def test_removed_old_screen_does_not_fail(self):
        old = XAML_ROOT.format(content='<Grid x:Name="LoginPage"/>')
        new = XAML_ROOT.format(content='<Frame x:Name="ContentFrame"/>')
        self.assertEqual([], mainwindow_findings(old, new))


if __name__ == '__main__':
    unittest.main()
