#!/usr/bin/env python3
# il2cpp_pty_wrapper.py — PTY wrapper for Tuanjie 的 il2cpp(.NET 應用程式)
#
# 同時修正兩個問題:
#
# (1) ConsolePal 崩潰(ARCHIVE FAILED):
#     Tuanjie 的 il2cpp 為 .NET 應用程式。在無 TTY 的 CI 管道下,System.ConsolePal
#     靜態建構式(.cctor)以 ioctl(TIOCGWINSZ) 查詢終端尺寸;在 pipe(非 TTY)上
#     失敗,改用未初始化的垃圾值配置陣列 → OutOfMemoryException:
#     Array dimensions exceeded supported range。實測 .cctor 查詢的是 stdin(fd 0),
#     故 fd 0 必須是 TTY(僅把 stdout/stderr 接 PTY 仍會崩潰)。
#     解法:fd 0/1/2 全部接同一個 PTY slave(24×80),讓 ioctl 成功。
#
# (2) 輸出死結 hang(前版 pty.spawn 的 6 小時 hang,run 44498856814):
#     pty.spawn 以「阻塞式」os.write 將 PTY 輸出排至真實 stdout。xcodebuild 會緩衝
#     建置階段(Run Script phase)的輸出、不即時讀取該 pipe → os.write 阻塞 →
#     pty.spawn 停止讀 master → PTY 緩衝(64KB)填滿 → il2cpp 阻塞於寫入 → 死結
#     6 小時。此外 pty.spawn 把子行程 stdin 也接 PTY,且 select 無逾時,進一步加劇
#     阻塞風險。
#     解法:真實 stdout 設為「非阻塞」,內部以 pending 緩衝;持續讀 master(釋放 PTY
#     緩衝,il2cpp 永不阻塞於寫入),待 stdout 可寫再排空 pending。
#
# 額外保險:15 分鐘無輸出看門狗 —— 若 il2cpp 真的卡住(理論上不應發生),強殺並以
# 退出碼 124 結束,避免再次 hang 到 6 小時上限。
#
# 安裝(CI build:ipa job):
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
import select
import errno
import signal
import time

# 找出真實 il2cpp 二進位檔(與本 wrapper 同目錄的 il2cpp.real)
script_dir = os.path.dirname(os.path.abspath(sys.argv[0]))
real = os.path.join(script_dir, "il2cpp.real")
real_args = [real] + sys.argv[1:]

# 單一 PTY 供 stdin/stdout/stderr 共用(ConsolePal..cctor 查詢 fd 0,故 fd 0 須為 TTY)
master, slave = pty.openpty()
fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 24, 80, 0, 0))

pid = os.fork()
if pid == 0:
    # ── 子行程(il2cpp.real)──
    try:
        os.setsid()  # 脫離任何控制終端(避免關閉 master 時收到 SIGHUP)
    except OSError:
        pass
    os.dup2(slave, 0)  # stdin  = PTY(ConsolePal..cctor 的 TIOCGWINSZ 在此成功)
    os.dup2(slave, 1)  # stdout = PTY
    os.dup2(slave, 2)  # stderr = PTY
    for fd in (slave, master):
        try:
            os.close(fd)
        except OSError:
            pass
    os.execv(real, real_args)
    os._exit(127)

# ── 父行程:非阻塞排空 PTY master → 真實 stdout ──
os.close(slave)
STDOUT = 1
orig_fl = fcntl.fcntl(STDOUT, fcntl.F_GETFL)
fcntl.fcntl(STDOUT, fcntl.F_SETFL, orig_fl | os.O_NONBLOCK)

pending = bytearray()
exit_code = 1
reaped = False
last_output = time.monotonic()
WATCHDOG_S = 900        # 15 分鐘無輸出 → 判定卡住(il2cpp 正常數分鐘完成)
MAX_PENDING = 64 << 20  # 64 MB 上限,避免 stdout 失聯時 pending 無限增長


def reap_child(blocking):
    """收回子行程;若已結束,設定 exit_code 並回傳 True。"""
    global exit_code, reaped
    if reaped:
        return True
    try:
        done, status = os.waitpid(pid, 0 if blocking else os.WNOHANG)
    except OSError:
        reaped = True
        return True
    if done:
        reaped = True
        if os.WIFEXITED(status):
            exit_code = os.WEXITSTATUS(status)
        elif os.WIFSIGNALED(status):
            exit_code = 128 + os.WTERMSIG(status)
        return True
    return False


def flush_pending_blocking():
    """結束前以阻塞方式排空 pending(子行程已結束,pipe 即將被消費端讀取)。"""
    try:
        fcntl.fcntl(STDOUT, fcntl.F_SETFL, orig_fl)
    except OSError:
        pass
    while pending:
        try:
            n = os.write(STDOUT, pending)
            del pending[:n]
        except OSError:
            break


while True:
    rfds = [master]
    wfds = [STDOUT] if pending else []
    try:
        r, w, _ = select.select(rfds, wfds, [], 1.0)
    except (OSError, ValueError):
        break

    if master in r:
        try:
            data = os.read(master, 65536)
        except OSError as e:
            break  # EIO(macOS:slave 關閉)→ 視為 EOF
        if not data:
            break  # EOF
        pending += data
        if len(pending) > MAX_PENDING:
            del pending[: len(pending) - MAX_PENDING]  # 丟棄最舊
        last_output = time.monotonic()

    if STDOUT in w and pending:
        try:
            n = os.write(STDOUT, pending)
            del pending[:n]
        except OSError as e:
            if e.errno not in (errno.EAGAIN, errno.EWOULDBLOCK):
                pending.clear()  # stdout 已壞,丟棄避免無限增長

    if reap_child(blocking=False):
        # 子行程已結束:排空 PTY 殘留後離開
        try:
            while True:
                data = os.read(master, 65536)
                if not data:
                    break
                pending += data
        except OSError:
            pass
        flush_pending_blocking()
        break

    # 看門狗:長時間無輸出 → 判定卡住,強殺以避免 6h hang
    if (time.monotonic() - last_output) > WATCHDOG_S:
        sys.stderr.write(
            "il2cpp_pty_wrapper: 達 %d 秒無輸出,判定 il2cpp 卡住,送 SIGKILL\n"
            % WATCHDOG_S
        )
        sys.stderr.flush()
        try:
            os.kill(pid, signal.SIGKILL)
        except OSError:
            pass
        reap_child(blocking=True)
        exit_code = 124
        break

# 若因 EOF/select 例外而離開迴圈但子行程尚未收回,阻塞收回之
if not reaped:
    reap_child(blocking=True)
flush_pending_blocking()

try:
    os.close(master)
except OSError:
    pass

sys.exit(exit_code)
