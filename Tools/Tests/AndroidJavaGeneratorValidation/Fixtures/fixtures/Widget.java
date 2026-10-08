package fixtures;

import java.util.List;

/**
 * A documented widget with XML-sensitive text: A < B & B > C.
 * Its initializer must never execute during metadata extraction.
 */
public class Widget extends BaseWidget implements Contract {
    /** A refreshable compile-time constant. */
    public static final int REVISION = 1;
    /** A signed Java byte. */
    public static final byte BYTE_MIN = -128;
    /** A UTF-16 Java character. */
    public static final char LETTER = '\u03A9';
    /** A Boolean compile-time constant. */
    public static final boolean ENABLED = true;
    /** A short compile-time constant. */
    public static final short SHORT_MIN = -32768;
    /** A long compile-time constant. */
    public static final long LONG_MAX = 9223372036854775807L;
    /** A finite floating-point constant. */
    public static final float FRACTION = 1.25f;
    /** A float NaN constant. */
    public static final float FLOAT_NAN = 0.0f / 0.0f;
    /** A positive float infinity constant. */
    public static final float FLOAT_INFINITY = 1.0f / 0.0f;
    /** A negative double infinity constant. */
    public static final double DOUBLE_NEGATIVE_INFINITY = -1.0 / 0.0;
    /** A double NaN constant. */
    public static final double DOUBLE_NAN = 0.0 / 0.0;
    /** Unicode, control characters, an embedded NUL, and escaped punctuation. */
    public static final String TEXT = "Line\n\"\\\t\u4F60\u597D\0";
    /** Unpaired UTF-16 surrogates that must not be replaced by Unicode conversion. */
    public static final String SURROGATES = "\uD800X\uDC00";
    /** A static final reference that is not a compile-time constant. */
    public static final Widget INSTANCE = null;

    /** Hides the inherited integer field. */
    public String hiddenValue;
    /** A mutable Java field, exposed as a getter-only C# property. */
    public int mutableCount;
    /** An annotated nullable peer. */
    public Widget peer;
    /** An unannotated nullable reference. */
    public Unknown unknown;
    /** An erased generic field. */
    public List<String> names;
    /** A signed byte array. */
    public byte[] bytes;
    /** A character array. */
    public char[] chars;
    /** A jagged array with possibly null rows and null elements. */
    public Widget[][] peers;

    static {
        if (true) {
            throw new IllegalStateException("EXTRACTOR_MUST_NOT_INITIALIZE_WIDGET");
        }
    }

    /** Creates an empty widget. */
    public Widget() { }

    /**
     * Creates a widget from every primitive Java value.
     * @param enabled the Boolean value
     * @param signedByte the signed byte value
     * @param character the UTF-16 character
     * @param small the short value
     * @param count the integer value
     * @param large the long value
     * @param fraction the float value
     * @param precise the double value
     */
    public Widget(boolean enabled, byte signedByte, char character, short small,
                  int count, long large, float fraction, double precise) { }

    /** Creates from an interface. @param contract the nullable contract */
    public Widget(Contract contract) { }

    /** Creates from a concrete peer. @param peer the nullable peer */
    public Widget(Widget peer) { }

    /**
     * Echoes XML-sensitive text safely: A < B & B > C.
     * @param text the nullable text; {@code <tag> & "quotes"} must remain valid XML
     * @return the same nullable text
     */
    public String echo(String text) { return text; }

    /** Echoes a Boolean. @param value the value @return the value */
    public boolean booleanValue(boolean value) { return value; }
    /** Echoes a signed byte. @param value the value @return the value */
    public byte byteValue(byte value) { return value; }
    /** Echoes a character. @param value the value @return the value */
    public char charValue(char value) { return value; }
    /** Echoes a short. @param value the value @return the value */
    public short shortValue(short value) { return value; }
    /** Echoes an integer. @param value the value @return the value */
    public int intValue(int value) { return value; }
    /** Echoes a long. @param value the value @return the value */
    public long longValue(long value) { return value; }
    /** Echoes a float. @param value the value @return the value */
    public float floatValue(float value) { return value; }
    /** Echoes a double. @param value the value @return the value */
    public double doubleValue(double value) { return value; }
    /** Accepts no return value. @param value the value */
    public void consume(int value) { }

    /** Returns a typed nullable wrapper. @param value the peer @return the peer */
    public Widget typed(Widget value) { return value; }
    /** Returns a typed nullable interface. @param value the contract @return the contract */
    public Contract contract(Contract value) { return value; }
    /** Returns an unknown object. @param value the value @return the value */
    public Unknown unknownValue(Unknown value) { return value; }
    /** Tests generic erasure. @param value the value @return the value */
    public <T> T erase(T value) { return value; }
    /** Tests bounded generic erasure. @param value the value @return the value */
    public <T extends Widget> T bounded(T value) { return value; }
    /** Tests a parameterized erased type. @param values the values @return the values */
    public List<String> list(List<String> values) { return values; }
    /** Tests a nullable interface overload. @param value the contract @return the value */
    public Contract choose(Contract value) { return value; }
    /** Tests a nullable concrete overload. @param value the widget @return the value */
    public Widget choose(Widget value) { return value; }

    @Override
    public Widget covariant() { return this; }
    @Override
    public String describe() { return "widget"; }

    /** Echoes Boolean arrays. @param values the values @return the values */
    public boolean[] booleanArray(boolean[] values) { return values; }
    /** Echoes signed-byte arrays. @param values the values @return the values */
    public byte[] byteArray(byte[] values) { return values; }
    /** Echoes character arrays. @param values the values @return the values */
    public char[] charArray(char[] values) { return values; }
    /** Echoes short arrays. @param values the values @return the values */
    public short[] shortArray(short[] values) { return values; }
    /** Echoes integer arrays. @param values the values @return the values */
    public int[] intArray(int[] values) { return values; }
    /** Echoes long arrays. @param values the values @return the values */
    public long[] longArray(long[] values) { return values; }
    /** Echoes float arrays. @param values the values @return the values */
    public float[] floatArray(float[] values) { return values; }
    /** Echoes double arrays. @param values the values @return the values */
    public double[] doubleArray(double[] values) { return values; }
    /** Echoes nullable strings. @param values the values @return the values */
    public String[] stringArray(String[] values) { return values; }
    /** Echoes nullable typed elements. @param values the values @return the values */
    public Widget[] typedArray(Widget[] values) { return values; }
    /** Echoes nullable unknown elements. @param values the values @return the values */
    public Unknown[] unknownArray(Unknown[] values) { return values; }
    /** Echoes jagged nullable rows and elements. @param values the values @return the values */
    public Widget[][] jagged(Widget[][] values) { return values; }
    /** Echoes jagged primitives. @param values the values @return the values */
    public int[][] jaggedPrimitives(int[][] values) { return values; }
    /** Accepts Java varargs. @param values the values */
    public void varargs(byte... values) { }

    /** A public nested Java type whose binary name contains a dollar sign. */
    public static class Nested {
        /** A nested primitive field. */
        public char marker;
        /** Creates a nested instance. @param marker the marker */
        public Nested(char marker) { this.marker = marker; }
        /** Returns a nested instance. @param value the value @return the value */
        public Nested roundTrip(Nested value) { return value; }
    }

    /** A public non-static member class with a synthetic enclosing-instance JVM parameter. */
    public class Inner {
        /** The requested primitive field. */
        public int count;
        /** Creates an inner object using only its implicit enclosing instance. */
        public Inner() { }
        /** Creates an inner object. @param count the requested count */
        public Inner(int count) { this.count = count; }
        /** Creates from a contract. @param contract the nullable contract */
        public Inner(Contract contract) { }
        /** Returns the enclosing widget. @return the enclosing widget */
        public Widget outer() { return Widget.this; }
    }
}
