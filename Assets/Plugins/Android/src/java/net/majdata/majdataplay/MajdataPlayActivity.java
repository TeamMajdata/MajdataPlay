package net.majdata.majdataplay;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.view.KeyEvent;

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
        super.onActivityResult(requestCode, resultCode, data);
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
