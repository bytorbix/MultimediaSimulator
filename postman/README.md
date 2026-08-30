# Postman Collection

Import `Multimedia Simulator.postman_collection.json` into Postman (Import -> Files) to hit this service's API locally.

## Requests

- **Start UAV Stream (42)** — pushes a local MPEG-TS file to MediaMTX as an RTSP path for a tail number. The file must be MPEG-TS with an H264 video track (video is passed through unchanged, audio is re-encoded to AAC). The stream loops indefinitely once started — always follow up with Stop UAV Stream. The `src` field points at a local file path, so after importing you may need to reselect the file in Postman.
- **Stop UAV Stream (42)** — stops the stream for that tail number.

## Typical order

1. Start UAV Stream
2. Stop UAV Stream when done
