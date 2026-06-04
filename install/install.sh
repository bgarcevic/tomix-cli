#!/bin/sh
# mdl installer — usage:
#   curl -LsSf https://raw.githubusercontent.com/bgarcevic/mdl-cli/main/install/install.sh | sh
# Pin a version:    MDL_VERSION=0.2.0 curl -LsSf ... | sh
# Custom location:  MDL_INSTALL=~/bin  curl -LsSf ... | sh
set -eu

REPO="bgarcevic/mdl-cli"
VERSION="${MDL_VERSION:-latest}"
INSTALL_DIR="${MDL_INSTALL:-$HOME/.local/bin}"

err() { printf '\033[31merror\033[0m: %s\n' "$1" >&2; exit 1; }
info() { printf '\033[36m%s\033[0m %s\n' "$1" "$2" >&2; }

# --- detect platform -------------------------------------------------------
os="$(uname -s)"
arch="$(uname -m)"
case "$os" in
  Linux)  os="linux" ;;
  Darwin) os="osx" ;;
  *) err "unsupported OS: $os (use the PowerShell installer on Windows)" ;;
esac
case "$arch" in
  x86_64|amd64)  arch="x64" ;;
  aarch64|arm64) arch="arm64" ;;
  *) err "unsupported architecture: $arch" ;;
esac
rid="${os}-${arch}"
asset="mdl-${rid}.tar.gz"

# --- resolve URLs ----------------------------------------------------------
if [ "$VERSION" = "latest" ]; then
  base="https://github.com/${REPO}/releases/latest/download"
else
  base="https://github.com/${REPO}/releases/download/v${VERSION#v}"
fi

command -v curl >/dev/null 2>&1 || err "curl is required"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

info "downloading" "${base}/${asset}"
curl -fsSL "${base}/${asset}" -o "${tmp}/${asset}" \
  || err "download failed — check that the release exists and includes ${asset}"
curl -fsSL "${base}/checksums.txt" -o "${tmp}/checksums.txt" \
  || err "could not download checksums.txt"

# --- verify checksum -------------------------------------------------------
expected="$(grep "  ${asset}\$" "${tmp}/checksums.txt" | awk '{print $1}')"
[ -n "$expected" ] || err "no checksum entry for ${asset}"
if command -v sha256sum >/dev/null 2>&1; then
  actual="$(sha256sum "${tmp}/${asset}" | awk '{print $1}')"
else
  actual="$(shasum -a 256 "${tmp}/${asset}" | awk '{print $1}')"
fi
[ "$expected" = "$actual" ] || err "checksum mismatch for ${asset}"
info "verified" "sha256 OK"

# --- install ---------------------------------------------------------------
tar -xzf "${tmp}/${asset}" -C "$tmp"
mkdir -p "$INSTALL_DIR"
install -m 755 "${tmp}/mdl-${rid}/mdl" "${INSTALL_DIR}/mdl"
info "installed" "${INSTALL_DIR}/mdl"

# --- PATH hint -------------------------------------------------------------
case ":$PATH:" in
  *":${INSTALL_DIR}:"*) ;;
  *)
    printf '\n%s is not on your PATH. Add it with:\n' "$INSTALL_DIR" >&2
    shell_name="$(basename "${SHELL:-sh}")"
    case "$shell_name" in
      zsh)  printf '  echo '\''export PATH="%s:$PATH"'\'' >> ~/.zshrc\n' "$INSTALL_DIR" >&2 ;;
      fish) printf '  fish_add_path %s\n' "$INSTALL_DIR" >&2 ;;
      *)    printf '  echo '\''export PATH="%s:$PATH"'\'' >> ~/.bashrc\n' "$INSTALL_DIR" >&2 ;;
    esac
    ;;
esac

"${INSTALL_DIR}/mdl" --version >&2 2>/dev/null || true
printf '\nRun `mdl doctor` to verify your setup.\n' >&2
