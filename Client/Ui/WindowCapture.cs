using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace AionDPS.Ui;

/// <summary>
/// The game window's picture the way Discord, OBS and the Xbox Game Bar take a window (Windows.Graphics.Capture, 13 November 2026): Windows hands over the
/// frames it composes for the screen anyway, so the game draws nothing more for it. PrintWindow, used until then, has Windows draw the window once more for
/// every picture; while the app took them several times a second, a tester's Discord stream of the game said "has minimized their application" and came
/// back as soon as the app was closed (the owner: keep every system, find a way that leaves Discord alone).
/// Only where Windows lets the capture go without its yellow frame around the window (build 20348 on: Windows 11, Server 2022); elsewhere, or when anything
/// here fails, <see cref="GameCapture"/> takes the picture with PrintWindow as before.
/// One session, for the game window, closed after half a minute without a picture. Its frame pool holds two frames, so Windows copies at most two of the
/// game's frames between two pictures, never every frame. Direct3D 11 is used in this process only (a device of its own that receives the frames): nothing
/// is put into the game or read from it. Called under <see cref="GameCapture"/>'s lock; its own lock covers the idle timer too.
/// </summary>
internal static class WindowCapture
{
    public enum Result
    {
        /// <summary>A new picture is in the bitmap.</summary>
        Drawn,

        /// <summary>The window showed nothing new since the last picture (Windows sends a frame only when it changes): the last picture still shows it.</summary>
        Unchanged,

        /// <summary>No frame came (the window is minimized, or not drawn yet).</summary>
        NoFrame,

        /// <summary>This picture failed: take it the old way.</summary>
        Failed,

        /// <summary>Windows' capture is not used on this PC (too old a Windows, or the yellow frame could not be left off, or it failed again and again).</summary>
        Unavailable,
    }

    /// <summary>A frame younger than this is taken as it is; an older one waits a moment for the game's next frame.</summary>
    private static readonly TimeSpan FreshEnough = TimeSpan.FromMilliseconds(50);

    /// <summary>How long the next frame is waited for (a game shows one every 7 to 33 ms), and the first one of a new session.</summary>
    private const int NextFrameWait = 80, FirstFrameWait = 600;

    /// <summary>A session without a picture this long is closed (the game is not in front, no picture is wanted).</summary>
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(30);

    private static readonly object Sync = new();
    private static readonly AutoResetEvent Arrived = new(false);
    private static bool? _supported;
    private static volatile bool _gaveUp;
    private static int _failures;
    private static IntPtr _device, _context, _staging;
    private static int _stagingWidth, _stagingHeight;
    private static IDirect3DDevice? _d3d;
    private static IntPtr _window;
    private static GraphicsCaptureItem? _item;
    private static Direct3D11CaptureFramePool? _pool;
    private static GraphicsCaptureSession? _session;
    private static SizeInt32 _poolSize;
    private static bool _waitingForFirst;
    private static DateTime _startedAt;
    private static volatile bool _itemClosed;
    private static DateTime _lastUse;
    private static System.Threading.Timer? _idleTimer;
    private static bool _mta;

    /// <summary>Why Windows' capture is not used (null while it works): for the Status page.</summary>
    public static string? LastError { get; private set; }

    /// <summary>Whether this Windows can take the picture this way at all (its capture without the yellow frame): build 20348 on, and supported.</summary>
    public static bool Supported
    {
        get
        {
            if (_supported is bool known)
            {
                return known && !_gaveUp;
            }

            bool ok;
            try
            {
                ok = Environment.OSVersion.Version.Build >= 20348 && GraphicsCaptureSession.IsSupported();
            }
            catch (Exception ex) when (Expected(ex))
            {
                ok = false;
            }

            if (!ok)
            {
                LastError ??= "this Windows has no capture without the yellow frame (Windows 11 has)";
            }

            _supported = ok;
            return ok && !_gaveUp;
        }
    }

    /// <summary>The client area of <paramref name="window"/> (as large as <paramref name="target"/>) into <paramref name="target"/>.</summary>
    public static Result Draw(IntPtr window, Bitmap target)
    {
        if (window == IntPtr.Zero || !Supported)
        {
            return Result.Unavailable;
        }

        lock (Sync)
        {
            try
            {
                if (!EnsureDevice())
                {
                    return Fail();
                }

                if (_session is null || _window != window || _itemClosed)
                {
                    Result? started = Start(window);
                    if (started is { } refused)
                    {
                        return refused;
                    }
                }

                _lastUse = DateTime.UtcNow;
                Arrived.Reset();
                Direct3D11CaptureFrame? frame = Newest();
                if (frame is null || Age(frame) > FreshEnough)
                {
                    // nothing since the last picture, or an old frame: the game's next one, a moment away at its frame rate (an unchanged window sends none)
                    bool first = _waitingForFirst && DateTime.UtcNow - _startedAt < TimeSpan.FromSeconds(1);
                    if (Arrived.WaitOne(first ? FirstFrameWait : NextFrameWait) && Newest() is { } newer)
                    {
                        frame?.Dispose();
                        frame = newer;
                    }
                }

                if (frame is null)
                {
                    // no first frame yet: this picture the old way (a window Windows can't capture fails five times and is then always taken so);
                    // afterwards nothing new means the window did not change
                    return _waitingForFirst ? Fail() : Result.Unchanged;
                }

                using (frame)
                {
                    _waitingForFirst = false;
                    SizeInt32 content = frame.ContentSize;
                    if (content.Width != _poolSize.Width || content.Height != _poolSize.Height)
                    {
                        // the window changed its size: the frames from the next one on are of the new size (this one is copied as far as it goes)
                        _poolSize = content;
                        _pool!.Recreate(_d3d!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, content);
                    }

                    if (!CopyClientArea(frame, window, target))
                    {
                        return Fail();
                    }
                }

                _failures = 0;
                LastError = null;
                return Result.Drawn;
            }
            catch (Exception ex) when (Expected(ex))
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                Stop();
                return Fail();
            }
        }
    }

    /// <summary>Closes the session (the app shuts, or the check is done).</summary>
    public static void Close()
    {
        lock (Sync)
        {
            Stop();
            ResetDevice();
        }
    }

    private static Result Fail()
    {
        if (++_failures >= 5)
        {
            // failed five times in a row: the old way for the rest of this run
            _gaveUp = true;
            LastError = "failed five times in a row (" + (LastError ?? "no reason given") + ")";
            Stop();
            ResetDevice();
            return Result.Unavailable;
        }

        return Result.Failed;
    }

    /// <summary>The newest frame in the pool (older ones go back at once); null when none came since the last picture.</summary>
    private static Direct3D11CaptureFrame? Newest()
    {
        Direct3D11CaptureFrame? newest = null;
        while (_pool!.TryGetNextFrame() is { } next)
        {
            newest?.Dispose();
            newest = next;
        }

        return newest;
    }

    /// <summary>How long ago Windows took the frame (its time is the performance counter's, as the stopwatch's).</summary>
    private static TimeSpan Age(Direct3D11CaptureFrame frame) =>
        TimeSpan.FromSeconds(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency) - frame.SystemRelativeTime;

    /// <summary>A session for <paramref name="window"/>; null when it runs, else what the picture comes to.</summary>
    private static Result? Start(IntPtr window)
    {
        Stop();
        GraphicsCaptureItem? item = CreateItem(window);
        if (item is null)
        {
            return Fail();
        }

        SizeInt32 size = item.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return Result.NoFrame;
        }

        Direct3D11CaptureFramePool pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_d3d!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
        GraphicsCaptureSession session = pool.CreateCaptureSession(item);
        if (!LeaveFrameOff(session))
        {
            // the yellow frame would go round the game for as long as the app runs: never that
            session.Dispose();
            pool.Dispose();
            _gaveUp = true;
            LastError = "Windows would draw its yellow frame round the game";
            ResetDevice();
            return Result.Unavailable;
        }

        session.IsCursorCaptureEnabled = false; // as PrintWindow's picture: no mouse cursor in it
        pool.FrameArrived += OnFrameArrived;
        item.Closed += OnItemClosed;
        _item = item;
        _pool = pool;
        _session = session;
        _poolSize = size;
        _window = window;
        _waitingForFirst = true;
        _startedAt = DateTime.UtcNow;
        _itemClosed = false;
        session.StartCapture();
        _idleTimer ??= new System.Threading.Timer(_ => CloseIfIdle(), null, 10_000, 10_000);
        return null;
    }

    private static void OnFrameArrived(Direct3D11CaptureFramePool sender, object args) => Arrived.Set();

    private static void OnItemClosed(GraphicsCaptureItem sender, object args) => _itemClosed = true;

    private static void CloseIfIdle()
    {
        lock (Sync)
        {
            if (_session is not null && DateTime.UtcNow - _lastUse > Idle)
            {
                Stop();
            }
        }
    }

    private static void Stop()
    {
        if (_pool is not null)
        {
            _pool.FrameArrived -= OnFrameArrived;
        }

        if (_item is not null)
        {
            _item.Closed -= OnItemClosed;
        }

        try
        {
            _session?.Dispose();
            _pool?.Dispose();
        }
        catch (Exception ex) when (Expected(ex))
        {
            // already gone with the window
        }

        _session = null;
        _pool = null;
        _item = null;
        _window = IntPtr.Zero;
    }

    private static void ResetDevice()
    {
        Release(ref _staging);
        try
        {
            _d3d?.Dispose();
        }
        catch (Exception ex) when (Expected(ex))
        {
            // a lost device
        }

        _d3d = null;
        Release(ref _context);
        Release(ref _device);
    }

    /// <summary>The Direct3D 11 device the frames come on (once; again after it was lost).</summary>
    private static bool EnsureDevice()
    {
        if (_d3d is not null)
        {
            return true;
        }

        int hr = D3D11CreateDevice(IntPtr.Zero, DriverTypeHardware, IntPtr.Zero, CreateBgraSupport, IntPtr.Zero, 0, SdkVersion, out IntPtr device, out _, out IntPtr context);
        if (hr < 0 || device == IntPtr.Zero)
        {
            LastError = $"no Direct3D 11 device (0x{hr:X8})";
            return false;
        }

        // Windows fills the frame pool from threads of its own: the device is guarded against two at once
        Guid multithreadId = IidMultithread;
        if (Marshal.QueryInterface(device, in multithreadId, out IntPtr multithread) >= 0)
        {
            Slot<SetMultithreadProtectedFn>(multithread, 5)(multithread, 1);
            Marshal.Release(multithread);
        }

        Guid dxgiId = IidDxgiDevice;
        hr = Marshal.QueryInterface(device, in dxgiId, out IntPtr dxgi);
        if (hr < 0)
        {
            Release(ref context);
            Release(ref device);
            LastError = $"no DXGI device (0x{hr:X8})";
            return false;
        }

        hr = CreateDirect3D11DeviceFromDXGIDevice(dxgi, out IntPtr inspectable);
        Marshal.Release(dxgi);
        if (hr < 0 || inspectable == IntPtr.Zero)
        {
            Release(ref context);
            Release(ref device);
            LastError = $"no WinRT device (0x{hr:X8})";
            return false;
        }

        try
        {
            _d3d = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }

        _device = device;
        _context = context;
        return true;
    }

    /// <summary>The capture item of a window (IGraphicsCaptureItemInterop: a window is not one of the items the WinRT side can name).</summary>
    private static GraphicsCaptureItem? CreateItem(IntPtr window)
    {
        if (!_mta)
        {
            // WinRT's factories need a multithreaded apartment in the process; this keeps one (what C#/WinRT does itself)
            _mta = CoIncrementMTAUsage(out _) >= 0;
        }

        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        if (WindowsCreateString(className, className.Length, out IntPtr name) < 0)
        {
            return null;
        }

        try
        {
            Guid interopId = IidCaptureItemInterop;
            int hr = RoGetActivationFactory(name, ref interopId, out IntPtr factory);
            if (hr < 0 || factory == IntPtr.Zero)
            {
                LastError = $"no capture item factory (0x{hr:X8})";
                return null;
            }

            try
            {
                Guid itemId = IidCaptureItem;
                hr = Slot<CreateForWindowFn>(factory, 3)(factory, window, ref itemId, out IntPtr item);
                if (hr < 0 || item == IntPtr.Zero)
                {
                    LastError = $"the window cannot be captured (0x{hr:X8})";
                    return null;
                }

                try
                {
                    return GraphicsCaptureItem.FromAbi(item);
                }
                finally
                {
                    Marshal.Release(item);
                }
            }
            finally
            {
                Marshal.Release(factory);
            }
        }
        finally
        {
            WindowsDeleteString(name);
        }
    }

    /// <summary>Asks Windows to leave its yellow frame off (IGraphicsCaptureSession3.IsBorderRequired, build 20348 on); false where it can't.</summary>
    private static bool LeaveFrameOff(GraphicsCaptureSession session)
    {
        IntPtr inspectable = WinRT.MarshalInspectable<object>.FromManaged(session);
        if (inspectable == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            Guid id = IidCaptureSession3;
            if (Marshal.QueryInterface(inspectable, in id, out IntPtr session3) < 0)
            {
                return false;
            }

            try
            {
                // IInspectable's six slots, then get_IsBorderRequired, put_IsBorderRequired
                return Slot<PutBooleanFn>(session3, 7)(session3, 0) >= 0;
            }
            finally
            {
                Marshal.Release(session3);
            }
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    /// <summary>The frame's client area (the window's own frame and title bar left out, as PrintWindow's client-only picture) into the bitmap.</summary>
    private static bool CopyClientArea(Direct3D11CaptureFrame frame, IntPtr window, Bitmap target)
    {
        using IDirect3DSurface surface = frame.Surface;
        IntPtr texture = TextureOf(surface);
        if (texture == IntPtr.Zero)
        {
            LastError = "the frame has no texture";
            return false;
        }

        try
        {
            Slot<GetDescFn>(texture, 10)(texture, out Texture2DDesc desc);
            int width = Math.Min((int)desc.Width, frame.ContentSize.Width), height = Math.Min((int)desc.Height, frame.ContentSize.Height);
            if (ClientBox(window, width, height) is not { } box)
            {
                LastError = "the window's client area is not in the frame";
                return false;
            }

            int w = Math.Min((int)(box.Right - box.Left), target.Width), h = Math.Min((int)(box.Bottom - box.Top), target.Height);
            if (w <= 0 || h <= 0 || !EnsureStaging(w, h))
            {
                return false;
            }

            box.Right = box.Left + (uint)w;
            box.Bottom = box.Top + (uint)h;
            Slot<CopySubresourceRegionFn>(_context, 46)(_context, _staging, 0, 0, 0, 0, texture, 0, ref box);
            int hr = Slot<MapFn>(_context, 14)(_context, _staging, 0, MapRead, 0, out MappedSubresource mapped);
            if (hr < 0)
            {
                LastError = $"the frame could not be read (0x{hr:X8})";
                if (hr is DeviceRemoved or DeviceReset)
                {
                    Stop();
                    ResetDevice();
                }

                return false;
            }

            try
            {
                BitmapData data = target.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                try
                {
                    var row = (UIntPtr)(uint)(w * 4);
                    for (int y = 0; y < h; y++)
                    {
                        CopyMemory(IntPtr.Add(data.Scan0, y * data.Stride), IntPtr.Add(mapped.Data, y * (int)mapped.RowPitch), row);
                    }
                }
                finally
                {
                    target.UnlockBits(data);
                }
            }
            finally
            {
                Slot<UnmapFn>(_context, 15)(_context, _staging, 0);
            }

            return true;
        }
        finally
        {
            Marshal.Release(texture);
        }
    }

    /// <summary>The frame's Direct3D 11 texture (IDirect3DDxgiInterfaceAccess); zero when there is none.</summary>
    private static IntPtr TextureOf(IDirect3DSurface surface)
    {
        IntPtr inspectable = WinRT.MarshalInspectable<object>.FromManaged(surface);
        if (inspectable == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        try
        {
            Guid accessId = IidDxgiInterfaceAccess;
            if (Marshal.QueryInterface(inspectable, in accessId, out IntPtr access) < 0)
            {
                return IntPtr.Zero;
            }

            try
            {
                Guid textureId = IidTexture2D;
                return Slot<GetInterfaceFn>(access, 3)(access, ref textureId, out IntPtr texture) >= 0 ? texture : IntPtr.Zero;
            }
            finally
            {
                Marshal.Release(access);
            }
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    /// <summary>
    /// Where the client area lies in a frame of the window (<paramref name="width"/> x <paramref name="height"/>), in real pixels: a frame is the window as
    /// Windows shows it (its extended frame bounds), title bar and edges included; OBS finds the client area in it the same way.
    /// </summary>
    private static Box? ClientBox(IntPtr window, int width, int height) => GameWindow.InRealPixels(() =>
    {
        if (IsIconic(window) || !GetClientRect(window, out NativeRect client) || client.Right <= 0 || client.Bottom <= 0)
        {
            return (Box?)null;
        }

        var origin = new NativePoint();
        if (!ClientToScreen(window, ref origin) || DwmGetWindowAttribute(window, ExtendedFrameBounds, out NativeRect bounds, Marshal.SizeOf<NativeRect>()) < 0)
        {
            return null;
        }

        uint left = origin.X > bounds.Left ? (uint)(origin.X - bounds.Left) : 0, top = origin.Y > bounds.Top ? (uint)(origin.Y - bounds.Top) : 0;
        if (left >= (uint)width || top >= (uint)height)
        {
            return null;
        }

        uint w = Math.Min((uint)width - left, (uint)client.Right), h = Math.Min((uint)height - top, (uint)client.Bottom);
        return new Box { Left = left, Top = top, Front = 0, Right = left + w, Bottom = top + h, Back = 1 };
    });

    /// <summary>The texture the GPU copies the client area into, for the processor to read (made again when the size changes).</summary>
    private static bool EnsureStaging(int width, int height)
    {
        if (_staging != IntPtr.Zero && _stagingWidth == width && _stagingHeight == height)
        {
            return true;
        }

        Release(ref _staging);
        var desc = new Texture2DDesc
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = FormatB8G8R8A8,
            SampleCount = 1,
            SampleQuality = 0,
            Usage = UsageStaging,
            BindFlags = 0,
            CpuAccessFlags = CpuAccessRead,
            MiscFlags = 0,
        };
        int hr = Slot<CreateTexture2DFn>(_device, 5)(_device, ref desc, IntPtr.Zero, out IntPtr staging);
        if (hr < 0 || staging == IntPtr.Zero)
        {
            LastError = $"no texture to read the frame from (0x{hr:X8})";
            return false;
        }

        _staging = staging;
        _stagingWidth = width;
        _stagingHeight = height;
        return true;
    }

    private static void Release(ref IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            Marshal.Release(pointer);
            pointer = IntPtr.Zero;
        }
    }

    /// <summary>What a failing call here throws (C#/WinRT turns the HRESULTs into these): the picture is then taken the old way.</summary>
    private static bool Expected(Exception ex) => ex is COMException or InvalidCastException or ArgumentException or InvalidOperationException or TypeLoadException
        or DllNotFoundException or EntryPointNotFoundException or NotSupportedException or UnauthorizedAccessException or ObjectDisposedException or OutOfMemoryException
        or System.ComponentModel.Win32Exception or MarshalDirectiveException;

    /// <summary>A COM method of <paramref name="instance"/> (its vtable's <paramref name="index"/>-th entry) as a delegate.</summary>
    private static T Slot<T>(IntPtr instance, int index) where T : Delegate
    {
        IntPtr table = Marshal.ReadIntPtr(instance);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(table, index * IntPtr.Size));
    }

    // ---- Direct3D 11, DXGI, WinRT and window interop ----

    private const int DriverTypeHardware = 1;
    private const uint CreateBgraSupport = 0x20, SdkVersion = 7;
    private const int FormatB8G8R8A8 = 87, UsageStaging = 3, MapRead = 1;
    private const uint CpuAccessRead = 0x20000;
    private const int DeviceRemoved = unchecked((int)0x887A0005), DeviceReset = unchecked((int)0x887A0007);
    private const int ExtendedFrameBounds = 9;

    private static readonly Guid IidDxgiDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IidMultithread = new("9b7e4e00-342c-4106-a19f-4f2704f689f0");
    private static readonly Guid IidTexture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid IidCaptureItemInterop = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
    private static readonly Guid IidCaptureItem = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
    private static readonly Guid IidDxgiInterfaceAccess = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
    private static readonly Guid IidCaptureSession3 = new("f2cdd966-22ae-5ea1-9596-3a289344c3be");

    [StructLayout(LayoutKind.Sequential)]
    private struct Texture2DDesc
    {
        public uint Width, Height, MipLevels, ArraySize;
        public int Format;
        public uint SampleCount, SampleQuality;
        public int Usage;
        public uint BindFlags, CpuAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MappedSubresource
    {
        public IntPtr Data;
        public uint RowPitch, DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Box
    {
        public uint Left, Top, Front, Right, Bottom, Back;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForWindowFn(IntPtr self, IntPtr window, ref Guid iid, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PutBooleanFn(IntPtr self, byte value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetMultithreadProtectedFn(IntPtr self, int protect);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetInterfaceFn(IntPtr self, ref Guid iid, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void GetDescFn(IntPtr self, out Texture2DDesc desc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateTexture2DFn(IntPtr self, ref Texture2DDesc desc, IntPtr initialData, out IntPtr texture);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CopySubresourceRegionFn(IntPtr self, IntPtr destination, uint destinationSubresource, uint x, uint y, uint z, IntPtr source, uint sourceSubresource, ref Box box);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MapFn(IntPtr self, IntPtr resource, uint subresource, int mapType, uint mapFlags, out MappedSubresource mapped);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UnmapFn(IntPtr self, IntPtr resource, uint subresource);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint featureLevelCount, uint sdkVersion,
        out IntPtr device, out int featureLevel, out IntPtr immediateContext);

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoIncrementMTAUsage(out IntPtr cookie);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect value, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory", ExactSpelling = true)]
    private static extern void CopyMemory(IntPtr destination, IntPtr source, UIntPtr length);
}
