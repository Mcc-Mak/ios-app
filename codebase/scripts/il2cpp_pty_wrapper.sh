#!/bin/sh
# il2cpp_pty_wrapper.sh — 修正 .NET 6 ReadyToRun 在 macOS arm64 VM 崩潰
#
# 根因(dotnet/runtime#64103):.NET 6 在 macOS Apple Silicon(arm64)虛擬機中,
# ReadyToRun(R2R)編譯的程式碼傳遞 variadic 引數時使用暫存器而非堆疊,違反 Apple
# arm64 ABI。ConsolePal..cctor 建立物件陣列時觸發此 bug → OutOfMemoryException:
# Array dimensions exceeded supported range → ARCHIVE FAILED。
# GitHub Actions macos-15 runner 為 arm64 虛擬機,觸發此 bug。
# (此 bug 已於 .NET 6.0.5 修復,但 Tuanjie 的 il2cpp 可能使用較舊 .NET 6.0.x。)
#
# 先前嘗試(PTY / controlling terminal / TIOCSWINSZ / TERM)均未修正——證實崩潰與
# 終端完全無關,純為 .NET arm64 R2R bug。舊 pty.spawn 版本的「6 小時 hang」實為
# pty._copy 輸出死結遮蔽了同樣的即時崩潰。
#
# 修正:COMPlus_ReadyToRun=0 停用 ReadyToRun,改用 JIT 編譯(避開 R2R bug)。
# 另設 stdin=/dev/null,避免 il2cpp 讀 stdin 阻塞(hang 保險)。
#
# 安裝(CI build:ipa job):
#   mv il2cpp il2cpp.real && chmod +x il2cpp.real
#   cp il2cpp_pty_wrapper.sh il2cpp && chmod +x il2cpp
DIR="$(dirname "$0")"
export COMPlus_ReadyToRun=0
export DOTNET_ReadyToRun=0
exec "$DIR/il2cpp.real" "$@" </dev/null
