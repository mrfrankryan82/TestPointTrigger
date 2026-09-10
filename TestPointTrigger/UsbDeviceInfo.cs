namespace TestPointTrigger
{
    /// <summary>
    /// Lightweight snapshot of a Win32_PnPEntity row for USB-class devices
    /// (root hubs, generic hubs, and host controllers all report
    /// PNPClass == "USB", which is exactly what shows under Device
    /// Manager's "Universal Serial Bus controllers" node).
    /// </summary>
    public class UsbDeviceInfo
    {
        public string Name { get; set; }
        public string DeviceId { get; set; }
        public string Status { get; set; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Status) ? Name : $"{Name}  [{Status}]";
        }
    }
}
