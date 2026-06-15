/*
    SomaticVR Code is placed under the MIT license
    Copyright (c) 2025 Somatic VR, LLC

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in
    all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    THE SOFTWARE.
*/

using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace SomaticVR.TrackerEmulator
{
    public static class PacketBuilder
    {
        // SlimeVR UDP Packet Type IDs
        // 0: Heartbeat
        // 1: Rotation (deprecated)
        // 3: Handshake
        // 4: Acceleration
        // 10: Ping/Pong
        // 11: Serial
        // 12: Battery Level
        // 13: Tap
        // 14: Error
        // 15: Sensor Info
        // 16: Rotation2 (deprecated)
        // 17: Rotation Data
        // 18: Magnetometer Accuracy
        // 19: Signal Strength
        // 20: Temperature
        // 21: User Action
        // 22: Feature Flags
        // 23: Rotation and Acceleration
        // 24: Ack Config Change
        // 25: Set Config Flag
        // 26: Flex Data
        // 100: Bundle
        // 101: Bundle Compact
        // 200: Protocol Change

        // --- Packet Templates ---

        public static byte[] BuildHeartbeatPacket()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            // Packet ID for Handshake is first byte
            bw.Write(BitConverter.GetBytes(1).Reverse().ToArray());
            // Packet number (int64, always 0)
            bw.Write(new byte[8]);

            // Console.WriteLine($"Sending data {BitConverter.ToString(ms.ToArray())}");

            return ms.ToArray();
        }

        public static byte[] BuildHandshakePacket(byte[] mac, byte trackerType = 0x01)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            // Packet ID for Handshake (int32, big endian)
            bw.Write(BitConverter.GetBytes(3).Reverse().ToArray());
            // Packet number (int64, always 0)
            bw.Write(new byte[8]);
            // IMU type (int32, big endian)
            bw.Write(BitConverter.GetBytes(9).Reverse().ToArray()); // ICM-20948
            // MCU type (int32)
            bw.Write(BitConverter.GetBytes(6).Reverse().ToArray()); // MCU_ESP32_C3
			// Backwards compatibility, unused IMU data
            bw.Write(BitConverter.GetBytes(0).Reverse().ToArray()); // Unused
            bw.Write(BitConverter.GetBytes(0).Reverse().ToArray()); // Unused
            bw.Write(BitConverter.GetBytes(0).Reverse().ToArray()); // Unused
            // Protocol version (int32, big endian)
            bw.Write(BitConverter.GetBytes(21).Reverse().ToArray());
            // Firmware string length (byte)
            var tempString = Encoding.ASCII.GetBytes("Tracker Emulator");
            bw.Write((byte)tempString.Length);
            bw.Write(tempString);
            // MAC address (6 bytes)
            bw.Write(mac);
            // Tracker type (byte)
            bw.Write(trackerType);
            // Vendor Name
            tempString = Encoding.ASCII.GetBytes("SomaticVR");
            bw.Write((byte)tempString.Length);
            bw.Write(tempString);
            // Vendor URL
            tempString = Encoding.ASCII.GetBytes("www.somaticvr.com");
            bw.Write((byte)tempString.Length);
            bw.Write(tempString);
            // Product Name
            tempString = Encoding.ASCII.GetBytes("Orion Tracker Emulator");
            bw.Write((byte)tempString.Length);
            bw.Write(tempString);
            // Update Address
            tempString = Encoding.ASCII.GetBytes("updateme.somaticvr.com/never");
            bw.Write((byte)tempString.Length);
            bw.Write(tempString);
            // Update Name
            tempString = Encoding.ASCII.GetBytes("Never do this");
            bw.Write((byte)tempString.Length);
            bw.Write(tempString);
            // Console.WriteLine($"Sending data {BitConverter.ToString(ms.ToArray())}");

            return ms.ToArray();
        }

        public static byte[] BuildAccelerationPacket(byte sensorId, Int64 packetNumber, Vector3 acceleration)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            // Packet ID (int32, big endian)
            bw.Write(BitConverter.GetBytes(4).Reverse().ToArray());
            // Packet number (int64, always 0)
            bw.Write(BitConverter.GetBytes(packetNumber).Reverse().ToArray());
            BuildAccelerationInnerPacket(bw, sensorId, acceleration);
            // Console.WriteLine($"Sending data {BitConverter.ToString(ms.ToArray())}");
            return ms.ToArray();
        }
        
        public static void BuildAccelerationInnerPacket(BinaryWriter bw, byte sensorId, Vector3 acceleration)
        {
            // Rotation quaternion (4 x float)
            bw.Write(BitConverter.GetBytes(acceleration.X).Reverse().ToArray());
            bw.Write(BitConverter.GetBytes(acceleration.Y).Reverse().ToArray());
            bw.Write(BitConverter.GetBytes(acceleration.Z).Reverse().ToArray());
            // Sensor ID (byte)
            bw.Write((byte)sensorId);
        }

        public static byte[] BuildPingPongPacket(byte[] packet)
        {
            // For this one, just send the received packet back
            return packet;
        }   

        public static byte[] BuildSerialPacket(/* params */)
        {
            // TODO: Implement Serial packet (ID 11)
            throw new NotImplementedException();
        }

        public static byte[] BuildBatteryLevelPacket(/* params */)
        {
            // TODO: Implement Battery Level packet (ID 12)
            throw new NotImplementedException();
        }

        public static byte[] BuildTapPacket(/* params */)
        {
            // TODO: Implement Tap packet (ID 13)
            throw new NotImplementedException();
        }

        public static byte[] BuildErrorPacket(/* params */)
        {
            // TODO: Implement Error packet (ID 14)
            throw new NotImplementedException();
        }

        public static byte[] BuildSensorInfoPacket(Int64 packetNumber, SensorEmulator sensor)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            // Packet ID (int32, big endian)
            bw.Write(BitConverter.GetBytes(15).Reverse().ToArray());
            // Packet number (int64, always 0)
            bw.Write(BitConverter.GetBytes(packetNumber).Reverse().ToArray());
            // Sensor ID (byte) 
            bw.Write((byte)sensor.index);
            // State (byte)
            bw.Write((byte)sensor.state);
            // Type (byte)
            bw.Write((byte)sensor.type);
            // Sensor Congfig Data (byte array)
            bw.Write(BitConverter.GetBytes((short)sensor.configData).Reverse().ToArray());
            // Has completed rest calibration (byte)
            bw.Write(sensor.hasCompletedRestCalibration ? (byte)1 : (byte)0);
            // Position (byte)
            bw.Write((byte)sensor.position);
            // Data type (byte)
            bw.Write((byte)sensor.dataType);

            // TPS Counter Average TPS (float)
            bw.Write(BitConverter.GetBytes(100.0f).Reverse().ToArray()); // Placeholder for now
            // Data Counter Average TPS (float)
            bw.Write(BitConverter.GetBytes(100.0f).Reverse().ToArray()); // Placeholder for now
            return ms.ToArray();
        }   

        public static byte[] BuildRotationPacket(byte sensorId, Int64 packetNumber, Quaternion rotation)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            // Packet ID (int32, big endian)
            bw.Write(BitConverter.GetBytes(17).Reverse().ToArray());
            // Packet number (int64, always 0)
            bw.Write(BitConverter.GetBytes(packetNumber).Reverse().ToArray());
            BuildRotationInnerPacket(bw, sensorId, rotation);
            // Console.WriteLine($"Sending data {BitConverter.ToString(ms.ToArray())}");
            return ms.ToArray();
        }

        private static void BuildRotationInnerPacket(BinaryWriter bw, byte sensorId, Quaternion rotation)
        {
            // Sensor ID (byte)
            bw.Write((byte)sensorId);
            // Data type (byte)
            bw.Write((byte)1);  // DATA_TYPE_NORMAL
            // Rotation quaternion (4 x float)
            bw.Write(BitConverter.GetBytes(rotation.X).Reverse().ToArray());
            bw.Write(BitConverter.GetBytes(rotation.Y).Reverse().ToArray());
            bw.Write(BitConverter.GetBytes(rotation.Z).Reverse().ToArray());
            bw.Write(BitConverter.GetBytes(rotation.W).Reverse().ToArray());
            // accuracy Info (byte)
            bw.Write((byte)0);
        }

        public static byte[] BuildMagnetometerAccuracyPacket(/* params */)
        {
            // TODO: Implement Magnetometer Accuracy packet (ID 18)
            throw new NotImplementedException();
        }

        public static byte[] BuildSignalStrengthPacket(/* params */)
        {
            // TODO: Implement Signal Strength packet (ID 19)
            throw new NotImplementedException();
        }

        public static byte[] BuildTemperaturePacket(/* params */)
        {
            // TODO: Implement Temperature packet (ID 20)
            throw new NotImplementedException();
        }

        public static byte[] BuildUserActionPacket(/* params */)
        {
            // TODO: Implement User Action packet (ID 21)
            throw new NotImplementedException();
        }

        public static byte[] BuildFeatureFlagsPacket(/* params */)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            // Packet ID (int32, big endian)
            bw.Write(BitConverter.GetBytes(22).Reverse().ToArray());
            // Send the 'Firmware' feature flags
            byte featureFlags = 0b00000001; // Wifi Scanning Enabled
            featureFlags |= 0b00000010; // Sensor Config Enabled
            bw.Write(featureFlags);
            // Console.WriteLine($"Sending data {BitConverter.ToString(ms.ToArray())}");
            return ms.ToArray();
        }

        public static byte[] BuildRotationAndAccelerationPacket(/* params */)
        {
            // TODO: Implement Rotation and Acceleration packet (ID 23)
            throw new NotImplementedException();
        }

        public static byte[] BuildAckConfigChangePacket(/* params */)
        {
            // TODO: Implement Ack Config Change packet (ID 24)
            throw new NotImplementedException();
        }

        public static byte[] BuildSetConfigFlagPacket(/* params */)
        {
            // TODO: Implement Set Config Flag packet (ID 25)
            throw new NotImplementedException();
        }

        public static byte[] BuildFlexDataPacket(/* params */)
        {
            // TODO: Implement Flex Data packet (ID 26)
            throw new NotImplementedException();
        }

        public static byte[] BuildBundlePacket(/* params */)
        {
            // TODO: Implement Bundle packet (ID 100)
            throw new NotImplementedException();
        }

        public static byte[] BuildBundleCompactPacket(/* params */)
        {
            // TODO: Implement Bundle Compact packet (ID 101)
            throw new NotImplementedException();
        }

        public static byte[] BuildProtocolChangePacket(/* params */)
        {
            // TODO: Implement Protocol Change packet (ID 200)
            throw new NotImplementedException();
        }


    }
}
