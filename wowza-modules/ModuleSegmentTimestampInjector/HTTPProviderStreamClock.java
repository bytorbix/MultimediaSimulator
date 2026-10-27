package com.multimediasimulator.wowza;

import java.io.OutputStream;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;

import com.wowza.wms.http.HTTPProvider2Base;
import com.wowza.wms.http.IHTTPRequest;
import com.wowza.wms.http.IHTTPResponse;
import com.wowza.wms.logging.WMSLogger;
import com.wowza.wms.logging.WMSLoggerFactory;
import com.wowza.wms.vhost.IVHost;

/**
 * Registers a stream's clock origin before it is published:
 *   POST /streamclock?stream={name}&startAtMs={epoch ms}
 * The module then stamps that stream's segments as startAtMs + (timecode - first packet timecode).
 * Registered in VHost.xml on the admin port, ahead of the catch-all "*" provider.
 */
public class HTTPProviderStreamClock extends HTTPProvider2Base
{
    private static final WMSLogger LOGGER = WMSLoggerFactory.getLogger(HTTPProviderStreamClock.class);

    @Override
    public void onHTTPRequest(IVHost vhost, IHTTPRequest req, IHTTPResponse resp)
    {
        if (!doHTTPAuthentication(vhost, req, resp))
            return;

        if (!"POST".equalsIgnoreCase(req.getMethod()))
        {
            respond(resp, 405, "Use POST /streamclock?stream={name}&startAtMs={epoch ms}");
            return;
        }

        // parsed from the raw query string, so it doesn't depend on how Wowza handles params on a POST
        Map<String, String> query = parseQuery(req.getQueryString());
        String stream = query.get("stream");
        String startAtText = query.get("startAtMs");

        if (stream == null || stream.isBlank() || startAtText == null)
        {
            respond(resp, 400, "Missing 'stream' or 'startAtMs'.");
            return;
        }

        long startAtMs;
        try
        {
            startAtMs = Long.parseLong(startAtText.trim());
        }
        catch (NumberFormatException e)
        {
            respond(resp, 400, "'startAtMs' must be epoch milliseconds, got '" + startAtText + "'.");
            return;
        }

        StreamClockRegistry.register(stream, startAtMs);
        LOGGER.info("HTTPProviderStreamClock: registered [" + stream + "] startAtMs=" + startAtMs);
        respond(resp, 200, "Registered " + stream + " startAtMs=" + startAtMs);
    }

    private static Map<String, String> parseQuery(String queryString)
    {
        Map<String, String> result = new HashMap<>();
        if (queryString == null || queryString.isEmpty())
            return result;

        for (String pair : queryString.split("&"))
        {
            int equalsIndex = pair.indexOf('=');
            if (equalsIndex <= 0)
                continue;

            String key = URLDecoder.decode(pair.substring(0, equalsIndex), StandardCharsets.UTF_8);
            String value = URLDecoder.decode(pair.substring(equalsIndex + 1), StandardCharsets.UTF_8);
            result.put(key, value);
        }
        return result;
    }

    private static void respond(IHTTPResponse resp, int code, String body)
    {
        try
        {
            resp.setHeader("Content-Type", "text/plain");
            resp.setResponseCode(code);

            OutputStream out = resp.getOutputStream();
            out.write((body + "\n").getBytes(StandardCharsets.UTF_8));
        }
        catch (Exception e)
        {
            LOGGER.error("HTTPProviderStreamClock: failed to write response", e);
        }
    }
}
