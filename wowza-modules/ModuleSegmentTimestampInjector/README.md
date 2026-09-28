# ModuleSegmentTimestampInjector

Wowza Streaming Engine server-side module. Stamps a UTC-now timestamp into every
HLS (Cupertino/TS) segment as a `timed_id3` ID3 tag, tied to the actual segment
boundary (not a fixed interval) — for verifying frame/telemetry sync downstream.

## How it works

- Hooks `IApplicationInstance.addLiveStreamPacketizerListener` on app start.
- When a Cupertino (HLS/TS) packetizer is created for a stream, attaches an
  `IHTTPStreamerCupertinoLivePacketizerDataHandler2`.
- On `onFillChunkStart` (fires once per real segment), writes a `TXXX` ID3 frame
  containing `epochMillis|ISO-8601` for "now" directly into that chunk's ID3 header.

This is a Java/Wowza toolchain, independent of the .NET MultimediaSimulator build —
nothing here is built or referenced by the main solution.

## Build

Compile against the target Wowza Streaming Engine's own `lib/*.jar` (matching
its version). No build script is checked in yet; this was built ad hoc via a
JDK container against a copy of Wowza's own libs, e.g.:

```bash
docker run --rm -v <path-to-wowza-libs>:/libs -v <this-dir>:/build \
  eclipse-temurin:17-jdk javac -classpath '/libs/*' \
  -d /build/out /build/src/main/java/com/multimediasimulator/wowza/ModuleSegmentTimestampInjector.java

docker run --rm -v <this-dir>:/build eclipse-temurin:17-jdk \
  jar cf /build/wowza-timestamp-module.jar -C /build/out .
```

## Deploy

1. Copy the built jar to Wowza's `lib.addon/` directory (NOT `lib/` — bind-mounting
   `lib/` directly will hide Wowza's own bundled jars and crash-loop the engine).
2. Register the module in the target application's `Application.xml`:

```xml
<Module>
    <Name>ModuleSegmentTimestampInjector</Name>
    <Description>Stamps a UTC-now ID3 tag into each HLS segment</Description>
    <Class>com.multimediasimulator.wowza.ModuleSegmentTimestampInjector</Class>
</Module>
```

3. Restart/reload the application.

## Verify

```bash
ffprobe -show_streams "http://<wowza-host>:1935/live/_definst_/<stream>/chunklist_w<N>.m3u8"
```

Look for a `Data: timed_id3` stream alongside video/audio.
