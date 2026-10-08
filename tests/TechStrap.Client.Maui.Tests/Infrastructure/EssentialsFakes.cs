using Microsoft.Extensions.Options;
using NSubstitute;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;

namespace TechStrap.Client.Maui.Tests.Infrastructure;

/// <summary>Substitutes for the Essentials interfaces the collector reads, with realistic values.</summary>
internal sealed record EssentialsFakes(
    IAppInfo AppInfo,
    IDeviceInfo DeviceInfo,
    IConnectivity Connectivity,
    IDeviceDisplay Display,
    IBattery Battery)
{
    public static EssentialsFakes Default()
    {
        var appInfo = Substitute.For<IAppInfo>();
        appInfo.Name.Returns("Puppies Plus");
        appInfo.VersionString.Returns("2.3.1");
        appInfo.BuildString.Returns("231");
        appInfo.PackageName.Returns("com.syntaxcircus.puppies");

        var deviceInfo = Substitute.For<IDeviceInfo>();
        deviceInfo.Platform.Returns(DevicePlatform.Android);
        deviceInfo.VersionString.Returns("14");
        deviceInfo.Manufacturer.Returns("Google");
        deviceInfo.Model.Returns("Pixel 8");
        deviceInfo.Idiom.Returns(DeviceIdiom.Phone);
        deviceInfo.DeviceType.Returns(DeviceType.Physical);

        var connectivity = Substitute.For<IConnectivity>();
        connectivity.NetworkAccess.Returns(NetworkAccess.Internet);

        var display = Substitute.For<IDeviceDisplay>();
        display.MainDisplayInfo.Returns(new DisplayInfo(1080, 2400, 2.625, DisplayOrientation.Portrait, DisplayRotation.Rotation0));

        var battery = Substitute.For<IBattery>();
        battery.State.Returns(BatteryState.Charging);
        battery.ChargeLevel.Returns(0.8);

        return new EssentialsFakes(appInfo, deviceInfo, connectivity, display, battery);
    }

    public MauiDeviceContextCollector Collector(DeviceContextOptions? options = null) =>
        new(AppInfo, DeviceInfo, Connectivity, Display, Battery, Options.Create(options ?? new DeviceContextOptions()));
}
