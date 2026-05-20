#!/bin/bash
# Wrapper for Arch/CachyOS where /usr/lib is not in ldconfig path for python3.14
DIR="$(cd "$(dirname "$0")" && pwd)"
LD_LIBRARY_PATH=/usr/lib "$DIR/.venv/bin/wave-osc-gui" "$@"
