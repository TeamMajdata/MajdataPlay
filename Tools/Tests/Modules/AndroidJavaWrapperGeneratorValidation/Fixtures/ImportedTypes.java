package fixtures;
import java.util.*;
import java.util.Map.Entry;
import static java.lang.Thread.State;
public class ImportedTypes {
    public List<String> list;
    public Entry<String, Integer> entry;
    public State state;
    public int first[], second[][];
    public Widget.Nested nested;
    public String text = "public class Fake { public int wrong; }";
    public List<? extends Number>[] lists(List<? extends Number>... values) { return values; }
}
