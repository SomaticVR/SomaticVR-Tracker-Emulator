namespace SomaticVR.TrackerEmulator
{
    public enum EmulationStage
    {
        BridgeConnection,
        BridgeDeviceHandshake,
        DeviceInfoPacket,
        FullReset,
        ResetMounting,
        SendData
    }
}
