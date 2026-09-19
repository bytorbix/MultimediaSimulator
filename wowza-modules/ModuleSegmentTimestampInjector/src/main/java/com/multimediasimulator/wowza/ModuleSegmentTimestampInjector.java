package com.multimediasimulator.wowza;

import java.time.Instant;
import java.time.format.DateTimeFormatter;

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
 * Stamps a UTC-now ID3 tag into the ID3 header of every HLS (Cupertino/TS) segment,
 * once per chunk boundary, for downstream sync verification.
 */
public class ModuleSegmentTimestampInjector extends ModuleBase
{
    private static final String ID3_FRAME_DESCRIPTION = "com.multimediasimulator.timestamp";

    private LiveStreamPacketizerListener listener;

    class CupertinoTimestampHandler implements IHTTPStreamerCupertinoLivePacketizerDataHandler2
    {
        private final LiveStreamPacketizerCupertino packetizer;
        private final String streamName;

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

                Instant now = Instant.now();
                ID3V2FrameTextInformationUserDefined frame = new ID3V2FrameTextInformationUserDefined(ID3_FRAME_DESCRIPTION);
                frame.setValue(now.toEpochMilli() + "|" + DateTimeFormatter.ISO_INSTANT.format(now));
                id3Frames.putFrame(frame);

                getLogger().info("ModuleSegmentTimestampInjector: stamped [" + streamName + "] chunk " + chunk.getChunkIndex() + " @ " + now);
            }
            catch (Exception e)
            {
                getLogger().error("ModuleSegmentTimestampInjector: failed to inject ID3 timestamp for [" + streamName + "]", e);
            }
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
        @Override
        public void onLiveStreamPacketizerCreate(ILiveStreamPacketizer packetizer, String streamName)
        {
            if (packetizer instanceof LiveStreamPacketizerCupertino cupertino)
            {
                cupertino.setDataHandler(new CupertinoTimestampHandler(cupertino, streamName));
                getLogger().info("ModuleSegmentTimestampInjector: attached to Cupertino packetizer for [" + streamName + "]");
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
