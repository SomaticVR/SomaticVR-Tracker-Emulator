# SomaticVR Tracker Emulator

A C#/.NET 8.0 console application that emulates SlimeVR-compatible trackers for development, testing, and integration purposes.

## Features

### Current
- Emulates multiple trackers and sensors
- Sends random quaternion rotation data to a SlimeVR server
- Implements SlimeVR UDP protocol handshake and packet types
- Command-line arguments for tracker and sensor count
- Retry logic for server discovery

### Planned
- Retry covnnecting to server after a lost connection
- Emulate acceleration data
- Replay saved data from real trackers for testing

## Usage

### Build

```
dotnet build
```

### Run

```
dotnet run -- [trackerCount] [sensorsPerTracker]
```
- `trackerCount` (optional, default: 10): Number of trackers to emulate
- `sensorsPerTracker` (optional, default: 1): Number of sensors per tracker

Example:
```
dotnet run -- 5 2
```

## Project Structure
- `src/` - All source code files
- `SomaticVR-TrackerEmulator.csproj` - Project file
- `LICENSE-MIT.txt`, `LICENSE-APACHE.txt` - License files

## License

This project is dual-licensed under the MIT or Apache 2.0 License, at your option.

Copyright (c) 2025 Somatic VR, LLC
