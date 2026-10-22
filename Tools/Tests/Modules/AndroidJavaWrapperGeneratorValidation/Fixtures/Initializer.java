package fixtures;
public class Initializer {
    public static int VALUE = fail();
    private static int fail() { throw new RuntimeException("must not initialize"); }
}
