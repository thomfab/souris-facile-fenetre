using System.Runtime.InteropServices;
using static SourisFacile.Native;

namespace SourisFacile;

// Sees keyboard and mouse events before any app does, and can swallow them.
// The hooks run on a thread of their own, so they always answer quickly
// (Windows drops hooks that are too slow) even while the app is busy moving
// a window.
internal sealed class InputHooks : IDisposable
{
    // Marks the keys the app sends itself, so the hooks let them through.
    private static readonly IntPtr OwnInput = new(0x50555253);

    // Each returns true to swallow the event. They run on the hook thread.
    public Func<int, bool, bool>? Key;            // virtual-key code, down
    public Func<int, bool, Point, bool>? Wheel;   // delta, horizontal, pointer
    public Func<bool, bool>? Click;               // left button down
    // The pointer moved, or a modifier key went down or up.
    public Action? Changed;

    private readonly HookProc keyboardProc;
    private readonly HookProc mouseProc;
    private Thread? thread;
    private uint threadId;

    public InputHooks()
    {
        // Kept in fields so the garbage collector doesn't free them.
        keyboardProc = OnKeyboard;
        mouseProc = OnMouse;
    }

    public void Start()
    {
        var started = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            var module = GetModuleHandle(null);
            var keyboard = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, module, 0);
            var mouse = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
            started.Set();
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(keyboard);
            UnhookWindowsHookEx(mouse);
        })
        {
            IsBackground = true,
            Name = "Input hooks",
            Priority = ThreadPriority.Highest,
        };
        thread.Start();
        started.Wait();
    }

    public void Dispose()
    {
        if (thread == null) return;
        PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(1000);
        thread = null;
    }

    private IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var message = (int)wParam;
            var down = message is WM_KEYDOWN or WM_SYSKEYDOWN;
            var own = (info.flags & LLKHF_INJECTED) != 0 && info.dwExtraInfo == OwnInput;
            if (!own && (down || message is WM_KEYUP or WM_SYSKEYUP))
            {
                if (IsModifier((int)info.vkCode))
                {
                    Volatile.Write(ref pendingKey, (int)info.vkCode | (down ? PendingDown : 0));
                    Changed?.Invoke();
                }
                else if (Key?.Invoke((int)info.vkCode, down) == true)
                {
                    return 1;
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = (int)wParam;
            if (message is WM_MOUSEWHEEL or WM_MOUSEHWHEEL)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                var delta = (short)(info.mouseData >> 16);
                if (Wheel?.Invoke(delta, message == WM_MOUSEHWHEEL, new Point(info.pt.X, info.pt.Y)) == true)
                {
                    return 1;
                }
            }
            else if (message is WM_LBUTTONDOWN or WM_LBUTTONUP)
            {
                if (Click?.Invoke(message == WM_LBUTTONDOWN) == true) return 1;
            }
            else if (message == WM_MOUSEMOVE)
            {
                Volatile.Write(ref pendingKey, 0);
                Changed?.Invoke();
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    // MARK: Modifiers

    // A modifier key going down or up, which the key state doesn't show yet
    // while its hook runs: its code, plus PendingDown if it went down. 0 for none.
    private static int pendingKey;
    private const int PendingDown = 0x10000;

    private static bool IsModifier(int vk) => vk is VK_SHIFT or VK_CONTROL or VK_MENU
        or VK_LSHIFT or VK_RSHIFT or VK_LCONTROL or VK_RCONTROL or VK_LMENU or VK_RMENU
        or VK_LWIN or VK_RWIN;

    private static Modifier ModifierOf(int vk) => vk switch
    {
        VK_SHIFT or VK_LSHIFT or VK_RSHIFT => Modifier.Shift,
        VK_CONTROL or VK_LCONTROL or VK_RCONTROL => Modifier.Control,
        VK_MENU or VK_LMENU or VK_RMENU => Modifier.Alt,
        VK_LWIN or VK_RWIN => Modifier.Win,
        _ => Modifier.None,
    };

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    // The modifier keys held right now. Safe to call from any thread.
    public static Modifier CurrentModifiers()
    {
        var result = Modifier.None;
        if (IsDown(VK_SHIFT)) result |= Modifier.Shift;
        if (IsDown(VK_CONTROL)) result |= Modifier.Control;
        if (IsDown(VK_MENU)) result |= Modifier.Alt;
        if (IsDown(VK_LWIN) || IsDown(VK_RWIN)) result |= Modifier.Win;

        var pending = Volatile.Read(ref pendingKey);
        if (pending != 0)
        {
            var vk = pending & 0xFFFF;
            var down = (pending & PendingDown) != 0;
            var modifier = ModifierOf(vk);
            if (down)
            {
                result |= modifier;
            }
            else if (modifier == Modifier.Win)
            {
                // The other Windows key may still be held.
                if (!IsDown(vk == VK_LWIN ? VK_RWIN : VK_LWIN)) result &= ~modifier;
            }
            else if (vk is VK_SHIFT or VK_CONTROL or VK_MENU)
            {
                result &= ~modifier;
            }
            else
            {
                // Left or right: the key on the other side may still be held.
                var other = vk switch
                {
                    VK_LSHIFT => VK_RSHIFT, VK_RSHIFT => VK_LSHIFT,
                    VK_LCONTROL => VK_RCONTROL, VK_RCONTROL => VK_LCONTROL,
                    VK_LMENU => VK_RMENU, _ => VK_LMENU,
                };
                if (!IsDown(other)) result &= ~modifier;
            }
        }
        return result;
    }

    // Presses and releases a key that does nothing. While the Windows key or
    // Alt is held, it keeps the Start menu or the menu bar from opening when
    // they're released, and it lets the app bring a window to the front.
    public static void PressMaskKey()
    {
        var inputs = new INPUT[2];
        for (var i = 0; i < 2; i++)
        {
            inputs[i].type = INPUT_KEYBOARD;
            inputs[i].u.ki = new KEYBDINPUT
            {
                wVk = VK_MASK,
                dwFlags = i == 0 ? 0 : KEYEVENTF_KEYUP,
                dwExtraInfo = OwnInput,
            };
        }
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
