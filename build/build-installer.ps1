# Gera dist\AkiraCore-Setup-<versão>.exe no Windows: testa, publica o app e empacota com NSIS.
# Pré-requisitos: .NET 8 SDK e NSIS 3 (winget install NSIS.NSIS).
# Uso: powershell -ExecutionPolicy Bypass -File build\build-installer.ps1
# $env:REQUIRE_WEBVIEW2_BOOTSTRAPPER = '1' faz o build falhar se o bootstrapper do WebView2 não vier junto.
# Salvo em UTF-8 com BOM: sem ele o Windows PowerShell 5.1 estraga os acentos.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\AkiraCore.App\AkiraCore.App.csproj'
$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Não achei <Version> em $project" }

# Windows só aceita 4 números nos metadados: 1.2.3-beta.1 vira 1.2.3.0
$parts = @(($version -split '[-+]')[0] -split '\.') + @('0', '0', '0', '0')
$viVersion = ($parts[0..3] | ForEach-Object { if ($_ -match '^\d+$') { $_ } else { '0' } }) -join '.'

$publish = Join-Path $root 'artifacts\publish'
$filesNsh = Join-Path $root 'artifacts\installer-files.nsh'
$bootstrapper = Join-Path $root 'artifacts\MicrosoftEdgeWebview2Setup.exe'
$setup = Join-Path $root "dist\AkiraCore-Setup-$version.exe"
$bootstrapperUrl = 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'

$makensis = (Get-Command makensis -ErrorAction SilentlyContinue).Source
if (-not $makensis) {
    $makensis = @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $makensis) { throw 'NSIS não encontrado. Instale com: winget install NSIS.NSIS' }

Write-Host '==> Testes'
dotnet test (Join-Path $root 'tests\AkiraCore.Core.Tests') -c Release --nologo -v quiet
if ($LASTEXITCODE) { throw 'Testes falharam' }

Write-Host "==> Publicando AkiraCore $version (win-x64, autossuficiente: não precisa de .NET instalado)"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained --nologo -v quiet `
    -p:PublishSingleFile=true -p:DebugType=none -p:PublishReferencesDocumentationFiles=false -o $publish
if ($LASTEXITCODE) { throw 'Publicação falhou' }

# Lista exata do que o instalador coloca e do que o desinstalador tira
Write-Host '==> Gerando a lista de arquivos'
$files = Get-ChildItem $publish -Recurse -File | ForEach-Object { $_.FullName.Substring($publish.Length + 1) } | Sort-Object
$dirs = Get-ChildItem $publish -Recurse -Directory | ForEach-Object { $_.FullName.Substring($publish.Length + 1) } |
    Sort-Object -Descending
$lines = @('; Gerado por build/build-installer.ps1. Não edite.', '!macro INSTALL_APP_FILES', '  SetOutPath "$INSTDIR"')
$currentDir = ''
foreach ($rel in $files) {
    $dir = Split-Path $rel -Parent
    if ($dir -ne $currentDir) {
        $lines += if ($dir) { "  SetOutPath `"`$INSTDIR\$dir`"" } else { '  SetOutPath "$INSTDIR"' }
        $currentDir = $dir
    }
    $lines += "  File `"$((Join-Path $publish $rel).Replace('$', '$$'))`""
}
$lines += '!macroend', '!macro UNINSTALL_APP_FILES'
$lines += $files | ForEach-Object { "  Delete `"`$INSTDIR\$_`"" }
$lines += $dirs | ForEach-Object { "  RMDir `"`$INSTDIR\$_`"" }
$lines += '!macroend'
[IO.File]::WriteAllLines($filesNsh, $lines, (New-Object Text.UTF8Encoding $true))
Write-Host "    $($files.Count) arquivos"

# Bootstrapper do WebView2 (~2 MB), baixado direto da Microsoft. Só entra se for um .exe de verdade.
function Test-Bootstrapper([string] $path) {
    if (-not (Test-Path $path)) { return $false }
    $bytes = [IO.File]::ReadAllBytes($path)
    return $bytes.Length -gt 100000 -and $bytes[0] -eq 0x4D -and $bytes[1] -eq 0x5A
}
if ((Test-Path $bootstrapper) -and -not (Test-Bootstrapper $bootstrapper)) { Remove-Item $bootstrapper -Force }
if (-not (Test-Path $bootstrapper)) {
    $part = "$bootstrapper.part"
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 'Tls12'
        Invoke-WebRequest $bootstrapperUrl -OutFile $part -UseBasicParsing
        if (Test-Bootstrapper $part) { Move-Item $part $bootstrapper -Force }
    } catch {
        Write-Warning "Não baixei o bootstrapper do WebView2: $($_.Exception.Message)"
    } finally {
        Remove-Item $part -Force -ErrorAction SilentlyContinue
    }
}

$defines = @("-DVERSION=$version", "-DVI_VERSION=$viVersion", "-DFILES_NSH=$filesNsh", "-DOUT_FILE=$setup")
if (Test-Path $bootstrapper) {
    $defines += "-DWEBVIEW2_BOOTSTRAPPER=$bootstrapper"
    $webview2Note = 'com o bootstrapper do WebView2'
} elseif ($env:REQUIRE_WEBVIEW2_BOOTSTRAPPER) {
    throw "Não consegui baixar o bootstrapper do WebView2 de $bootstrapperUrl"
} else {
    Write-Warning 'Sem o bootstrapper do WebView2; se o runtime faltar, o instalador só indica a página de download.'
    $webview2Note = 'SEM o bootstrapper do WebView2'
}

Write-Host '==> Montando o instalador'
New-Item -ItemType Directory -Force (Split-Path $setup) | Out-Null
& $makensis -V2 -INPUTCHARSET UTF8 @defines (Join-Path $root 'installer\AkiraCore.nsi')
if ($LASTEXITCODE) { throw 'makensis falhou' }

Write-Host "==> Pronto ($webview2Note): $setup"
Get-FileHash $setup -Algorithm SHA256 | Format-List
