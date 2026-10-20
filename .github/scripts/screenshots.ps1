# Captures the README's screenshots from a published build, so they show the real UI
# rather than a mock-up. Run by screenshots.yml on a Windows runner; it also works on
# any Windows desktop, from an elevated prompt (the app requires administrator).
#
# It overwrites %LOCALAPPDATA%\QuickNetSwitcher\settings.json to put the app in a
# known state for each shot, and turns Windows Firewall logging on for every profile
# (then off again at the end) so the firewall shots have hits to show. Do not run it
# where either matters.
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

# Clicks the first button with this name (a link button's name is its text).
function Invoke-Button([IntPtr]$hwnd, [string]$name) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $isButton = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $isNamed = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $condition = New-Object System.Windows.Automation.AndCondition($isButton, $isNamed)
    $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $button) { throw "Button '$name' not found." }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# Logging on for every profile, and some traffic for it to record, so the hits panel
# and the log viewer show real lines. Windows writes the log in batches, so this
# waits for the file to have something in it.
function Start-FirewallLogging {
    & netsh advfirewall set allprofiles logging droppedconnections enable | Out-Null
    & netsh advfirewall set allprofiles logging allowedconnections enable | Out-Null
    $log = Join-Path $env:SystemRoot 'System32\LogFiles\Firewall\pfirewall.log'
    $deadline = (Get-Date).AddSeconds(90)
    do {
        foreach ($name in 'github.com', 'www.microsoft.com', 'nuget.org') {
            try { Resolve-DnsName $name -DnsOnly -ErrorAction Stop | Out-Null } catch { }
            try { Invoke-WebRequest "https://$name" -UseBasicParsing -TimeoutSec 10 | Out-Null } catch { }
        }
        Start-Sleep -Seconds 5
        $size = if (Test-Path $log) { (Get-Item $log).Length } else { 0 }
    } while ($size -lt 4096 -and (Get-Date) -lt $deadline)
    Write-Host "pfirewall.log: $size bytes"
}

function Stop-FirewallLogging {
    & netsh advfirewall set allprofiles logging droppedconnections disable | Out-Null
    & netsh advfirewall set allprofiles logging allowedconnections disable | Out-Null
}

# One screenshot: write the settings, start the app, optionally change tab and click
# a button, capture the window, stop the app.
function Save-Shot([string]$name, [bool]$simple, [bool]$dark, [string]$tab,
                   [string]$button = '', [int]$settle = 4) {
    $settings = [ordered]@{
        PinToDesktop        = $false
        MinimizeToTray      = $false
        SimpleView          = $simple
        HideDisconnected    = $false
        DarkMode            = $dark
        Accent              = 'Teal'
        ResolveOutsideNames = $false
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
        if ($button) {
            Invoke-Button $hwnd $button
            Start-Sleep -Seconds $settle
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
Save-Shot 'adapters-dark'    $true  $true  ''

Start-FirewallLogging
try {
    # The hits panel counts what the log holds from the last five minutes.
    Save-Shot 'firewall'      $true  $false 'Firewall'
    # The hits panel's link: UI Automation does not reach the View log links inside
    # the profile rows.
    Save-Shot 'firewall-log'  $true  $false 'Firewall' 'Open viewer' 6
    # Register now checks for up to 30 seconds before it writes its last line. A
    # runner adapter with DNS registration off has no Register now link; the log is
    # then shot as it is.
    try {
        Save-Shot 'activity-log' $false $false '' 'Register now' 40
    }
    catch {
        Write-Warning "Register now unavailable ($_); capturing the activity log as it is."
        Save-Shot 'activity-log' $false $false '' 'Log' 2
    }
}
finally {
    Stop-FirewallLogging
}

# The whole screen, for working out what went wrong when a shot comes back blank or
# covered. Not used by the README.
$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
Save-Region $screen.Left $screen.Top $screen.Width $screen.Height (Join-Path $outPath 'debug-desktop.png')
