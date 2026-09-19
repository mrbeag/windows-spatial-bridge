using System.Runtime.CompilerServices;

namespace SpatialBridgeTray;

public sealed class SettingsFormLayoutTests
{
    [Theory]
    [InlineData(false, false, false, "Audio setup required", "Set up audio")]
    [InlineData(true, false, false, "Stopped", "Enable")]
    [InlineData(false, false, true, "Audio setup required", "Get VB-CABLE")]
    public void HiddenRefresh_PreservesActionButtonColumn(
        bool formatsValid, bool isDefault, bool vbCableMissing,
        string status, string expectedButton)
    {
        RunSta(() =>
        {
            var controller = (TrayContext)RuntimeHelpers.GetUninitializedObject(typeof(TrayContext));
            using var form = new SettingsForm(controller);
            var setup = new AudioSetupStatus(false, "Audio setup required",
                formatsValid, isDefault, vbCableMissing);

            form.RefreshView([], null, status, setup,
                "Available — static mask 0xFFFF, 128 dynamic objects", running: false);

            Assert.True(form.ActionColumnWidth > 0);
            Assert.Equal(expectedButton, form.ActionText);
        });
    }

    [Fact]
    public void ReadyState_HidesActionButtonColumn()
    {
        RunSta(() =>
        {
            var controller = (TrayContext)RuntimeHelpers.GetUninitializedObject(typeof(TrayContext));
            using var form = new SettingsForm(controller);
            var setup = new AudioSetupStatus(true, "Ready", FormatsValid: true, IsDefault: true);

            form.RefreshView([], null, "Ready → Headphones", setup,
                "Available — static mask 0xFFFF, 128 dynamic objects", running: true);

            Assert.Equal(0, form.ActionColumnWidth);
            Assert.Equal("", form.ActionText);
        });
    }

    static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
