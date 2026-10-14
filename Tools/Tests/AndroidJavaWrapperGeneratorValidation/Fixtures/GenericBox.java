package fixtures;
public class GenericBox<T extends Number & Comparable<T>> implements Comparable<GenericBox<T>> {
    public T value;
    public T[] values;
    public GenericBox(T value) { this.value = value; }
    public <U extends T> U identity(U value) { return value; }
    public <R> R[] arrays(R[] value) { return value; }
    public int compareTo(GenericBox<T> other) { return 0; }
    public class Inner<U extends T> {
        public U value;
        public Inner(U value) { this.value = value; }
    }
    public static class Nested<V> { public V field; }
    public class NonGenericInner {
        public T value;
        public T echo(T value) { return value; }
    }
}
