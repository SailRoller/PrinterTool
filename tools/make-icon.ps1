# 生成 app.ico：绘制打印机图形，输出多尺寸图标（16/32/48 为 BMP DIB，256 为 PNG 压缩）
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$icoPath = Join-Path $root "app.ico"

function New-RoundRectPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-PrinterBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    $bodyBlue     = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 58, 119, 216))
    $bodyDark     = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 34, 84, 164))
    $white        = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $paperBorder  = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 176, 190, 208))
    $trayGray     = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 226, 232, 240))
    $ledGreen     = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 62, 213, 152))

    # 顶部出纸（白纸）
    $g.FillPath($white, (New-RoundRectPath (66*$s) (26*$s) (124*$s) (70*$s) (8*$s)))
    $g.DrawPath($paperBorder, (New-RoundRectPath (66*$s) (26*$s) (124*$s) (70*$s) (8*$s)))

    # 打印机机身
    $g.FillPath($bodyBlue, (New-RoundRectPath (30*$s) (92*$s) (196*$s) (104*$s) (14*$s)))

    # 出纸口（深色横槽）
    $g.FillRectangle($bodyDark, (78*$s), (92*$s), 100*$s, 12*$s)

    # 按键与指示灯
    $g.FillPath($bodyDark, (New-RoundRectPath (46*$s) (122*$s) (44*$s) (16*$s) (8*$s)))
    $g.FillEllipse($ledGreen, (188*$s), (122*$s), 16*$s, 16*$s)

    # 底部进纸托盘（浅灰）
    $g.FillPath($trayGray, (New-RoundRectPath (74*$s) (176*$s) (108*$s) (42*$s) (8*$s)))
    $g.DrawPath($paperBorder, (New-RoundRectPath (74*$s) (176*$s) (108*$s) (42*$s) (8*$s)))

    $g.Dispose()
    return $bmp
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

function Get-IconDib([System.Drawing.Bitmap]$bmp) {
    $rect = New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $bmp.Height)
    $bd = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $rowBytes = $bmp.Width * 4
    $pixelBytes = New-Object byte[] ($rowBytes * $bmp.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($bd.Scan0, $pixelBytes, 0, $pixelBytes.Length)
    $bmp.UnlockBits($bd)

    # ICO 的 DIB 为自底向上，翻转行序
    $flipped = New-Object byte[] $pixelBytes.Length
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        [Array]::Copy($pixelBytes, ($y * $rowBytes), $flipped, (($bmp.Height - 1 - $y) * $rowBytes), $rowBytes)
    }

    # AND 掩码（1bpp，32 位对齐，全 0，透明度由 alpha 表达）
    $maskRowBytes = [int][Math]::Ceiling($bmp.Width / 32.0) * 4
    $maskBytes = New-Object byte[] ($maskRowBytes * $bmp.Height)

    $header = New-Object byte[] 40
    [BitConverter]::GetBytes([UInt32]40).CopyTo($header, 0)                    # biSize
    [BitConverter]::GetBytes([Int32]$bmp.Width).CopyTo($header, 4)             # biWidth
    [BitConverter]::GetBytes([Int32]($bmp.Height * 2)).CopyTo($header, 8)      # biHeight = XOR + AND
    [BitConverter]::GetBytes([UInt16]1).CopyTo($header, 12)                    # biPlanes
    [BitConverter]::GetBytes([UInt16]32).CopyTo($header, 14)                   # biBitCount

    $list = New-Object System.Collections.Generic.List[byte]
    $list.AddRange($header)
    $list.AddRange($flipped)
    $list.AddRange($maskBytes)
    return ,$list.ToArray()
}

# ── 组装 ICO ──
$entries = @()   # 每项: @{ Size=..; Bytes=byte[] }
foreach ($size in @(16, 32, 48)) {
    $bmp = New-PrinterBitmap $size
    $entries += ,@{ Size = $size; Bytes = (Get-IconDib $bmp) }
    $bmp.Dispose()
}
$bmp256 = New-PrinterBitmap 256
$entries += ,@{ Size = 256; Bytes = (Get-PngBytes $bmp256) }
$bmp256.Dispose()

$out = New-Object System.Collections.Generic.List[byte]
# ICONDIR: reserved=0, type=1(icon), count
$out.AddRange([BitConverter]::GetBytes([UInt16]0))
$out.AddRange([BitConverter]::GetBytes([UInt16]1))
$out.AddRange([BitConverter]::GetBytes([UInt16]$entries.Count))

$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $w = if ($e.Size -ge 256) { [byte]0 } else { [byte]$e.Size }
    $entry = New-Object System.Collections.Generic.List[byte]
    $entry.Add($w)                                                    # 宽
    $entry.Add($w)                                                    # 高
    $entry.Add([byte]0)                                               # 调色板数
    $entry.Add([byte]0)                                               # 保留
    $entry.AddRange([BitConverter]::GetBytes([UInt16]1))              # planes
    $entry.AddRange([BitConverter]::GetBytes([UInt16]32))             # bitcount
    $entry.AddRange([BitConverter]::GetBytes([UInt32]$e.Bytes.Length))# 数据长度
    $entry.AddRange([BitConverter]::GetBytes([UInt32]$offset))        # 数据偏移
    $out.AddRange($entry.ToArray())
    $offset += $e.Bytes.Length
}
foreach ($e in $entries) { $out.AddRange($e.Bytes) }

[System.IO.File]::WriteAllBytes($icoPath, $out.ToArray())
Write-Host ("已生成 {0}（{1:N0} 字节）" -f $icoPath, (Get-Item $icoPath).Length)

