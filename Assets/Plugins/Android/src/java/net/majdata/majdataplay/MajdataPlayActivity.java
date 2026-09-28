package net.majdata.majdataplay;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.view.KeyEvent;
import net.majdata.majdataplay.oniimai.OniimaiController;

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
        // Unity's generated manifest can disable Android View acceleration.
        // Enable the window before its views attach so dashboard updates do not
        // rasterize the entire phone UI on the CPU during external gameplay.
        getWindow().addFlags(android.view.WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED);
        super.onCreate(savedInstanceState);
        getWindow().getDecorView().post(() -> android.util.Log.i("OniimaiDisplay",
                "Phone UI hardware accelerated=" + getWindow().getDecorView().isHardwareAccelerated()));
        SystemCAImporter.tryInit(getApplicationContext());
        OniimaiController.attach(this);
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
        if (OniimaiController.handleKey(event)) return true;
        if (onDispatchKeyEventCallbackProxy != null)
        {
            onDispatchKeyEventCallbackProxy.OnDispatchKeyEvent(event.getAction(), event.getKeyCode());
        }

        return super.dispatchKeyEvent(event);
    }
    @Override public void onBackPressed() {
        if(!OniimaiController.dashboardBack())super.onBackPressed();
    }
    @Override protected void onResume() {
        super.onResume(); OniimaiController.resume();
    }
    @Override protected void onPause() {
        OniimaiController.pause(); super.onPause();
    }
    @Override protected void onDestroy() {
        OniimaiController.detach(); super.onDestroy(); currentActivity = null;
    }
    @Override public void onWindowFocusChanged(boolean focused) {
        super.onWindowFocusChanged(focused); OniimaiController.focus(focused);
    }
    @Override public void onConfigurationChanged(android.content.res.Configuration configuration) {
        super.onConfigurationChanged(configuration); OniimaiController.textConfigurationChanged();
    }
    public static Activity getCurrentActivity()
    {
        return currentActivity;
    }
    /** Only called on the Android UI thread at external surface attach/detach. */
    public boolean oniimaiSurface(android.view.Surface surface)
    {
        return mUnityPlayer.displayChanged(0, surface);
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
