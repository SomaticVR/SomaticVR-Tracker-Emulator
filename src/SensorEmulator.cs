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
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace SomaticVR.TrackerEmulator
{
    public enum SensorStatus : byte {
        SENSOR_OFFLINE = 0,
        SENSOR_OK = 1,
        SENSOR_ERROR = 2
    };

    public enum SensorTypeID : byte {
        Unknown = 0,
        MPU9250,
        MPU6500,
        BNO080,
        BNO085,
        BNO055,
        MPU6050,
        BNO086,
        BMI160,
        ICM20948,
        ICM42688,
        BMI270,
        LSM6DS3TRC,
        LSM6DSV,
        LSM6DSO,
        LSM6DSR,
        ICM45686,
        ICM45605,
        ADC_RESISTANCE,
        Empty = 255
    };

    [Flags]
    public enum SensorConfigBits : ushort {
	    magEnabled = 0,
	    magSupported = 1<<0,
	    calibrationEnabled = 1<<1,
	    calibrationSupported = 1 << 2,
	    tempGradientCalibrationEnabled = 1 << 3,
	    tempGradientCalibrationSupported = 1 << 4,
        max = 1 << 15,
    }

    public enum SensorPosition : byte {
        POSITION_NO = 0,
        POSITION_HEAD = 1,
        POSITION_NECK = 2,
        POSITION_UPPER_CHEST = 3,
        POSITION_CHEST = 4,
        POSITION_WAIST = 5,
        POSITION_HIP = 6,
        POSITION_LEFT_UPPER_LEG = 7,
        POSITION_RIGHT_UPPER_LEG = 8,
        POSITION_LEFT_LOWER_LEG = 9,
        POSITION_RIGHT_LOWER_LEG = 10,
        POSITION_LEFT_FOOT = 11,
        POSITION_RIGHT_FOOT = 12,
        POSITION_LEFT_LOWER_ARM = 13,
        POSITION_RIGHT_LOWER_ARM = 14,
        POSITION_LEFT_UPPER_ARM = 15,
        POSITION_RIGHT_UPPER_ARM = 16,
        POSITION_LEFT_HAND = 17,
        POSITION_RIGHT_HAND = 18,
        POSITION_LEFT_SHOULDER = 19,
        POSITION_RIGHT_SHOULDER = 20,
        POSITION_LEFT_THUMB_PROXIMAL = 21,
        POSITION_LEFT_THUMB_INTERMEDIATE = 22,
        POSITION_LEFT_THUMB_DISTAL = 23,
        POSITION_LEFT_INDEX_PROXIMAL = 24,
        POSITION_LEFT_INDEX_INTERMEDIATE = 25,
        POSITION_LEFT_INDEX_DISTAL = 26,
        POSITION_LEFT_MIDDLE_PROXIMAL = 27,
        POSITION_LEFT_MIDDLE_INTERMEDIATE = 28,
        POSITION_LEFT_MIDDLE_DISTAL = 29,
        POSITION_LEFT_RING_PROXIMAL = 30,
        POSITION_LEFT_RING_INTERMEDIATE = 31,
        POSITION_LEFT_RING_DISTAL = 32,
        POSITION_LEFT_LITTLE_PROXIMAL = 33,
        POSITION_LEFT_LITTLE_INTERMEDIATE = 34,
        POSITION_LEFT_LITTLE_DISTAL = 35,
        POSITION_RIGHT_THUMB_PROXIMAL = 36,
        POSITION_RIGHT_THUMB_INTERMEDIATE = 37,
        POSITION_RIGHT_THUMB_DISTAL = 38,
        POSITION_RIGHT_INDEX_PROXIMAL = 39,
        POSITION_RIGHT_INDEX_INTERMEDIATE = 40,
        POSITION_RIGHT_INDEX_DISTAL = 41,
        POSITION_RIGHT_MIDDLE_PROXIMAL = 42,
        POSITION_RIGHT_MIDDLE_INTERMEDIATE = 43,
        POSITION_RIGHT_MIDDLE_DISTAL = 44,
        POSITION_RIGHT_RING_PROXIMAL = 45,
        POSITION_RIGHT_RING_INTERMEDIATE = 46,
        POSITION_RIGHT_RING_DISTAL = 47,
        POSITION_RIGHT_LITTLE_PROXIMAL = 48,
        POSITION_RIGHT_LITTLE_INTERMEDIATE = 49,
        POSITION_RIGHT_LITTLE_DISTAL = 50
    };

    public enum SensorDataType : byte {
        SENSOR_DATATYPE_ROTATION = 0,
        SENSOR_DATATYPE_FLEX_RESISTANCE,
        SENSOR_DATATYPE_FLEX_ANGLE
    };

    public class SensorEmulator
    {
        public byte index {get;}

            // State (byte)
        public SensorStatus state {get; private set;} = SensorStatus.SENSOR_OK;
        public SensorTypeID type {get; private set;} = SensorTypeID.ICM20948;

        public SensorConfigBits configData {get; private set;} = (SensorConfigBits)0;
        public bool hasCompletedRestCalibration { get; private set; } = true;
        public SensorPosition position {get; set;} = SensorPosition.POSITION_NO; // Default position is "no position"

        public SensorDataType dataType {get; private set;} = SensorDataType.SENSOR_DATATYPE_ROTATION; 
        public Quaternion rotation {get; private set;} 

        public SensorEmulator(byte sensorIndex, Quaternion? initialRotation = null)
        {
            index = sensorIndex;
            rotation = initialRotation ?? Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI/2);
        }

        public void UpdateRotationAsync()
        {
            Random rnd = new Random();
            const double offsetMax = 0.005f;
            // Quaternion randomRotation = new Quaternion(
            //     (float)(rnd.NextDouble() * 2 * offsetMax - offsetMax), // X in [-offsetMax, offsetMax]
            //     (float)(rnd.NextDouble() * 2 * offsetMax - offsetMax), // Y in [-offsetMax, offsetMax]
            //     (float)(rnd.NextDouble() * 2 * offsetMax - offsetMax), // Z in [-offsetMax, offsetMax]
            //     (float)(rnd.NextDouble() * 2 * offsetMax - offsetMax)  // W in [-offsetMax, offsetMax]
            // );
            // Quaternion randomRotation = new Quaternion(
            //     (float)(rnd.NextDouble() * 2 * offsetMax - offsetMax), // X in [-offsetMax, offsetMax]
            //     0.0f, // Y in [-offsetMax, offsetMax]
            //     0.0f, // Z in [-offsetMax, offsetMax]
            //     0.0f  // W in [-offsetMax, offsetMax]
            // );
            Quaternion randomRotation = Quaternion.CreateFromYawPitchRoll((float)(rnd.NextDouble() * 2 * offsetMax - offsetMax), (float)(rnd.NextDouble() * 2 * offsetMax - offsetMax), (float)(rnd.NextDouble() * 2 * offsetMax - offsetMax));
            rotation = randomRotation * rotation; // Apply random rotation to current rotation
            rotation = Quaternion.Normalize(rotation); // Normalize to ensure valid quaternion
            // Console.WriteLine($"Sensor {index}: Updated rotation to {rotation}");
        }

    }
}
