// Wrapper for the native code/lib

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Linq;

namespace WebSocketSharp
{
    internal enum WsppRes
    {
        OK = 0,
        InvalidState = 1,
        Unknown = -1,
    }

    public partial class WebSocket : IDisposable
    {
        private UIntPtr ws;
        private OnMessageCallback messageHandler;
        private OnOpenCallback openHandler;
        private OnCloseCallback closeHandler;
        private OnErrorCallback errorHandler;
        private OnPongCallback pongHandler;

    #if C_WSPP_CALLING_CONVENTION_CDECL
        internal const CallingConvention CALLING_CONVENTION = CallingConvention.Cdecl;
    #else
        internal const CallingConvention CALLING_CONVENTION = CallingConvention.Winapi;
#endif

#if OS_WINDOWS
        internal const string DLL_NAME = "c-wspp.dll";
#elif OS_MAC
#error "Not implemented"
#elif OS_LINUX || OS_UNIX || OS_BSD
        internal const string DLL_NAME = "c-wspp.so";
#else
#error "Please define an OS_* macro"
#endif

        internal static readonly string[] dll_file = new string[]
        {
            "c-wspp.dll",
            "c-wspp.so"
        };
        /*{
            get
            {
                if (System.Environment.OSVersion.Platform == System.PlatformID.Win32NT)
                {
                    return "c-wspp.dll";
                }
                else
                {
                    return "c-wspp.so";
                }
            }
        }*/

        [UnmanagedFunctionPointer(CALLING_CONVENTION)]
        internal delegate void OnMessageCallback(IntPtr data, ulong len, int opCode);
        [UnmanagedFunctionPointer(CALLING_CONVENTION)]
        internal delegate void OnOpenCallback();
        [UnmanagedFunctionPointer(CALLING_CONVENTION)]
        internal delegate void OnCloseCallback(); // TODO: code, reason
        [UnmanagedFunctionPointer(CALLING_CONVENTION)]
        internal delegate void OnErrorCallback(IntPtr msg); // TODO: errorCode
        [UnmanagedFunctionPointer(CALLING_CONVENTION)]
        internal delegate void OnPongCallback(IntPtr data, ulong len);

        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0: 1], CharSet=CharSet.Ansi, CallingConvention=CALLING_CONVENTION)]
        internal static extern UIntPtr wspp_new(IntPtr uri);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern void wspp_delete(UIntPtr ws);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern ulong wspp_poll(UIntPtr ws);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern ulong wspp_run(UIntPtr ws);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern bool wspp_stopped(UIntPtr ws);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern int wspp_connect(UIntPtr ws);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern int wspp_close(UIntPtr ws, ushort code, IntPtr reason);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern int wspp_send_text(UIntPtr ws, IntPtr message);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern int wspp_send_binary(UIntPtr ws, byte[] data, ulong len);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern int wspp_ping(UIntPtr ws, byte[] data, ulong len);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern void wspp_set_open_handler(UIntPtr ws, OnOpenCallback f);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern void wspp_set_close_handler(UIntPtr ws, OnCloseCallback f);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern void wspp_set_message_handler(UIntPtr ws, OnMessageCallback f);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern void wspp_set_error_handler(UIntPtr ws, OnErrorCallback f);
        [DllImport(dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1], CallingConvention=CALLING_CONVENTION)]
        internal static extern void wspp_set_pong_handler(UIntPtr ws, OnPongCallback f);

        // NOTE: currently we do string -> UTF8 in C#, but it might be better to change that.
        internal static IntPtr StringToHGlobalUTF8(string s, out int length)
        {
            if (s == null)
            {
                length = 0;
                return IntPtr.Zero;
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            Marshal.WriteByte(ptr, bytes.Length, 0);
            length = bytes.Length;

            return ptr;
        }

        internal static IntPtr StringToHGlobalUTF8(string s)
        {
            int temp;
            return StringToHGlobalUTF8(s, out temp);
        }

        internal bool sequenceEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) {
                return false;
            }
            for (int i=0; i<a.Length; i++) {
                if (a[i] != b[i]) {
                    return false;
                }
            }
            return true;
        }

        private void OpenHandler()
        {
            debug("on Open");

            // ignore events that happen during shutdown of the socket
            if (ws == UIntPtr.Zero)
                return;

            debug("ReadyState = Open");
            readyState = WebSocketState.Open;
            EventArgs e = new EventArgs();
            // FIXME: on .net >=4.0 we could use an async task to fire from main thread
            dispatcher.Enqueue(e);
        }

        private void CloseHandler()
        {
            debug("on Close");

            // ignore events that happen during shutdown of the socket
            if (ws == UIntPtr.Zero)
                return;

            debug("ReadyState = Closed");
            readyState = WebSocketState.Closed; // TODO: move this after nulling dispatcher in WebSocket.cs to avoid a race if another thread polls ReadyState

            CloseEventArgs e = new CloseEventArgs(0, ""); // TODO: code and reason
            // FIXME: on .net >=4.0 we could use an async task to fire from main thread
            dispatcher.Enqueue(e);
        }

        private void MessageHandler(IntPtr data, ulong len, int opCode)
        {
            debug("on Message");

            // ignore events that happen during shutdown of the socket
            if (ws == UIntPtr.Zero)
                return;

            if (len > Int32.MaxValue) {
                error("Received message that was too long");
                return;
            }
            byte[] bytes = new byte[(int)len];
            Marshal.Copy(data, bytes, 0, (int)len);
            MessageEventArgs e = new MessageEventArgs(bytes, (OpCode)opCode);
            // FIXME: on .net >=4.0 we could use an async task to fire from main thread
            dispatcher.Enqueue(e);
        }

        private void ErrorHandler(IntPtr msgPtr)
        {
            debug("on Error");

            // ignore events that happen during shutdown of the socket
            if (ws == UIntPtr.Zero)
                return;

            string msg = "Unknown";
            if (msgPtr != IntPtr.Zero) {
                msg = Marshal.PtrToStringAnsi(msgPtr); // FIXME: this fails for non-ascii on windows
            }

            if (readyState == WebSocketState.Connecting) {
                // no need to close
                debug("ReadyState = Closed");
                readyState = WebSocketState.Closed; // TODO: move this after nulling dispatcher in WebSocket.cs to avoid a race if another thread polls ReadyState
            } else if (readyState == WebSocketState.Open) {
                // this should never happen since we throw all exceptions in-line
                Close();
            }
            lastError = msg;
            error("Connect error: " + msg);
        }

        private void PongHandler(IntPtr data, ulong len)
        {
            byte[] bytes = new byte[(int)len];
            Marshal.Copy(data, bytes, 0, (int)len);

            // look for internal ping
            lock (pings)
            {
                foreach (byte[] b in pings) {
                    if (sequenceEqual(bytes, b)) {
                        pings.Remove(b);
                        lastPong = DateTime.UtcNow;
                        return;
                    }
                }
            }

            // emit event for external ping
            MessageEventArgs e = new MessageEventArgs(bytes, OpCode.Pong);
            // FIXME: on .net >=4.0 we could use an async task to fire from main thread
            dispatcher.Enqueue(e);
        }

        /// <summary>
        /// wspp_new with string -> utf8 conversion.
        /// Has its own function scope to ensure DllDirectory.Set is run before wspp_new.
        /// </summary>
        static private UIntPtr wspp_new(string uriString)
        {
            IntPtr uriUTF8 = StringToHGlobalUTF8(uriString);
            try {
                return wspp_new(uriUTF8);
            } finally {
                Marshal.FreeHGlobal(uriUTF8);
            }
        }

        /// <summary>
        /// Create new native wspp websocket with DLL from dllDirectory
        /// </summary>
        static private UIntPtr wspp_new_from(string uriString, string dllDirectory)
        {
            sdebug("wspp_new(\"" + uriString + "\") in " + dll_file[System.Environment.OSVersion.Platform == System.PlatformID.Win32NT ? 0 : 1] + " from " + dllDirectory);

            using (DllDirectory.Context(dllDirectory))
            {
                return wspp_new(uriString);
            }
        }

        static internal string directory {
            get {
                string ktaneLocation = System.IO.Directory.GetCurrentDirectory();
                var dsc = Char.ToString(Path.DirectorySeparatorChar);
                string location = ktaneLocation + dsc + "mods";
                string apLocation = null;
                try
                {
                    apLocation = Directory.GetFiles(location, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith("archipelago.dll")).FirstOrDefault();
                }
                catch (System.Exception e) { } //if the mod folder doesn't exist, it should try to get it from the workshop instead

                if (apLocation == null)
                {
                    location = GetUntil(ktaneLocation, "steamapps") + "steamapps" + dsc + "workshop" + dsc + "content" + dsc + "341800";
                    if (location != String.Empty)
                        apLocation = Directory.GetFiles(location, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith("archipelago.dll")).FirstOrDefault();
                }
                string directory = new FileInfo(apLocation).Directory.FullName;
                return directory + dsc + "lib"; 
                //return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/lib";
            }
        }

        static private string GetUntil(string text, string stopper)
        {
            if (text == String.Empty)
                return String.Empty;
            int charLocation = text.IndexOf(stopper, StringComparison.Ordinal);
            if (charLocation < 0)
                return String.Empty;
            return text.Substring(0, charLocation);
        }

        static private void close(UIntPtr ws, ushort code, string reason)
        {
            sdebug("wspp_close(" + code + ", \"" + reason + "\')");
            IntPtr reasonUTF8 = StringToHGlobalUTF8(reason);
            wspp_close(ws, code, reasonUTF8);
            Marshal.FreeHGlobal(reasonUTF8);
        }

        private void setHandlers()
        {
            openHandler = new OnOpenCallback(OpenHandler);
            closeHandler = new OnCloseCallback(CloseHandler);
            messageHandler = new OnMessageCallback(MessageHandler);
            errorHandler = new OnErrorCallback(ErrorHandler);
            pongHandler = new OnPongCallback(PongHandler);

            wspp_set_open_handler(ws, openHandler);
            wspp_set_close_handler(ws, closeHandler);
            wspp_set_message_handler(ws, messageHandler);
            wspp_set_error_handler(ws, errorHandler);
            wspp_set_pong_handler(ws, pongHandler);
        }

        private void clearHandlers()
        {
            wspp_set_open_handler(ws, null);
            wspp_set_close_handler(ws, null);
            wspp_set_message_handler(ws, null);
            wspp_set_error_handler(ws, null);
            wspp_set_pong_handler(ws, null);
        }
    }
}

