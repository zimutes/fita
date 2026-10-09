# Gera src/Fita/Assets/fita.ico (uma cassete, nas cores do Purple Haze)
# e docs/icon.png. Tamanhos pequenos em BMP 32 bits, 256 em PNG.
param([string]$Destino = (Join-Path $PSScriptRoot '..\src\Fita\Assets\fita.ico'))

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Fill-Circle($g, $brush, [float]$cx, [float]$cy, [float]$r) {
    $g.FillEllipse($brush, [float]($cx - $r), [float]($cy - $r), [float](2 * $r), [float](2 * $r))
}

function Fill-Rounded($g, $brush, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-RoundedPath $x $y $w $h ([Math]::Max(0.5, $r))
    $g.FillPath($brush, $path)
}

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Fundo: beringela quase preta com a névoa violeta por cima (o Purple Haze do Jazmin).
    $top = [System.Drawing.Color]::FromArgb(255, 74, 26, 140)
    $bottom = [System.Drawing.Color]::FromArgb(255, 10, 3, 20)
    $bg = New-RoundedPath 0 0 $s $s ([Math]::Max(2, $s * 0.22))
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF([float]($s * 1.02), [float]($s * 1.02))), $top, $bottom)
    $g.FillPath($grad, $bg)

    $cyan = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 13, 230, 246))
    $pink = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 32, 121))
    $dark = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 8, 3, 16))
    $shell = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 22, 9, 42))

    # A cassete: corpo escuro, etiqueta em rosa néon, janela com as duas bobinas em ciano.
    Fill-Rounded $g $dark ($s * 0.10) ($s * 0.22) ($s * 0.80) ($s * 0.58) ($s * 0.07)
    Fill-Rounded $g $pink ($s * 0.16) ($s * 0.28) ($s * 0.68) ($s * 0.11) ($s * 0.025)
    Fill-Rounded $g $shell ($s * 0.24) ($s * 0.44) ($s * 0.52) ($s * 0.18) ($s * 0.09)
    $ry = $s * 0.53
    foreach ($rx in ($s * 0.36), ($s * 0.64)) {
        Fill-Circle $g $cyan $rx $ry ([Math]::Max(1.2, $s * 0.068))
        Fill-Circle $g $dark $rx $ry ([Math]::Max(0.5, $s * 0.025))
    }
    if ($s -ge 24) {
        # A peça de baixo, com os dois furos.
        $foot = New-Object System.Drawing.Drawing2D.GraphicsPath
        $foot.AddPolygon([System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF([float]($s * 0.26), [float]($s * 0.80))),
            (New-Object System.Drawing.PointF([float]($s * 0.31), [float]($s * 0.68))),
            (New-Object System.Drawing.PointF([float]($s * 0.69), [float]($s * 0.68))),
            (New-Object System.Drawing.PointF([float]($s * 0.74), [float]($s * 0.80)))))
        $g.FillPath($shell, $foot)
        Fill-Circle $g $dark ($s * 0.40) ($s * 0.745) ($s * 0.018)
        Fill-Circle $g $dark ($s * 0.60) ($s * 0.745) ($s * 0.018)
    }

    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    # BITMAPINFOHEADER (altura a dobrar: cor + máscara)
    $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int]($s * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $s; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $bw.Write((New-Object byte[] ($maskRow * $s)))
    $bw.Flush()
    return $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    if ($s -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        ,@($s, $ms.ToArray())
    } else {
        ,@($s, (Get-DibBytes $bmp))
    }
    $bmp.Dispose()
}

# O [IO.File] usa a pasta atual do processo, não a do PowerShell: resolver o caminho antes.
$Destino = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destino)
New-Item -ItemType Directory -Force -Path (Split-Path $Destino) | Out-Null
$fs = [System.IO.File]::Create($Destino)
$w = New-Object System.IO.BinaryWriter($fs)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $s = $img[0]; $data = $img[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$data.Length); $w.Write([int]$offset)
    $offset += $data.Length
}
foreach ($img in $images) { $w.Write([byte[]]$img[1]) }
$w.Close()

# docs/icon.png (para o README) e o PNG de 256 para o Linux (.desktop).
$png = Draw-Icon 256
$docs = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath((Join-Path $PSScriptRoot '..\docs\icon.png'))
New-Item -ItemType Directory -Force -Path (Split-Path $docs) | Out-Null
$png.Save($docs, [System.Drawing.Imaging.ImageFormat]::Png)
$png.Save((Join-Path (Split-Path $Destino) 'fita.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$png.Dispose()
Write-Host "Icone gerado: $Destino"
