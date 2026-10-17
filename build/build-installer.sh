#!/usr/bin/env bash
# Gera dist/AkiraCore-Setup-<versão>.exe: testa, publica o app para win-x64 e empacota com NSIS.
# Roda em Linux, macOS, WSL ou Git Bash. Precisa do .NET 8 SDK da Microsoft e do makensis (NSIS 3).
# REQUIRE_WEBVIEW2_BOOTSTRAPPER=1 faz o build falhar se o bootstrapper do WebView2 não vier junto
# (ligado sozinho em builds de tag no GitHub Actions).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$root/src/AkiraCore.App/AkiraCore.App.csproj"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$project" | head -n 1)"
publish="$root/artifacts/publish"
files_nsh="$root/artifacts/installer-files.nsh"
bootstrapper="$root/artifacts/MicrosoftEdgeWebview2Setup.exe"
setup="$root/dist/AkiraCore-Setup-$version.exe"
bootstrapper_url="https://go.microsoft.com/fwlink/p/?LinkId=2124703"

fail() { echo "Erro: $*" >&2; exit 1; }

[[ -n "$version" ]] || fail "não achei <Version> em $project"
# Windows só aceita 4 números nos metadados: 1.2.3-beta.1 vira 1.2.3.0
numeric="${version%%[-+]*}"
IFS=. read -r v1 v2 v3 v4 _ <<<"$numeric"
vi_version="${v1:-0}.${v2:-0}.${v3:-0}.${v4:-0}"
[[ "$vi_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || fail "versão '$version' não começa com números (x.y.z)"

# No CI, a tag precisa bater com a versão do projeto (v1.2.3 ↔ 1.2.3)
if [[ "${GITHUB_REF_TYPE:-}" == "tag" ]]; then
  [[ "${GITHUB_REF_NAME:-}" == "v$version" ]] || fail "a tag ${GITHUB_REF_NAME:-} não bate com a versão $version do projeto"
  REQUIRE_WEBVIEW2_BOOTSTRAPPER=1
fi

# makensis: no PATH ou, no Git Bash, onde o instalador do NSIS (winget) coloca
makensis_bin="$(command -v makensis || true)"
for candidate in "/c/Program Files (x86)/NSIS/makensis.exe" "/c/Program Files/NSIS/makensis.exe"; do
  if [[ -z "$makensis_bin" && -x "$candidate" ]]; then makensis_bin="$candidate"; fi
done
[[ -n "$makensis_bin" ]] || fail "instale o NSIS (apt install nsis, brew install makensis ou winget install NSIS.NSIS)"

# O dotnet-sdk-8.0 empacotado pelo Ubuntu/Fedora não traz os alvos de WinForms (erro MSB4019)
if [[ "$(uname -s)" == "Linux" ]]; then
  sdk_version="$(dotnet --version)"
  sdk_dir="$(dotnet --list-sdks | awk -v v="$sdk_version" '$1 == v { sub(/^[^[]*\[/, ""); sub(/\]$/, ""); print; exit }')"
  [[ -d "$sdk_dir/$sdk_version/Sdks/Microsoft.NET.Sdk.WindowsDesktop" ]] \
    || fail "o .NET SDK $sdk_version em $sdk_dir não tem o Microsoft.NET.Sdk.WindowsDesktop. Use o SDK da Microsoft (dotnet-install.sh ou packages.microsoft.com), não o da distribuição."
fi

echo "==> Testes"
dotnet test "$root/tests/AkiraCore.Core.Tests" -c Release --nologo -v quiet

echo "==> Publicando AkiraCore $version (win-x64, autossuficiente: não precisa de .NET instalado)"
rm -rf "$publish"
dotnet publish "$project" -c Release -r win-x64 --self-contained --nologo -v quiet \
  -p:PublishSingleFile=true -p:DebugType=none -p:PublishReferencesDocumentationFiles=false \
  -o "$publish"

# Lista exata do que o instalador coloca e do que o desinstalador tira
echo "==> Gerando a lista de arquivos"
nsis_escape() { printf '%s' "${1//\$/\$\$}"; }
{
  echo "; Gerado por build/build-installer.sh. Não edite."
  echo "!macro INSTALL_APP_FILES"
  current_dir="."
  echo "  SetOutPath \"\$INSTDIR\""
  while IFS= read -r rel; do
    dir="$(dirname "$rel")"
    if [[ "$dir" != "$current_dir" ]]; then
      if [[ "$dir" == "." ]]; then echo "  SetOutPath \"\$INSTDIR\""; else echo "  SetOutPath \"\$INSTDIR\\${dir//\//\\}\""; fi
      current_dir="$dir"
    fi
    echo "  File \"$(nsis_escape "$publish/$rel")\""
  done < <(cd "$publish" && find . -type f | sed 's:^\./::' | LC_ALL=C sort)
  echo "!macroend"
  echo "!macro UNINSTALL_APP_FILES"
  while IFS= read -r rel; do
    echo "  Delete \"\$INSTDIR\\${rel//\//\\}\""
  done < <(cd "$publish" && find . -type f | sed 's:^\./::' | LC_ALL=C sort)
  while IFS= read -r rel; do
    echo "  RMDir \"\$INSTDIR\\${rel//\//\\}\""
  done < <(cd "$publish" && find . -mindepth 1 -type d | sed 's:^\./::' | LC_ALL=C sort -r)
  echo "!macroend"
} > "$files_nsh"
grep -c '^  File ' "$files_nsh" | xargs -I{} echo "    {} arquivos"

# Bootstrapper do WebView2 (~2 MB), baixado direto da Microsoft. Só entra se for um .exe de verdade.
valid_bootstrapper() {
  [[ -s "$1" && "$(head -c 2 "$1")" == "MZ" && "$(wc -c < "$1")" -gt 100000 ]]
}
if [[ -e "$bootstrapper" ]] && ! valid_bootstrapper "$bootstrapper"; then
  echo "    bootstrapper em cache inválido, apagando"
  rm -f "$bootstrapper"
fi
if [[ ! -e "$bootstrapper" ]]; then
  if curl -fsSL --retry 2 -o "$bootstrapper.part" "$bootstrapper_url" && valid_bootstrapper "$bootstrapper.part"; then
    mv "$bootstrapper.part" "$bootstrapper"
  else
    rm -f "$bootstrapper.part"
  fi
fi
nsis_defines=()
if [[ -e "$bootstrapper" ]]; then
  nsis_defines+=("-DWEBVIEW2_BOOTSTRAPPER=$bootstrapper")
  webview2_note="com o bootstrapper do WebView2"
elif [[ -n "${REQUIRE_WEBVIEW2_BOOTSTRAPPER:-}" ]]; then
  fail "não consegui baixar o bootstrapper do WebView2 de $bootstrapper_url"
else
  echo "Aviso: sem o bootstrapper do WebView2; se o runtime faltar, o instalador só indica a página de download." >&2
  webview2_note="SEM o bootstrapper do WebView2"
fi

echo "==> Montando o instalador"
mkdir -p "$(dirname "$setup")"
"$makensis_bin" -V2 -INPUTCHARSET UTF8 \
  "-DVERSION=$version" "-DVI_VERSION=$vi_version" "-DFILES_NSH=$files_nsh" "-DOUT_FILE=$setup" \
  ${nsis_defines[@]+"${nsis_defines[@]}"} \
  "$root/installer/AkiraCore.nsi"

echo "==> Pronto ($webview2_note): $setup"
ls -l "$setup"
if command -v sha256sum >/dev/null; then sha256sum "$setup"; fi
