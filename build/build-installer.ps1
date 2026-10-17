# Gera dist\AkiraCore-Setup-<versão>.exe no Windows: testa, publica o app e empacota com NSIS.
# Pré-requisitos: .NET 8 SDK e NSIS 3 (winget install NSIS.NSIS).
# Uso: powershell -ExecutionPolicy Bypass -File build\build-installer.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\AkiraCore.App\AkiraCore.App.csproj'
$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Não achei <Version> em $project" }

$publish = Join-Path $root 'artifacts\publish'
$bootstrapper = Join-Path $root 'artifacts\MicrosoftEdgeWebview2Setup.exe'
$setup = Join-Path $root "dist\AkiraCore-Setup-$version.exe"

$makensis = (Get-Command makensis -ErrorAction SilentlyContinue).Source
if (-not $makensis) {
    $makensis = @("${env:ProgramFiles(x86)}\NSIS\makensis.exe", "$env:ProgramFiles\NSIS\makensis.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $makensis) { throw 'NSIS não encontrado. Instale com: winget install NSIS.NSIS' }

Write-Host '==> Testes'
dotnet test (Join-Path $root 'tests\AkiraCore.Core.Tests') -c Release --nologo -v quiet
if ($LASTEXITCODE) { throw 'Testes falharam' }

Write-Host "==> Publicando AkiraCore $version (win-x64, arquivo único)"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained --nologo -v quiet `
    -p:PublishSingleFile=true -p:DebugType=none -p:PublishReferencesDocumentationFiles=false -o $publish
if ($LASTEXITCODE) { throw 'Publicação falhou' }

$defines = @("-DVERSION=$version", "-DPUBLISH_DIR=$publish", "-DOUT_FILE=$setup")
if (-not (Test-Path $bootstrapper)) {
    try {
        Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper -UseBasicParsing
    } catch {
        Write-Warning 'Não baixei o bootstrapper do WebView2; o instalador vai só indicar o link.'
    }
}
if (Test-Path $bootstrapper) { $defines += "-DWEBVIEW2_BOOTSTRAPPER=$bootstrapper" }

Write-Host '==> Montando o instalador'
New-Item -ItemType Directory -Force (Split-Path $setup) | Out-Null
& $makensis -V2 -INPUTCHARSET UTF8 @defines (Join-Path $root 'installer\AkiraCore.nsi')
if ($LASTEXITCODE) { throw 'makensis falhou' }

Write-Host "==> Pronto: $setup"
Get-FileHash $setup -Algorithm SHA256 | Format-List
