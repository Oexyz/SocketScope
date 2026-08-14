#Requires -Version 7.4

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw 'The reviewed System.Drawing icon renderer currently runs on Windows only.'
}

Add-Type -AssemblyName System.Drawing.Common

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$iconDirectory = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'src/OeXYZ.SocketScope.Desktop/Assets/Icons'))
$expectedPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $iconDirectory.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The icon output directory escaped the repository.'
}
[IO.Directory]::CreateDirectory($iconDirectory) | Out-Null

function New-SocketScopeBitmap {
    param([Parameter(Mandatory)][int] $Size)

    $bitmap = [Drawing.Bitmap]::new($Size, $Size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bitmap.SetResolution(96, 96)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $scale = $Size / 512.0

        function Scale-Rectangle([double] $X, [double] $Y, [double] $Width, [double] $Height) {
            return [Drawing.RectangleF]::new(
                [single]($X * $scale),
                [single]($Y * $scale),
                [single]($Width * $scale),
                [single]($Height * $scale))
        }

        function New-RoundedPath([Drawing.RectangleF] $Rectangle, [double] $Radius) {
            $path = [Drawing.Drawing2D.GraphicsPath]::new()
            $diameter = [single](2 * $Radius * $scale)
            $path.AddArc($Rectangle.X, $Rectangle.Y, $diameter, $diameter, 180, 90)
            $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Y, $diameter, $diameter, 270, 90)
            $path.AddArc($Rectangle.Right - $diameter, $Rectangle.Bottom - $diameter, $diameter, $diameter, 0, 90)
            $path.AddArc($Rectangle.X, $Rectangle.Bottom - $diameter, $diameter, $diameter, 90, 90)
            $path.CloseFigure()
            return $path
        }

        $outer = Scale-Rectangle 16 16 480 480
        $outerPath = New-RoundedPath $outer 112
        $outerBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#0b0f16'))
        $outerPen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#29384b'), [single][Math]::Max(1, 16 * $scale))
        try {
            $graphics.FillPath($outerBrush, $outerPath)
            $graphics.DrawPath($outerPen, $outerPath)
        } finally {
            $outerPen.Dispose()
            $outerBrush.Dispose()
            $outerPath.Dispose()
        }

        $scopeBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#101c2a'))
        $scopePen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#1688f8'), [single][Math]::Max(1.4, 28 * $scale))
        try {
            $scope = Scale-Rectangle 108 140 264 264
            $graphics.FillEllipse($scopeBrush, $scope)
            $graphics.DrawEllipse($scopePen, $scope)
        } finally {
            $scopePen.Dispose()
            $scopeBrush.Dispose()
        }

        $accentPen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#33a3ff'), [single][Math]::Max(1.4, 28 * $scale))
        $accentPen.StartCap = [Drawing.Drawing2D.LineCap]::Round
        $accentPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $accentPen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
        try {
            $graphics.DrawLine($accentPen, [single](338 * $scale), [single](174 * $scale), [single](426 * $scale), [single](86 * $scale))
            $graphics.DrawLine($accentPen, [single](352 * $scale), [single](112 * $scale), [single](426 * $scale), [single](112 * $scale))
            $graphics.DrawLine($accentPen, [single](426 * $scale), [single](112 * $scale), [single](426 * $scale), [single](186 * $scale))
        } finally {
            $accentPen.Dispose()
        }

        if ($Size -ge 32) {
            $tickPen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#42546b'), [single][Math]::Max(1, 20 * $scale))
            $tickPen.StartCap = [Drawing.Drawing2D.LineCap]::Round
            $tickPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
            try {
                foreach ($line in @(
                    @(240, 196, 240, 232),
                    @(240, 312, 240, 348),
                    @(164, 272, 200, 272),
                    @(280, 272, 316, 272))) {
                    $graphics.DrawLine(
                        $tickPen,
                        [single]($line[0] * $scale),
                        [single]($line[1] * $scale),
                        [single]($line[2] * $scale),
                        [single]($line[3] * $scale))
                }
            } finally {
                $tickPen.Dispose()
            }
        }

        $endpointBrush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#43d7a0'))
        try {
            $endpoint = Scale-Rectangle 198 230 84 84
            $graphics.FillEllipse($endpointBrush, $endpoint)
        } finally {
            $endpointBrush.Dispose()
        }
    } finally {
        $graphics.Dispose()
    }
    return $bitmap
}

$pngPath = Join-Path $iconDirectory 'socketscope.png'
$pngBitmap = New-SocketScopeBitmap 512
try {
    $pngBitmap.Save($pngPath, [Drawing.Imaging.ImageFormat]::Png)
} finally {
    $pngBitmap.Dispose()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = foreach ($size in $sizes) {
    $bitmap = New-SocketScopeBitmap $size
    try {
        $stream = [IO.MemoryStream]::new()
        try {
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
        } finally {
            $stream.Dispose()
        }
    } finally {
        $bitmap.Dispose()
    }
}

$icoPath = Join-Path $iconDirectory 'socketscope.ico'
$stream = [IO.File]::Open($icoPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) {
        $writer.Write([byte[]]$frame.Bytes)
    }
} finally {
    $writer.Dispose()
}

[pscustomobject]@{
    Png = $pngPath
    PngSha256 = (Get-FileHash -LiteralPath $pngPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Ico = $icoPath
    IcoSha256 = (Get-FileHash -LiteralPath $icoPath -Algorithm SHA256).Hash.ToLowerInvariant()
}
