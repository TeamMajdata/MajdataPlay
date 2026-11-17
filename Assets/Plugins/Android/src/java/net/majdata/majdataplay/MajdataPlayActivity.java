package net.majdata.majdataplay;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.util.Log;
import android.view.Display;
import android.view.KeyEvent;
import android.view.WindowManager;

import com.unity3d.player.UnityPlayer;
import com.unity3d.player.UnityPlayerActivity;

import net.majdata.majdataplay.runtime.SystemCAImporter;

public class MajdataPlayActivity extends UnityPlayerActivity
{
    static CSharpOnNewIntentCallback onNewIntentCallbackProxy;
    static CSharpOnActivityResultCallback onActivityResultCallbackProxy;
    static CSharpOnDispatchKeyEventCallback onDispatchKeyEventCallbackProxy;
    static Activity currentActivity;
    @Override
    protected void onCreate(Bundle savedInstanceState)
    {
        currentActivity = this;
        super.onCreate(savedInstanceState);
        SystemCAImporter.tryInit(getApplicationContext());
    }
    @Override
    protected void onNewIntent(Intent intent)
    {
        super.onNewIntent(intent);

        setIntent(intent);
        if (onNewIntentCallbackProxy != null)
        {
            onNewIntentCallbackProxy.OnNewIntent(intent);
        }
    }
    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data)
    {
        if (onActivityResultCallbackProxy != null)
        {
            onActivityResultCallbackProxy.OnActivityResult(requestCode, resultCode, data);
        }
    }
    @Override
    public boolean dispatchKeyEvent(KeyEvent event)
    {
        if (onDispatchKeyEventCallbackProxy != null)
        {
            onDispatchKeyEventCallbackProxy.OnDispatchKeyEvent(event.getAction(), event.getKeyCode());
        }

        return super.dispatchKeyEvent(event);
    }
    public static Activity getCurrentActivity()
    {
        return currentActivity;
    }
    public void requestXiaomiRefreshRate(int targetFrameRate)
    {
        if (!"Xiaomi".equalsIgnoreCase(Build.MANUFACTURER) &&
            !"Redmi".equalsIgnoreCase(Build.MANUFACTURER) &&
            !"POCO".equalsIgnoreCase(Build.MANUFACTURER))
        {
            return;
        }

        runOnUiThread(() -> {
            try
            {
                // Unity uses 30 FPS on mobile when no positive frame limit is set.
                int requestedRate = targetFrameRate > 0 ? targetFrameRate : 30;
                Display display = getWindowManager().getDefaultDisplay();
                Display.Mode currentMode = display.getMode();
                Display.Mode bestMode = currentMode;
                float bestDistance = Math.abs(currentMode.getRefreshRate() - requestedRate);
                for (Display.Mode mode : display.getSupportedModes())
                {
                    if (mode.getPhysicalWidth() != currentMode.getPhysicalWidth() ||
                        mode.getPhysicalHeight() != currentMode.getPhysicalHeight())
                    {
                        continue;
                    }

                    float distance = Math.abs(mode.getRefreshRate() - requestedRate);
                    if (distance < bestDistance - 0.001f ||
                        (Math.abs(distance - bestDistance) < 0.001f &&
                         mode.getRefreshRate() > bestMode.getRefreshRate()))
                    {
                        bestMode = mode;
                        bestDistance = distance;
                    }
                }

                WindowManager.LayoutParams attributes = getWindow().getAttributes();
                attributes.preferredDisplayModeId = bestMode.getModeId();
                attributes.preferredRefreshRate = requestedRate;
                getWindow().setAttributes(attributes);

                // PowerKeeper can override the window request with its default 60 Hz game policy.
                // Use the selected mode's rate so a 144 Hz display is not capped at 120 Hz.
                Intent request = new Intent("com.xiaomi.joyose.OVERRIDE_GAME_FRESHRATE");
                request.setPackage("com.miui.powerkeeper");
                request.putExtra("override_pkg_name", getPackageName());
                request.putExtra("override_freshrate", Math.round(bestMode.getRefreshRate()));
                sendBroadcast(request);
            }
            catch (Exception exception)
            {
                Log.w("MajdataPlay", "Xiaomi refresh rate preference unavailable", exception);
            }
        });
    }
    public static void registerOnNewIntentCallback(CSharpOnNewIntentCallback callback)
    {
        if (onNewIntentCallbackProxy == null)
        {
            onNewIntentCallbackProxy = callback;
        }
    }
    public static void registerOnActivityResultCallback(CSharpOnActivityResultCallback callback)
    {
        if (onActivityResultCallbackProxy == null) {
            onActivityResultCallbackProxy = callback;
        }
    }
    public static void registerDispatchKeyEventCallback(CSharpOnDispatchKeyEventCallback callback)
    {
        if (onDispatchKeyEventCallbackProxy == null) {
            onDispatchKeyEventCallbackProxy = callback;
        }
    }
}
