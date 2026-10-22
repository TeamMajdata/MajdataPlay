package fixtures;

/** An enum with compiler-generated public values and valueOf methods. */
public enum Mode {
    /** The first mode. */
    FIRST,
    /** The second mode. */
    SECOND;

    static {
        if (true) {
            throw new IllegalStateException("EXTRACTOR_MUST_NOT_INITIALIZE_ENUM");
        }
    }

    /** Returns an ordinary enum member. @return the ordinal */
    public int code() { return ordinal(); }
}
