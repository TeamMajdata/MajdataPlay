package fixtures;

/** An abstract type that cannot be instantiated through JNI. */
public abstract class AbstractWidget {
    /** Declares a constructor that the generator must not expose as Java instantiation. */
    protected AbstractWidget() { }

    /** Returns a value. @return the value */
    public int value() { return 7; }
}
