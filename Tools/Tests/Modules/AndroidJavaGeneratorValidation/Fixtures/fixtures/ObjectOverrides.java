package fixtures;

/** Distinguishes genuine Object overrides from overloads and interface contracts. */
public final class ObjectOverrides {
    /** Prevents construction of the fixture container. */
    private ObjectOverrides() { }

    /** Overrides all three Java Object virtual methods. */
    public static class All {
        /** Compares identity. @param other the comparison target @return identity equality */
        @Override
        public boolean equals(Object other) { return this == other; }

        /** Gets a stable fixture hash. @return the fixture hash */
        @Override
        public int hashCode() { return 17; }

        /** Gets the fixture description. @return the description */
        @Override
        public String toString() { return "all"; }
    }

    /** Overrides equality without declaring hashCode or toString. */
    public static class EqualsOnly {
        /** Compares identity. @param other the comparison target @return identity equality */
        @Override
        public boolean equals(Object other) { return this == other; }
    }

    /** Overrides hashing without declaring equality or toString. */
    public static class HashOnly {
        /** Gets a stable fixture hash. @return the fixture hash */
        @Override
        public int hashCode() { return 23; }
    }

    /** Overrides formatting without declaring equality or hashCode. */
    public static class StringOnly {
        /** Gets the fixture description. @return the description */
        @Override
        public String toString() { return "string-only"; }
    }

    /** Inherits effective overrides from a Java ancestor. */
    public static class Inherited extends All {
    }

    /** Inherits only Java Object's default implementations. */
    public static class Plain {
    }

    /** Has similarly named instance overloads that do not override Object. */
    public static class Overloads {
        /** Compares this fixture. @param other the fixture @return identity equality */
        public boolean equals(Overloads other) { return this == other; }

        /** Returns the input hash. @param value the input @return the input */
        public int hashCode(int value) { return value; }

        /** Returns the input description. @param value the input @return the input */
        public String toString(String value) { return value; }

        /** Exposes the common spelling mistake. @return the fixture hash */
        public int getHashCode() { return 29; }
    }

    /** Has legal static overloads that do not override Object. */
    public static class StaticLookalikes {
        /** Tests a string. @param value the input @return whether the input is non-null */
        public static boolean equals(String value) { return value != null; }

        /** Returns the input hash. @param value the input @return the input */
        public static int hashCode(int value) { return value; }

        /** Returns the input description. @param value the input @return the input */
        public static String toString(String value) { return value; }
    }

    /** Redeclares Object signatures without supplying a class implementation. */
    public interface RedeclaredContract {
        /** Compares an object. @param other the comparison target @return equality */
        boolean equals(Object other);

        /** Gets a hash. @return the hash */
        int hashCode();

        /** Gets a description. @return the description */
        String toString();
    }

    /** Satisfies interface declarations with the inherited Java Object implementations. */
    public static class InterfaceImplementer implements RedeclaredContract {
    }
}
