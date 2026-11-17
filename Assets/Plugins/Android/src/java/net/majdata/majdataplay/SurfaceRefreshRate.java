package net.majdata.majdataplay;

import android.app.Activity;
import android.os.Build;
import android.util.Log;
import android.view.Display;
import android.view.Surface;
import android.view.SurfaceHolder;
import android.view.SurfaceView;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;

import java.util.Locale;

/**
 * Asks Android for a display refresh rate on the current window and surface.
 * {@code AndroidRuntime.SetFrameRate} calls it.
 *
 * <p>Android 15 keeps an app whose category is "game" at 60 Hz until its surface asks for more
 * through {@code Surface.setFrameRate}, and some vendor systems keep the display at 60 Hz for an
 * app that does not select a display mode through {@code preferredDisplayModeId}.
 * {@code Application.targetFrameRate} alone does neither, so both are requested here. The system
 * may still refuse the request because of power saving, heat, or a vendor's own list of games.
 */
public final class SurfaceRefreshRate implements SurfaceHolder.Callback
{
    private static final String TAG = "MajdataPlaySurfaceRefreshRate";
    // The UI thread owns the current requester.
    private static SurfaceRefreshRate current;

    private final Activity activity;
    private float wanted;
    private float asked;
    private SurfaceHolder holder;

    private SurfaceRefreshRate(Activity activity)
    {
        this.activity = activity;
    }

    /**
     * Requests the display refresh rate that can show the given frame rate.
     *
     * @param activity The activity whose display and surface should follow the request.
     * @param fps The frames per second the game will draw. A value of zero or less asks for the
     *            fastest refresh rate the display offers.
     */
    public static void request(final Activity activity, final float fps)
    {
        activity.runOnUiThread(new Runnable()
        {
            @Override
            public void run()
            {
                if (current == null || current.activity != activity)
                {
                    current = new SurfaceRefreshRate(activity);
                }
                current.wanted = fps;
                current.apply();
            }
        });
    }

    private void apply()
    {
        try
        {
            Window window = activity.getWindow();
            Display.Mode mode = pick(activity.getWindowManager().getDefaultDisplay(), wanted);
            asked = mode.getRefreshRate();
            WindowManager.LayoutParams attributes = window.getAttributes();
            if (attributes.preferredDisplayModeId != mode.getModeId())
            {
                attributes.preferredDisplayModeId = mode.getModeId();
                window.setAttributes(attributes);
            }
            if (holder == null)
            {
                SurfaceView view = findSurfaceView(window.getDecorView());
                if (view == null)
                {
                    Log.w(TAG, "no SurfaceView to ask through");
                }
                else
                {
                    holder = view.getHolder();
                    holder.addCallback(this);
                }
            }
            applyToSurface();
            Log.i(TAG, "asked for " + describe(mode));
        }
        catch (RuntimeException exception)
        {
            Log.w(TAG, "the request failed", exception);
        }
    }

    // The request belongs to the surface, and Android destroys the surface whenever the game
    // leaves the screen: ask again on every new one.
    private void applyToSurface()
    {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R || holder == null || asked <= 0)
        {
            return;
        }
        Surface surface = holder.getSurface();
        if (surface == null || !surface.isValid())
        {
            return;
        }
        try
        {
            surface.setFrameRate(asked, Surface.FRAME_RATE_COMPATIBILITY_DEFAULT);
        }
        catch (RuntimeException exception)
        {
            Log.w(TAG, "setFrameRate failed", exception);
        }
    }

    // At the current resolution: the slowest mode that is not slower than wanted, or the fastest
    // one when none reaches it (or when wanted is zero or less).
    private static Display.Mode pick(Display display, float wanted)
    {
        Display.Mode now = display.getMode();
        Display.Mode best = now;
        for (Display.Mode mode : display.getSupportedModes())
        {
            if (mode.getPhysicalWidth() != now.getPhysicalWidth()
                || mode.getPhysicalHeight() != now.getPhysicalHeight())
            {
                continue;
            }
            boolean reaches = reaches(mode, wanted);
            boolean bestReaches = reaches(best, wanted);
            if (reaches != bestReaches)
            {
                if (reaches)
                {
                    best = mode;
                }
            }
            else if (reaches ? mode.getRefreshRate() < best.getRefreshRate()
                : mode.getRefreshRate() > best.getRefreshRate())
            {
                best = mode;
            }
        }
        return best;
    }

    private static boolean reaches(Display.Mode mode, float wanted)
    {
        return wanted > 0 && mode.getRefreshRate() >= wanted - 0.5f;
    }

    private static SurfaceView findSurfaceView(View view)
    {
        if (view instanceof SurfaceView)
        {
            return (SurfaceView) view;
        }
        if (!(view instanceof ViewGroup))
        {
            return null;
        }
        ViewGroup group = (ViewGroup) view;
        for (int index = 0; index < group.getChildCount(); index++)
        {
            SurfaceView found = findSurfaceView(group.getChildAt(index));
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    private static String describe(Display.Mode mode)
    {
        return mode.getPhysicalWidth() + "x" + mode.getPhysicalHeight() + "@" + hz(mode.getRefreshRate());
    }

    private static String hz(float rate)
    {
        return String.format(Locale.ROOT, "%.2f Hz", rate);
    }

    @Override
    public void surfaceCreated(SurfaceHolder holder)
    {
        applyToSurface();
    }

    @Override
    public void surfaceChanged(SurfaceHolder holder, int format, int width, int height)
    {
        applyToSurface();
    }

    @Override
    public void surfaceDestroyed(SurfaceHolder holder)
    {
    }
}
