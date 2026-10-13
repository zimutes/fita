# Compila a APK da Fita para Android com a toolchain portátil de C:\Android (o .NET com o
# workload android em C:\Android\dotnet, o JDK e o SDK). -Emulador: para o emulador (x86_64);
# sem ele, para os telemóveis (arm64). -Release: APK assinada com a chave de debug, pronta a
# instalar com "adb install -r".
param([switch]$Emulador, [switch]$Release)
$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:JAVA_HOME = "C:\Android\jdk21"
$env:ANDROID_HOME = "C:\Android\Sdk"
$projeto = Join-Path $PSScriptRoot "..\..\src\Fita.Android"
$rid = if ($Emulador) { "android-x64" } else { "android-arm64" }
$cfg = if ($Release) { "Release" } else { "Debug" }
# Uma arquitetura só por APK (RuntimeIdentifiers no plural, senão o .NET junta as outras).
& C:\Android\dotnet\dotnet.exe build $projeto -c $cfg -p:RuntimeIdentifier=$rid -p:RuntimeIdentifiers=$rid `
    -p:JavaSdkDirectory=C:\Android\jdk21 -p:AndroidSdkDirectory=C:\Android\Sdk -nologo -v q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-ChildItem (Join-Path $projeto "bin\$cfg\net10.0-android\$rid") -Filter *-Signed.apk | Select-Object -ExpandProperty FullName
