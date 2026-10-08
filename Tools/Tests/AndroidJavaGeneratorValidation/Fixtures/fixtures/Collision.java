package fixtures;

import java.util.List;
import java.util.Set;

/** Java overloads and names that cannot be silently merged in C#. */
public class Collision {
    /** A field that collides with the PascalCase method below. */
    public int sample;

    /** First erased constructor. @param values the values */
    public Collision(List<String> values) { }
    /** Second erased constructor. @param values the values */
    public Collision(Set<String> values) { }

    /** First erased overload. @param values the values */
    public void select(List<String> values) { }
    /** Second erased overload. @param values the values */
    public void select(Set<String> values) { }
    /** A PascalCase collision with the field. @return a value */
    public int Sample() { return 1; }
    /** First case-colliding method. */
    public void ping() { }
    /** Second case-colliding method. */
    public void Ping() { }
}
