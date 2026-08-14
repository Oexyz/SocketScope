#Requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Package,

    [string] $ImageOutput = 'docs/images/socketscope-test-evidence.png',

    [string] $JsonOutput = 'docs/release-evidence/windows-x64-local.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'The rendered release-test evidence currently requires Windows.'
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Release-test evidence must be collected from an ordinary-user run.'
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
        throw "The evidence input must be an existing workspace file: $fullPath"
    }

    return $fullPath
}

function Resolve-WorkspaceOutput {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $ExpectedExtension
    )

    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($workspacePrefix, $comparison) -or
        -not [IO.Path]::GetExtension($fullPath).Equals($ExpectedExtension, $comparison)) {
        throw "The evidence output must be a $ExpectedExtension file inside the repository."
    }

    $parentPath = [IO.Path]::GetDirectoryName($fullPath)
    [IO.Directory]::CreateDirectory($parentPath) | Out-Null
    $parent = [IO.DirectoryInfo]::new($parentPath)
    if (($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        $null -ne $parent.LinkTarget) {
        throw 'A linked or reparse directory cannot hold release evidence.'
    }

    return $fullPath
}

function Get-TrxCounters {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Name
    )

    [xml] $document = [IO.File]::ReadAllText($Path)
    $counter = $document.SelectSingleNode(
        "/*[local-name()='TestRun']/*[local-name()='ResultSummary']/*[local-name()='Counters']")
    if ($null -eq $counter) {
        throw "The $Name TRX result does not contain counters."
    }

    $total = [int] $counter.total
    $executed = [int] $counter.executed
    $passed = [int] $counter.passed
    $failed = [int] $counter.failed
    $skipped = $total - $executed
    if ($total -le 0 -or $passed + $failed -ne $executed -or $skipped -lt 0) {
        throw "The $Name TRX counters are inconsistent."
    }

    $skippedNames = @(
        $document.SelectNodes(
            "//*[local-name()='UnitTestResult' and @outcome='NotExecuted']") |
            ForEach-Object { [string] $_.testName } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )

    return [pscustomobject]@{
        Name = $Name
        Total = $total
        Passed = $passed
        Failed = $failed
        Skipped = $skipped
        SkippedTests = $skippedNames
    }
}

function New-RoundedRectanglePath {
    param(
        [Parameter(Mandatory)][Drawing.RectangleF] $Rectangle,
        [Parameter(Mandatory)][single] $Radius
    )

    $diameter = $Radius * 2
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Rectangle.X, $Rectangle.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Y, $diameter, $diameter, 270, 90)
    $path.AddArc(
        $Rectangle.Right - $diameter,
        $Rectangle.Bottom - $diameter,
        $diameter,
        $diameter,
        0,
        90)
    $path.AddArc($Rectangle.X, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Add-RoundedPanel {
    param(
        [Parameter(Mandatory)][Drawing.Graphics] $Graphics,
        [Parameter(Mandatory)][Drawing.RectangleF] $Rectangle,
        [Parameter(Mandatory)][Drawing.Color] $Fill,
        [Parameter(Mandatory)][Drawing.Color] $Border,
        [single] $Radius = 14
    )

    $path = New-RoundedRectanglePath -Rectangle $Rectangle -Radius $Radius
    try {
        $brush = [Drawing.SolidBrush]::new($Fill)
        $pen = [Drawing.Pen]::new($Border, 1)
        try {
            $Graphics.FillPath($brush, $path)
            $Graphics.DrawPath($pen, $path)
        } finally {
            $brush.Dispose()
            $pen.Dispose()
        }
    } finally {
        $path.Dispose()
    }
}

function Add-Text {
    param(
        [Parameter(Mandatory)][Drawing.Graphics] $Graphics,
        [Parameter(Mandatory)][string] $Value,
        [Parameter(Mandatory)][Drawing.Font] $Font,
        [Parameter(Mandatory)][Drawing.Color] $Color,
        [Parameter(Mandatory)][single] $X,
        [Parameter(Mandatory)][single] $Y,
        [single] $Width = 1300,
        [single] $Height = 80
    )

    $brush = [Drawing.SolidBrush]::new($Color)
    $format = [Drawing.StringFormat]::new()
    try {
        $format.Trimming = [Drawing.StringTrimming]::EllipsisCharacter
        $format.FormatFlags = [Drawing.StringFormatFlags]::NoWrap
        $Graphics.DrawString(
            $Value,
            $Font,
            $brush,
            [Drawing.RectangleF]::new($X, $Y, $Width, $Height),
            $format)
    } finally {
        $format.Dispose()
        $brush.Dispose()
    }
}

$packagePath = Resolve-WorkspaceFile $Package
if ([IO.Path]::GetFileName($packagePath) -notmatch '^OeXYZ-SocketScope-v(?<version>\d+\.\d+\.\d+)-win-x64\.exe$') {
    throw 'The evidence package must be a named Windows x64 release executable.'
}
$version = $Matches.version
$imagePath = Resolve-WorkspaceOutput -Path $ImageOutput -ExpectedExtension '.png'
$jsonPath = Resolve-WorkspaceOutput -Path $JsonOutput -ExpectedExtension '.json'
$fontPath = Resolve-WorkspaceFile 'src/OeXYZ.SocketScope.Desktop/Assets/Fonts/Inter-Variable.ttf'
$dotnetPath = Resolve-WorkspaceFile '.tools/dotnet/dotnet.exe'
$testRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot ".cache/test-evidence/$([Guid]::NewGuid().ToString('N'))"))
if (-not $testRoot.StartsWith($workspacePrefix, $comparison)) {
    throw 'The temporary test-evidence directory escaped the repository.'
}
[IO.Directory]::CreateDirectory($testRoot) | Out-Null

$projects = @(
    [pscustomobject]@{ Name = 'Core'; Path = 'tests/OeXYZ.SocketScope.Core.Tests/OeXYZ.SocketScope.Core.Tests.csproj' },
    [pscustomobject]@{ Name = 'Updater & terminal'; Path = 'tests/OeXYZ.SocketScope.Updater.Tests/OeXYZ.SocketScope.Updater.Tests.csproj' },
    [pscustomobject]@{ Name = 'Windows provider'; Path = 'tests/OeXYZ.SocketScope.Platform.Windows.Tests/OeXYZ.SocketScope.Platform.Windows.Tests.csproj' },
    [pscustomobject]@{ Name = 'Linux provider'; Path = 'tests/OeXYZ.SocketScope.Platform.Linux.Tests/OeXYZ.SocketScope.Platform.Linux.Tests.csproj' },
    [pscustomobject]@{ Name = 'Integration'; Path = 'tests/OeXYZ.SocketScope.IntegrationTests/OeXYZ.SocketScope.IntegrationTests.csproj' },
    [pscustomobject]@{ Name = 'Architecture'; Path = 'tests/OeXYZ.SocketScope.ArchitectureTests/OeXYZ.SocketScope.ArchitectureTests.csproj' },
    [pscustomobject]@{ Name = 'Avalonia UI smoke'; Path = 'tests/OeXYZ.SocketScope.UiSmokeTests/OeXYZ.SocketScope.UiSmokeTests.csproj' }
)

$startedUtc = [DateTimeOffset]::UtcNow
$previousLanguage = $env:DOTNET_CLI_UI_LANGUAGE
$suiteResults = @()
try {
    $env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
    foreach ($project in $projects) {
        $projectPath = Resolve-WorkspaceFile $project.Path
        $resultDirectory = Join-Path $testRoot ($project.Name -replace '[^A-Za-z0-9]+', '-')
        [IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
        $resultPath = Join-Path $resultDirectory 'result.trx'
        $testOutput = & $dotnetPath test $projectPath `
            --configuration Release `
            --no-restore `
            --no-build `
            --logger 'trx;LogFileName=result.trx' `
            --results-directory $resultDirectory 2>&1
        if ($LASTEXITCODE -ne 0) {
            $summary = @($testOutput | Select-Object -Last 12) -join [Environment]::NewLine
            throw "The $($project.Name) suite failed.`n$summary"
        }
        if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
            throw "The $($project.Name) suite did not produce a TRX result."
        }
        $suiteResults += Get-TrxCounters -Path $resultPath -Name $project.Name
    }
} finally {
    $env:DOTNET_CLI_UI_LANGUAGE = $previousLanguage
    if (Test-Path -LiteralPath $testRoot) {
        $safePath = [IO.Path]::GetFullPath($testRoot)
        if (-not $safePath.StartsWith($workspacePrefix, $comparison)) {
            throw 'Refusing to remove test results outside the repository.'
        }
        Remove-Item -LiteralPath $safePath -Recurse -Force
    }
}
$finishedUtc = [DateTimeOffset]::UtcNow

$total = [int] ($suiteResults | Measure-Object -Property Total -Sum).Sum
$passed = [int] ($suiteResults | Measure-Object -Property Passed -Sum).Sum
$failed = [int] ($suiteResults | Measure-Object -Property Failed -Sum).Sum
$skipped = [int] ($suiteResults | Measure-Object -Property Skipped -Sum).Sum
$passedSuites = @($suiteResults | Where-Object Failed -eq 0).Count
if ($failed -ne 0 -or $passedSuites -ne $projects.Count) {
    throw 'Release-test evidence cannot be rendered for a failing suite.'
}

$packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
$signature = Get-AuthenticodeSignature -FilePath $packagePath
if ($signature.Status -ne [Management.Automation.SignatureStatus]::NotSigned) {
    throw "The release-candidate Authenticode status is '$($signature.Status)' instead of not present."
}
$evidence = [ordered]@{
    schemaVersion = 1
    product = 'OeXYZ SocketScope'
    productVersion = $version
    status = 'pass'
    scope = 'Local Windows x64 release-candidate validation'
    package = [IO.Path]::GetFileName($packagePath)
    packageSha256 = $packageHash
    windowsAuthenticode = 'not-present'
    host = [ordered]@{
        operatingSystem = 'Windows'
        architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
        elevated = $false
    }
    startedUtc = $startedUtc.ToString('O')
    finishedUtc = $finishedUtc.ToString('O')
    command = 'dotnet test <each test project> --configuration Release --no-restore --no-build --logger trx'
    totals = [ordered]@{
        suites = $projects.Count
        passedSuites = $passedSuites
        tests = $total
        passed = $passed
        failed = $failed
        skipped = $skipped
    }
    suites = @(
        $suiteResults | ForEach-Object {
            [ordered]@{
                name = $_.Name
                total = $_.Total
                passed = $_.Passed
                failed = $_.Failed
                skipped = $_.Skipped
                skippedTests = @($_.SkippedTests)
            }
        }
    )
    limitations = @(
        'This evidence covers a local Windows x64 host only.',
        'Cross-compiled ARM64 and Linux release assets require native-host validation.',
        'A test screenshot supplements, but does not replace, machine-readable CI evidence.'
    )
}

$json = $evidence | ConvertTo-Json -Depth 8
$jsonTemp = "$jsonPath.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    [IO.File]::WriteAllText($jsonTemp, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    [IO.File]::Move($jsonTemp, $jsonPath, $true)
} finally {
    if (Test-Path -LiteralPath $jsonTemp) {
        Remove-Item -LiteralPath $jsonTemp -Force
    }
}

Add-Type -AssemblyName System.Drawing
$privateFonts = [Drawing.Text.PrivateFontCollection]::new()
$bitmap = $null
$titleFont = $null
$headingFont = $null
$bodyFont = $null
$smallFont = $null
$numberFont = $null
try {
    $privateFonts.AddFontFile($fontPath)
    $bodyFamily = $privateFonts.Families |
        Where-Object Name -eq 'Inter' |
        Select-Object -First 1
    $headingFamily = $privateFonts.Families |
        Where-Object Name -eq 'Inter ExtraBold' |
        Select-Object -First 1
    if ($null -eq $bodyFamily -or $null -eq $headingFamily) {
        throw 'The local Inter font could not be loaded for the evidence image.'
    }
    $titleFont = [Drawing.Font]::new($headingFamily, 30, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $headingFont = [Drawing.Font]::new($headingFamily, 21, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $bodyFont = [Drawing.Font]::new($bodyFamily, 17, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $smallFont = [Drawing.Font]::new($bodyFamily, 14, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $numberFont = [Drawing.Font]::new($headingFamily, 39, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)

    $background = [Drawing.ColorTranslator]::FromHtml('#080D13')
    $surface = [Drawing.ColorTranslator]::FromHtml('#111A26')
    $surfaceStrong = [Drawing.ColorTranslator]::FromHtml('#172436')
    $border = [Drawing.ColorTranslator]::FromHtml('#344A63')
    $text = [Drawing.ColorTranslator]::FromHtml('#F4F8FC')
    $muted = [Drawing.ColorTranslator]::FromHtml('#A8C4E3')
    $blue = [Drawing.ColorTranslator]::FromHtml('#2498FF')
    $green = [Drawing.ColorTranslator]::FromHtml('#45D6A2')
    $amber = [Drawing.ColorTranslator]::FromHtml('#FFB02E')

    $bitmap = [Drawing.Bitmap]::new(1500, 1020, [Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $bitmap.SetResolution(144, 144)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $graphics.Clear($background)

        $logoPen = [Drawing.Pen]::new($blue, 3)
        $logoBrush = [Drawing.SolidBrush]::new($green)
        try {
            $graphics.DrawEllipse($logoPen, 56, 48, 46, 46)
            $graphics.FillEllipse($logoBrush, 72, 64, 14, 14)
        } finally {
            $logoPen.Dispose()
            $logoBrush.Dispose()
        }
        Add-Text -Graphics $graphics -Value 'OeXYZ' -Font $smallFont -Color $blue -X 120 -Y 47 -Width 160 -Height 28
        Add-Text -Graphics $graphics -Value 'SocketScope' -Font $headingFont -Color $text -X 120 -Y 68 -Width 240 -Height 38
        Add-Text -Graphics $graphics -Value 'Release verification — Windows x64' -Font $titleFont -Color $text -X 56 -Y 122 -Width 900 -Height 55
        Add-Text -Graphics $graphics -Value "v$version · ordinary-user local run · $($finishedUtc.UtcDateTime.ToString('yyyy-MM-dd HH:mm')) UTC" -Font $bodyFont -Color $muted -X 56 -Y 173 -Width 900 -Height 34

        $badge = [Drawing.RectangleF]::new(1160, 52, 284, 52)
        Add-RoundedPanel -Graphics $graphics -Rectangle $badge -Fill $surfaceStrong -Border $green -Radius 14
        Add-Text -Graphics $graphics -Value '✓  ALL SUITES PASS' -Font $bodyFont -Color $green -X 1192 -Y 67 -Width 230 -Height 30

        $cardWidth = 327
        $cardY = 232
        $labels = @('Passed', 'Failed', 'Skipped', 'Suites')
        $values = @([string] $passed, [string] $failed, [string] $skipped, "$passedSuites/$($projects.Count)")
        $colors = @($green, $green, $amber, $blue)
        for ($index = 0; $index -lt 4; $index++) {
            $cardX = 56 + ($index * 346)
            Add-RoundedPanel -Graphics $graphics -Rectangle ([Drawing.RectangleF]::new($cardX, $cardY, $cardWidth, 132)) -Fill $surface -Border $border
            Add-Text -Graphics $graphics -Value $labels[$index] -Font $bodyFont -Color $muted -X ($cardX + 24) -Y ($cardY + 20) -Width 220 -Height 30
            Add-Text -Graphics $graphics -Value $values[$index] -Font $numberFont -Color $colors[$index] -X ($cardX + 24) -Y ($cardY + 53) -Width 220 -Height 58
        }

        Add-RoundedPanel -Graphics $graphics -Rectangle ([Drawing.RectangleF]::new(56, 392, 1388, 380)) -Fill $surface -Border $border
        Add-Text -Graphics $graphics -Value 'Automated test suites' -Font $headingFont -Color $text -X 82 -Y 416 -Width 500 -Height 36
        Add-Text -Graphics $graphics -Value 'TRX counters generated by the pinned .NET SDK' -Font $smallFont -Color $muted -X 950 -Y 421 -Width 458 -Height 28

        $rowY = 470
        foreach ($suite in $suiteResults) {
            $separatorPen = [Drawing.Pen]::new($border, 1)
            try {
                $graphics.DrawLine($separatorPen, 82, $rowY + 38, 1418, $rowY + 38)
            } finally {
                $separatorPen.Dispose()
            }
            Add-Text -Graphics $graphics -Value '✓' -Font $headingFont -Color $green -X 84 -Y $rowY -Width 30 -Height 32
            Add-Text -Graphics $graphics -Value $suite.Name -Font $bodyFont -Color $text -X 125 -Y $rowY -Width 490 -Height 32
            $suiteSummary = "$($suite.Passed) passed"
            if ($suite.Skipped -gt 0) {
                $suiteSummary += " · $($suite.Skipped) skipped"
            }
            Add-Text -Graphics $graphics -Value $suiteSummary -Font $bodyFont -Color $(if ($suite.Skipped -gt 0) { $amber } else { $green }) -X 1120 -Y $rowY -Width 270 -Height 32
            $rowY += 43
        }

        Add-RoundedPanel -Graphics $graphics -Rectangle ([Drawing.RectangleF]::new(56, 800, 1388, 116)) -Fill $surfaceStrong -Border $border
        Add-Text -Graphics $graphics -Value 'Verified candidate' -Font $smallFont -Color $muted -X 82 -Y 821 -Width 220 -Height 28
        Add-Text -Graphics $graphics -Value ([IO.Path]::GetFileName($packagePath)) -Font $bodyFont -Color $text -X 82 -Y 847 -Width 600 -Height 32
        Add-Text -Graphics $graphics -Value 'SHA-256' -Font $smallFont -Color $muted -X 716 -Y 821 -Width 120 -Height 28
        Add-Text -Graphics $graphics -Value $packageHash -Font $smallFont -Color $text -X 716 -Y 848 -Width 692 -Height 30
        Add-Text -Graphics $graphics -Value 'Windows Authenticode' -Font $smallFont -Color $muted -X 82 -Y 883 -Width 190 -Height 26
        Add-Text -Graphics $graphics -Value 'not present' -Font $smallFont -Color $amber -X 270 -Y 883 -Width 140 -Height 26

        $skipNote = if ($skipped -eq 0) {
            'No failures or skips. This image is rendered from seven machine-readable TRX summaries.'
        } else {
            "$skipped host-capability test skipped; no failures. This image is rendered from seven machine-readable TRX summaries."
        }
        Add-Text -Graphics $graphics -Value $skipNote -Font $smallFont -Color $muted -X 56 -Y 942 -Width 1000 -Height 28
        Add-Text -Graphics $graphics -Value 'Scope: local Windows x64 only' -Font $smallFont -Color $amber -X 1168 -Y 942 -Width 276 -Height 28
    } finally {
        $graphics.Dispose()
    }

    $imageTemp = "$imagePath.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        $bitmap.Save($imageTemp, [Drawing.Imaging.ImageFormat]::Png)
        [IO.File]::Move($imageTemp, $imagePath, $true)
    } finally {
        if (Test-Path -LiteralPath $imageTemp) {
            Remove-Item -LiteralPath $imageTemp -Force
        }
    }
} finally {
    if ($null -ne $bitmap) {
        $bitmap.Dispose()
    }
    foreach ($font in @($titleFont, $headingFont, $bodyFont, $smallFont, $numberFont)) {
        if ($null -ne $font) {
            $font.Dispose()
        }
    }
    $privateFonts.Dispose()
}

[pscustomobject]@{
    Result = 'PASS'
    Suites = $projects.Count
    Tests = $total
    Passed = $passed
    Failed = $failed
    Skipped = $skipped
    Package = [IO.Path]::GetFileName($packagePath)
    PackageSha256 = $packageHash
    WindowsAuthenticode = 'not-present'
    Image = [IO.Path]::GetRelativePath($repositoryRoot, $imagePath)
    Json = [IO.Path]::GetRelativePath($repositoryRoot, $jsonPath)
}
