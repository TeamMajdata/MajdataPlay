package fixtures;

public class StringBox<T extends String> {
    public T value;
    public T[] values;
    public T echo(T value) { return value; }
}
