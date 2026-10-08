package fixtures;

/** The inherited public API, including a covariant method. */
public class BaseWidget {
    /** A base field that is hidden with a different type. */
    public int hiddenValue = 1;
    /** An inherited instance field. */
    public long inheritedCount = 2;

    /** Creates a base widget. */
    public BaseWidget() { }

    /** Returns this base widget. @return the receiver */
    public BaseWidget covariant() { return this; }

    /** Returns an inherited value. @param value the input @return the input */
    public int inheritedValue(int value) { return value; }

    /** Returns a static inherited value. @param value the input @return the input */
    public static int inheritedStatic(int value) { return value; }
}
