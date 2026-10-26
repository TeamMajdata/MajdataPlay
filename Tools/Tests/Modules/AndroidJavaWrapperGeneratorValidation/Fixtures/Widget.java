package fixtures;
class HiddenBase extends Base {
    public int inherited() { return 8; }
}
public class Widget extends HiddenBase implements Child, Secondary {
    public static final int CONSTANT = 3;
    public static int explode = explode();
    public int value;
    public String Class;
    public String JavaReference;
    public String Dispose;
    public byte signedByte;
    public Widget() {}
    public Widget(byte value) { signedByte = value; }
    public Widget(Parent parent) {}
    public int value() { return value; }
    public Widget widget() { return this; }
    public int foo() { return 1; }
    public int Foo() { return 2; }
    public int GetType() { return 3; }
    public String find() { return "widget"; }
    public byte[] bytes(byte[] value) { return value; }
    public Object[][] objects(Object[][] value) { return value; }
    public int[][] integers(int[][] value) { return value; }
    public Parent[] callbacks(Parent[] value) { return value; }
    public Integer boxed(Integer value) { return value; }
    public String text(String value) { return value; }
    public int varargs(String... values) { return values.length; }
    public boolean equals(Object other) { return this == other; }
    public int hashCode() { return 7; }
    public String toString() { return "widget"; }
    public static int explode() { throw new RuntimeException("Extraction must not initialize Widget"); }
    public static class Nested {
        public int id;
        public static class Deep { public int depth; }
    }
    public class Inner { public Inner(int id) {} }
    private static class Hidden { public static class PublicInside {} }
}
