package com.travelagent.ble;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;
import android.text.Html;
import android.widget.Toast;
import java.io.IOException;
import java.time.Instant;

/** Explicit share/context-menu entry, accepting selected text only. All imported fields need user review. */
public final class IncomingShareActivity extends Activity {
    @Override protected void onCreate(Bundle state) {
        super.onCreate(state);
        setResult(RESULT_CANCELED); // PROCESS_TEXT must never replace text in the source application.
        if (state != null) { openUnity(); finish(); return; }
        try {
            String text = selectedText(getIntent());
            IncomingShareInbox.Record accepted = new IncomingShareInbox(getFilesDir()).accept(text, Instant.now().toString());
            Toast.makeText(this, accepted == null ? "Review or discard the earlier shared text in TravelAgent first. This new share was not saved." : "Shared text saved privately. Review it in TravelAgent.", Toast.LENGTH_LONG).show();
            openUnity();
        } catch (IOException rejected) { Toast.makeText(this, rejected.getMessage(), Toast.LENGTH_LONG).show(); }
        catch (RuntimeException rejected) { Toast.makeText(this, "This share could not be read. Select plain ticket text and try again.", Toast.LENGTH_LONG).show(); }
        finally { finish(); }
    }
    private String selectedText(Intent intent) throws IOException {
        if (intent == null || !IncomingShareText.supported(intent.getAction(), intent.getType()) || intent.hasExtra(Intent.EXTRA_STREAM))
            throw new IOException("Share selected plain ticket text. Images, PDF files and attachments are not supported.");
        if (Intent.ACTION_PROCESS_TEXT.equals(intent.getAction())) return IncomingShareText.require(bounded(intent.getCharSequenceExtra(Intent.EXTRA_PROCESS_TEXT)));
        CharSequence supplied = intent.getCharSequenceExtra(Intent.EXTRA_TEXT);
        if ("text/plain".equals(intent.getType()) && supplied != null && supplied.length() > 0) return IncomingShareText.require(bounded(supplied));
        String html = intent.getStringExtra(Intent.EXTRA_HTML_TEXT);
        if (html == null || html.isEmpty()) html = supplied == null ? null : bounded(supplied);
        if (html == null) throw new IOException("No selected text was shared. Select ticket text and try again.");
        IncomingShareText.require(html); // Bound raw markup before converting it, not just the stripped output.
        String plain = Html.fromHtml(html, Html.FROM_HTML_MODE_LEGACY, source -> null, null).toString().replace("\uFFFC", "");
        return IncomingShareText.require(plain);
    }
    private static String bounded(CharSequence supplied) throws IOException {
        if (supplied == null || supplied.length() > IncomingShareText.MAX_BYTES) throw new IOException("Share selected text up to 64 KiB.");
        return IncomingShareText.require(supplied.toString());
    }
    private void openUnity() {
        try {
            // Resolve only this app's launcher (currently UnityPlayerGameActivity), never an incoming component/URI.
            Intent main = getPackageManager().getLaunchIntentForPackage(getPackageName());
            if (main == null) main = new Intent().setClassName(getPackageName(), "com.unity3d.player.UnityPlayerGameActivity");
            main.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
            startActivity(main);
        } catch (RuntimeException unavailable) { Toast.makeText(this, "Shared text is retained. Open TravelAgent to review it.", Toast.LENGTH_LONG).show(); }
    }
}
