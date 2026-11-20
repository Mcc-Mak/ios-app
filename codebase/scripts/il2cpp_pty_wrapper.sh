#!/bin/sh
# il2cpp_pty_wrapper.sh — 以 PTY + 環境變數修正包裹 Tuanjie 的 il2cpp(.NET)
#
# 採 /bin/sh 外殼 + 內嵌 python3(heredoc)。建置階段(Run Script phase)以 exec 呼叫
# 本檔,核心恆接受 #!/bin/sh;先前純 .py(#!/usr/bin/env python3)未被實際執行(run
# 44653433207 驗證)。
#
# 問題演進與診斷:
#   - 原始崩潰(run 44498190149,無 wrapper):ConsolePal..cctor OOM。
#   - pty.spawn wrapper(run 44498856814):6h hang(實為 _copy 輸出死結,崩潰被遮蔽)。
#   - pty.fork + 非阻塞排空(run 44656281598):崩潰重新顯露——證明 PTY/controlling
#     terminal 並非崩潰根因。ConsolePal..cctor 在標準 .NET 6 僅配置 int[16] + string[2,16],
#     不可能 OOM。故 Tuanjie 的 il2cpp 使用修改版 .NET,cctor 可能隨 TERM 載入 terminfo。
#   - 本版同時:(a) 以 pty.fork 提供 PTY(以防 TIOCGWINSZ 相關路徑);(b) 移除 TERM 環境
#     變數(讓 .NET 跳過 terminfo 載入——主修正);(c) 非阻塞排空 + 看門狗(防 hang);
#     (d) 診斷輸出至 fd 3(原始 stderr,驗證 PTY 狀態)。
#
# 安裝(CI build:ipa job):
#   mv il2cpp il2cpp.real && chmod +x il2cpp.real
#   cp il2cpp_pty_wrapper.sh il2cpp && chmod +x il2cpp
DIR="$(dirname "$0")"
exec python3 - "$DIR/il2cpp.real" "$@" <<'PYEOF'
import os, pty, fcntl, termios, struct, sys, select, errno, signal, time

real = sys.argv[1]
real_args = sys.argv[1:]

WINSIZE = struct.pack("HHHH", 24, 80, 0, 0)

# 將原始 stderr 複製至 fd 3,供子行程寫診斷(pty.fork 後子行程 fd 0/1/2 為 PTY)
diag_fd = os.dup(2)  # fd 3 = 原始 stderr(pipe → xcodebuild log)

def diag(msg):
    os.write(diag_fd, (msg + "\n").encode())

diag("IL2CPP_WRAPPER start pid=%d real=%s" % (os.getpid(), real))
diag("  TERM=%r NO_COLOR=%r" % (os.environ.get("TERM"), os.environ.get("NO_COLOR")))

# pty.fork():底層 os.forkpty() 做 openpty + fork + login_tty(setsid + TIOCSCTTY + dup2)
pid, master_fd = pty.fork()

if pid == 0:
    # ── 子行程(il2cpp.real)── fd 0/1/2 = PTY slave,fd 3 = 原始 stderr(診斷)
    diag("IL2CPP_WRAPPER child pid=%d" % os.getpid())
    diag("  isatty(0)=%s isatty(1)=%s isatty(2)=%s" %
         (os.isatty(0), os.isatty(1), os.isatty(2)))
    # 測試 TIOCGWINSZ
    try:
        ws = fcntl.ioctl(0, termios.TIOCGWINSZ, b"\x00" * 8)
        rows, cols, _, _ = struct.unpack("HHHH", ws)
        diag("  TIOCGWINSZ(0): rows=%d cols=%d" % (rows, cols))
    except OSError as e:
        diag("  TIOCGWINSZ(0) FAILED: %s" % e)
    # 設定視窗尺寸 24×80
    for fd in (0, 1, 2):
        try:
            fcntl.ioctl(fd, termios.TIOCSWINSZ, WINSIZE)
        except OSError:
            pass
    # ★ 主修正:移除 TERM,讓 .NET ConsolePal..cctor 跳過 terminfo 載入
    os.environ.pop("TERM", None)
    os.environ["NO_COLOR"] = "1"
    os.environ["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "0"
    diag("  env after: TERM=%r NO_COLOR=%r" %
         (os.environ.get("TERM"), os.environ.get("NO_COLOR")))
    diag("  -> execv %s" % real)
    os.close(diag_fd)
    os.execv(real, real_args)
    os._exit(127)

# ── 父行程:非阻塞排空 PTY master → 真實 stdout(取代 pty._copy)──
os.close(diag_fd)
try:
    fcntl.ioctl(master_fd, termios.TIOCSWINSZ, WINSIZE)
except OSError:
    pass

STDOUT = 1
orig_fl = fcntl.fcntl(STDOUT, fcntl.F_GETFL)
fcntl.fcntl(STDOUT, fcntl.F_SETFL, orig_fl | os.O_NONBLOCK)

pending = bytearray()
exit_code = 1
reaped = False
last_output = time.monotonic()
WATCHDOG_S = 900
MAX_PENDING = 64 << 20


def reap_child(blocking):
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


def flush_blocking():
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
    rfds = [master_fd]
    wfds = [STDOUT] if pending else []
    try:
        r, w, _ = select.select(rfds, wfds, [], 1.0)
    except (OSError, ValueError):
        break

    if master_fd in r:
        try:
            data = os.read(master_fd, 65536)
        except OSError:
            break
        if not data:
            break
        pending += data
        if len(pending) > MAX_PENDING:
            del pending[: len(pending) - MAX_PENDING]
        last_output = time.monotonic()

    if STDOUT in w and pending:
        try:
            n = os.write(STDOUT, pending)
            del pending[:n]
        except OSError as e:
            if e.errno not in (errno.EAGAIN, errno.EWOULDBLOCK):
                pending.clear()

    if reap_child(False):
        try:
            while True:
                data = os.read(master_fd, 65536)
                if not data:
                    break
                pending += data
        except OSError:
            pass
        flush_blocking()
        break

    if (time.monotonic() - last_output) > WATCHDOG_S:
        sys.stderr.write("IL2CPP_WRAPPER: %ds 無輸出,送 SIGKILL\n" % WATCHDOG_S)
        sys.stderr.flush()
        try:
            os.kill(pid, signal.SIGKILL)
        except OSError:
            pass
        reap_child(True)
        exit_code = 124
        break

if not reaped:
    reap_child(True)
flush_blocking()

try:
    os.close(master_fd)
except OSError:
    pass

sys.exit(exit_code)
PYEOF
