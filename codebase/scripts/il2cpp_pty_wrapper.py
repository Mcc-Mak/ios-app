#!/usr/bin/env python3
# il2cpp_pty_wrapper.py — 以 PTY 包裹 IL2CPP,修正 .NET ConsolePal 崩潰「且」不再 hang
#
# 問題(崩潰):
#   Tuanjie 2022.3 的 il2cpp 為 .NET 應用程式。在無 TTY 的 CI 管道環境下,
#   System.ConsolePal 靜態建構式呼叫 ioctl(TIOCGWINSZ) 查詢終端尺寸;在 pipe 上
#   失敗,改用未初始化的垃圾值配置陣列 → OutOfMemoryException:
#   Array dimensions exceeded supported range → xcodebuild ARCHIVE FAILED。
#
# 問題(hang,前版 pty.spawn 的缺陷):
#   前版以 Python pty.spawn 包裹。pty.spawn 會把子行程的 stdin/stdout/stderr 全部
#   接到 PTY,並以「無逾時」的 select 同時監看 master 與父行程 stdin。在 CI 中
#   父行程 stdin 無資料、il2cpp 又讀 stdin → 子行程永久阻塞在 PTY 讀取 → master
#   永不可讀 → select 永不回返 → 整個 job hang 到 6 小時上限被取消(實測 run
#   44498856814:09:44 起零輸出 6 小時,取消時遺留 orphan Python 行程)。
#
# 解法:
#   - stdout / stderr = PTY slave:讓 ConsolePal 的 TIOCGWINSZ 成功(回傳 24x80),
#     修正崩潰。
#   - stdin = /dev/null:讀取立即回傳 EOF,il2cpp 不會阻塞在 stdin 讀取 → 修正 hang。
#   - 父行程以阻塞式 read 排空 master → 真實 stdout;EOF/EIO 時結束並 waitpid。
#
# 安裝(由 CI build:ipa job 執行):
#   mv il2cpp il2cpp.real && chmod +x il2cpp.real
#   cp il2cpp_pty_wrapper.py il2cpp && chmod +x il2cpp
#
# 呼叫鏈:建置階段腳本 → il2cpp(本 wrapper)→ fork → il2cpp.real <原始參數>

import os
import pty
import fcntl
import termios
import struct
import sys
import errno

# 找出真實 il2cpp 二進位檔(與本 wrapper 同目錄的 il2cpp.real)
script_dir = os.path.dirname(os.path.abspath(sys.argv[0]))
real = os.path.join(script_dir, "il2cpp.real")
real_args = [real] + sys.argv[1:]

# 配置 PTY 並設定 24 列 × 80 行視窗尺寸(讓 ConsolePal 的 TIOCGWINSZ 成功)
master, slave = pty.openpty()
fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 24, 80, 0, 0))

pid = os.fork()
if pid == 0:
    # ── 子行程(il2cpp.real)──
    try:
        os.setsid()  # 脫離任何控制終端
    except OSError:
        pass
    # stdin = /dev/null:讀取立即 EOF。關鍵——PTY stdin 會讓 il2cpp 永久阻塞(hang 根因)。
    devnull = os.open("/dev/null", os.O_RDONLY)
    os.dup2(devnull, 0)
    # stdout / stderr = PTY slave:.NET ConsolePal 對其一查詢尺寸即可成功(修正崩潰)。
    os.dup2(slave, 1)
    os.dup2(slave, 2)
    for fd in (devnull, slave, master):
        try:
            os.close(fd)
        except OSError:
            pass
    os.execv(real, real_args)
    os._exit(127)

# ── 父行程:排空 PTY master → 真實 stdout ──
os.close(slave)
while True:
    try:
        data = os.read(master, 65536)
    except OSError:
        # macOS:slave 關閉後 read master 會丟出 EIO,視為 EOF。
        break
    if not data:
        break
    # 阻塞式寫出;若 stdout(管道)暫時滿,os.write 會等待消費端讀取。
    # CI 持續消費 action log,不會死結。若 stdout 已壞則丟棄該段並繼續排空,
    # 避免子行程因 PTY 緩衝滿而阻塞。
    off = 0
    while off < len(data):
        try:
            off += os.write(1, data[off:])
        except OSError:
            break

try:
    os.close(master)
except OSError:
    pass

_, status = os.waitpid(pid, 0)
if os.WIFEXITED(status):
    sys.exit(os.WEXITSTATUS(status))
if os.WIFSIGNALED(status):
    sys.exit(128 + os.WTERMSIG(status))
sys.exit(1)
