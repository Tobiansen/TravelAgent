package com.travelagent.ble;

import java.io.IOException;
import java.nio.*;
import java.nio.charset.*;

/** Bound user-supplied text before parsing/persisting. No URI, network or account operations. */
public final class IncomingShareText {
    public static final int MAX_BYTES = 64 * 1024;
    public static final String ORIGIN = "Android shared text · unverified source";
    private IncomingShareText() { }
    public static boolean supported(String action, String mime) {
        return ("android.intent.action.SEND".equals(action) && ("text/plain".equals(mime) || "text/html".equals(mime))) ||
            ("android.intent.action.PROCESS_TEXT".equals(action) && "text/plain".equals(mime));
    }
    public static String require(String text) throws IOException {
        if (text == null || text.length() > MAX_BYTES) throw new IOException("Share selected text up to 64 KiB. Attachments are not supported.");
        if (text.indexOf('\0') >= 0) throw new IOException("The shared text contains unsupported control characters.");
        boolean visible = text.codePoints().anyMatch(value -> !Character.isWhitespace(value) && !Character.isSpaceChar(value));
        if (!visible) throw new IOException("No selected text was shared. Select the ticket text and try again.");
        encode(text); return text;
    }
    public static byte[] encode(String text) throws IOException {
        if (text == null || text.length() > MAX_BYTES) throw new IOException("Share selected text up to 64 KiB.");
        try {
            ByteBuffer bytes = StandardCharsets.UTF_8.newEncoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).encode(CharBuffer.wrap(text));
            if (bytes.remaining() > MAX_BYTES) throw new IOException("Share selected text up to 64 KiB.");
            byte[] result = new byte[bytes.remaining()]; bytes.get(result); return result;
        } catch (CharacterCodingException invalid) { throw new IOException("The shared text has invalid Unicode characters."); }
    }
    public static String decode(byte[] bytes) throws IOException {
        if (bytes.length > MAX_BYTES) throw new IOException("Saved shared text exceeds the size limit.");
        try { return require(StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes)).toString()); }
        catch (CharacterCodingException invalid) { throw new IOException("Saved shared text is invalid."); }
    }
}
