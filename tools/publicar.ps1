# Compila a Fita num único .exe e instala-a para o utilizador atual:
#   %LOCALAPPDATA%\Programs\Fita\Fita.exe
#   + atalho no menu Iniciar, "Abrir com" nos formatos de áudio e, no botão direito das pastas
#     e dos ficheiros de áudio, «Tocar na Fita» e «Juntar à lista da Fita» (como no Winamp).
# Usa o runtime .NET 10 já instalado. Para um .exe que corre em qualquer PC (sem .NET), usar
# -Autonomo. -SemMenus não mexe no botão direito do Explorador. -SemInstalar só compila.
param([switch]$Autonomo, [switch]$SemInstalar, [switch]$SemMenus)

$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot
$projeto = Join-Path $raiz 'src\Fita\Fita.csproj'
$saida = Join-Path $raiz 'publicar'

# As bibliotecas nativas (Skia, HarfBuzz, OpenAL) vão dentro do .exe e saem para uma pasta
# temporária na primeira vez que corre.
$dotnetArgs = @('publish', $projeto, '-c', 'Release', '-r', 'win-x64', '-o', $saida,
          '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none')
if ($Autonomo) { $dotnetArgs += @('--self-contained', '-p:EnableCompressionInSingleFile=true') }
else { $dotnetArgs += @('--no-self-contained') }

if (Test-Path -LiteralPath $saida) { Remove-Item -LiteralPath $saida -Recurse -Force }
& dotnet @dotnetArgs
if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou.' }
Write-Host "Publicado em $saida"
if ($SemInstalar) { return }

$destino = Join-Path $env:LOCALAPPDATA 'Programs\Fita'
# Se estiver aberta, fecha-se com jeito (para gravar a lista e as definições) e só depois à força.
foreach ($p in Get-Process Fita -ErrorAction SilentlyContinue) {
    $p.CloseMainWindow() | Out-Null
    if (-not $p.WaitForExit(4000)) { $p.Kill() }
}
Start-Sleep -Milliseconds 500
New-Item -ItemType Directory -Force -Path $destino | Out-Null
Copy-Item -LiteralPath (Join-Path $saida 'Fita.exe') -Destination $destino -Force
$exe = Join-Path $destino 'Fita.exe'

$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Fita.lnk'))
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $destino
$lnk.Description = 'Music player · by zimutek'
$lnk.Save()

# "Abrir com": a Fita aparece na lista para estes formatos (não muda a app por omissão).
$formatos = '.mp3', '.mp2', '.flac', '.ogg', '.oga', '.opus', '.wav', '.w64', '.aif', '.aiff', '.aifc', '.m4a', '.m4b',
    '.aac', '.wma', '.ape', '.wv', '.mka', '.mpc', '.tta', '.ac3', '.dts', '.dsf', '.dff', '.caf', '.tak',
    '.mod', '.xm', '.it', '.s3m', '.m3u', '.m3u8', '.pls'
$app = 'HKCU:\Software\Classes\Applications\Fita.exe'
New-Item -Path "$app\shell\open\command" -Force | Out-Null
Set-Item -Path "$app\shell\open\command" -Value "`"$exe`" `"%1`""
Set-ItemProperty -Path $app -Name 'FriendlyAppName' -Value 'Fita'
New-Item -Path "$app\DefaultIcon" -Force | Out-Null
Set-Item -Path "$app\DefaultIcon" -Value "`"$exe`",0"
New-Item -Path "$app\SupportedTypes" -Force | Out-Null
foreach ($f in $formatos) { Set-ItemProperty -Path "$app\SupportedTypes" -Name $f -Value '' }

if (-not $SemMenus) {
    $pt = (Get-UICulture).TwoLetterISOLanguageName -eq 'pt'
    $tocar = if ($pt) { 'Tocar na Fita' } else { 'Play in Fita' }
    $juntar = if ($pt) { 'Juntar à lista da Fita' } else { 'Add to Fita playlist' }
    foreach ($base in 'HKCU:\Software\Classes\Directory\shell', 'HKCU:\Software\Classes\SystemFileAssociations\audio\shell') {
        foreach ($verbo in @(@('Fita.Play', $tocar, "`"$exe`" `"%1`""), @('Fita.Enqueue', $juntar, "`"$exe`" --enqueue `"%1`""))) {
            $chave = Join-Path $base $verbo[0]
            New-Item -Path "$chave\command" -Force | Out-Null
            Set-ItemProperty -Path $chave -Name 'MUIVerb' -Value $verbo[1]
            Set-ItemProperty -Path $chave -Name 'Icon' -Value "`"$exe`",0"
            Set-Item -Path "$chave\command" -Value $verbo[2]
        }
    }
}

Write-Host "Instalado em $destino (atalho no menu Iniciar; «Abrir com» e botão direito no Explorador)."
