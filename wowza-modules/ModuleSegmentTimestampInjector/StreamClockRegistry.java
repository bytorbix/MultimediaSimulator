package com.multimediasimulator.wowza;

import java.util.concurrent.ConcurrentHashMap;

/**
 * Stream name -> clock, shared between the HTTP provider (VHost level) that registers
 * a StartAt and the module (application level) that stamps segments from it.
 */
public final class StreamClockRegistry
{
    // one stream's clock: the StartAt it was registered with, and the first chunk's timecode once it arrives
    static final class StreamClock
    {
        final long startAtMs;

        // null until the first chunk; written by the packetizer thread, read by others
        volatile Long baselineTimecode;

        StreamClock(long startAtMs)
        {
            this.startAtMs = startAtMs;
        }
    }

    private static final ConcurrentHashMap<String, StreamClock> CLOCKS = new ConcurrentHashMap<>();

    private StreamClockRegistry() { }

    // always a fresh clock, so re-registering a stream resets its baseline
    public static void register(String stream, long startAtMs)
    {
        CLOCKS.put(stream, new StreamClock(startAtMs));
    }

    static StreamClock get(String stream)
    {
        return CLOCKS.get(stream);
    }

    // only removes this exact clock, so an old packetizer shutting down can't drop a newer registration
    static void remove(String stream, StreamClock clock)
    {
        CLOCKS.remove(stream, clock);
    }
}
