package fixtures;

/** An interface with default and static methods and an implicit constant. */
public interface Contract {
    /** The interface constant. */
    int ANSWER = 42;

    /** Describes a contract. @return a description */
    String describe();

    /** Implements a default method. @param value the input @return the input */
    default int defaultValue(int value) { return value; }

    /** Implements a static method. @param value the input @return the input */
    static long staticValue(long value) { return value; }
}
