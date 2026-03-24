# XumWrap

## Introduction

XumWrap is a lightweight networking helper built around the [FishNet](https://github.com/FirstGearGames/FishNet/) library for Unity. It provides a simple way to make remote procedure calls (RPCs) between clients and the server. The `XumView` component scans your scripts for methods marked with the `XumRPC` attribute and exposes them for network invocation. `XumNetwork` offers helper methods for spawning and managing `NetworkObject` instances.

## Getting Started

1. **Add XumNetwork**
   - Place the `XumNetwork` script in your scene on a `NetworkObject`. This component acts as a singleton and registers scene callbacks when the game starts.
2. **Create networked objects**
   - Add the `XumView` component to any `NetworkObject` that should expose RPC methods.
   - Decorate the desired methods with the `XumRPC` attribute. Optionally accept a `NetworkConnection` parameter as the last argument to get sender information.
3. **Invoke RPCs**
   - Call `XumView.RPC("MethodName", RpcTarget.All, arguments...)` to execute the method on the appropriate peers. The library handles serialization of common Unity types like `Vector3`, `Quaternion`, and even `GameObject` references.

For a complete list of serializable types, see the `ObjectSerializer` class in `XumView.cs`.
