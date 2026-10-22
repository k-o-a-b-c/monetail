# CIの一時デスクトップを拡張し、相対200%のフォーム全体を描画できるようにする。
$ErrorActionPreference = 'Stop'
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class UiTestDisplay {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct Mode {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint Orientation, FixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel, Width, Height, DisplayFlags, Frequency;
        public uint ICMMethod, ICMIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string device, int index, ref Mode mode);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettings(ref Mode mode, uint flags);

    // 現在のモードを取得し、幅と高さだけを一時的に変更する。
    public static string Resize(int width, int height) {
        var mode = new Mode();
        mode.Size = (ushort)Marshal.SizeOf(typeof(Mode));
        if (!EnumDisplaySettings(null, -1, ref mode)) throw new Exception("Could not read current display mode.");
        if (mode.Width >= width && mode.Height >= height) return mode.Width + "x" + mode.Height;
        mode.Fields = 0x00080000 | 0x00100000;
        mode.Width = (uint)width;
        mode.Height = (uint)height;
        var result = ChangeDisplaySettings(ref mode, 0);
        if (result != 0) return "unsupported:" + result;
        mode.Size = (ushort)Marshal.SizeOf(typeof(Mode));
        if (!EnumDisplaySettings(null, -1, ref mode)) throw new Exception("Could not verify display mode.");
        return mode.Width + "x" + mode.Height;
    }
}
'@
foreach ($resolution in @(@(2560, 1600), @(2560, 1440), @(2048, 1536), @(1920, 1440))) {
    $result = [UiTestDisplay]::Resize($resolution[0], $resolution[1])
    if ($result -notlike 'unsupported:*') {
        Write-Host "UI test desktop: $result"
        exit 0
    }
    Write-Host "UI test desktop $($resolution[0])x$($resolution[1]): $result"
}
throw 'The CI display cannot fit the 200% receipt layout; do not count this visual acceptance as passed.'
