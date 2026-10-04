package com.multimediasimulator.wowza;

import java.time.Instant;
import java.time.format.DateTimeFormatter;
import java.util.concurrent.ConcurrentHashMap;

import com.multimediasimulator.wowza.StreamClockRegistry.StreamClock;

import com.wowza.wms.amf.AMFPacket;
import com.wowza.wms.application.IApplicationInstance;
import com.wowza.wms.httpstreamer.cupertinostreaming.livestreampacketizer.CupertinoPacketHolder;
import com.wowza.wms.httpstreamer.cupertinostreaming.livestreampacketizer.IHTTPStreamerCupertinoLivePacketizerDataHandler2;
import com.wowza.wms.httpstreamer.cupertinostreaming.livestreampacketizer.LiveStreamPacketizerCupertino;
import com.wowza.wms.httpstreamer.cupertinostreaming.livestreampacketizer.LiveStreamPacketizerCupertinoChunk;
import com.wowza.wms.media.mp3.model.idtags.ID3Frames;
import com.wowza.wms.media.mp3.model.idtags.ID3V2FrameTextInformationUserDefined;
import com.wowza.wms.module.ModuleBase;
import com.wowza.wms.stream.livepacketizer.ILiveStreamPacketizer;
import com.wowza.wms.stream.livepacketizer.LiveStreamPacketizerActionNotifyBase;

/**
 * Stamps each HLS (Cupertino/TS) segment's ID3 header with the stream's clock time at that
 * segment's start: startAtMs + (chunk start timecode - first chunk's start timecode).
 * startAtMs comes from HTTPProviderStreamClock, registered before the stream is published.
 * Streams with no registered clock are not stamped, so downstream drops them instead of
 * silently getting wall-clock times.
 */
public class ModuleSegmentTimestampInjector extends ModuleBase
{
    private static final String ID3_FRAME_DESCRIPTION = "com.multimediasimulator.timestamp";

    private LiveStreamPacketizerListener listener;

    class CupertinoTimestampHandler implements IHTTPStreamerCupertinoLivePacketizerDataHandler2
    {
        private final LiveStreamPacketizerCupertino packetizer;
        private final String streamName;

        // the clock this packetizer last stamped with, so shutdown only removes that one
        private StreamClock lastClock;
        private boolean warnedNoClock;

        CupertinoTimestampHandler(LiveStreamPacketizerCupertino packetizer, String streamName)
        {
            this.packetizer = packetizer;
            this.streamName = streamName;
        }

        @Override
        public void onFillChunkStart(LiveStreamPacketizerCupertinoChunk chunk)
        {
            try
            {
                ID3Frames id3Frames = packetizer.getID3FramesHeader(chunk.getRendition());
                if (id3Frames == null)
                    return;

                id3Frames.clear();

                // looked up every chunk: a new POST (new clock) restarts the baseline from this chunk
                StreamClock clock = StreamClockRegistry.get(streamName);
                if (clock == null)
                {
                    if (!warnedNoClock)
                    {
                        getLogger().warn("ModuleSegmentTimestampInjector: no clock registered for [" + streamName + "], not stamping its segments");
                        warnedNoClock = true;
                    }
                    return;
                }
                warnedNoClock = false;
                lastClock = clock;

                long chunkStartTimecode = chunk.getStartTimecode();
                if (clock.baselineTimecode == null)
                    clock.baselineTimecode = chunkStartTimecode;

                long stampMs = clock.startAtMs + (chunkStartTimecode - clock.baselineTimecode);
                Instant stamp = Instant.ofEpochMilli(stampMs);

                ID3V2FrameTextInformationUserDefined frame = new ID3V2FrameTextInformationUserDefined(ID3_FRAME_DESCRIPTION);
                frame.setValue(stampMs + "|" + DateTimeFormatter.ISO_INSTANT.format(stamp));
                id3Frames.putFrame(frame);

                getLogger().info("ModuleSegmentTimestampInjector: stamped [" + streamName + "] chunk " + chunk.getChunkIndex()
                        + " @ " + stamp + " (startTimecode=" + chunkStartTimecode
                        + ", firstVideoTimecode=" + chunk.getFirstVideoTimecode()
                        + ", baseline=" + clock.baselineTimecode + ")");
            }
            catch (Exception e)
            {
                getLogger().error("ModuleSegmentTimestampInjector: failed to inject ID3 timestamp for [" + streamName + "]", e);
            }
        }

        void release()
        {
            if (lastClock != null)
                StreamClockRegistry.remove(streamName, lastClock);
        }

        @Override
        public void onFillChunkEnd(LiveStreamPacketizerCupertinoChunk chunk, long timecode)
        {
            // no-op: timestamp is written once, at chunk start
        }

        @Override
        public void onFillChunkDataPacket(LiveStreamPacketizerCupertinoChunk chunk, CupertinoPacketHolder holder,
                AMFPacket packet, ID3Frames id3Frames)
        {
            // no-op: not converting AMF stream events, only stamping chunk boundaries
        }

        @Override
        public void onFillChunkMediaPacket(LiveStreamPacketizerCupertinoChunk chunk, CupertinoPacketHolder holder, AMFPacket packet)
        {
            // no-op
        }
    }

    class LiveStreamPacketizerListener extends LiveStreamPacketizerActionNotifyBase
    {
        // destroy isn't given the stream name, so keep each packetizer's handler to release its clock
        private final ConcurrentHashMap<ILiveStreamPacketizer, CupertinoTimestampHandler> handlers = new ConcurrentHashMap<>();

        @Override
        public void onLiveStreamPacketizerCreate(ILiveStreamPacketizer packetizer, String streamName)
        {
            if (packetizer instanceof LiveStreamPacketizerCupertino cupertino)
            {
                CupertinoTimestampHandler handler = new CupertinoTimestampHandler(cupertino, streamName);
                cupertino.setDataHandler(handler);
                handlers.put(packetizer, handler);
                getLogger().info("ModuleSegmentTimestampInjector: attached to Cupertino packetizer for [" + streamName + "]");
            }
        }

        @Override
        public void onLiveStreamPacketizerDestroy(ILiveStreamPacketizer packetizer)
        {
            CupertinoTimestampHandler handler = handlers.remove(packetizer);
            if (handler != null)
            {
                handler.release();
                getLogger().info("ModuleSegmentTimestampInjector: released clock for [" + handler.streamName + "]");
            }
        }
    }

    public void onAppStart(IApplicationInstance appInstance)
    {
        listener = new LiveStreamPacketizerListener();
        appInstance.addLiveStreamPacketizerListener(listener);
        getLogger().info("ModuleSegmentTimestampInjector: onAppStart[" + appInstance.getContextStr() + "]");
    }

    public void onAppStop(IApplicationInstance appInstance)
    {
        if (listener != null)
        {
            appInstance.removeLiveStreamPacketizerListener(listener);
        }
    }
}
