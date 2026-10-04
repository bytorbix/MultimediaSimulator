# Postman Collection

Import `Multimedia Simulator.postman_collection.json` into Postman (Import -> Files) to hit this service's API locally.

## Requests

- **Upload UAV File (42)** — `POST /api/uav/{uavId}/upload` (form field `file`). Saves the MPEG-TS file, doesn't stream.
  The file must be MPEG-TS with an H264 video track (video is passed through unchanged, audio is re-encoded to AAC).
  409 while that UAV is scheduled or streaming. The `src` field points at a local file path, so after importing you
  may need to reselect the file in Postman.
- **Start UAV Stream (42)** — `POST /api/uav/{uavId}/start`, body `{ "startAt": "2026-10-04T12:00:00Z" }` (optional,
  defaults to now; 400 if in the past or more than 7 days ahead). Registers `startAt` as the stream's clock origin with
  the Wowza module (`Streaming:WowzaClockUrl`), returns 202, then at `startAt` publishes the uploaded file over RTSP
  straight to Wowza (`Streaming:PublishUrlFormat`, app `{uavId}`, stream `{uavId}.stream`). Give the telemetry
  simulator the same `startAt`. 404 if nothing was uploaded, 409 if already scheduled/streaming, 502 if Wowza didn't
  accept the clock. The stream loops indefinitely once started — always follow up with Stop UAV Stream.
- **Stop UAV Stream (42)** — stops the stream, or cancels it if it is still waiting for its `startAt`.

Update `startAt` in the Start request's body before sending; a past date is rejected.

## Typical order

1. Upload UAV File
2. Start UAV Stream (same `startAt` as the telemetry simulation)
3. Stop UAV Stream when done
