using System.Runtime.InteropServices;
using System.Windows.Interop;
using DesktopIconGrid.Services;

namespace DesktopIconGrid.App.Tests;

/// <summary>
/// Reading a window's AppUserModelID, which is how a minimised Store app's frame is still found: through the real
/// property store of a hidden window of this process, with the ID set and without one.
/// </summary>
[Collection(WpfWindowsCollection.Name)]
public sealed class AppUserModelIdInteropTests(WpfThread wpf)
{
    private const string Calculator = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

    [Fact]
    public void The_property_structures_have_the_sizes_Windows_has()
    {
        Assert.Equal(20, Marshal.SizeOf<NativeMethods.PROPERTYKEY>());
        Assert.Equal(Environment.Is64BitProcess ? 24 : 16, Marshal.SizeOf<NativeMethods.PROPVARIANT>());
    }

    [Fact]
    public void A_window_s_AppUserModelID_is_read_back_and_one_without_reads_as_none()
    {
        wpf.Invoke(() =>
        {
            using var window = new HwndSource(new HwndSourceParameters("AppUserModelID test") { WindowStyle = 0 }); // hidden, top-level

            Assert.Null(NativeMethods.AppUserModelIdOf(window.Handle));

            SetAppUserModelId(window.Handle, Calculator);
            try
            {
                Assert.Equal(Calculator, NativeMethods.AppUserModelIdOf(window.Handle));
            }
            finally
            {
                SetAppUserModelId(window.Handle, null); // the shell wants it taken off before the window goes
            }
        });
    }

    private static void SetAppUserModelId(IntPtr window, string? id)
    {
        var iid = typeof(NativeMethods.IPropertyStore).GUID;
        Assert.Equal(0, NativeMethods.SHGetPropertyStoreForWindow(window, ref iid, out var store));
        var value = new NativeMethods.PROPVARIANT();
        if (id is not null)
        {
            value.vt = NativeMethods.VT_LPWSTR;
            value.Pointer = Marshal.StringToCoTaskMemUni(id);
        }

        try
        {
            var key = NativeMethods.PKEY_AppUserModel_ID;
            Assert.Equal(0, store!.SetValue(ref key, ref value));
            Assert.Equal(0, store.Commit());
        }
        finally
        {
            NativeMethods.PropVariantClear(ref value);
            Marshal.ReleaseComObject(store!);
        }
    }
}
