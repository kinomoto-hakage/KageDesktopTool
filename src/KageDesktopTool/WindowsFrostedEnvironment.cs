using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Windows.System.Power;
using Windows.UI.ViewManagement;

namespace Kage.Desktop;

// 系统通知只发布事实变化；Runtime 在 UI Dispatcher 上统一重读和应用。
internal sealed class WindowsFrostedEnvironment : IDisposable
{
    private static readonly Guid EnergySaverGuid = new("550E8400-E29B-41D4-A716-446655440000");
    private readonly UISettings? settings;
    private readonly bool modernEnergySaver = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100);
    private readonly PowerCallback callback;
    private IntPtr subscription;
    private IntPtr registration;
    private int energySaver = -1;
    private bool disposed;
    internal event Action? Changed;

    internal WindowsFrostedEnvironment()
    {
        callback = OnPowerChanged;
        try
        {
            settings = new UISettings();
            settings.AdvancedEffectsEnabledChanged += EffectsChanged;
        }
        catch { settings = null; }
        SystemParameters.StaticPropertyChanged += SystemChanged;
        if (modernEnergySaver) RegisterEnergySaver();
        else PowerManager.EnergySaverStatusChanged += LegacyEnergyChanged;
    }

    internal FrostedEnvironment Read()
    {
        if (disposed) return new(Available: false);
        // 无法注册时在现有轮询重新尝试；未知值不冒充“已解除”。
        if (modernEnergySaver && registration == IntPtr.Zero) RegisterEnergySaver();
        try
        {
            var energy = modernEnergySaver ? Volatile.Read(ref energySaver)
                : PowerManager.EnergySaverStatus == EnergySaverStatus.On ? 1 : 0;
            return new(Available: settings != null && energy is >= 0 and <= 2,
                TransparencyEnabled: settings?.AdvancedEffectsEnabled ?? true,
                HighContrast: SystemParameters.HighContrast, EnergySaver: energy < 0 ? uint.MaxValue : (uint)energy);
        }
        catch { return new(Available: false); }
    }

    private void RegisterEnergySaver()
    {
        if (disposed || registration != IntPtr.Zero) return;
        if (subscription == IntPtr.Zero)
        {
            subscription = Marshal.AllocHGlobal(Marshal.SizeOf<SubscribeParameters>());
            Marshal.StructureToPtr(new SubscribeParameters { Callback = Marshal.GetFunctionPointerForDelegate(callback) }, subscription, false);
        }
        try
        {
            var guid = EnergySaverGuid;
            if (PowerSettingRegisterNotification(ref guid, 2, subscription, out var handle) == 0) registration = handle;
        }
        catch { /* API 缺失／注册失败保留未知状态，下一轮可以重试。 */ }
    }

    private uint OnPowerChanged(IntPtr context, uint type, IntPtr setting)
    {
        try
        {
            if (disposed || type != 0x8013 || setting == IntPtr.Zero) return 0;
            var guid = Marshal.PtrToStructure<Guid>(setting);
            if (guid != EnergySaverGuid || Marshal.ReadInt32(setting, 16) != 4) return 0;
            var value = Marshal.ReadInt32(setting, 20);
            Volatile.Write(ref energySaver, value is >= 0 and <= 2 ? value : -1);
            Changed?.Invoke();
        }
        catch { /* native callback 不允许传播托管异常。 */ }
        return 0;
    }

    private void EffectsChanged(UISettings sender, object args) => Changed?.Invoke();
    private void LegacyEnergyChanged(object? sender, object args) => Changed?.Invoke();
    private void SystemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(SystemParameters.HighContrast)) Changed?.Invoke();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (settings != null) settings.AdvancedEffectsEnabledChanged -= EffectsChanged;
        SystemParameters.StaticPropertyChanged -= SystemChanged;
        if (!modernEnergySaver) PowerManager.EnergySaverStatusChanged -= LegacyEnergyChanged;
        if (registration != IntPtr.Zero) { PowerSettingUnregisterNotification(registration); registration = IntPtr.Zero; }
        if (subscription != IntPtr.Zero) { Marshal.FreeHGlobal(subscription); subscription = IntPtr.Zero; }
        GC.KeepAlive(callback);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint PowerCallback(IntPtr context, uint type, IntPtr setting);
    [StructLayout(LayoutKind.Sequential)] private struct SubscribeParameters { internal IntPtr Callback; internal IntPtr Context; }
    [DllImport("powrprof.dll")] private static extern uint PowerSettingRegisterNotification(ref Guid guid, uint flags, IntPtr recipient, out IntPtr handle);
    [DllImport("powrprof.dll")] private static extern uint PowerSettingUnregisterNotification(IntPtr handle);
}
