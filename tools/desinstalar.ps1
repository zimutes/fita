<#
    Remove a Fita: fecha-a, apaga a instalação, o atalho do menu Iniciar e as entradas no
    Explorador ("Abrir com" e o botão direito).

    As definições e a lista em %APPDATA%\Fita ficam, a não ser que se use -Tudo.
    Com -WhatIf mostra o que faria, sem apagar nada.
#>

[CmdletBinding(SupportsShouldProcess)]
param([Alias("All")][switch]$Tudo)

$ErrorActionPreference = "Continue"

$destino = Join-Path $env:LOCALAPPDATA "Programs\Fita"
$atalho = Join-Path ([Environment]::GetFolderPath("Programs")) "Fita.lnk"
$dados = Join-Path $env:APPDATA "Fita"
$chaves = @(
    "HKCU:\Software\Classes\Applications\Fita.exe",
    "HKCU:\Software\Classes\Directory\shell\Fita.Play",
    "HKCU:\Software\Classes\Directory\shell\Fita.Enqueue",
    "HKCU:\Software\Classes\SystemFileAssociations\audio\shell\Fita.Play",
    "HKCU:\Software\Classes\SystemFileAssociations\audio\shell\Fita.Enqueue",
    "HKCU:\Software\Classes\Fita.Audio",
    "HKCU:\Software\Classes\Fita.Playlist",
    "HKCU:\Software\zimutek\Fita"
)

function Feito($texto) { Write-Host "  $texto" -ForegroundColor Green }

# Apagar uma junção pelo caminho apaga o destino real: nesse caso não se toca em nada.
function Remover-Pasta($pasta, $descricao) {
    if (-not (Test-Path -LiteralPath $pasta)) { return }
    $item = Get-Item -LiteralPath $pasta -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        Write-Host "  $pasta é uma junção/ligação: não foi apagada" -ForegroundColor Yellow
        return
    }
    if ($PSCmdlet.ShouldProcess($pasta, "Apagar")) {
        Remove-Item -LiteralPath $pasta -Recurse -Force
        Feito $descricao
    }
}

Write-Host "`nFita — remoção`n" -ForegroundColor White

$processos = Get-Process Fita -ErrorAction SilentlyContinue
if ($processos -and $PSCmdlet.ShouldProcess("Fita.exe", "Fechar")) {
    $processos | Stop-Process -Force
    Start-Sleep -Milliseconds 700
    Feito "app fechada"
}

if ((Test-Path -LiteralPath $atalho) -and $PSCmdlet.ShouldProcess($atalho, "Apagar")) {
    Remove-Item -LiteralPath $atalho -Force
    Feito "atalho do menu Iniciar removido"
}

foreach ($chave in $chaves) {
    if ((Test-Path -LiteralPath $chave) -and $PSCmdlet.ShouldProcess($chave, "Apagar")) {
        Remove-Item -LiteralPath $chave -Recurse -Force
        Feito "registo removido: $(Split-Path $chave -Leaf)"
    }
}

# Nas extensões só se tira o valor da Fita: o resto do OpenWithProgids é de outras apps.
$progIds = "Fita.Audio", "Fita.Playlist"
$listas = Get-ChildItem "HKCU:\Software\Classes" -ErrorAction SilentlyContinue |
    Where-Object { $_.PSChildName.StartsWith(".") } |
    ForEach-Object { Join-Path $_.PSPath "OpenWithProgids" } |
    Where-Object { Test-Path -LiteralPath $_ }
foreach ($lista in $listas) {
    $item = Get-Item -LiteralPath $lista
    foreach ($nome in $progIds | Where-Object { $item.GetValueNames() -contains $_ }) {
        if ($PSCmdlet.ShouldProcess("$($item.Name) ($nome)", "Apagar")) {
            Remove-ItemProperty -LiteralPath $lista -Name $nome
        }
    }
}
$registadas = "HKCU:\Software\RegisteredApplications"
if ((Test-Path -LiteralPath $registadas) -and (Get-Item -LiteralPath $registadas).GetValueNames() -contains "Fita" -and
        $PSCmdlet.ShouldProcess("$registadas (Fita)", "Apagar")) {
    Remove-ItemProperty -LiteralPath $registadas -Name "Fita"
    Feito "registo removido: Abrir com e Aplicações predefinidas"
}
$zimutek = "HKCU:\Software\zimutek"
if ((Test-Path -LiteralPath $zimutek) -and -not (Get-ChildItem -LiteralPath $zimutek) -and
        (Get-Item -LiteralPath $zimutek).ValueCount -eq 0 -and $PSCmdlet.ShouldProcess($zimutek, "Apagar")) {
    Remove-Item -LiteralPath $zimutek
}

Remover-Pasta $destino "ficheiros removidos"

if ($Tudo) {
    Remover-Pasta $dados "definições e lista removidas"
}
elseif (Test-Path -LiteralPath $dados) {
    Write-Host "  definições mantidas em $dados (usa -Tudo para as apagar)" -ForegroundColor Yellow
}

if ($WhatIfPreference) { Write-Host "`nSimulação: nada foi apagado.`n" -ForegroundColor White }
else { Write-Host "`nRemovido.`n" -ForegroundColor White }
