package fixtures;

import java.util.Comparator;
import java.util.Iterator;

public class ManagedContracts {
    public static class Result {
        public byte[] data;
    }

    public static class Natural implements Comparable<Natural> {
        public int compareTo(Natural other) { return 0; }
    }

    public static class Order implements Comparator<Natural> {
        public int compare(Natural left, Natural right) { return 0; }
    }

    public static class Items implements Iterable<Natural> {
        public Iterator<Natural> iterator() { return null; }
    }

    public static class Failure extends IllegalArgumentException {
        public Failure(String message) { super(message); }
    }
}
