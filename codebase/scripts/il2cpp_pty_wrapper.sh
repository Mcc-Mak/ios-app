#!/bin/sh
# il2cpp_pty_wrapper.sh — 以 Python PTY 包裹 IL2CPP,修正 .NET ConsolePal 崩潰
#
# Tuanjie 2022.3 的 IL2CPP(.NET/Mono 應用程式)在無 TTY 的 CI 管道環境下,
# System.ConsolePal 靜態建構式崩潰(OutOfMemoryException:
# Array dimensions exceeded supported range)。原因:.NET 嘗試 ioctl(TIOCGWINSZ)
# 查詢終端尺寸,在管道環境下失敗並使用未初始化的垃圾值配置陣列。
#
# macOS BSD script(1) 雖提供 PTY,但不保證設定有效視窗尺寸(可能為 0x0),
# .NET 仍會崩潰。此 wrapper 改用 Python pty.spawn + 顯式 ioctl(TIOCSWINSZ)
# 設定 24x80 視窗尺寸,確保 .NET ConsolePal 取得有效值。
#
# 安裝方式(由 CI build:ipa job 執行):
#   mv il2cpp il2cpp.real
#   chmod +x il2cpp.real
#   cp il2cpp_pty_wrapper.sh il2cpp
#   chmod +x il2cpp
#
# 建置階段腳本呼叫 "$IL2CPP" → wrapper → python3 pty.spawn → il2cpp.real

DIR="$(dirname "$0")"
export TERM=dumb
export COLUMNS=80
export LINES=24

exec python3 -c "
import os, pty, sys, fcntl, termios, struct

# 設定 PTY 視窗尺寸為 24 列 x 80 行(避免 .NET 取得 0x0 或垃圾值)
winsize = struct.pack('HHHH', 24, 80, 0, 0)

def set_winsize(fd):
    fcntl.ioctl(fd, termios.TIOCSWINSZ, winsize)

# pty.spawn:建立 PTY,fork,子行程以 PTY 為 stdin/stdout/stderr
# set_winsize 回呼在父行程的 PTY master 設定視窗尺寸
status = pty.spawn(sys.argv[1:], set_winsize)

# 正確傳遞子行程退出碼
if os.WIFEXITED(status):
    sys.exit(os.WEXITSTATUS(status))
elif os.WIFSIGNALED(status):
    sys.exit(128 + os.WTERMSIG(status))
else:
    sys.exit(1)
" "$DIR/il2cpp.real" "$@"
