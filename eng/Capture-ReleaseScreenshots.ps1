#Requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Package,

    [Parameter(Mandatory)]
    [string] $FixtureExecutable,

    [string] $OutputDirectory = 'docs/images'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'Release screenshot capture currently requires an interactive Windows desktop.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Release screenshots must be captured from an ordinary-user run.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$comparison = [StringComparison]::OrdinalIgnoreCase
$workspacePrefix = $repositoryRoot.TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

function Resolve-WorkspaceFile {
    param([Parameter(Mandatory)][string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($workspacePrefix, $comparison) -or
        -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Screenshot input must be an existing workspace file: $fullPath"
    }

    return $fullPath
}

function Resolve-WorkspaceDirectory {
    param([Parameter(Mandatory)][string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($workspacePrefix, $comparison)) {
        throw "Screenshot output must remain inside the repository: $fullPath"
    }

    [IO.Directory]::CreateDirectory($fullPath) | Out-Null
    $directory = [IO.DirectoryInfo]::new($fullPath)
    if (($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $null -ne $directory.LinkTarget) {
        throw 'A linked or reparse output directory cannot hold release screenshots.'
    }

    return $fullPath
}

function Wait-Until {
    param(
        [Parameter(Mandatory)][scriptblock] $Condition,
        [Parameter(Mandatory)][string] $Failure,
        [int] $Seconds = 15
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        if (& $Condition) {
            return
        }
        [Threading.Tasks.Task]::Delay(150).GetAwaiter().GetResult()
    } while ([DateTime]::UtcNow -lt $deadline)

    throw $Failure
}

function Find-ByAutomationId {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)][string] $AutomationId
    )

    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    return $Root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-Button {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)][string[]] $Names
    )

    foreach ($name in $Names) {
        $condition = [Windows.Automation.AndCondition]::new(
            [Windows.Automation.PropertyCondition]::new(
                [Windows.Automation.AutomationElement]::NameProperty,
                $name),
            [Windows.Automation.PropertyCondition]::new(
                [Windows.Automation.AutomationElement]::ControlTypeProperty,
                [Windows.Automation.ControlType]::Button))
        $button = $Root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $button) {
            return $button
        }
    }

    return $null
}

function Invoke-Button {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)][string[]] $Names
    )

    $button = Find-Button -Root $Root -Names $Names
    if ($null -eq $button) {
        throw "The expected UI button was not found: $($Names -join ', ')"
    }
    $pattern = $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Move-VerticalScrollToPercent {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)]
        [ValidateRange(0, 100)]
        [double] $Percent
    )

    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.ScrollPattern]::VerticallyScrollableProperty,
        $true)
    $scrollables = $Root.FindAll([Windows.Automation.TreeScope]::Descendants, $condition)
    $scrollable = $null
    $largestArea = -1.0
    for ($index = 0; $index -lt $scrollables.Count; $index++) {
        $candidate = $scrollables[$index]
        $bounds = $candidate.Current.BoundingRectangle
        $area = $bounds.Width * $bounds.Height
        Write-Verbose "Scrollable candidate ${index}: id='$($candidate.Current.AutomationId)', type='$($candidate.Current.ControlType.ProgrammaticName)', bounds='$bounds', area=$area."
        if ($area -gt $largestArea) {
            $scrollable = $candidate
            $largestArea = $area
        }
    }
    if ($null -eq $scrollable) {
        return
    }

    $scroll = $scrollable.GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern)
    $scroll.SetScrollPercent(
        [Windows.Automation.ScrollPattern]::NoScroll,
        $Percent)
    [Threading.Tasks.Task]::Delay(500).GetAwaiter().GetResult()
}

function Select-EnglishLanguage {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)][int] $ProcessId
    )

    Invoke-Button -Root $Root -Names @('Settings', 'Einstellungen')
    Wait-Until -Failure 'The language selector did not appear.' -Condition {
        $null -ne (Find-ByAutomationId -Root $Root -AutomationId 'LanguageSelector')
    }
    $selector = Find-ByAutomationId -Root $Root -AutomationId 'LanguageSelector'
    $expand = $selector.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    $english = $null
    Wait-Until -Failure 'The English language option did not appear.' -Condition {
        $condition = [Windows.Automation.AndCondition]::new(
            [Windows.Automation.PropertyCondition]::new(
                [Windows.Automation.AutomationElement]::NameProperty,
                'English'),
            [Windows.Automation.PropertyCondition]::new(
                [Windows.Automation.AutomationElement]::ControlTypeProperty,
                [Windows.Automation.ControlType]::ListItem),
            [Windows.Automation.PropertyCondition]::new(
                [Windows.Automation.AutomationElement]::ProcessIdProperty,
                $ProcessId))
        $script:english = [Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [Windows.Automation.TreeScope]::Descendants,
            $condition)
        $null -ne $script:english
    }
    $selection = $script:english.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)
    $selection.Select()
    Wait-Until -Failure 'The desktop did not switch to English.' -Condition {
        $null -ne (Find-Button -Root $Root -Names @('Settings'))
    }
}

function Set-InventorySearch {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)][string] $Value
    )

    Wait-Until -Failure 'The inventory search field did not appear.' -Condition {
        $null -ne (Find-ByAutomationId -Root $Root -AutomationId 'InventorySearch')
    }
    $search = Find-ByAutomationId -Root $Root -AutomationId 'InventorySearch'
    $valuePattern = $search.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern)
    $valuePattern.SetValue($Value)
}

function Get-VisibleInventoryRows {
    param([Parameter(Mandatory)] $Root)

    $gridCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::ControlTypeProperty,
        [Windows.Automation.ControlType]::DataGrid)
    $grid = $Root.FindFirst([Windows.Automation.TreeScope]::Descendants, $gridCondition)
    if ($null -eq $grid) {
        return @()
    }
    $rowCondition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::ControlTypeProperty,
        [Windows.Automation.ControlType]::DataItem)
    $rows = $grid.FindAll([Windows.Automation.TreeScope]::Children, $rowCondition)
    $result = @()
    foreach ($row in $rows) {
        $texts = $row.FindAll(
            [Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new(
                [Windows.Automation.AutomationElement]::ControlTypeProperty,
                [Windows.Automation.ControlType]::Text))
        $values = foreach ($text in $texts) {
            if (-not [string]::IsNullOrWhiteSpace($text.Current.Name)) {
                $text.Current.Name
            }
        }
        $result += ($values -join ' | ')
    }

    return $result
}

function Assert-ControlledRows {
    param(
        [Parameter(Mandatory)][string[]] $Rows,
        [Parameter(Mandatory)][int] $MinimumRows,
        [Parameter(Mandatory)][string] $View
    )

    if ($Rows.Count -lt $MinimumRows) {
        throw "$View displayed only $($Rows.Count) controlled rows; at least $MinimumRows were expected."
    }
    foreach ($row in $Rows) {
        if (-not $row.Contains('SocketScopeFixture', [StringComparison]::Ordinal)) {
            throw "$View contains a row that does not belong to the controlled fixture."
        }
        $addresses = [regex]::Matches($row, '(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?!\d)')
        foreach ($address in $addresses) {
            if ($address.Value -notin @('0.0.0.0', '127.0.0.1')) {
                throw "$View contains an address outside the controlled loopback/wildcard set."
            }
        }
    }
}

function Assert-PrivacySafeTree {
    param([Parameter(Mandatory)] $Root)

    $windowBounds = $Root.Current.BoundingRectangle
    $all = $Root.FindAll(
        [Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.Condition]::TrueCondition)
    $visibleEntries = [Collections.Generic.List[object]]::new()
    foreach ($element in $all) {
        $bounds = $element.Current.BoundingRectangle
        $intersectsWindow =
            $bounds.Width -gt 0 -and
            $bounds.Height -gt 0 -and
            $bounds.Left -lt ($windowBounds.Left + $windowBounds.Width) -and
            ($bounds.Left + $bounds.Width) -gt $windowBounds.Left -and
            $bounds.Top -lt ($windowBounds.Top + $windowBounds.Height) -and
            ($bounds.Top + $bounds.Height) -gt $windowBounds.Top
        if ($element.Current.IsOffscreen -or -not $intersectsWindow) {
            continue
        }
        if (-not [string]::IsNullOrWhiteSpace($element.Current.Name)) {
            $visibleEntries.Add([pscustomobject]@{
                Text = $element.Current.Name
                Property = 'Name'
                AutomationId = $element.Current.AutomationId
                Bounds = $bounds.ToString()
            })
        }
        $valuePattern = $null
        if ($element.TryGetCurrentPattern(
                [Windows.Automation.ValuePattern]::Pattern,
            [ref] $valuePattern) -and
            -not [string]::IsNullOrWhiteSpace($valuePattern.Current.Value)) {
            $visibleEntries.Add([pscustomobject]@{
                Text = $valuePattern.Current.Value
                Property = 'Value'
                AutomationId = $element.Current.AutomationId
                Bounds = $bounds.ToString()
            })
        }
    }
    $forbidden = @(
        [pscustomobject]@{ Label = 'Windows user name'; Value = [Environment]::UserName }
        [pscustomobject]@{ Label = 'machine name'; Value = [Environment]::MachineName }
        [pscustomobject]@{ Label = 'Windows home-path prefix'; Value = 'C:\Users\' }
        [pscustomobject]@{ Label = 'Linux home-path prefix'; Value = '/home/' }
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Value) }
    foreach ($entry in $forbidden) {
        $match = $visibleEntries | Where-Object {
            $_.Text.Contains($entry.Value, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1
        if ($null -ne $match) {
            throw "The visible screenshot tree contains a forbidden $($entry.Label) in $($match.Property) for '$($match.AutomationId)' at $($match.Bounds)."
        }
    }
}

function Save-WindowScreenshot {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)][IntPtr] $WindowHandle,
        [Parameter(Mandatory)][string] $Path
    )

    [ScreenshotNativeMethods]::SetForegroundWindow($WindowHandle) | Out-Null
    [Threading.Tasks.Task]::Delay(250).GetAwaiter().GetResult()
    $bounds = $Root.Current.BoundingRectangle
    $width = [Math]::Ceiling($bounds.Width)
    $height = [Math]::Ceiling($bounds.Height)
    if ($width -lt 760 -or $height -lt 520) {
        throw "The release window is too small for a readable screenshot: ${width}x${height}."
    }

    $target = [IO.Path]::GetFullPath($Path)
    if (-not $target.StartsWith($workspacePrefix, $comparison) -or
        -not [IO.Path]::GetDirectoryName($target).Equals($outputPath, $comparison)) {
        throw 'A screenshot target escaped the reviewed output directory.'
    }
    if ([IO.File]::Exists($target)) {
        [IO.File]::Delete($target)
    }

    $bitmap = [Drawing.Bitmap]::new(
        $width,
        $height,
        [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Black)
            $deviceContext = $graphics.GetHdc()
            try {
                $captured = [ScreenshotNativeMethods]::PrintWindow(
                    $WindowHandle,
                    $deviceContext,
                    [ScreenshotNativeMethods]::PwRenderFullContent)
            } finally {
                $graphics.ReleaseHdc($deviceContext)
            }
            if (-not $captured) {
                throw 'Windows could not render the SocketScope window into the reviewed screenshot buffer.'
            }
        } finally {
            $graphics.Dispose()
        }
        $bitmap.Save($target, [Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $bitmap.Dispose()
    }

    $file = [IO.FileInfo]::new($target)
    if (-not $file.Exists -or $file.Length -lt 50KB) {
        throw 'The captured screenshot is missing or unexpectedly small.'
    }

    return [pscustomobject]@{
        Name = $file.Name
        Width = $width
        Height = $height
        Bytes = $file.Length
        Sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Set-WindowGeometry {
    param(
        [Parameter(Mandatory)][IntPtr] $WindowHandle,
        [Parameter(Mandatory)][int] $X,
        [Parameter(Mandatory)][int] $Y,
        [Parameter(Mandatory)][int] $Width,
        [Parameter(Mandatory)][int] $Height
    )

    [ScreenshotNativeMethods]::ShowWindow($WindowHandle, [ScreenshotNativeMethods]::SwRestore) | Out-Null
    $positioned = [ScreenshotNativeMethods]::SetWindowPos(
        $WindowHandle,
        [IntPtr]::Zero,
        $X,
        $Y,
        $Width,
        $Height,
        [ScreenshotNativeMethods]::SwpNoZOrder -bor [ScreenshotNativeMethods]::SwpShowWindow)
    if (-not $positioned) {
        throw 'The release window could not be positioned for controlled capture.'
    }

    [Threading.Tasks.Task]::Delay(750).GetAwaiter().GetResult()
}

$packagePath = Resolve-WorkspaceFile $Package
$fixturePath = Resolve-WorkspaceFile $FixtureExecutable
if ([IO.Path]::GetFileName($packagePath) -notmatch '^OeXYZ-SocketScope-v\d+\.\d+\.\d+-win-(?:x64|arm64)\.exe$') {
    throw 'The screenshot package is not a named Windows release executable.'
}
if ([IO.Path]::GetFileName($fixturePath) -ne 'SocketScopeFixture.exe') {
    throw 'The screenshot fixture must be the reviewed SocketScopeFixture executable.'
}
$outputPath = Resolve-WorkspaceDirectory $OutputDirectory
$testRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot ".cache/screenshot-capture/$([Guid]::NewGuid().ToString('N'))"))
if (-not $testRoot.StartsWith($workspacePrefix, $comparison)) {
    throw 'The screenshot capture directory escaped the repository.'
}
[IO.Directory]::CreateDirectory($testRoot) | Out-Null

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ScreenshotNativeMethods
{
    public const int SwRestore = 9;
    public const uint PwRenderFullContent = 0x00000002;
    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpShowWindow = 0x0040;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);
}
'@

$fixture = $null
$application = $null
$substDrive = $null
$userPathBefore = [Environment]::GetEnvironmentVariable('Path', [EnvironmentVariableTarget]::User)
try {
    $substExecutable = Join-Path $env:SystemRoot 'System32\subst.exe'
    if (-not (Test-Path -LiteralPath $substExecutable -PathType Leaf)) {
        throw 'The Windows subst utility required for a sanitized screenshot path is unavailable.'
    }
    $substDrive = @('S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z') |
        Where-Object { -not (Test-Path -LiteralPath "${_}:\") } |
        Select-Object -First 1
    if ($null -eq $substDrive) {
        throw 'No temporary drive letter is available for sanitized release screenshots.'
    }
    & $substExecutable "${substDrive}:" $testRoot
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath "${substDrive}:\" -PathType Container)) {
        throw 'The sanitized screenshot data mapping could not be created.'
    }

    $fixtureStart = [Diagnostics.ProcessStartInfo]::new()
    $fixtureStart.FileName = $fixturePath
    $fixtureStart.UseShellExecute = $false
    $fixtureStart.CreateNoWindow = $true
    $fixtureStart.RedirectStandardOutput = $true
    $fixtureStart.RedirectStandardError = $true
    $fixtureStart.WorkingDirectory = [IO.Path]::GetDirectoryName($fixturePath)
    $fixtureStart.Environment['DOTNET_ROOT'] = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.tools/dotnet'))
    $fixture = [Diagnostics.Process]::Start($fixtureStart)
    if ($null -eq $fixture) {
        throw 'The controlled socket fixture could not start.'
    }
    $fixtureLine = $fixture.StandardOutput.ReadLineAsync().WaitAsync(
        [TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
    $fixtureEvidence = $fixtureLine | ConvertFrom-Json
    if ($fixtureEvidence.processName -ne 'SocketScopeFixture' -or
        $fixtureEvidence.processId -ne $fixture.Id) {
        throw 'The controlled socket fixture returned invalid identity evidence.'
    }

    $appStart = [Diagnostics.ProcessStartInfo]::new()
    $appStart.FileName = $packagePath
    $appStart.UseShellExecute = $false
    $appStart.WorkingDirectory = $testRoot
    $appStart.Environment['SOCKETSCOPE_DATA_ROOT'] = "${substDrive}:\data"
    $application = [Diagnostics.Process]::Start($appStart)
    if ($null -eq $application) {
        throw 'The release application could not start.'
    }
    Wait-Until -Failure 'The release application did not create a main window.' -Condition {
        $application.Refresh()
        $application.MainWindowHandle -ne [IntPtr]::Zero
    }
    [Threading.Tasks.Task]::Delay(2500).GetAwaiter().GetResult()
    $workingArea = [Windows.Forms.Screen]::FromHandle($application.MainWindowHandle).WorkingArea
    $wideWidth = [Math]::Min(1700, $workingArea.Width - 48)
    $wideHeight = [Math]::Min(950, $workingArea.Height - 48)
    if ($wideWidth -lt 960 -or $wideHeight -lt 640) {
        throw 'The current display work area is too small for the reviewed wide screenshots.'
    }
    Set-WindowGeometry `
        -WindowHandle $application.MainWindowHandle `
        -X ($workingArea.Left + 24) `
        -Y ($workingArea.Top + 24) `
        -Width $wideWidth `
        -Height $wideHeight
    $root = [Windows.Automation.AutomationElement]::FromHandle($application.MainWindowHandle)

    Select-EnglishLanguage -Root $root -ProcessId $application.Id
    Invoke-Button -Root $root -Names @('Listeners')
    Set-InventorySearch -Root $root -Value 'SocketScopeFixture'
    Invoke-Button -Root $root -Names @('Refresh')
    $listenerRows = @()
    Wait-Until -Seconds 20 -Failure 'Controlled listener rows did not appear.' -Condition {
        $script:listenerRows = @(Get-VisibleInventoryRows -Root $root)
        $script:listenerRows.Count -ge 4 -and
            @($script:listenerRows | Where-Object { -not $_.Contains('SocketScopeFixture', [StringComparison]::Ordinal) }).Count -eq 0
    }
    $listenerRows = $script:listenerRows
    Assert-ControlledRows -Rows $listenerRows -MinimumRows 4 -View 'Listeners'
    Assert-PrivacySafeTree -Root $root
    $listeners = Save-WindowScreenshot `
        -Root $root `
        -WindowHandle $application.MainWindowHandle `
        -Path (Join-Path $outputPath 'socketscope-listeners.png')

    Invoke-Button -Root $root -Names @('Connections')
    Invoke-Button -Root $root -Names @('Refresh')
    $connectionRows = @()
    Wait-Until -Seconds 20 -Failure 'Controlled TCP connection rows did not appear.' -Condition {
        $script:connectionRows = @(Get-VisibleInventoryRows -Root $root)
        $script:connectionRows.Count -ge 2 -and
            @($script:connectionRows | Where-Object { -not $_.Contains('SocketScopeFixture', [StringComparison]::Ordinal) }).Count -eq 0
    }
    $connectionRows = $script:connectionRows
    Assert-ControlledRows -Rows $connectionRows -MinimumRows 2 -View 'Connections'
    Assert-PrivacySafeTree -Root $root
    $connections = Save-WindowScreenshot `
        -Root $root `
        -WindowHandle $application.MainWindowHandle `
        -Path (Join-Path $outputPath 'socketscope-connections.png')

    Invoke-Button -Root $root -Names @('Reports')
    Wait-Until -Failure 'The Full and Share-safe report choices did not appear.' -Condition {
        $fullCondition = [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::NameProperty,
            'Full report')
        $shareSafeCondition = [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::NameProperty,
            'Share-safe report')
        $null -ne $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $fullCondition) -and
            $null -ne $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $shareSafeCondition)
    }
    Assert-PrivacySafeTree -Root $root
    $reports = Save-WindowScreenshot `
        -Root $root `
        -WindowHandle $application.MainWindowHandle `
        -Path (Join-Path $outputPath 'socketscope-reports.png')

    Invoke-Button -Root $root -Names @('Settings')
    Wait-Until -Failure 'The Terminal command settings card did not appear.' -Condition {
        $null -ne (Find-ByAutomationId -Root $root -AutomationId 'TerminalInstallPathText') -and
            $null -ne (Find-Button -Root $root -Names @('Install terminal command', 'Terminal command installed', 'Update terminal command', 'Add to user PATH'))
    }
    $terminalPath = Find-ByAutomationId -Root $root -AutomationId 'TerminalInstallPathText'
    $terminalPathValue = $terminalPath.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($terminalPathValue -ne '%LOCALAPPDATA%\OeXYZ\SocketScope\bin\socketscope.exe') {
        throw 'The Settings terminal path is missing or exposes an unexpected location.'
    }
    Wait-Until -Failure 'The export-profile settings did not appear.' -Condition {
        $null -ne (Find-ByAutomationId -Root $root -AutomationId 'ExportProfileSelector')
    }
    Move-VerticalScrollToPercent -Root $root -Percent 60
    Assert-PrivacySafeTree -Root $root
    $settingsExport = Save-WindowScreenshot `
        -Root $root `
        -WindowHandle $application.MainWindowHandle `
        -Path (Join-Path $outputPath 'socketscope-settings-export.png')

    Move-VerticalScrollToPercent -Root $root -Percent 100
    Assert-PrivacySafeTree -Root $root
    $settings = Save-WindowScreenshot `
        -Root $root `
        -WindowHandle $application.MainWindowHandle `
        -Path (Join-Path $outputPath 'socketscope-settings-terminal.png')

    Invoke-Button -Root $root -Names @('Help & diagnostics')
    Wait-Until -Failure 'The Help and diagnostics content did not appear.' -Condition {
        $helpCondition = [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::NameProperty,
            'Help and local diagnostics')
        $copyCondition = [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::NameProperty,
            'Copy diagnostic summary')
        $null -ne $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $helpCondition) -and
            $null -ne $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $copyCondition)
    }
    Assert-PrivacySafeTree -Root $root
    $helpGlossary = Save-WindowScreenshot `
        -Root $root `
        -WindowHandle $application.MainWindowHandle `
        -Path (Join-Path $outputPath 'socketscope-help-glossary.png')

    Move-VerticalScrollToPercent -Root $root -Percent 100
    Assert-PrivacySafeTree -Root $root
    $helpDiagnostics = Save-WindowScreenshot `
        -Root $root `
        -WindowHandle $application.MainWindowHandle `
        -Path (Join-Path $outputPath 'socketscope-help-diagnostics.png')

    $compactWidth = [Math]::Min(900, $workingArea.Width - 48)
    $compactHeight = [Math]::Min(720, $workingArea.Height - 48)
    Set-WindowGeometry `
        -WindowHandle $application.MainWindowHandle `
        -X ($workingArea.Left + 24) `
        -Y ($workingArea.Top + 24) `
        -Width $compactWidth `
        -Height $compactHeight
    $root = [Windows.Automation.AutomationElement]::FromHandle($application.MainWindowHandle)
    Invoke-Button -Root $root -Names @('Overview')
    Wait-Until -Failure 'The compact overview did not appear.' -Condition {
        $condition = [Windows.Automation.PropertyCondition]::new(
            [Windows.Automation.AutomationElement]::NameProperty,
            'Current endpoints')
        $null -ne $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    }
    Assert-PrivacySafeTree -Root $root
    $compact = Save-WindowScreenshot `
        -Root $root `
        -WindowHandle $application.MainWindowHandle `
        -Path (Join-Path $outputPath 'socketscope-responsive-compact.png')

    $userPathAfter = [Environment]::GetEnvironmentVariable('Path', [EnvironmentVariableTarget]::User)
    if (-not [string]::Equals($userPathBefore, $userPathAfter, [StringComparison]::Ordinal)) {
        throw 'Viewing Terminal command settings unexpectedly changed the real user PATH.'
    }

    [pscustomobject]@{
        Result = 'PASS'
        Source = 'real release executable plus controlled local socket process'
        ListenerRows = $listenerRows.Count
        ConnectionRows = $connectionRows.Count
        SensitiveDataTreeReview = 'passed'
        UserPathChanged = $false
        Screenshots = @(
            $listeners,
            $connections,
            $reports,
            $settingsExport,
            $settings,
            $helpGlossary,
            $helpDiagnostics,
            $compact)
    }
} finally {
    foreach ($process in @($application, $fixture)) {
        if ($null -eq $process) {
            continue
        }
        try {
            if (-not $process.HasExited) {
                $process.Kill($true)
                $process.WaitForExit()
            }
        } finally {
            $process.Dispose()
        }
    }

    if ($null -ne $substDrive) {
        & (Join-Path $env:SystemRoot 'System32\subst.exe') "${substDrive}:" /D | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Temporary screenshot drive ${substDrive}: could not be removed automatically."
        }
    }

    if (Test-Path -LiteralPath $testRoot) {
        $safePath = [IO.Path]::GetFullPath($testRoot)
        if (-not $safePath.StartsWith($workspacePrefix, $comparison)) {
            throw 'Refusing to remove screenshot data outside the repository.'
        }
        Remove-Item -LiteralPath $safePath -Recurse -Force
    }
}
