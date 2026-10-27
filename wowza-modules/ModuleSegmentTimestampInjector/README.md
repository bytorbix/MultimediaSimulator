# ModuleSegmentTimestampInjector

Wowza Streaming Engine module that stamps every HLS (Cupertino/TS) segment with the
stream's **shared clock** time, as a `timed_id3` ID3 tag. The Data Ingestor reads that
tag to give each video frame a UTC time and match it to telemetry.

The clock works like the telemetry simulator's: a caller registers a `startAt` for the
stream before publishing, and the module stamps

```
segment stamp = startAt + (segment start timecode − first segment's start timecode)
```

so the first segment is exactly `startAt` and every later one is offset along the
stream's own timeline. Network and processing delay never end up in the stamps.

This is a Java/Wowza toolchain, independent of the .NET MultimediaSimulator build.

## Files

| File | Role |
|---|---|
| `ModuleSegmentTimestampInjector.java` | Application module. Hooks the Cupertino packetizer and writes the stamp at each segment start. Streams with no registered clock are **not** stamped (the ingestor then drops them, so a missing registration is visible). |
| `HTTPProviderStreamClock.java` | HTTP provider for `POST /streamclock?stream={name}&startAtMs={epoch ms}`. 200 registered, 400 bad/missing params, 405 not a POST. |
| `StreamClockRegistry.java` | Stream name → clock (`startAtMs` + baseline), shared by the provider and the module. A new POST replaces the clock, which resets the baseline. |

The MultimediaSimulator sends the POST itself on `/api/uav/{uavId}/start` (see
`Streaming:WowzaClockUrl`), then publishes at `startAt`.

## Requirement: Wowza must receive the stream directly

The baseline is the first segment Wowza packetizes, so that segment has to be the
file's first frame. Publish straight to Wowza (`rtsp://<wowza>:1935/{uavId}/{uavId}.stream`).
Pulling through a relay (MediaMTX + a `.stream` file) lets Wowza join mid-stream and
offsets every stamp by however late it joined.

## Build and install

```bash
./build.sh            # compile against the host's Wowza libs, install the jar (backs up the old one)
./build.sh --check    # compile only
```

It copies the sources to the Wowza host, compiles them in an `eclipse-temurin:17-jdk`
container against `/home/user/wowza/lib` (the libs of the running Wowza, currently
4.11.3), and writes `wowza-timestamp-module.jar` there. Override the target with
`WOWZA_HOST` / `WOWZA_DIR`. Then restart Wowza: `docker restart wowza-engine`
(it takes ~30 s before it answers HTTP again; a restart also clears all registered clocks).

On this host all of Wowza's `lib/` is bind-mounted from `/home/user/wowza/lib`, so the
jar goes there (there is no `lib.addon/`). With a stock install, use `lib.addon/` instead
and don't bind-mount `lib/` itself, or Wowza loses its own jars.

## Wowza configuration

**`conf/VHost.xml`**: register the provider inside a HostPort's `<HTTPProviders>`, before
any catch-all `*` provider. On this host it's in the *Default Streaming* HostPort, so it
answers on **1935**:

```xml
<HTTPProvider>
    <BaseClass>com.multimediasimulator.wowza.HTTPProviderStreamClock</BaseClass>
    <RequestFilters>streamclock*</RequestFilters>
    <AuthenticationMethod>none</AuthenticationMethod>
</HTTPProvider>
```

**`conf/{app}/Application.xml`**: register the module, and allow RTSP publishing:

```xml
<Module>
    <Name>ModuleSegmentTimestampInjector</Name>
    <Description>Stamps each HLS segment with the stream's shared clock</Description>
    <Class>com.multimediasimulator.wowza.ModuleSegmentTimestampInjector</Class>
</Module>
```

Under `<RTP><Authentication>`, `<PublishMethod>none</PublishMethod>` (LAN test rig), or keep
`digest` and give the publisher credentials.

## Verify

```bash
curl -i -X POST "http://<wowza>:1935/streamclock?stream=42.stream&startAtMs=1791115200000"
```

After publishing, Wowza's access log shows one line per segment:

```
stamped [42.stream] chunk 1 @ <startAt> (startTimecode=…, firstVideoTimecode=…, baseline=…)
```

and `ffprobe` on a segment shows the ID3 packet at the segment's first video frame pts,
with the value `epochMillis|ISO-8601`.
