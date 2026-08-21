namespace SomaticVR.TrackerEmulator
{
    public enum EmulationStage
    {
        BridgeConnection,
        BridgeDeviceHandshake,
        DeviceInfoPacket,
        FullReset,
        ResetMounting,
        FootMounting,
        StandingStayAligned,
        ChairStayAligned,
        FloorStayAligned,
        SendData
    }
}
