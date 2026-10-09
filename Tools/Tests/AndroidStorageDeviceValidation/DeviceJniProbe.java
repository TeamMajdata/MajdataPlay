package net.majdata.validation;

import android.content.Intent;
import android.os.Build;
import android.os.Process;
import android.view.KeyEvent;

/** Small real-JVM inputs for the generated JNI/IL2CPP device validation, never a storage substitute. */
public final class DeviceJniProbe
{
    /** Prevents construction of this static-only probe. */
    private DeviceJniProbe()
    {
    }

    /** @param value signed bytes, including null; @return an independent copy, or null. */
    public static byte[] echoBytes(byte[] value)
    {
        return value == null ? null : value.clone();
    }

    /** @param value any UTF-16 string, including embedded NUL; @return the same value, or null. */
    public static String echoString(String value)
    {
        return value;
    }

    /** @return a null typed Java reference. */
    public static Intent nullIntent()
    {
        return null;
    }

    /** @param value the intent to return; @return the same Java object through a newly owned JNI result. */
    public static Intent echoIntent(Intent value)
    {
        return value;
    }

    /** @param value the key to return; @return the same Java object through a newly owned JNI result. */
    public static KeyEvent echoKeyEvent(KeyEvent value)
    {
        return value;
    }

    /** @return an untyped reference whose ownership can be adopted by the generated KeyEvent wrapper. */
    public static Object createAdoptableKeyEvent()
    {
        return new KeyEvent(KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_A);
    }

    /** @return the running Android SDK version, not the generator's compile-time API level. */
    public static int getSdkInt()
    {
        return Build.VERSION.SDK_INT;
    }

    /** @return the current Linux process ID used to distinguish replay from the picker process. */
    public static int getProcessId()
    {
        return Process.myPid();
    }

    /** @return whether this process actually uses the 64-bit Android runtime. */
    public static boolean is64Bit()
    {
        return Process.is64Bit();
    }

    /** @throws IllegalStateException always, with a fixed non-sensitive validation sentinel. */
    public static void throwExpected()
    {
        throw new IllegalStateException("MAJDATA_VALIDATION_EXPECTED_JAVA_EXCEPTION");
    }
}
