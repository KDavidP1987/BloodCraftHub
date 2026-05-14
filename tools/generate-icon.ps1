<#
.SYNOPSIS
    Generates BloodCraftHub/icon.png — a 256x256 Thunderstore-ready icon
    combining V Rising vampire iconography (fangs + blood drip) with a UI
    theme (panel window outline + buttons).

.DESCRIPTION
    Pure System.Drawing — no external dependencies, runs anywhere PowerShell
    has access to System.Drawing.Common (Windows or Mono). Re-run this script
    any time the icon design changes; output is deterministic.

.EXAMPLE
    .\tools\generate-icon.ps1
#>

[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'BloodCraftHub\icon.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$size = 256
$bmp  = New-Object System.Drawing.Bitmap $size, $size
$g    = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

# Palette ------------------------------------------------------------------
$bgInner   = [System.Drawing.Color]::FromArgb(255, 60, 8, 14)    # very dark blood
$bgOuter   = [System.Drawing.Color]::FromArgb(255, 20, 4, 6)     # near-black
$ringColor = [System.Drawing.Color]::FromArgb(255, 110, 18, 22)  # crimson border
$ringHi    = [System.Drawing.Color]::FromArgb(255, 180, 30, 36)  # ring highlight
$fangFill  = [System.Drawing.Color]::FromArgb(255, 245, 240, 230)  # ivory
$fangShade = [System.Drawing.Color]::FromArgb(255, 195, 188, 175)  # ivory shadow
$panelFill = [System.Drawing.Color]::FromArgb(255, 28, 28, 36)    # UI panel bg
$panelLine = [System.Drawing.Color]::FromArgb(255, 200, 195, 200) # UI panel chrome
$panelTitle= [System.Drawing.Color]::FromArgb(255, 80, 18, 26)    # title bar (blood)
$bloodDrip = [System.Drawing.Color]::FromArgb(255, 170, 18, 24)   # bright blood

# 1. Radial-ish background via concentric circles ---------------------------
# (System.Drawing's PathGradientBrush is finicky; do a series of fades instead)
$g.Clear($bgOuter)
for ($r = $size; $r -gt 0; $r -= 4) {
    $t = 1.0 - ($r / [double]$size)
    $a = [int](255 * ($t * 0.85))
    $col = [System.Drawing.Color]::FromArgb($a, $bgInner.R, $bgInner.G, $bgInner.B)
    $brush = New-Object System.Drawing.SolidBrush $col
    $g.FillEllipse($brush, [int](($size - $r) / 2), [int](($size - $r) / 2), $r, $r)
    $brush.Dispose()
}

# 2. Outer crimson ring -----------------------------------------------------
$ringPen = New-Object System.Drawing.Pen $ringColor, 6
$g.DrawEllipse($ringPen, 6, 6, $size - 12, $size - 12)
$ringPen.Dispose()
$ringHiPen = New-Object System.Drawing.Pen $ringHi, 1.5
$g.DrawEllipse($ringHiPen, 9, 9, $size - 18, $size - 18)
$ringHiPen.Dispose()

# 3. Two vampire fangs (downward triangles) crossing the panel --------------
function Draw-Fang($cx, $tipY, $width, $tipExtra, $shadowOffset) {
    # Fang body (ivory triangle)
    $top = [System.Drawing.PointF]::new($cx - $width / 2, $tipY - $tipExtra)
    $top2= [System.Drawing.PointF]::new($cx + $width / 2, $tipY - $tipExtra)
    $tip = [System.Drawing.PointF]::new($cx, $tipY)
    $pts = @($top, $top2, $tip)
    # Shadow side first
    $pts2 = @(
        [System.Drawing.PointF]::new($cx, $tipY - $tipExtra),
        [System.Drawing.PointF]::new($cx + $width / 2, $tipY - $tipExtra),
        [System.Drawing.PointF]::new($cx, $tipY)
    )
    $shadow = New-Object System.Drawing.SolidBrush $fangShade
    $g.FillPolygon($shadow, $pts2)
    $shadow.Dispose()
    # Light side
    $pts3 = @(
        [System.Drawing.PointF]::new($cx - $width / 2, $tipY - $tipExtra),
        [System.Drawing.PointF]::new($cx, $tipY - $tipExtra),
        [System.Drawing.PointF]::new($cx, $tipY)
    )
    $light = New-Object System.Drawing.SolidBrush $fangFill
    $g.FillPolygon($light, $pts3)
    $light.Dispose()
}

# Two fangs flanking the central panel — taller, sharper, and starting
# higher up so the triangle is visible above the panel rather than reading
# as a rectangle cut off by the panel chrome.
Draw-Fang -cx 80  -tipY 220 -width 30 -tipExtra 145 -shadowOffset 0
Draw-Fang -cx 176 -tipY 220 -width 30 -tipExtra 145 -shadowOffset 0

# 4. Blood drips at fang tips ----------------------------------------------
function Draw-Drip($x, $y, $r) {
    $brush = New-Object System.Drawing.SolidBrush $bloodDrip
    # Drop = circle + small triangle on top
    $g.FillEllipse($brush, $x - $r, $y - $r, $r * 2, $r * 2)
    $tri = @(
        [System.Drawing.PointF]::new($x, $y - $r * 2.4),
        [System.Drawing.PointF]::new($x - $r * 0.7, $y - $r * 0.6),
        [System.Drawing.PointF]::new($x + $r * 0.7, $y - $r * 0.6)
    )
    $g.FillPolygon($brush, $tri)
    $brush.Dispose()
}
Draw-Drip -x 80  -y 238 -r 7
Draw-Drip -x 176 -y 238 -r 7

# 5. UI panel window in center ----------------------------------------------
# Centered slightly higher so the fangs read as descending below it.
$panelX = 70; $panelY = 92; $panelW = 116; $panelH = 78
$panelBgBrush = New-Object System.Drawing.SolidBrush $panelFill
$g.FillRectangle($panelBgBrush, $panelX, $panelY, $panelW, $panelH)
$panelBgBrush.Dispose()

# Title bar (blood-red strip)
$titleBrush = New-Object System.Drawing.SolidBrush $panelTitle
$g.FillRectangle($titleBrush, $panelX, $panelY, $panelW, 14)
$titleBrush.Dispose()

# Three small "buttons" / rows in the panel body
$rowBrush = New-Object System.Drawing.SolidBrush $panelLine
for ($i = 0; $i -lt 3; $i++) {
    $g.FillRectangle($rowBrush, $panelX + 10, $panelY + 24 + ($i * 14), $panelW - 20, 7)
}
$rowBrush.Dispose()

# Panel border
$panelPen = New-Object System.Drawing.Pen $panelLine, 1.5
$g.DrawRectangle($panelPen, $panelX, $panelY, $panelW, $panelH)
$panelPen.Dispose()

# 6. Subtle "BCH" marque at the top of the panel title ---------------------
try {
    $font = New-Object System.Drawing.Font 'Cinzel', 7, ([System.Drawing.FontStyle]::Bold)
} catch {
    $font = New-Object System.Drawing.Font 'Georgia', 7, ([System.Drawing.FontStyle]::Bold)
}
$txtBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 230, 220, 210))
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = [System.Drawing.StringAlignment]::Center
$g.DrawString('BLOODCRAFT HUB', $font, $txtBrush, ($panelX + $panelW / 2.0), ($panelY + 3.0), $fmt)
$txtBrush.Dispose()
$font.Dispose()
$fmt.Dispose()

# Save ---------------------------------------------------------------------
$dir = Split-Path -Parent $OutputPath
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
$bmp.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose()
$bmp.Dispose()

Write-Host "Wrote $OutputPath ($([System.IO.FileInfo]::new($OutputPath).Length) bytes)" -ForegroundColor Green
