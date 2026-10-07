# Captures the README's screenshots from a published build, so they show the real UI
# rather than a mock-up. Run by screenshots.yml on a Windows runner; it also works on
# any Windows desktop, from an elevated prompt (the app requires administrator).
#
# It overwrites %LOCALAPPDATA%\QuickNetSwitcher\settings.json to put the app in a
# known state for each shot, so do not run it where those settings matter.
#
# Usage: screenshots.ps1 -Exe publish\QuickNetSwitcher.exe -OutDir screenshots
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct QnsRect { public int Left, Top, Right, Bottom; }

public static class QnsNative
{
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out QnsRect rect, int size);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out QnsRect rect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);
}
'@

$exePath = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outPath = (Resolve-Path $OutDir).Path
$settingsDir = Join-Path $env:LOCALAPPDATA 'QuickNetSwitcher'
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null

function Save-Region([int]$left, [int]$top, [int]$width, [int]$height, [string]$file) {
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($left, $top, 0, 0, $bitmap.Size)
        $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

function Get-WindowBounds([IntPtr]$hwnd) {
    $rect = New-Object QnsRect
    # DWMWA_EXTENDED_FRAME_BOUNDS (9) leaves out the invisible resize border that
    # GetWindowRect includes; fall back when composition is off.
    $size = [System.Runtime.InteropServices.Marshal]::SizeOf([type][QnsRect])
    if ([QnsNative]::DwmGetWindowAttribute($hwnd, 9, [ref]$rect, $size) -ne 0) {
        [void][QnsNative]::GetWindowRect($hwnd, [ref]$rect)
    }
    return $rect
}

function Select-Tab([IntPtr]$hwnd, [string]$name) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $isTab = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)
    $isNamed = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $condition = New-Object System.Windows.Automation.AndCondition($isTab, $isNamed)
    $tab = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $tab) { throw "Tab '$name' not found." }
    $pattern = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
}

# One screenshot: write the settings, start the app, optionally change tab, capture
# the window, stop the app.
function Save-Shot([string]$name, [bool]$simple, [bool]$dark, [string]$tab) {
    $settings = [ordered]@{
        PinToDesktop     = $false
        MinimizeToTray   = $false
        SimpleView       = $simple
        HideDisconnected = $false
        DarkMode         = $dark
        Accent           = 'Teal'
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $settingsDir 'settings.json'), ($settings | ConvertTo-Json))

    $process = Start-Process -FilePath $exePath -PassThru
    try {
        $deadline = (Get-Date).AddSeconds(45)
        while ($true) {
            if ($process.HasExited) { throw "The app exited with code $($process.ExitCode) before showing a window." }
            $process.Refresh()
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) { break }
            if ((Get-Date) -gt $deadline) { throw 'The app showed no window within 45 seconds.' }
            Start-Sleep -Milliseconds 500
        }
        $hwnd = $process.MainWindowHandle

        # The adapter list is read from WMI after the window appears.
        Start-Sleep -Seconds 6
        if ($tab) {
            Select-Tab $hwnd $tab
            Start-Sleep -Seconds 4
        }

        [void][QnsNative]::SetForegroundWindow($hwnd)
        Start-Sleep -Milliseconds 800

        $rect = Get-WindowBounds $hwnd
        $width = $rect.Right - $rect.Left
        $height = $rect.Bottom - $rect.Top
        if ($width -lt 300 -or $height -lt 100) { throw "Window bounds look wrong: ${width}x${height}." }

        $file = Join-Path $outPath "$name.png"
        Save-Region $rect.Left $rect.Top $width $height $file
        Write-Host "$name.png ${width}x${height}"
    }
    finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit(10000) | Out-Null
        }
    }
}

Save-Shot 'adapters'         $true  $false ''
Save-Shot 'adapters-details' $false $false ''
Save-Shot 'route-table'      $true  $false 'Route Table'
Save-Shot 'firewall'         $true  $false 'Firewall'
Save-Shot 'adapters-dark'    $true  $true  ''

# The whole screen, for working out what went wrong when a shot comes back blank or
# covered. Not used by the README.
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Save-Region $screen.Left $screen.Top $screen.Width $screen.Height (Join-Path $outPath 'debug-desktop.png')
