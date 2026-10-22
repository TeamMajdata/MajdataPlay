package fixtures;

/** Distinguishes instantiable public constructors from inaccessible or absent ones. */
public final class ConstructorVisibility {
    /** Prevents instantiation of this fixture container. */
    private ConstructorVisibility() { }

    /** Exposes public overloads alongside every non-public constructor visibility. */
    public static class Mixed {
        /** Creates from a public integer. @param count the integer */
        public Mixed(int count) { }
        /** Creates from a public string. @param text the nullable string */
        public Mixed(String text) { }
        /** An inaccessible no-argument constructor. */
        private Mixed() { }
        /** An inaccessible protected overload. @param enabled the Boolean */
        protected Mixed(boolean enabled) { }
        /** An inaccessible package-private overload. @param count the long integer */
        Mixed(long count) { }
    }

    /** Has no public constructor despite being a concrete public class. */
    public static class NoPublic {
        /** An inaccessible no-argument constructor. */
        private NoPublic() { }
        /** An inaccessible protected overload. @param count the integer */
        protected NoPublic(int count) { }
        /** An inaccessible package-private overload. @param text the string */
        NoPublic(String text) { }
    }

    /** Has a public parameterized constructor and no no-argument constructor. */
    public static class Parameterized {
        /** Creates from a character. @param marker the UTF-16 character */
        public Parameterized(char marker) { }
    }

    /** Uses the compiler's implicit public no-argument constructor. */
    public static class Default {
    }

    /** Cannot be instantiated even though its declared constructor is public. */
    public abstract static class Abstract {
        /** A public constructor that must not produce JNI instantiation. */
        public Abstract() { }
    }
}
