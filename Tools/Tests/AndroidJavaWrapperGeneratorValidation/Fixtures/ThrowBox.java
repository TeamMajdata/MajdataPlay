package fixtures;

public class ThrowBox<T extends Throwable> {
    public Class<T> type;
    public T value;
    public Class<T> typeOf() { return type; }
    public <E extends T> Class<E> subtype() { return null; }
    public ThrowBox<T> self() { return this; }
    public class Inner {
        public Class<T> enclosingExceptionType;
    }
}
