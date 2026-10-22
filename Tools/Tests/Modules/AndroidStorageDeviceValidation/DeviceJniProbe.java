package net.majdata.validation;

import android.content.Intent;
import android.app.Activity;
import android.content.pm.PackageInfo;
import android.content.res.AssetFileDescriptor;
import android.os.Build;
import android.os.Looper;
import android.os.Process;
import android.os.ParcelFileDescriptor;
import android.system.Os;

import com.unity3d.player.UnityPlayer;
import net.majdata.majdataplay.MajdataPlayActivity;
import net.majdata.majdataplay.StorageAccess;

import java.io.File;
import java.io.FileDescriptor;
import java.io.IOException;
import java.io.RandomAccessFile;
import java.lang.reflect.Constructor;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import android.view.KeyEvent;

/** Small real-JVM inputs for the generated JNI/IL2CPP device validation, never a storage substitute. */
public final class DeviceJniProbe
{
    /** Prevents construction of this static-only probe. */
    private DeviceJniProbe()
    {
    }

    /** A nested value with observable Java equality, hashing, formatting and ordering. */
    public static class WrapperValue implements Comparable<WrapperValue>
    {
        /** Stores the value used by each Java contract. */
        public final int value;

        /** @param value the value retained by this Java instance. */
        public WrapperValue(int value)
        {
            this.value = value;
        }

        /** @param other another value; @return true when the numeric Java values match. */
        @Override
        public boolean equals(Object other)
        {
            return other instanceof WrapperValue && ((WrapperValue)other).value == value;
        }

        /** @return a stable hash derived from the numeric value. */
        @Override
        public int hashCode()
        {
            return value * 31;
        }

        /** @return a deterministic representation distinct from Object.toString. */
        @Override
        public String toString()
        {
            return "WrapperValue:" + value;
        }

        /** @param other the value to compare; @return the signed numeric ordering. */
        @Override
        public int compareTo(WrapperValue other)
        {
            return Integer.compare(value, other.value);
        }
    }

    /** Retains a Java class bound and exposes typed fields, calls, arrays and iteration. */
    public static class WrapperBox<T extends WrapperValue> implements Iterable<T>
    {
        /** Stores the constrained Java value. */
        public final T value;

        /** @param value the value retained for field and iterator checks. */
        public WrapperBox(T value)
        {
            this.value = value;
        }

        /** @param value the typed input; @return the same Java object through a new JNI result. */
        public T echo(T value)
        {
            return value;
        }

        /** @param values the typed inputs; @return a cloned array retaining its element references. */
        public T[] echoArray(T[] values)
        {
            return values.clone();
        }

        /** @return an iterator over the retained typed value. */
        @Override
        public java.util.Iterator<T> iterator()
        {
            return java.util.Collections.singletonList(value).iterator();
        }

        /** Retains its generic outer instance and a transitive bound on the inner type argument. */
        public class Inner<U extends T>
        {
            /** Stores the inner instance's value. */
            public final U value;

            /** @param value the value retained by the nonstatic inner instance. */
            public Inner(U value)
            {
                this.value = value;
            }

            /** @return the retained value with its inner type argument. */
            public U getValue()
            {
                return value;
            }
        }
    }

    /** Exercises the intersection of the JavaObject and recursive Comparable bounds. */
    public static class SelfBox<T extends Comparable<T>>
    {
        /** Creates the generic Java fixture. */
        public SelfBox()
        {
        }

        /** @param value the self-comparable input; @return the same Java object. */
        public T echo(T value)
        {
            return value;
        }
    }

    /** Supplies the Java Comparator behavior adapted to the CLR comparer contract. */
    public static class WrapperOrder implements java.util.Comparator<WrapperValue>
    {
        /** Creates the reverse-order Java comparer. */
        public WrapperOrder()
        {
        }

        /** @param left the first value; @param right the second value; @return the reversed ordering. */
        @Override
        public int compare(WrapperValue left, WrapperValue right)
        {
            return Integer.compare(right.value, left.value);
        }
    }

    /** Exposes a throwable that must survive managed stripping and remain catchable by its wrapper. */
    public static class WrapperFailure extends IllegalArgumentException
    {
        /** @param message the controlled failure description. */
        public WrapperFailure(String message)
        {
            super(message);
        }
    }

    /** Has no exported wrapper, requiring fallback to its closest exported throwable ancestor. */
    private static class PrivateWrapperFailure extends WrapperFailure
    {
        /** @param message the controlled failure description. */
        private PrivateWrapperFailure(String message)
        {
            super(message);
        }
    }

    /** @throws WrapperFailure the controlled exported Java failure. */
    public static void throwWrapperFailure()
    {
        throw new WrapperFailure("MAJDATA_VALIDATION_EXPECTED_WRAPPER_FAILURE");
    }

    /** @throws WrapperFailure a private Java subtype whose exported ancestor must be used. */
    public static void throwPrivateWrapperFailure()
    {
        throw new PrivateWrapperFailure("MAJDATA_VALIDATION_EXPECTED_PRIVATE_WRAPPER_FAILURE");
    }

    /** Fails initialization only when the device harness explicitly reads its static field. */
    public static final class InitializerFailure
    {
        /** Triggers initialization rather than becoming a compile-time Java constant. */
        public static final int VALUE = failInitialization();

        /** @return never returns; @throws IllegalStateException the expected initializer failure. */
        private static int failInitialization()
        {
            throw new IllegalStateException("MAJDATA_VALIDATION_EXPECTED_INITIALIZER_FAILURE");
        }
    }

    /** @param value signed bytes, including null; @return an independent copy, or null. */
    public static byte[] echoBytes(byte[] value)
    {
        return value == null ? null : value.clone();
    }

    /** @param value any signed 64-bit value; @return the unchanged value through JNI. */
    public static long echoLong(long value)
    {
        return value;
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

    /** @return the actual installed validation package's target SDK from Android's package metadata. */
    public static int getTargetSdkVersion()
    {
        return UnityPlayer.currentActivity.getApplicationInfo().targetSdkVersion;
    }

    /** @return the actual installed validation package's version code; @throws Exception if its metadata is unavailable. */
    public static long getVersionCode() throws Exception
    {
        Activity activity = UnityPlayer.currentActivity;
        PackageInfo info = activity.getPackageManager().getPackageInfo(activity.getPackageName(), 0);
        return Build.VERSION.SDK_INT >= 28 ? info.getLongVersionCode() : info.versionCode;
    }

    /**
     * Invokes the production activity callbacks without opening a picker or changing any URI grant.
     * The original launch intent is restored even if a callback fails. Protected callbacks are reached
     * through reflection so no validation-only hooks are added to the production activity.
     * @param extraName the inert extra key observed by the managed validation subscriber
     * @param sentinel the unique value for this invocation
     * @param requestCode an otherwise unused request code for the non-null result; the null result uses its successor
     * @throws Exception if this is not the UI thread, the production activity is absent, or a callback fails
     */
    public static void dispatchRuntimeCallbacks(String extraName, String sentinel, int requestCode) throws Exception
    {
        require(Looper.myLooper() == Looper.getMainLooper(), "Activity callbacks must run on Android's UI thread.");
        Activity activity = UnityPlayer.currentActivity;
        require(activity instanceof MajdataPlayActivity, "The validation launcher is not the production activity.");
        Intent original = activity.getIntent();
        Method newIntent = MajdataPlayActivity.class.getDeclaredMethod("onNewIntent", Intent.class);
        Method activityResult = MajdataPlayActivity.class.getDeclaredMethod("onActivityResult", int.class, int.class, Intent.class);
        newIntent.setAccessible(true);
        activityResult.setAccessible(true);
        try
        {
            newIntent.invoke(activity, new Intent(original).putExtra(extraName, sentinel));
            activityResult.invoke(activity, requestCode, Activity.RESULT_OK, new Intent().putExtra(extraName, sentinel));
            activityResult.invoke(activity, requestCode + 1, Activity.RESULT_CANCELED, null);
        }
        finally
        {
            activity.setIntent(original);
        }
    }

    /**
     * Posts a real key event through the production activity's registered keyboard callback.
     * @param action the Android key action, normally ACTION_DOWN or ACTION_UP
     * @param keyCode the Android key code to deliver
     * @throws IllegalStateException if the validation launcher is not the production activity
     */
    public static void dispatchRuntimeKeyEvent(int action, int keyCode)
    {
        Activity activity = UnityPlayer.currentActivity;
        require(activity instanceof MajdataPlayActivity, "The validation launcher is not the production activity.");
        activity.runOnUiThread(() -> activity.dispatchKeyEvent(new KeyEvent(action, keyCode)));
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

    /**
     * Creates real Android descriptor inputs without a SAF picker or URI grant.
     * This isolates StreamHandle/managed-adapter behavior; the full mode separately tests provider opens.
     * @param kind read, slice, large, write, append, pipe, or bounded-pipe
     * @return a caller-owned production Java stream handle
     * @throws Exception if creating or adopting the controlled descriptor fails
     */
    public static StorageAccess.StreamHandle createStreamFixture(String kind) throws Exception
    {
        AssetFileDescriptor asset;
        boolean readable = !"write".equals(kind) && !"append".equals(kind);
        boolean append = "append".equals(kind);
        if ("pipe".equals(kind) || "bounded-pipe".equals(kind))
        {
            ParcelFileDescriptor[] pipe = ParcelFileDescriptor.createPipe();
            try
            {
                try (ParcelFileDescriptor.AutoCloseOutputStream writer = new ParcelFileDescriptor.AutoCloseOutputStream(pipe[1]))
                {
                    writer.write(new byte[] { 10, 20, 30, 40 });
                }
                asset = new AssetFileDescriptor(pipe[0], 0,
                    "bounded-pipe".equals(kind) ? 4 : AssetFileDescriptor.UNKNOWN_LENGTH);
            }
            catch (Exception | Error exception)
            {
                pipe[0].close();
                pipe[1].close();
                throw exception;
            }
        }
        else
        {
            if (!"read".equals(kind) && !"slice".equals(kind) && !"large".equals(kind) && !"write".equals(kind) && !append)
            {
                throw new IllegalArgumentException("Unknown stream fixture.");
            }
            File file = File.createTempFile("majdata-stream-", ".bin", UnityPlayer.currentActivity.getCacheDir());
            try
            {
                try (RandomAccessFile writer = new RandomAccessFile(file, "rw"))
                {
                    if ("large".equals(kind))
                    {
                        writer.seek((1L << 32) + 17);
                        writer.write(new byte[] { 23, 24 });
                    }
                    else if ("slice".equals(kind))
                    {
                        writer.write(new byte[] { 11, -128, -1, 0, 127, 12 });
                    }
                    else if (!"write".equals(kind))
                    {
                        writer.write(new byte[] { 10, 20, 30, 40 });
                    }
                }
                int mode = readable ? ParcelFileDescriptor.MODE_READ_ONLY : ParcelFileDescriptor.MODE_READ_WRITE;
                if (append)
                {
                    mode |= ParcelFileDescriptor.MODE_APPEND;
                }
                asset = new AssetFileDescriptor(ParcelFileDescriptor.open(file, mode),
                    "slice".equals(kind) ? 1 : 0, "slice".equals(kind) ? 4 : AssetFileDescriptor.UNKNOWN_LENGTH);
            }
            finally
            {
                // The open descriptor keeps the unlinked inode alive; its owner reclaims even sparse files on close.
                file.delete();
            }
        }
        try
        {
            Constructor<StorageAccess.StreamHandle> constructor = StorageAccess.StreamHandle.class.getDeclaredConstructor(
                AssetFileDescriptor.class, boolean.class, boolean.class);
            constructor.setAccessible(true);
            return constructor.newInstance(asset, readable, append);
        }
        catch (Exception | Error exception)
        {
            asset.close();
            throw exception;
        }
    }

    /** @return true after real-descriptor regressions pass; @throws Exception on any failed assertion. */
    public static boolean validateDocumentStreams() throws Exception
    {
        StorageAccess.StreamHandle slice = createStreamFixture("slice");
        FileDescriptor sliceDescriptor = fixtureDescriptor(slice);
        try
        {
            require(slice.seekable, "The regular asset slice must be seekable.");
            requireValue(slice.getLength(), 4);
            requireValue(slice.getPosition(), 0);
            requireData(slice.read(2), new byte[] { -128, -1 });
            requireValue(slice.getPosition(), 2);
            requireValue(slice.seek(-1, 1), 1);
            requireValue(slice.seek(-1, 2), 3);
            requireData(slice.read(5), new byte[] { 127 });
            require(slice.read(1).count == -1, "The slice exposed its suffix sentinel.");
            requireValue(slice.setPosition(0), 0);
            requireData(slice.read(10), new byte[] { -128, -1, 0, 127 });
            requireValue(slice.seek(8, 0), 8);
            require(slice.read(1).count == -1, "Reading beyond a slice must return EOF.");
            requireValue(slice.setPosition(1), 1);
            require(slice.setPosition(-1).errorCode == StorageAccess.INVALID_ARGUMENT, "Negative position was accepted.");
            require(slice.seek(0, 99).errorCode == StorageAccess.INVALID_ARGUMENT, "Invalid seek origin was accepted.");
            require(slice.seek(-2, 1).errorCode == StorageAccess.IO_ERROR, "Seeking before the asset was accepted.");
            require(slice.seek(Long.MAX_VALUE, 1).errorCode == StorageAccess.IO_ERROR, "Logical offset overflow was accepted.");
            require(slice.seek(Long.MAX_VALUE, 0).errorCode == StorageAccess.IO_ERROR, "Physical offset overflow was accepted.");
            requireValue(slice.getPosition(), 1);
        }
        finally
        {
            requireSuccess(slice.close());
        }
        require(!sliceDescriptor.valid(), "Closing the slice leaked its descriptor.");
        require(slice.getPosition().errorCode == StorageAccess.IO_ERROR, "A closed handle remained usable.");
        StorageAccess.StreamHandle other = createStreamFixture("read");
        try
        {
            requireSuccess(slice.close());
            requireData(other.read(1), new byte[] { 10 });
        }
        finally
        {
            requireSuccess(other.close());
        }

        StorageAccess.StreamHandle large = createStreamFixture("large");
        try
        {
            long offset = (1L << 32) + 17;
            require(large.seekable, "The sparse file must be seekable even without a declared length.");
            requireValue(large.getLength(), offset + 2);
            requireValue(large.seek(offset, 0), offset);
            requireData(large.read(2), new byte[] { 23, 24 });
            requireValue(large.seek(-1, 2), offset + 1);
            requireData(large.read(1), new byte[] { 24 });
        }
        finally
        {
            requireSuccess(large.close());
        }

        for (String kind : new String[] { "pipe", "bounded-pipe" })
        {
            StorageAccess.StreamHandle pipe = createStreamFixture(kind);
            FileDescriptor descriptor = fixtureDescriptor(pipe);
            try
            {
                require(!pipe.seekable, "A pipe incorrectly reported seeking support.");
                require(pipe.getLength().errorCode == StorageAccess.UNSUPPORTED, "A pipe exposed a seekable length.");
                require(pipe.getPosition().errorCode == StorageAccess.UNSUPPORTED, "A pipe exposed a seekable position.");
                require(pipe.setPosition(0).errorCode == StorageAccess.UNSUPPORTED, "A pipe accepted position changes.");
                require(pipe.seek(0, 0).errorCode == StorageAccess.UNSUPPORTED, "A pipe accepted seeking.");
                requireData(pipe.read(2), new byte[] { 10, 20 });
                requireData(pipe.read(2), new byte[] { 30, 40 });
                require(pipe.read(1).count == -1, "The pipe did not reach EOF.");
            }
            finally
            {
                requireSuccess(pipe.close());
            }
            require(!descriptor.valid(), "Closing the pipe leaked its descriptor.");
        }

        StorageAccess.StreamHandle writer = createStreamFixture("write");
        FileDescriptor writeDescriptor = fixtureDescriptor(writer);
        try
        {
            require(writer.seekable, "The regular output must support seeking.");
            requireValue(writer.getLength(), 0);
            requireSuccess(writer.write(new byte[] { 1, 2, 3 }, 3));
            requireValue(writer.getLength(), 3);
            requireValue(writer.getPosition(), 3);
            requireValue(writer.setPosition(1), 1);
            requireSuccess(writer.write(new byte[] { 9 }, 1));
            byte[] content = new byte[3];
            require(Os.pread(writeDescriptor, content, 0, 3, 0) == 3 && content[0] == 1 && content[1] == 9 && content[2] == 3,
                "Output position changes did not write at the requested byte.");
            long offset = (1L << 32) + 17;
            requireValue(writer.seek(offset, 0), offset);
            requireSuccess(writer.write(new byte[] { 7 }, 1));
            requireValue(writer.getLength(), offset + 1);
            requireValue(writer.getPosition(), offset + 1);
            requireSuccess(writer.flush());
        }
        finally
        {
            requireSuccess(writer.close());
        }
        require(!writeDescriptor.valid(), "Closing the output leaked its descriptor.");

        StorageAccess.StreamHandle append = createStreamFixture("append");
        try
        {
            require(!append.seekable, "Append must keep sequential provider semantics.");
            require(append.setPosition(0).errorCode == StorageAccess.UNSUPPORTED, "Append accepted positioning.");
            requireSuccess(append.write(new byte[] { 50 }, 1));
            byte[] content = new byte[5];
            require(Os.pread(fixtureDescriptor(append), content, 0, 5, 0) == 5 && content[0] == 10 && content[4] == 50,
                "Append overwrote the original bytes.");
        }
        finally
        {
            requireSuccess(append.close());
        }
        return true;
    }

    /** Reads the private owner only to observe actual native descriptor closure in this test. */
    private static FileDescriptor fixtureDescriptor(StorageAccess.StreamHandle handle) throws Exception
    {
        Field field = StorageAccess.StreamHandle.class.getDeclaredField("asset");
        field.setAccessible(true);
        return ((AssetFileDescriptor)field.get(handle)).getFileDescriptor();
    }

    /** Checks the typed protocol without translating a failure into a false positive. */
    private static void requireSuccess(StorageAccess.Result result)
    {
        require(result.errorCode == StorageAccess.SUCCESS, "Stream fixture failed: " + result.errorMessage);
    }

    /** Checks that lengths and positions preserve all 64 bits through the typed envelope. */
    private static void requireValue(StorageAccess.Result result, long expected)
    {
        requireSuccess(result);
        require(result.value == expected, "The logical stream value differs from the expected 64-bit value.");
    }

    /** Checks a bounded transfer including signed-byte patterns and exact EOF boundaries. */
    private static void requireData(StorageAccess.Result result, byte[] expected)
    {
        requireSuccess(result);
        require(result.count == expected.length && java.util.Arrays.equals(result.data, expected), "The stream read unexpected bytes.");
    }

    /** Throws a fixed-context failure which the device harness observes and reports. */
    private static void require(boolean condition, String message)
    {
        if (!condition)
        {
            throw new IllegalStateException(message);
        }
    }

    /** @throws IllegalStateException always, with a fixed non-sensitive validation sentinel. */
    public static void throwExpected()
    {
        throw new IllegalStateException("MAJDATA_VALIDATION_EXPECTED_JAVA_EXCEPTION");
    }
}
