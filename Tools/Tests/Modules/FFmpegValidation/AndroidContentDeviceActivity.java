package net.majdata.ffmpegplayer.validation;

import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.UriPermission;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import com.unity3d.player.UnityPlayer;
import com.unity3d.player.UnityPlayerActivity;

/** Owns only the validation application's SAF picker and optional persisted read grant. */
public final class AndroidContentDeviceActivity extends UnityPlayerActivity {
    private static final Object STATE_LOCK = new Object();
    private static final int VIDEO_REQUEST = 0x4d43;
    private static final String PREFERENCES = "ffmpeg_content_validation";
    private static final String SELECTED_URI = "selected_video_uri";
    private static String selectedUri = "";
    private static String failure = "";
    private static String requestState = "idle";
    private static boolean persistedReadGrant;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        restoreSelection();
    }

    /** Requests a video on the activity UI thread, reusing only a still-held persisted read grant. */
    public static void RequestVideo() {
        final Activity current = UnityPlayer.currentActivity;
        if (!(current instanceof AndroidContentDeviceActivity)) {
            fail("failed", "validation_activity_not_current");
            return;
        }
        final AndroidContentDeviceActivity activity = (AndroidContentDeviceActivity) current;
        activity.runOnUiThread(() -> {
            synchronized (STATE_LOCK) {
                if ("pending".equals(requestState)) {
                    return;
                }
            }
            if (activity.restoreSelection()) {
                return;
            }
            synchronized (STATE_LOCK) {
                selectedUri = "";
                failure = "";
                persistedReadGrant = false;
                requestState = "pending";
            }
            try {
                final Intent request = new Intent(Intent.ACTION_OPEN_DOCUMENT);
                request.addCategory(Intent.CATEGORY_OPENABLE);
                request.setType("video/*");
                request.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION
                        | Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
                activity.startActivityForResult(request, VIDEO_REQUEST);
            } catch (RuntimeException exception) {
                fail("failed", "document_picker_launch_failed:" + exception.getClass().getSimpleName());
            }
        });
    }

    /** Returns the original provider URI string; callers must never log it. */
    public static String GetSelectedUri() {
        synchronized (STATE_LOCK) {
            return selectedUri;
        }
    }

    /** Returns a URI-free failure code, never a provider exception message. */
    public static String GetFailure() {
        synchronized (STATE_LOCK) {
            return failure;
        }
    }

    /** Returns idle, pending, selected, canceled, or failed. */
    public static String GetRequestState() {
        synchronized (STATE_LOCK) {
            return requestState;
        }
    }

    /** Returns whether the current selection has an Android-persisted read grant. */
    public static boolean GetHasPersistedReadGrant() {
        synchronized (STATE_LOCK) {
            return persistedReadGrant;
        }
    }

    /** Returns the running device API level without requiring another generated wrapper. */
    public static int GetAndroidApiLevel() {
        return Build.VERSION.SDK_INT;
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != VIDEO_REQUEST) {
            return;
        }
        if (resultCode != Activity.RESULT_OK) {
            fail("canceled", "document_picker_canceled");
            return;
        }
        if (data == null || data.getData() == null) {
            fail("failed", "document_picker_missing_uri");
            return;
        }
        final Uri uri = data.getData();
        if (!"content".equalsIgnoreCase(uri.getScheme())) {
            fail("failed", "document_picker_not_content");
            return;
        }
        try {
            final int grantedFlags = data.getFlags();
            final boolean readGranted = (grantedFlags & Intent.FLAG_GRANT_READ_URI_PERMISSION) != 0;
            final boolean persistable = (grantedFlags & Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION) != 0;
            if (!readGranted && !hasPersistedReadGrant(uri)) {
                fail("failed", "document_picker_missing_read_grant");
                return;
            }
            if (readGranted && persistable) {
                try {
                    getContentResolver().takePersistableUriPermission(uri, Intent.FLAG_GRANT_READ_URI_PERMISSION);
                } catch (SecurityException exception) {
                    // The transient read grant remains usable; never expose the provider's exception message.
                }
            }
            final boolean retained = hasPersistedReadGrant(uri);
            final SharedPreferences.Editor preferences = getSharedPreferences(PREFERENCES, MODE_PRIVATE).edit();
            if (retained) {
                preferences.putString(SELECTED_URI, uri.toString());
            } else {
                preferences.remove(SELECTED_URI);
            }
            preferences.commit();
            select(uri.toString(), retained);
        } catch (RuntimeException exception) {
            fail("failed", "document_selection_failed:" + exception.getClass().getSimpleName());
        }
    }

    /** Restores only this application's stored URI when Android still reports a persisted read permission. */
    private boolean restoreSelection() {
        try {
            final SharedPreferences preferences = getSharedPreferences(PREFERENCES, MODE_PRIVATE);
            final String location = preferences.getString(SELECTED_URI, null);
            if (location != null) {
                final Uri uri = Uri.parse(location);
                if ("content".equalsIgnoreCase(uri.getScheme()) && hasPersistedReadGrant(uri)) {
                    select(location, true);
                    return true;
                }
                preferences.edit().remove(SELECTED_URI).commit();
            }
        } catch (RuntimeException exception) {
            // An unavailable old grant must lead to a fresh picker, never a logged document URI.
        }
        synchronized (STATE_LOCK) {
            selectedUri = "";
            failure = "";
            persistedReadGrant = false;
            requestState = "idle";
        }
        return false;
    }

    /** Checks the OS grant list, not a filesystem path, without opening or copying the document. */
    private boolean hasPersistedReadGrant(Uri uri) {
        for (UriPermission permission : getContentResolver().getPersistedUriPermissions()) {
            if (permission.isReadPermission() && permission.getUri().equals(uri)) {
                return true;
            }
        }
        return false;
    }

    /** Atomically publishes an opaque selection and its actual permission lifetime. */
    private static void select(String location, boolean persisted) {
        synchronized (STATE_LOCK) {
            selectedUri = location;
            persistedReadGrant = persisted;
            failure = "";
            requestState = "selected";
        }
    }

    /** Atomically publishes a terminal picker state using only controlled, URI-free diagnostics. */
    private static void fail(String state, String code) {
        synchronized (STATE_LOCK) {
            selectedUri = "";
            persistedReadGrant = false;
            failure = code;
            requestState = state;
        }
    }
}
