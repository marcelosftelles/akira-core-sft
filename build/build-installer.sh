#!/usr/bin/env bash
# Gera dist/AkiraCore-Setup-<versão>.exe: testa, publica o app para win-x64 e empacota com NSIS.
# Roda em Linux, macOS, WSL ou Git Bash. Precisa do .NET 8 SDK e do makensis (NSIS 3).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/src/AkiraCore.App/AkiraCore.App.csproj"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -n 1)"
publish="$root/artifacts/publish"
bootstrapper="$root/artifacts/MicrosoftEdgeWebview2Setup.exe"
setup="$root/dist/AkiraCore-Setup-$version.exe"

if [[ -z "$version" ]]; then
  echo "Não achei <Version> em $project" >&2
  exit 1
fi
# No CI, a tag precisa bater com a versão do projeto (v1.2.3 ↔ 1.2.3)
if [[ "${GITHUB_REF_TYPE:-}" == "tag" && "${GITHUB_REF_NAME:-}" != "v$version" ]]; then
  echo "A tag ${GITHUB_REF_NAME} não bate com a versão $version do projeto" >&2
  exit 1
fi
command -v makensis >/dev/null || { echo "Instale o NSIS (apt install nsis / winget install NSIS.NSIS)" >&2; exit 1; }

echo "==> Testes"
dotnet test "$root/tests/AkiraCore.Core.Tests" -c Release --nologo -v quiet

echo "==> Publicando AkiraCore $version (win-x64, arquivo único, sem precisar de .NET instalado)"
rm -rf "$publish"
dotnet publish "$project" -c Release -r win-x64 --self-contained --nologo -v quiet \
  -p:PublishSingleFile=true -p:DebugType=none -p:PublishReferencesDocumentationFiles=false \
  -o "$publish"

# O bootstrapper do WebView2 (~2 MB) vai dentro do instalador quando der para baixar;
# sem ele, o instalador só oferece o link de download se o runtime faltar.
nsis_defines=()
if [[ ! -s "$bootstrapper" ]]; then
  curl -fsSL --retry 2 -o "$bootstrapper" "https://go.microsoft.com/fwlink/p/?LinkId=2124703" \
    || { rm -f "$bootstrapper"; echo "Aviso: não baixei o bootstrapper do WebView2; o instalador vai só indicar o link."; }
fi
if [[ -s "$bootstrapper" ]]; then
  nsis_defines+=("-DWEBVIEW2_BOOTSTRAPPER=$bootstrapper")
fi

echo "==> Montando o instalador"
mkdir -p "$(dirname "$setup")"
makensis -V2 -INPUTCHARSET UTF8 \
  "-DVERSION=$version" "-DPUBLISH_DIR=$publish" "-DOUT_FILE=$setup" ${nsis_defines[@]+"${nsis_defines[@]}"} \
  "$root/installer/AkiraCore.nsi"

echo "==> Pronto: $setup"
ls -l "$setup"
if command -v sha256sum >/dev/null; then sha256sum "$setup"; fi
